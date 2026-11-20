using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>Owns the zone's hostiles: night wolf packs and the bandit camp. Spawning, group
/// behaviour, strikes on players, loot and replication. Lives at Main/Wildlife on every machine.
///
/// Server: spawns packs at night (sized by Difficulty.PackSize) and keeps the camp manned (more
/// grunts per player, captain scaled by Difficulty.BigMonster*), runs them, sends positions 15×/s,
/// resolves strikes and kills. Clients: create / remove enemies when told and show them where the
/// server says. GDD §9, §16.</summary>
public partial class Wildlife : Node3D
{
    public static Wildlife Instance { get; private set; }

    /// <summary>Night packs and camp manning on/off (scripted tests turn it off and spawn by hand).</summary>
    public static bool AutoSpawn = true;

    /// <summary>Wolves per pack for one player; more players means bigger packs.</summary>
    public const int SoloPackSize = 2;
    public const int PacksPerNight = 2;
    /// <summary>Nights (counted by the day they start on) when no packs come.</summary>
    public const int QuietNights = 1;
    private bool _quietNight;
    /// <summary>Bandit grunts in the camp for one player.</summary>
    public const int SoloCampSize = 2;
    /// <summary>A cleared camp is re-occupied after this many days.</summary>
    public const int CampRespawnDays = 3;
    public const int AnimAttack = 0, AnimStun = 1;

    /// <summary>The plateau and the vale, where ground enemies live (and the navmesh is baked).</summary>
    public const float WorldMinX = -56f, WorldMaxX = 56f, WorldMinZ = -27f, WorldMaxZ = 232f;
    public static readonly Rect2 HomeBounds = new(WorldMinX, WorldMinZ, WorldMaxX - WorldMinX, WorldMaxZ - WorldMinZ);

    /// <summary>Harpies per roost for one player (more players, more harpies).</summary>
    public const int SoloRoostSize = 3;
    /// <summary>A cleared roost fills again after this many days.</summary>
    public const int RoostRespawnDays = 1;
    /// <summary>Raiders per raid for one player.</summary>
    public const int SoloRaidSize = 3;
    /// <summary>Raids start on this day, when the base has at least RaidMinBuildings.</summary>
    public const int RaidFirstDay = 3;
    public const int RaidMinBuildings = 3;
    /// <summary>Chance of a raid at each dusk (at least RaidMinGap days apart; certain after RaidMaxGap).</summary>
    public const float RaidChance = 0.45f;
    public const int RaidMinGap = 2, RaidMaxGap = 4;
    /// <summary>Raiders gather out of sight on the vale road and march north.</summary>
    public static readonly Vector3 RaidSpawn = new(2, 0, 106);
    /// <summary>Most a raider takes from the stockpile per blow on a chest.</summary>
    public const int StealPerStrike = 4;

    /// <summary>Harpy roosts in Falls Valley (the world builder puts a spire and nest here).</summary>
    public static readonly Vector3[] RoostSites = { new(118, 0, 80) };

    public const float FirstPackDelay = 6f;
    private const float PackInterval = 110f;
    private const float SendInterval = 1f / 15f;
    /// <summary>Pack anchors creep toward the base at this speed (m/s), so wolves come to you.</summary>
    private const float AnchorDrift = 0.6f;
    private const int CampGroupBase = 10000, RoostGroupBase = 20000, RaidGroupBase = 30000;

    /// <summary>Bandit camps (the world builder puts the tents and fires here).</summary>
    public static readonly Vector3[] CampSites = { new(38, 0, 140) };

    /// <summary>Plateau and vale edges, away from the base and the village.</summary>
    private static readonly Vector3[] SpawnPoints =
    {
        new(-50, 0, 62), new(50, 0, 70), new(2, 0, 84), new(-50, 0, 12), new(50, 0, 22), new(-30, 0, 84), new(32, 0, 84),
        new(-48, 0, 125), new(-46, 0, 100),
    };

    public readonly Dictionary<int, Enemy> Enemies = new();
    /// <summary>The wolves among the enemies (a fresh dictionary each call).</summary>
    public Dictionary<int, Wolf> Wolves => Enemies.Values.OfType<Wolf>().ToDictionary(w => w.NetId);
    public IEnumerable<Bandit> Bandits => Enemies.Values.OfType<Bandit>().Where(b => !b.Raider);
    public IEnumerable<Bandit> Raiders => Enemies.Values.OfType<Bandit>().Where(b => b.Raider);
    public IEnumerable<Harpy> Harpies => Enemies.Values.OfType<Harpy>();
    /// <summary>Server: total strikes that reached a player, blocked or not (tests, status).</summary>
    public int Bites { get; private set; }
    public int Parries { get; private set; }

    private class Camp
    {
        public Vector3 Center;
        public bool Cleared;
        public int RespawnDay;
    }

    private readonly Camp[] _camps = CampSites.Select(c => new Camp { Center = c }).ToArray();
    private readonly Camp[] _roosts = RoostSites.Select(c => new Camp { Center = c }).ToArray();
    private int _lastRaidDay, _nextRaid, _raidGroup;
    private bool _wasDusk, _lootWarned;
    /// <summary>A raid is under way (raiders still standing).</summary>
    public bool RaidActive => _raidGroup != 0 && Enemies.Values.Any(e => e.Group == _raidGroup && e.Current != Enemy.State.Dead);
    /// <summary>Server: raids so far (tests, status).</summary>
    public int RaidCount { get; private set; }
    private readonly Dictionary<int, Vector3> _anchors = new();
    private readonly RandomNumberGenerator _rng = new();
    private int _nextId = 1, _nextPack = 1, _packsThisNight;
    private float _packTimer, _sendTimer, _campTimer;
    private bool _wasNight;

    public static bool IsNight(float t) => t >= 0.78f || t < 0.22f;
    /// <summary>Full daylight: wolves give up and slink off.</summary>
    public static bool IsDay(float t) => t >= 0.24f && t < 0.76f;

    public static Vector3 ClampTo(Vector3 p, Rect2 r) =>
        new(Mathf.Clamp(p.X, r.Position.X, r.End.X), Mathf.Max(p.Y, -1f), Mathf.Clamp(p.Z, r.Position.Y, r.End.Y));

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ================================================================== server: spawning

    public override void _Process(double delta)
    {
        var net = Net.Instance;
        if (!net.InSession || !net.IsServer) return;
        float dt = (float)delta;
        RunSpawner(dt);
        DriftAnchors(dt);

        _campTimer -= dt;
        if (_campTimer <= 0)
        {
            _campTimer = 1f;
            for (int i = 0; i < _camps.Length; i++) TendCamp(i);
            for (int i = 0; i < _roosts.Length; i++) TendRoost(i);
            TendWest();
        }

        // Raids come at dusk.
        float t = GameState.Instance.TimeOfDay;
        bool dusk = t >= 0.70f && t < 0.78f;
        if (dusk && !_wasDusk) MaybeRaid();
        _wasDusk = dusk;

        _sendTimer -= dt;
        if (_sendTimer <= 0 && Enemies.Count > 0)
        {
            _sendTimer = SendInterval;
            SendStates();
        }
    }

    private void RunSpawner(float dt)
    {
        var gs = GameState.Instance;
        bool night = IsNight(gs.TimeOfDay);
        if (night && !_wasNight)
        {
            _packsThisNight = 0;
            _packTimer = FirstPackDelay;
            // The first night is quiet: the wolves only watch, so a new player has time to
            // learn the fire keeps them off. (Day only changes at midnight, so this is the
            // night that starts on day 1.)
            _quietNight = gs.Day <= QuietNights;
            if (_quietNight && AutoSpawn && Net.Instance.Config.Enemies)
                Net.Instance.ToastAll("Eyes glint at the edge of the plateau. Tomorrow night the wolves will hunt: stay near a fire after dark.");
        }
        if (!night && _wasNight) RepairBuildings();
        _wasNight = night;
        if (!AutoSpawn || !Net.Instance.Config.Enemies || gs.Players.Count == 0) return;
        if (!night || _quietNight || _packsThisNight >= PacksPerNight) return;

        _packTimer -= dt;
        if (_packTimer > 0) return;
        _packTimer = PackInterval;
        _packsThisNight++;
        SpawnPack(PickSpawnPoint(), Difficulty.PackSize(SoloPackSize));
        Net.Instance.ToastAll("Wolves howl somewhere on the plateau…");
    }

    /// <summary>A spawn point no player is close to (so wolves never pop in on someone).</summary>
    private Vector3 PickSpawnPoint()
    {
        var players = GameState.Instance.Players.Values.ToList();
        var ok = SpawnPoints.Where(p => players.All(pl => pl.GlobalPosition.DistanceTo(p) > 28f)).ToList();
        var pool = ok.Count > 0 ? ok : SpawnPoints.ToList();
        return pool[_rng.RandiRange(0, pool.Count - 1)];
    }

    /// <summary>Server: spawn a wolf pack around a point. Returns the wolves.</summary>
    public List<Wolf> SpawnPack(Vector3 center, int count)
    {
        int pack = _nextPack++;
        _anchors[pack] = center;
        var wolves = new List<Wolf>();
        for (int i = 0; i < count; i++)
            wolves.Add((Wolf)SpawnAround("wolf", pack, center, i, count, Wolf.MaxHealth, Wolf.BiteDamage));
        GD.Print($"[Wildlife] Pack {pack}: {count} wolves at {center} ({GameState.Instance.Players.Count} players)");
        return wolves;
    }

    private Enemy SpawnAround(string kind, int group, Vector3 center, int i, int count, float hp, float damage, float radius = -1f)
    {
        float a = Mathf.Tau * i / Mathf.Max(1, count) + _rng.RandfRange(-0.3f, 0.3f);
        float r = radius >= 0 ? radius : _rng.RandfRange(1.5f, 3f);
        var pos = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r + Vector3.Up * 0.2f;
        return Spawn(kind, group, pos, _rng.RandfRange(0, Mathf.Tau), hp, damage);
    }

    /// <summary>Server: create one enemy here and on every client.</summary>
    public Enemy Spawn(string kind, int group, Vector3 pos, float yaw, float hp, float damage)
    {
        int id = _nextId++;
        var e = SpawnLocal(kind, id, group, pos, yaw, hp, damage);
        foreach (var peer in Net.Instance.JoinedPeers) RpcId(peer, MethodName.C_Spawn, kind, id, group, pos, yaw, hp, damage);
        return e;
    }

    private Enemy SpawnLocal(string kind, int id, int group, Vector3 pos, float yaw, float hp, float damage)
    {
        var e = Enemy.Create(kind, id, group, pos, yaw, hp, damage);
        if (e == null) { GD.PushError($"[Wildlife] Unknown enemy kind {kind}"); return null; }
        AddChild(e);
        Enemies[id] = e;
        _nextId = Mathf.Max(_nextId, id + 1);
        return e;
    }

    /// <summary>Server: remove an enemy everywhere (deferred: it may be mid-physics).</summary>
    public void Despawn(Enemy e) => CallDeferred(MethodName.DespawnNow, e.NetId);

    private void DespawnNow(int id)
    {
        if (!Enemies.TryGetValue(id, out var e)) return;
        Enemies.Remove(id);
        RemoveChild(e);
        e.QueueFree();
        foreach (var peer in Net.Instance.JoinedPeers) RpcId(peer, MethodName.C_Despawn, id);
        if ((e.Group < CampGroupBase || (e.Group >= RaidGroupBase && !IsWestGroup(e.Group))) && Enemies.Values.All(o => o.Group != e.Group)) _anchors.Remove(e.Group);
        if (e is Bandit { Raider: true } r && r.CarriedCount > 0 && r.Current != Enemy.State.Dead)
            Net.Instance.ToastAll($"A raider got away with {r.CarriedCount} items from the stockpile.");
    }

    private void DriftAnchors(float dt)
    {
        var home = GameState.Instance.ClaimCenter;
        foreach (var pack in _anchors.Keys.ToList())
        {
            var a = _anchors[pack];
            var to = new Vector3(home.X - a.X, 0, home.Z - a.Z);
            if (to.Length() > 12f) _anchors[pack] = a + to.Normalized() * AnchorDrift * dt;
        }
    }

    public Vector3 GroupAnchor(int group, Vector3 fallback)
    {
        int camp = group - CampGroupBase;
        if (camp >= 0 && camp < _camps.Length) return _camps[camp].Center;
        int roost = group - RoostGroupBase;
        if (roost >= 0 && roost < _roosts.Length) return _roosts[roost].Center;
        if (WestAnchor(group, out var west)) return west;
        return _anchors.TryGetValue(group, out var a) ? a : fallback;
    }

    // ================================================================== server: bandit camps

    public static int CampGroup(int camp) => CampGroupBase + camp;
    public bool CampCleared(int camp) => _camps[camp].Cleared;

    /// <summary>Server: man an empty camp (first visit, or its respawn day came).</summary>
    private void TendCamp(int i)
    {
        var camp = _camps[i];
        var gs = GameState.Instance;
        if (!AutoSpawn || !Net.Instance.Config.Enemies || gs.Players.Count == 0) return;
        if (Enemies.Values.Any(e => e.Group == CampGroup(i))) return;
        if (camp.Cleared && gs.Day < camp.RespawnDay) return;
        // Not while someone is standing in it.
        if (gs.Players.Values.Any(p => p.GlobalPosition.DistanceTo(camp.Center) < 30f)) return;
        SpawnCamp(i);
    }

    /// <summary>Server: fill a camp with bandits (grunts scale in number, the captain in strength).</summary>
    public List<Bandit> SpawnCamp(int i)
    {
        var camp = _camps[i];
        camp.Cleared = false;
        int grunts = Difficulty.PackSize(SoloCampSize);
        var list = new List<Bandit>();
        for (int n = 0; n < grunts; n++)
            list.Add((Bandit)SpawnAround("bandit", CampGroup(i), camp.Center, n, grunts, Bandit.GruntHealth, Bandit.GruntDamage, 5f));
        list.Add((Bandit)Spawn("captain", CampGroup(i), camp.Center + new Vector3(0, 0.2f, 2f), 0f,
            Difficulty.BigMonsterHealth(Bandit.CaptainHealth), Difficulty.BigMonsterDamage(Bandit.CaptainDamage)));
        GD.Print($"[Wildlife] Bandit camp {i}: {grunts} bandits + captain ({GameState.Instance.Players.Count} players)");
        return list;
    }

    private void CheckCamp(int camp)
    {
        if (camp < 0 || camp >= _camps.Length) return;
        if (Enemies.Values.Any(e => e.Group == CampGroup(camp) && e.Current != Enemy.State.Dead)) return;
        _camps[camp].Cleared = true;
        _camps[camp].RespawnDay = GameState.Instance.Day + CampRespawnDays;
        Net.Instance.ToastAll("The bandit camp is cleared! The Ashby road is safe… for now.");
        Journal.Fact("camp_cleared");
    }

    // ================================================================== server: harpy roosts

    public static int RoostGroup(int roost) => RoostGroupBase + roost;
    public bool RoostCleared(int roost) => _roosts[roost].Cleared;

    private void TendRoost(int i)
    {
        var roost = _roosts[i];
        var gs = GameState.Instance;
        if (!AutoSpawn || !Net.Instance.Config.Enemies || gs.Players.Count == 0) return;
        if (Enemies.Values.Any(e => e.Group == RoostGroup(i))) return;
        if (roost.Cleared && gs.Day < roost.RespawnDay) return;
        if (gs.Players.Values.Any(p => p.GlobalPosition.DistanceTo(roost.Center) < 30f)) return;
        SpawnRoost(i);
    }

    /// <summary>Server: fill a roost with harpies, circling high over the nest.</summary>
    public List<Harpy> SpawnRoost(int i)
    {
        var roost = _roosts[i];
        roost.Cleared = false;
        int count = Difficulty.PackSize(SoloRoostSize);
        var list = new List<Harpy>();
        for (int n = 0; n < count; n++)
        {
            float a = Mathf.Tau * n / count;
            var pos = roost.Center + new Vector3(Mathf.Cos(a) * 6f, Harpy.CruiseHeight + n, Mathf.Sin(a) * 6f);
            list.Add((Harpy)Spawn("harpy", RoostGroup(i), pos, a, Harpy.MaxHealth, Harpy.ClawDamage));
        }
        GD.Print($"[Wildlife] Harpy roost {i}: {count} harpies ({GameState.Instance.Players.Count} players)");
        return list;
    }

    private void CheckRoost(int roost)
    {
        if (roost < 0 || roost >= _roosts.Length) return;
        if (Enemies.Values.Any(e => e.Group == RoostGroup(roost) && e.Current != Enemy.State.Dead)) return;
        _roosts[roost].Cleared = true;
        _roosts[roost].RespawnDay = GameState.Instance.Day + RoostRespawnDays;
        Net.Instance.ToastAll("The harpy roost is quiet. For now.");
        Journal.Fact("roost_cleared");
    }

    // ================================================================== server: raids

    private void MaybeRaid()
    {
        var gs = GameState.Instance;
        if (!AutoSpawn || !Net.Instance.Config.Enemies || gs.Players.Count == 0 || RaidActive) return;
        if (gs.Day < RaidFirstDay || gs.PlacedBuildings.Count < RaidMinBuildings) return;
        int since = gs.Day - _lastRaidDay;
        if (since < RaidMinGap) return;
        if (since >= RaidMaxGap || _rng.Randf() < RaidChance) StartRaid();
    }

    /// <summary>Server: bandits march on the base from the vale road (or from <paramref name="from"/>).</summary>
    public List<Bandit> StartRaid(Vector3? from = null)
    {
        var at = from ?? RaidSpawn;
        int group = RaidGroupBase + _nextRaid++;
        _raidGroup = group;
        _anchors[group] = GameState.Instance.ClaimCenter;
        _lastRaidDay = GameState.Instance.Day;
        _lootWarned = false;
        RaidCount++;
        int count = Difficulty.PackSize(SoloRaidSize);
        var list = new List<Bandit>();
        for (int n = 0; n < count; n++)
            list.Add((Bandit)SpawnAround("raider", group, at, n, count, Bandit.RaiderHealth, Bandit.RaiderDamage, 2.5f));
        Net.Instance.ToastAll("Raiders! Bandits with torches are marching up the road toward your home!");
        HornLocal();
        foreach (var peer in Net.Instance.JoinedPeers) RpcId(peer, MethodName.C_Horn);
        GD.Print($"[Wildlife] Raid: {count} raiders at {at} ({GameState.Instance.Players.Count} players)");
        return list;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Horn() => HornLocal();

    private void HornLocal()
    {
        if (!Net.Instance.IsDedicated) Sfx.Ui("raid_horn", -2f);
    }

    private void CheckRaid(int group)
    {
        if (group != _raidGroup || RaidActive) return;
        _raidGroup = 0;
        Net.Instance.ToastAll("The raid is beaten off!");
        Net.Instance.ServerAwardXpAll(Levels.RaidBeaten, "raid beaten off");
    }

    /// <summary>Hit points of a placed building against raiders.</summary>
    public static float BuildingHp(string id) => id switch
    {
        "palisade" => 150f,
        "watchtower" => 300f,
        "well" => 140f,
        "storage_chest" => 120f,
        "workbench" => 90f,
        "campfire" => 60f,
        "bedroll" => 40f,
        "wheat_plot" or "turnip_plot" => 50f,
        _ => 80f,
    };

    private Node3D _homeChest;
    private Node3D HomeChest => _homeChest != null && IsInstanceValid(_homeChest)
        ? _homeChest
        : _homeChest = GetTree().CurrentScene?.FindChild("HomeChest", true, false) as Node3D;

    public bool ValidObjective(Node3D n) =>
        n != null && IsInstanceValid(n) && n.IsInsideTree() && (n == HomeChest || GameState.Instance.FindBuilding(n) != null);

    /// <summary>What a raider goes for: the nearest building, chests first (they want the stockpile),
    /// walls last (only when they're in the way; see NearestBuilding).</summary>
    public Node3D PickObjective(Vector3 from)
    {
        var gs = GameState.Instance;
        Node3D best = null;
        float bestScore = float.MaxValue;
        void Consider(Node3D n, float weight)
        {
            if (n == null || !IsInstanceValid(n)) return;
            float s = n.GlobalPosition.DistanceTo(from) * weight;
            if (s < bestScore) { bestScore = s; best = n; }
        }
        Consider(HomeChest, 0.6f);
        foreach (var b in gs.PlacedBuildings)
        {
            if (b.Node == null || !IsInstanceValid(b.Node)) continue;
            if (b.Position.DistanceTo(gs.ClaimCenter) > gs.ClaimRadius + 12f) continue;
            Consider(b.Node, b.Node is StorageChest ? 0.6f : b.Id == "palisade" ? 3f : 1f);
        }
        return best;
    }

    /// <summary>The closest placed building within range (a wall in a raider's way).</summary>
    public Node3D NearestBuilding(Vector3 from, float range)
    {
        Node3D best = null;
        float bestD = range;
        foreach (var b in GameState.Instance.PlacedBuildings)
        {
            if (b.Node == null || !IsInstanceValid(b.Node)) continue;
            float d = new Vector2(b.Position.X - from.X, b.Position.Z - from.Z).Length() - ObjectiveRadius(b.Node) * 0.5f;
            if (d < bestD) { bestD = d; best = b.Node; }
        }
        return best;
    }

    /// <summary>How far from its center a building can be struck (half its footprint).</summary>
    public float ObjectiveRadius(Node3D n)
    {
        if (n == null) return 0f;
        var b = GameState.Instance.FindBuilding(n);
        if (b == null) return 0.7f; // the home chest
        var f = ItemDb.GetBuilding(b.Id)?.Footprint ?? Vector3.One;
        return Mathf.Max(f.X, f.Z) / 2f;
    }

    /// <summary>Server: a raider's blow lands on a building or chest. Chests are looted; buildings
    /// take damage and are wrecked at zero (no refund). Repaired at dawn.</summary>
    public void RaidStrike(Bandit raider, Node3D target)
    {
        var gs = GameState.Instance;
        if (target is StorageChest)
        {
            var stacks = gs.Inventory.Items.Where(kv => kv.Value > 0 && kv.Key != "cottage_blueprint" && kv.Key != "bellows_plans").Select(kv => kv.Key).ToList();
            if (stacks.Count > 0)
            {
                var id = stacks[_rng.RandiRange(0, stacks.Count - 1)];
                int take = Mathf.Min(id == "coin" ? StealPerStrike * 3 : StealPerStrike, gs.Inventory.Count(id));
                gs.Inventory.Remove(id, take);
                raider.Carried[id] = (raider.Carried.TryGetValue(id, out var have) ? have : 0) + take;
                if (!_lootWarned)
                {
                    _lootWarned = true;
                    Net.Instance.ToastAll("Raiders are looting the stockpile! Kill them to get it back.");
                }
            }
        }
        var b = gs.FindBuilding(target);
        ThudLocal(target.GlobalPosition, b?.NetId ?? 0);
        foreach (var peer in Net.Instance.JoinedPeers) RpcId(peer, MethodName.C_Thud, target.GlobalPosition, b?.NetId ?? 0);
        if (b == null) return;
        b.Damage += raider.Damage;
        if (b.Damage >= BuildingHp(b.Id))
        {
            Net.Instance.ServerRemoveBuilding(b, refund: false);
            Net.Instance.ToastAll($"Raiders wrecked your {ItemDb.GetBuilding(b.Id)?.DisplayName ?? b.Id}!");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Thud(Vector3 at, int buildingId) => ThudLocal(at, buildingId);

    /// <summary>Everyone: the sound and shudder of a blow on a building.</summary>
    private void ThudLocal(Vector3 at, int buildingId)
    {
        if (Net.Instance.IsDedicated) return;
        Sfx.At("chop", at + Vector3.Up, 2f, 0.1f);
        Fx.Sparks(GetTree().CurrentScene, at + Vector3.Up * 0.8f, new Color(0.6f, 0.45f, 0.3f), 12, 2.5f, 0.3f, 0.6f);
        var node = buildingId != 0 ? GameState.Instance.FindBuilding(buildingId)?.Node : null;
        if (node == null || !IsInstanceValid(node)) return;
        var rest = node.Position;
        var t = node.CreateTween();
        t.TweenProperty(node, "position", rest + new Vector3(0.08f, 0, 0.05f), 0.04);
        t.TweenProperty(node, "position", rest - new Vector3(0.06f, 0, 0.04f), 0.05);
        t.TweenProperty(node, "position", rest, 0.06);
    }

    /// <summary>Server, at dawn: everyone patches up the base.</summary>
    private void RepairBuildings()
    {
        bool any = false;
        foreach (var b in GameState.Instance.PlacedBuildings)
            if (b.Damage > 0) { b.Damage = 0; any = true; }
        if (any) Net.Instance.ToastAll("At first light you patch up the damage to the base.");
    }

    public GDict SaveRaids() => new() { ["last_day"] = _lastRaidDay };

    public void LoadRaids(GDict d) => _lastRaidDay = d != null && d.TryGetValue("last_day", out var v) ? v.AsInt32() : 0;

    public GArray SaveRoosts()
    {
        var list = new GArray();
        foreach (var c in _roosts) list.Add(new GDict { ["cleared"] = c.Cleared, ["respawn_day"] = c.RespawnDay });
        return list;
    }

    public void LoadRoosts(GArray list)
    {
        for (int i = 0; i < _roosts.Length; i++)
        {
            var d = list != null && i < list.Count ? list[i].AsGodotDictionary() : null;
            _roosts[i].Cleared = d != null && d["cleared"].AsBool();
            _roosts[i].RespawnDay = d != null ? d["respawn_day"].AsInt32() : 0;
        }
    }

    public GArray SaveCamps()
    {
        var list = new GArray();
        foreach (var c in _camps) list.Add(new GDict { ["cleared"] = c.Cleared, ["respawn_day"] = c.RespawnDay });
        return list;
    }

    public void LoadCamps(GArray list)
    {
        for (int i = 0; i < _camps.Length; i++)
        {
            var d = list != null && i < list.Count ? list[i].AsGodotDictionary() : null;
            _camps[i].Cleared = d != null && d["cleared"].AsBool();
            _camps[i].RespawnDay = d != null ? d["respawn_day"].AsInt32() : 0;
        }
    }

    // ================================================================== server: behaviour

    public PlayerController FindTarget(Enemy e, float range) =>
        GameState.Instance.Players.Values
            .Where(p => e.CanHunt(p) && p.GlobalPosition.DistanceTo(e.GlobalPosition) < range)
            .OrderBy(p => p.GlobalPosition.DistanceSquaredTo(e.GlobalPosition))
            .FirstOrDefault();

    public PlayerController NearestPlayer(Vector3 pos, out float distance)
    {
        PlayerController best = null;
        distance = float.MaxValue;
        foreach (var p in GameState.Instance.Players.Values)
        {
            float d = p.GlobalPosition.DistanceTo(pos);
            if (d < distance) { distance = d; best = p; }
        }
        return best;
    }

    /// <summary>One member found prey: groupmates within earshot join in.</summary>
    public void AlertGroup(Enemy from, PlayerController target)
    {
        foreach (var e in Enemies.Values)
            if (e != from && e.Group == from.Group && e.GlobalPosition.DistanceTo(from.GlobalPosition) < 25f)
                e.Hunt(target);
    }

    /// <summary>Keep enemies from stacking on one spot.</summary>
    public Vector3 Separation(Enemy e)
    {
        var push = Vector3.Zero;
        foreach (var o in Enemies.Values)
        {
            if (o == e || o.Current == Enemy.State.Dead) continue;
            var off = e.GlobalPosition - o.GlobalPosition;
            off.Y = 0;
            float d = off.Length();
            if (d > 0.01f && d < 1.4f) push += off / d * (1.4f - d);
        }
        return push;
    }

    /// <summary>Server: an attack reaches a player (bite, sword). Blocks and parries apply.</summary>
    public void Strike(Enemy e, PlayerController p)
    {
        Bites++;
        if (p.Sleeping)
        {
            Net.Instance.SetSleeping(p, false);
            Net.Instance.ToastTo(p.PeerId, "Attacked! You were dragged out of your sleep.");
        }
        var result = Net.Instance.ServerDamagePlayer(p, e.Damage, e);
        if (result == Net.HitResult.Parried)
        {
            Parries++;
            e.Stun(e is Bandit { Captain: true } or Crawler { Mother: true } ? 1.2f : 1.6f);
        }
    }

    /// <summary>Server: loot into the shared stockpile; the body lingers a moment.</summary>
    public void OnEnemyKilled(Enemy e, Node source)
    {
        e.ServerDropLoot(source);
        Net.Instance.ServerXpForKill(e);
        if (IsWestGroup(e.Group)) CheckWest(e.Group);
        else if (e.Group >= RaidGroupBase) CheckRaid(e.Group);
        else if (e.Group >= RoostGroupBase) CheckRoost(e.Group - RoostGroupBase);
        else if (e.Group >= CampGroupBase) CheckCamp(e.Group - CampGroupBase);
        GetTree().CreateTimer(e is Bandit ? 5.0 : 3.0).Timeout += () =>
        {
            if (IsInstanceValid(e)) Despawn(e);
        };
    }

    public void BroadcastAnim(Enemy e, int anim)
    {
        PlayAnim(e, anim);
        foreach (var peer in Net.Instance.JoinedPeers) RpcId(peer, MethodName.C_Anim, e.NetId, anim);
    }

    private static void PlayAnim(Enemy e, int anim)
    {
        if (Net.Instance.IsDedicated) return;
        if (anim == AnimAttack) e.PlayAttack();
        else e.PlayStun();
    }

    // ================================================================== replication

    private void SendStates()
    {
        var peers = Net.Instance.JoinedPeers.ToList();
        if (peers.Count == 0) return;
        var data = new float[Enemies.Count * 5];
        int i = 0;
        foreach (var e in Enemies.Values)
        {
            var p = e.GlobalPosition;
            data[i++] = e.NetId;
            data[i++] = p.X;
            data[i++] = p.Y;
            data[i++] = p.Z;
            data[i++] = e.Rotation.Y;
        }
        foreach (var peer in peers) RpcId(peer, MethodName.C_States, data);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Spawn(string kind, int id, int group, Vector3 pos, float yaw, float hp, float damage)
    {
        if (!Enemies.ContainsKey(id)) SpawnLocal(kind, id, group, pos, yaw, hp, damage);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Despawn(int id)
    {
        if (!Enemies.Remove(id, out var e)) return;
        RemoveChild(e);
        e.QueueFree();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 3)]
    private void C_States(float[] data)
    {
        for (int i = 0; i + 4 < data.Length; i += 5)
            if (Enemies.TryGetValue((int)data[i], out var e))
                e.ApplyNetState(new Vector3(data[i + 1], data[i + 2], data[i + 3]), data[i + 4]);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Anim(int id, int anim)
    {
        if (Enemies.TryGetValue(id, out var e)) PlayAnim(e, anim);
    }

    /// <summary>Server: living enemies for a joining player.</summary>
    public GArray Snapshot()
    {
        var list = new GArray();
        foreach (var e in Enemies.Values.Where(e => e.Current != Enemy.State.Dead))
            list.Add(new GArray { e.NetId, e.Kind, e.Group, e.GlobalPosition, e.Rotation.Y, e.Health.Max, e.Damage });
        return list;
    }

    /// <summary>Client: match the server's enemies (join or resync). Health is applied after, by Net.</summary>
    public void ApplySnapshot(GArray list)
    {
        var keep = new HashSet<int>();
        foreach (var v in list)
        {
            var a = v.AsGodotArray();
            int id = a[0].AsInt32();
            keep.Add(id);
            if (!Enemies.ContainsKey(id))
                SpawnLocal(a[1].AsString(), id, a[2].AsInt32(), a[3].AsVector3(), (float)a[4].AsDouble(), (float)a[5].AsDouble(), (float)a[6].AsDouble());
        }
        foreach (var id in Enemies.Keys.Where(id => !keep.Contains(id)).ToList()) C_Despawn(id);
    }
}
