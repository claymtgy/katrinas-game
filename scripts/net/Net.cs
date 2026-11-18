using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

public enum NetMode { Offline, Host, Client, Dedicated }

/// <summary>Autoload: the multiplayer session and every gameplay RPC (GDD §16, §15).
///
/// Authority: the server (host, dedicated, or the offline "server of one") owns shared state:
/// the stockpile, buildings, time, every Health value, sleeping and saving. A client owns only its
/// own player's movement and camera. Gameplay code calls the Request* methods; on the server they
/// run immediately, on a client they're sent to the server. S_* methods run on the server,
/// C_* methods run on clients.</summary>
public partial class Net : Node
{
    public const int ProtocolVersion = 9;
    public const string MainScenePath = "res://scenes/Main.tscn";
    public const string MenuScenePath = "res://scenes/ui/MainMenu.tscn";
    private const float StateSendInterval = 1f / 30f;
    private const float TimeSyncInterval = 2f;
    private const float HandshakeTimeout = 30f;
    /// <summary>How far (m) the server lets a player reach when using things.</summary>
    private const float ServerReach = 6f;
    /// <summary>A block raised this soon before a blow lands is a parry (no damage, attacker staggered).</summary>
    public const float ParryWindow = 0.3f;
    /// <summary>Damage that gets through a (non-parry) block.</summary>
    public const float BlockedDamageFactor = 0.2f;

    /// <summary>How long a dodge roll can't be hit (s).</summary>
    public const float DodgeIFrames = 0.32f;

    public enum HitResult { Hit, Blocked, Parried, Dodged }

    /// <summary>Server clock for block timing (seconds).</summary>
    public static double Now => Time.GetTicksMsec() / 1000.0;

    public static Net Instance { get; private set; }

    public NetMode Mode { get; private set; } = NetMode.Offline;
    public ServerConfig Config { get; private set; } = new();
    public string LocalName { get; set; } = "Player";
    /// <summary>Why the last session ended (shown by the main menu).</summary>
    public string LastError { get; set; } = "";

    public bool IsServer => Mode != NetMode.Client;
    public bool IsDedicated => Mode == NetMode.Dedicated;
    public bool IsMultiplayer => Mode != NetMode.Offline;
    public bool InSession { get; private set; }
    public int LocalId => Multiplayer.GetUniqueId();
    /// <summary>Server: clients that are in the world (safe to send world RPCs to).</summary>
    public IEnumerable<int> JoinedPeers => _joined;
    public bool Pvp => Config.Pvp;

    /// <summary>Sleep vote, for the HUD.</summary>
    public int SleepingCount { get; private set; }
    public int SleepTotal { get; private set; }

    /// <summary>Server: the clock stops on an empty dedicated server (pause_when_empty).</summary>
    public bool ClockPaused => IsDedicated && Config.PauseWhenEmpty && GameState.Instance.Players.Count == 0;

    // Server: clients that finished the handshake and have the world loaded.
    private readonly HashSet<int> _joined = new();
    private string _joinPassword = "";
    private float _stateTimer, _timeSyncTimer, _autosaveTimer;
    private bool _inventoryDirty;
    private readonly HashSet<int> _packDirty = new();
    private bool _leaving;

    private static GameState Gs => GameState.Instance;

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += () => EndSession("Could not connect to the server.");
        Multiplayer.ServerDisconnected += () => EndSession("Lost connection to the server.");
        Gs.Inventory.Changed += () => _inventoryDirty = true;
    }

    // ================================================================== starting / leaving

    public void StartOffline(string world)
    {
        ResetPeer();
        Mode = NetMode.Offline;
        Config = new ServerConfig { World = world, MaxPlayers = 1 };
        Config.Clamp();
        LoadMain();
    }

    /// <summary>Listen server: this machine hosts and plays.</summary>
    public Error StartHost(ServerConfig cfg) => StartServer(cfg, NetMode.Host, Mathf.Max(1, cfg.MaxPlayers - 1));

    /// <summary>Headless dedicated server with no local player. Type status / save / say / quit in its console.</summary>
    public Error StartDedicated(ServerConfig cfg)
    {
        var err = StartServer(cfg, NetMode.Dedicated, cfg.MaxPlayers);
        if (err == Error.Ok) StartConsole();
        return err;
    }

    // ---------------------------------------------------------------- dedicated server console

    private void StartConsole()
    {
        var thread = new System.Threading.Thread(() =>
        {
            while (true)
            {
                string line;
                try { line = System.Console.ReadLine(); }
                catch { return; }
                if (line == null) return; // no console attached
                CallDeferred(MethodName.RunConsoleCommand, line);
            }
        }) { IsBackground = true, Name = "ServerConsole" };
        thread.Start();
        GD.Print("[Server] Console commands: status, save, say <message>, wolves, quit");
    }

    private void RunConsoleCommand(string line)
    {
        line = line.StripEdges();
        string cmd = line.Split(' ')[0].ToLowerInvariant();
        switch (cmd)
        {
            case "": return;
            case "status":
                GD.Print($"[Server] '{Config.Name}' port {Config.Port} · day {Gs.Day} {Gs.TimeOfDay * 24f:00.0}h" +
                         $"{(ClockPaused ? " (paused)" : "")} · {Wildlife.Instance?.Wolves.Count ?? 0} wolves, " +
                         $"{Wildlife.Instance?.Bandits.Count() ?? 0} bandits · " +
                         $"{Gs.Players.Count}/{Config.MaxPlayers} players: " +
                         string.Join(", ", Gs.Players.Values.Select(p => p.DisplayName)));
                break;
            case "wolves":
                if (Gs.Players.Count == 0) { GD.Print("[Server] Nobody is here to hunt."); break; }
                var prey = Gs.Players.Values.First();
                Wildlife.Instance?.SpawnPack(prey.GlobalPosition + new Vector3(14, 0, 0), Difficulty.PackSize(Wildlife.SoloPackSize));
                ToastAll("Wolves howl nearby…");
                break;
            case "save": Gs.SaveGame(silent: true); break;
            case "say": ToastAll($"[Server] {line[3..].StripEdges()}"); break;
            case "quit": case "stop": case "exit":
                Gs.SaveGame(silent: true);
                GD.Print("[Server] Shutting down");
                GetTree().Quit();
                break;
            default: GD.Print($"[Server] Unknown command '{cmd}'. Try: status, save, say <message>, wolves, quit"); break;
        }
    }

    private Error StartServer(ServerConfig cfg, NetMode mode, int maxClients)
    {
        ResetPeer();
        cfg.Clamp();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(cfg.Port, maxClients);
        if (err != Error.Ok)
        {
            LastError = $"Could not open port {cfg.Port} ({err}). Is another server running?";
            GD.PushError("[Net] " + LastError);
            return err;
        }
        Multiplayer.MultiplayerPeer = peer;
        Mode = mode;
        Config = cfg;
        GD.Print($"[Net] {(mode == NetMode.Dedicated ? "Dedicated server" : "Hosting")} '{cfg.Name}' on port {cfg.Port}, " +
                 $"max {cfg.MaxPlayers} players, PVP {(cfg.Pvp ? "on" : "off")}, world '{cfg.World}'");
        LoadMain();
        return Error.Ok;
    }

    public Error Join(string address, int port, string password)
    {
        ResetPeer();
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(address, port);
        if (err != Error.Ok)
        {
            LastError = $"Could not connect to {address}:{port} ({err}).";
            return err;
        }
        Multiplayer.MultiplayerPeer = peer;
        Mode = NetMode.Client;
        _joinPassword = password ?? "";
        GD.Print($"[Net] Connecting to {address}:{port}...");
        return Error.Ok; // OnConnectedToServer loads the world.
    }

    /// <summary>Back to the main menu. The host saves first; clients just disconnect.</summary>
    public void Leave() => EndSession("");

    private void EndSession(string reason)
    {
        if (_leaving) return;
        _leaving = true;
        if (InSession && IsServer) Gs.SaveGame(silent: true);
        if (reason != "") GD.Print($"[Net] Session ended: {reason}");
        LastError = reason;
        ResetPeer();
        Mode = NetMode.Offline;
        Gs.ResetSession();
        if (OS.GetCmdlineUserArgs().Contains("--server")) { GetTree().Quit(); return; }
        GetTree().ChangeSceneToFile(MenuScenePath);
        _leaving = false;
    }

    private void ResetPeer()
    {
        InSession = false;
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet) enet.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        _joined.Clear();
        SleepingCount = SleepTotal = 0;
    }

    private void LoadMain() => GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MainScenePath);

    /// <summary>Called by Main once the world scene is built.</summary>
    public void OnWorldReady()
    {
        InSession = true;
        if (IsServer)
        {
            Gs.LoadGame(silent: true, resync: false);
            if (!IsDedicated) ServerAddPlayer(LocalId, LocalName, Appearance.Local);
            _autosaveTimer = Config.AutosaveMinutes * 60f;
            UpdateSleep();
        }
        else
        {
            RpcId(1, MethodName.S_Hello, LocalName, _joinPassword, ProtocolVersion, Appearance.Local.ToDict());
        }
    }

    // ================================================================== connection events

    private void OnConnectedToServer()
    {
        GD.Print("[Net] Connected; loading the world");
        LoadMain();
    }

    private void OnPeerConnected(long id)
    {
        if (!IsServer) return;
        GD.Print($"[Net] Peer {id} connected, waiting for handshake");
        GetTree().CreateTimer(HandshakeTimeout).Timeout += () =>
        {
            if (IsServer && !_joined.Contains((int)id) && Multiplayer.GetPeers().Contains((int)id))
                Multiplayer.MultiplayerPeer?.DisconnectPeer((int)id);
        };
    }

    private void OnPeerDisconnected(long id)
    {
        if (!IsServer) return;
        int peer = (int)id;
        if (!_joined.Remove(peer)) return;
        var p = Gs.GetPlayer(peer);
        string name = p?.DisplayName ?? $"Player {peer}";
        ServerRemovePlayer(peer);
        ToastAll($"{name} left the game");
        if (Gs.Players.Count == 0 || (IsDedicated && _joined.Count == 0)) Gs.SaveGame(silent: true);
    }

    // ================================================================== handshake

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Hello(string name, string password, int version, GDict look)
    {
        if (!IsServer) return;
        int id = Sender;
        if (version != ProtocolVersion) { Reject(id, "The server is running a different version of the game."); return; }
        if (Config.Password != "" && password != Config.Password) { Reject(id, "Wrong server password."); return; }
        if (Gs.Players.Count >= Config.MaxPlayers) { Reject(id, $"The server is full ({Config.MaxPlayers} players)."); return; }

        name = UniqueName(name);
        _joined.Add(id);
        ServerAddPlayer(id, name, Appearance.FromDict(look));
        RpcId(id, MethodName.C_Welcome, BuildSnapshot());
        var joined = Gs.GetPlayer(id);
        if (joined != null) RpcId(id, MethodName.C_Pack, joined.Pack.ToDictionary());
        ToastAll($"{name} joined the game", except: id);
        UpdateSleep();
    }

    private void Reject(int id, string reason)
    {
        GD.Print($"[Net] Rejected peer {id}: {reason}");
        RpcId(id, MethodName.C_Rejected, reason);
        GetTree().CreateTimer(0.5).Timeout += () => Multiplayer.MultiplayerPeer?.DisconnectPeer(id);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Rejected(string reason) => EndSession(reason);

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Welcome(GDict snapshot)
    {
        ApplySnapshot(snapshot);
        Hud.Toast($"Joined {Config.Name}" + (Pvp ? "  (PVP is on)" : ""));
    }

    private string UniqueName(string name)
    {
        name = (name ?? "").StripEdges();
        if (name.Length > 20) name = name[..20];
        if (name == "") name = "Player";
        var taken = Gs.Players.Values.Select(p => p.DisplayName).ToHashSet();
        string candidate = name;
        for (int i = 2; taken.Contains(candidate); i++) candidate = $"{name} ({i})";
        return candidate;
    }

    // ================================================================== snapshot (late join, reload)

    private GDict BuildSnapshot()
    {
        var buildings = new GArray();
        foreach (var b in Gs.PlacedBuildings)
            buildings.Add(new GArray { b.NetId, b.Id, b.Position, b.RotationY, b.State });

        var players = new GArray();
        foreach (var p in Gs.Players.Values) players.Add(PlayerInfo(p));

        var health = new GArray();
        foreach (var node in GetTree().GetNodesInGroup(Health.Group))
            if (node is Health h && h.Current < h.Max)
                health.Add(new GArray { h.GetPath(), h.Current });

        return new GDict
        {
            ["settings"] = new GDict
            {
                ["name"] = Config.Name, ["max"] = Config.MaxPlayers, ["pvp"] = Config.Pvp, ["world"] = Config.World,
            },
            ["time"] = Gs.TimeOfDay,
            ["day"] = Gs.Day,
            ["inventory"] = Gs.Inventory.ToDictionary(),
            ["buildings"] = buildings,
            ["players"] = players,
            ["health"] = health,
            ["sleep"] = new GArray { SleepingCount, SleepTotal },
            ["enemies"] = Wildlife.Instance?.Snapshot() ?? new GArray(),
            ["home_tier"] = Gs.HomeTier,
            ["rain"] = Gs.Raining,
            ["journal"] = Journal.Instance?.ToDict() ?? new GDict(),
        };
    }

    private static GDict PlayerInfo(PlayerController p) => new()
    {
        ["id"] = p.PeerId, ["name"] = p.DisplayName, ["pos"] = p.GlobalPosition,
        ["equipped"] = p.Equipped, ["sleeping"] = p.Sleeping, ["look"] = p.Look.ToDict(), ["worn"] = Gear.ToDict(p.Worn),
        ["xp"] = p.Xp,
    };

    private void ApplySnapshot(GDict s)
    {
        var settings = s["settings"].AsGodotDictionary();
        Config = new ServerConfig
        {
            Name = settings["name"].AsString(), MaxPlayers = settings["max"].AsInt32(),
            Pvp = settings["pvp"].AsBool(), World = settings["world"].AsString(),
        };
        Gs.TimeOfDay = (float)s["time"].AsDouble();
        Gs.Day = s["day"].AsInt32();
        Gs.Inventory.ReplaceAll(s["inventory"].AsGodotDictionary());

        Gs.ClearBuildings();
        foreach (var v in s["buildings"].AsGodotArray())
        {
            var b = v.AsGodotArray();
            Gs.PlaceBuilding(ItemDb.GetBuilding(b[1].AsString()), b[2].AsVector3(), (float)b[3].AsDouble(), b[0].AsInt32(), b[4]);
        }

        foreach (var v in s["players"].AsGodotArray())
        {
            var info = v.AsGodotDictionary();
            int id = info["id"].AsInt32();
            var look = Appearance.FromDict(info.TryGetValue("look", out var lv) ? lv.AsGodotDictionary() : null);
            var p = Gs.GetPlayer(id);
            if (p == null) p = SpawnPlayerNode(id, info["name"].AsString(), info["pos"].AsVector3(), look);
            else p.SetLook(look);
            p.Equipped = info["equipped"].AsString();
            p.Sleeping = info["sleeping"].AsBool();
            p.SetWorn(Gear.FromDict(info.TryGetValue("worn", out var wv) ? wv.AsGodotDictionary() : null));
            p.SetXp(info.TryGetValue("xp", out var xv) ? xv.AsInt32() : 0);
        }

        Gs.SetHomeTier(s["home_tier"].AsInt32());
        Gs.SetRaining(s.TryGetValue("rain", out var rain) && rain.AsBool());
        Journal.Instance?.Apply(s.TryGetValue("journal", out var journal) ? journal.AsGodotDictionary() : null);
        // Enemies before health, so their Health nodes exist when health is applied.
        if (s.TryGetValue("enemies", out var enemies)) Wildlife.Instance?.ApplySnapshot(enemies.AsGodotArray());

        var damaged = new Dictionary<string, float>();
        foreach (var v in s["health"].AsGodotArray())
        {
            var a = v.AsGodotArray();
            damaged[a[0].AsString()] = (float)a[1].AsDouble();
        }
        foreach (var node in GetTree().GetNodesInGroup(Health.Group))
            if (node is Health h)
                h.ApplySnapshot(damaged.TryGetValue(h.GetPath().ToString(), out var cur) ? cur : h.Max);

        var sleep = s["sleep"].AsGodotArray();
        SleepingCount = sleep[0].AsInt32();
        SleepTotal = sleep[1].AsInt32();
    }

    /// <summary>Server: after F9 load, push the whole world to every client again.</summary>
    public void ResyncAll()
    {
        if (!IsServer) return;
        var snap = BuildSnapshot();
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Resync, snap);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Resync(GDict snapshot) => ApplySnapshot(snapshot);

    // ================================================================== players

    private PlayerController SpawnPlayerNode(int id, string name, Vector3 pos, Appearance look)
    {
        var p = GD.Load<PackedScene>("res://scenes/player/Player.tscn").Instantiate<PlayerController>();
        p.Name = id.ToString();
        p.PeerId = id;
        p.DisplayName = name;
        p.Look = look ?? new Appearance();
        p.SpawnPoint = Gs.WorldSpawn;
        p.Position = pos;
        Gs.PlayersRoot.AddChild(p);
        return p;
    }

    private void ServerAddPlayer(int id, string name, Appearance look)
    {
        var rec = Gs.PlayerRecords.TryGetValue(name, out var r) ? r : null;
        int index = Gs.Players.Count;
        var pos = rec != null ? rec["pos"].AsVector3() : Gs.WorldSpawn + new Vector3(index * 1.2f, 0, index * 0.6f);
        var p = SpawnPlayerNode(id, name, pos, look);
        if (rec != null)
        {
            p.SpawnPoint = rec["spawn"].AsVector3();
            if (rec.TryGetValue("pack", out var pack)) p.Pack.ReplaceAll(pack.AsGodotDictionary());
            if (rec.TryGetValue("worn", out var worn)) p.SetWorn(Gear.FromDict(worn.AsGodotDictionary()));
            GameState.ApplyXpRecord(p, rec);
        }
        p.Pack.Changed += () => _packDirty.Add(id);
        GD.Print($"[Net] {name} (peer {id}) is in the world. {Gs.Players.Count}/{Config.MaxPlayers} players");
        foreach (var peer in _joined)
            if (peer != id) RpcId(peer, MethodName.C_SpawnPlayer, PlayerInfo(p));
    }

    private void ServerRemovePlayer(int id)
    {
        var p = Gs.GetPlayer(id);
        if (p == null) return;
        if (p.Equipped != "") Gs.Inventory.Add(p.Equipped, 1); // held weapon goes back to the stockpile
        Gs.RememberPlayer(p);
        Gs.Players.Remove(id);
        p.QueueFree();
        foreach (var peer in _joined) RpcId(peer, MethodName.C_DespawnPlayer, id);
        UpdateSleep();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_SpawnPlayer(GDict info)
    {
        int id = info["id"].AsInt32();
        if (Gs.GetPlayer(id) != null) return;
        var look = Appearance.FromDict(info.TryGetValue("look", out var lv) ? lv.AsGodotDictionary() : null);
        var p = SpawnPlayerNode(id, info["name"].AsString(), info["pos"].AsVector3(), look);
        p.Equipped = info["equipped"].AsString();
        p.SetWorn(Gear.FromDict(info.TryGetValue("worn", out var wv) ? wv.AsGodotDictionary() : null));
        p.SetXp(info.TryGetValue("xp", out var xv) ? xv.AsInt32() : 0);
        Hud.Toast($"{p.DisplayName} joined the game");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_DespawnPlayer(int id)
    {
        var p = Gs.GetPlayer(id);
        if (p == null) return;
        Gs.Players.Remove(id);
        p.QueueFree();
    }

    // ---------------------------------------------------------------- movement state

    /// <summary>Local player: called every physics frame; sends at 30 Hz.</summary>
    public void SendLocalState(PlayerController p, float delta)
    {
        if (!IsMultiplayer) return;
        _stateTimer -= delta;
        if (_stateTimer > 0) return;
        _stateTimer = StateSendInterval;
        var (yaw, pitch) = p.Rig.Look;
        int mode = (int)p.Mode;
        if (IsServer) RelayState(p.PeerId, p.GlobalPosition, yaw, pitch, p.Velocity, p.IsOnFloor(), mode);
        else RpcId(1, MethodName.S_State, p.GlobalPosition, yaw, pitch, p.Velocity, p.IsOnFloor(), mode);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void S_State(Vector3 pos, float yaw, float pitch, Vector3 vel, bool floor, int mode)
    {
        int id = Sender;
        if (!_joined.Contains(id)) return;
        Gs.GetPlayer(id)?.ApplyRemoteState(pos, yaw, pitch, vel, floor, mode);
        RelayState(id, pos, yaw, pitch, vel, floor, mode);
    }

    private void RelayState(int id, Vector3 pos, float yaw, float pitch, Vector3 vel, bool floor, int mode)
    {
        foreach (var peer in _joined)
            if (peer != id) RpcId(peer, MethodName.C_State, id, pos, yaw, pitch, vel, floor, mode);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void C_State(int id, Vector3 pos, float yaw, float pitch, Vector3 vel, bool floor, int mode) =>
        Gs.GetPlayer(id)?.ApplyRemoteState(pos, yaw, pitch, vel, floor, mode);

    // ================================================================== requests (any machine -> server)

    public void RequestBuild(string buildingId, Vector3 pos, float rotY) => ToServer(MethodName.S_Build, buildingId, pos, rotY);
    public void RequestCraft(string recipeId, Node station) => ToServer(MethodName.S_Craft, recipeId, station.GetPath());
    public void RequestEquip(string itemId) => ToServer(MethodName.S_Equip, itemId ?? "");
    /// <summary>Put on a piece of armour from your pack (it swaps with what's in that slot).</summary>
    public void RequestWear(string itemId) => ToServer(MethodName.S_Wear, itemId ?? "");
    /// <summary>Take off whatever is in a slot (into your pack).</summary>
    public void RequestTakeOff(string slot) => ToServer(MethodName.S_TakeOff, slot ?? "");
    /// <summary>Enchant a piece you carry or wear, at a Rune Altar.</summary>
    public void RequestEnchant(string itemId, string enchant, Node altar) => ToServer(MethodName.S_Enchant, itemId ?? "", enchant ?? "", altar.GetPath());
    public void RequestInteract(Node target) => ToServer(MethodName.S_Interact, target.GetPath());
    /// <summary>Fast travel from the sanctuary you're praying at to another attuned one.</summary>
    public void RequestTravel(BlossomShrine from, BlossomShrine to) => ToServer(MethodName.S_Travel, from.GetPath(), to.Name.ToString());
    public void RequestWake() => ToServer(MethodName.S_Wake);
    public void RequestAttack(bool heavy, Vector3 aim) => ToServer(MethodName.S_Attack, heavy, aim);
    public void ReportFallDamage(float amount) => ToServer(MethodName.S_FallDamage, amount);
    public void ReportFellOut() => ToServer(MethodName.S_FellOut);
    public void RequestBlock(bool on) => ToServer(MethodName.S_Block, on);
    public void RequestTrade(Node trader, string itemId, bool buy) => ToServer(MethodName.S_Trade, trader.GetPath(), itemId, buy);
    public void RequestEat() => ToServer(MethodName.S_Eat);
    public void RequestDodge(Vector3 dir) => ToServer(MethodName.S_Dodge, dir);

    // ---------------------------------------------------------------- appearance

    /// <summary>Local player: change how we look (from the pause menu's character creator).</summary>
    public void RequestLook(Appearance look) => ToServer(MethodName.S_Look, look.ToDict());

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Look(GDict look)
    {
        var p = SenderPlayer();
        if (p == null) return;
        var a = Appearance.FromDict(look); // sanitised
        if (a.Class != p.Look.Class)
        {
            if (!Supplies.AtHome(p))
            {
                a.Class = p.Look.Class;
                ToastTo(p.PeerId, "You can only change class at home.");
            }
            else
            {
                if (p.Vanished) ServerEndVanish(p);
                p.Ability.ServerReadyAt = 0;
                p.Ability2.ServerReadyAt = 0;
                p.CritUntil = 0;
                ToastTo(p.PeerId, $"You are now a {PlayerClasses.Get(a.Class).Name}.");
            }
        }
        if (IsDedicated) p.Look = a; // (no body to rebuild, but later joiners get it)
        else p.SetLook(a);
        var clean = a.ToDict();
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Look, p.PeerId, clean);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Look(int id, GDict look) => Gs.GetPlayer(id)?.SetLook(Appearance.FromDict(look));

    // ---------------------------------------------------------------- dodge roll

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Dodge(Vector3 dir)
    {
        var p = SenderPlayer();
        if (p == null || p.Health.IsDead) return;
        p.DodgeUntil = Now + DodgeIFrames * p.Class.DodgeIFrames;
        foreach (var peer in _joined)
            if (peer != p.PeerId) RpcId(peer, MethodName.C_Dodge, p.PeerId, dir);
        if (!p.IsLocal && !IsDedicated) p.PlayDodge(dir);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Dodge(int id, Vector3 dir) => Gs.GetPlayer(id)?.PlayDodge(dir);

    // ---------------------------------------------------------------- blocking (GDD §9 combat)

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Block(bool on)
    {
        var p = SenderPlayer();
        if (p == null) return;
        p.Blocking = on && !p.Health.IsDead;
        if (p.Blocking) p.BlockStartedAt = Now;
        foreach (var peer in _joined)
            if (peer != p.PeerId) RpcId(peer, MethodName.C_Block, p.PeerId, p.Blocking);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Block(int id, bool on)
    {
        var p = Gs.GetPlayer(id);
        if (p != null) p.Blocking = on;
    }

    /// <summary>Server: every hit on a player goes through here (enemies, PVP). A guard raised
    /// toward the attacker blocks most of it; raised just in time it's a parry: no damage, and
    /// the caller staggers the attacker.</summary>
    public HitResult ServerDamagePlayer(PlayerController p, float amount, Node source)
    {
        var result = HitResult.Hit;
        if (Now < p.DodgeUntil) return HitResult.Dodged; // rolled clear
        float raw = amount;
        amount *= 1f - Gear.Reduction(p); // armour
        if (p.Blocking && source is Node3D s && p.Facing(s.GlobalPosition))
            result = Now - p.BlockStartedAt <= ParryWindow ? HitResult.Parried : HitResult.Blocked;

        switch (result)
        {
            case HitResult.Hit:
                p.Health.TakeDamage(amount, source);
                break;
            case HitResult.Blocked:
                p.Health.TakeDamage(amount * BlockedDamageFactor, source);
                ToPeer(p.PeerId, MethodName.C_Guard, false, (8f + amount * 0.8f) * p.Class.GuardCost);
                break;
            case HitResult.Parried:
                ToPeer(p.PeerId, MethodName.C_Guard, true, 0f);
                break;
        }
        // Thorns: a melee attacker close by takes back a share of the blow.
        if (result != HitResult.Parried && Gear.ThornsShare(p) > 0 && source is Node3D attacker && attacker != p
            && attacker.GlobalPosition.DistanceTo(p.GlobalPosition) < 4.5f && Health.FindOn(attacker) is { IsDead: false } back)
        {
            float thorns = raw * Gear.ThornsShare(p);
            if (attacker is PlayerController) { if (Pvp) back.TakeDamage(thorns, p); }
            else back.TakeDamage(thorns, p);
        }
        if (result != HitResult.Hit)
        {
            if (!IsDedicated) C_GuardFx(p.PeerId, result == HitResult.Parried);
            foreach (var peer in _joined) RpcId(peer, MethodName.C_GuardFx, p.PeerId, result == HitResult.Parried);
        }
        return result;
    }

    /// <summary>The local player's guard took a blow: stamina cost, or a parry.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Guard(bool parried, float staminaCost) => Gs.LocalPlayer?.Combat.OnGuardHit(parried, staminaCost);

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_GuardFx(int id, bool parried) => Gs.GetPlayer(id)?.Combat.PlayGuardImpact(parried);

    /// <summary>Server: this player's swing was parried: they reel for a moment.</summary>
    public void StaggerPlayer(PlayerController p, float seconds) => ToPeer(p.PeerId, MethodName.C_Staggered, seconds);

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Staggered(float seconds) => Gs.LocalPlayer?.Combat.Stagger(seconds);

    // ---------------------------------------------------------------- trading, eating

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Trade(NodePath traderPath, string itemId, bool buy)
    {
        var p = SenderPlayer();
        var trader = GetNodeOrNull<Trader>(traderPath);
        var item = ItemDb.Get(itemId);
        if (p == null || trader == null || item == null) return;
        if (!InReach(p, trader)) { ToastTo(p.PeerId, "Get closer to the trader"); return; }
        if (buy)
        {
            if (item.BuyPrice <= 0) return;
            if (p.Pack.Load >= Supplies.PackCapacity) { ToastTo(p.PeerId, "Your pack is full"); return; }
            if (!p.Pack.Remove("coin", item.BuyPrice)) { ToastTo(p.PeerId, $"Not enough coins in your pack ({item.BuyPrice} needed)"); return; }
            p.Pack.Add(itemId, 1);
            Journal.Fact("traded");
            ToastTo(p.PeerId, $"Bought {item.DisplayName} for {item.BuyPrice} coins");
        }
        else
        {
            if (item.SellPrice <= 0) return;
            if (!p.Pack.Remove(itemId, 1)) { ToastTo(p.PeerId, $"No {item.DisplayName} in your pack"); return; }
            p.Pack.Add("coin", item.SellPrice);
            Journal.Fact("traded");
            ToastTo(p.PeerId, $"Sold {item.DisplayName} for {item.SellPrice} coins");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Eat()
    {
        var p = SenderPlayer();
        if (p == null || p.Health.IsDead) return;
        var foods = ItemDb.Items.Values.Where(d => d.IsFood && Supplies.Count(p, d.Id) > 0).ToList();
        if (foods.Count == 0)
        {
            ToastTo(p.PeerId, Supplies.AtHome(p) ? "No food in your pack or the stockpile. Cook at a campfire." : "No food in your pack.");
            return;
        }
        float missing = p.Health.Max - p.Health.Current;
        ItemDef food;
        if (missing > 0.5f)
            // Hurt: the smallest meal that heals it all, else the biggest.
            food = foods.Where(d => d.FoodHeal >= missing).OrderBy(d => d.FoodHeal).FirstOrDefault()
                   ?? foods.OrderByDescending(d => d.FoodHeal).First();
        else
        {
            // Healthy: only worth eating for a buff you don't have yet.
            food = foods.Where(d => d.HasBuff && p.BuffAmount(d.BuffKind) <= 0f).OrderBy(d => d.SellPrice).FirstOrDefault();
            if (food == null) { ToastTo(p.PeerId, "You're not hungry (full health, already well fed)"); return; }
        }
        if (!Supplies.TryConsume(p, food.Id)) return;
        if (food.HasBuff) ServerBuff(p, food.BuffKind, food.BuffAmount, food.BuffSeconds);
        if (food.FoodHeal > 0) ServerRestore(p, food.FoodHeal * p.Class.FoodHealing);
        ToastTo(p.PeerId, $"Ate {food.DisplayName}" + (food.FoodHeal > 0 ? $" (+{food.FoodHeal * p.Class.FoodHealing:0} health)" : "") +
                          (food.HasBuff ? $". {food.BuffText} for {food.BuffSeconds / 60f:0} min" : ""));
    }

    // ---------------------------------------------------------------- food buffs

    /// <summary>Server: start a timed buff on a player, on every machine.</summary>
    public void ServerBuff(PlayerController p, string kind, float amount, float seconds)
    {
        p.ApplyBuff(kind, amount, seconds);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Buff, p.PeerId, kind, amount, seconds);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Buff(int id, string kind, float amount, float seconds) => Gs.GetPlayer(id)?.ApplyBuff(kind, amount, seconds);

    private void ServerClearBuffs(PlayerController p)
    {
        p.ClearBuffs();
        foreach (var peer in _joined) RpcId(peer, MethodName.C_ClearBuffs, p.PeerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_ClearBuffs(int id) => Gs.GetPlayer(id)?.ClearBuffs();

    // ---------------------------------------------------------------- packs

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Pack(GDict items) => Gs.LocalPlayer?.Pack.ReplaceAll(items);

    // ---------------------------------------------------------------- building state, home

    /// <summary>Server: a placed building's state changed (crop planted / ripe...). Tell everyone.</summary>
    public void BroadcastBuildingState(GameState.PlacedBuilding b)
    {
        foreach (var peer in _joined) RpcId(peer, MethodName.C_BuildingState, b.NetId, b.State);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_BuildingState(int netId, Variant state) => Gs.FindBuilding(netId)?.ApplyState(state);

    /// <summary>Server: take a placed building down everywhere (refund = its cost back to the stockpile).</summary>
    public void ServerRemoveBuilding(GameState.PlacedBuilding b, bool refund)
    {
        if (refund)
            foreach (var kv in ItemDb.GetBuilding(b.Id)?.GetCost() ?? new()) Gs.Inventory.Add(kv.Key, kv.Value);
        Gs.RemoveBuilding(b.NetId);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_RemoveBuilding, b.NetId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_RemoveBuilding(int netId) => Gs.RemoveBuilding(netId);

    /// <summary>Server: the home was upgraded (GDD §6 tiers).</summary>
    public void ServerSetHomeTier(int tier)
    {
        Gs.SetHomeTier(tier);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_HomeTier, tier);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_HomeTier(int tier) => Gs.SetHomeTier(tier);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Build(string buildingId, Vector3 pos, float rotY)
    {
        var p = SenderPlayer();
        var def = ItemDb.GetBuilding(buildingId);
        if (p == null || def == null) return;
        string reason = p.GlobalPosition.DistanceTo(pos) > p.Build.MaxPlaceDistance + 6f
            ? "Too far away"
            : BuildSystem.CheckPlacement(p.GetWorld3D().DirectSpaceState, def, pos, rotY);
        if (reason != "" || !Supplies.TryConsume(p, def.GetCost()))
        {
            ToastTo(p.PeerId, $"Can't build: {(reason == "" ? "Not enough materials" : reason)}");
            return;
        }
        var b = Gs.PlaceBuilding(def, pos, rotY);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_SpawnBuilding, b.NetId, def.Id, pos, rotY);
        ToastTo(p.PeerId, $"Built {def.DisplayName}");
        if (IsMultiplayer) ToastAll($"{p.DisplayName} built a {def.DisplayName}", except: p.PeerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_SpawnBuilding(int netId, string buildingId, Vector3 pos, float rotY) =>
        Gs.PlaceBuilding(ItemDb.GetBuilding(buildingId), pos, rotY, netId);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Craft(string recipeId, NodePath stationPath)
    {
        var p = SenderPlayer();
        var recipe = ItemDb.Recipes.FirstOrDefault(r => r.Id == recipeId);
        var station = GetNodeOrNull<CraftingStation>(stationPath);
        if (p == null || recipe == null || station == null || station.StationId != recipe.Station) return;
        if (!InReach(p, station)) { ToastTo(p.PeerId, $"Get closer to the {station.StationName}"); return; }
        if (!Supplies.TryConsume(p, recipe.GetInputs())) { ToastTo(p.PeerId, "Not enough materials in your pack or the stockpile"); return; }

        var item = ItemDb.Get(recipe.OutputItem);
        int count = recipe.OutputCount;
        Journal.Fact($"crafted:{recipe.OutputItem}");
        ServerXpForCraft(p, recipe.OutputItem);
        if (item is { IsWeapon: true }) Journal.Fact("crafted_weapon");
        if (item != null && item.IsWeapon)
        {
            // A fresh weapon goes straight into the crafter's hand.
            if (p.Equipped != "") Gs.Inventory.Add(p.Equipped, 1);
            SetEquipped(p, item.Id);
            count--;
        }
        if (item != null && item.IsPersonal)
        {
            // Gear (the glider, armour) is personal: it goes into the crafter's pack.
            Supplies.Give(p, item.Id, count);
            ToastTo(p.PeerId, $"Crafted: {recipe.DisplayName}. It's in your pack.");
            return;
        }
        if (count > 0) Gs.Inventory.Add(recipe.OutputItem, count);
        ToastTo(p.PeerId, item != null && item.IsWeapon ? $"Crafted and equipped {recipe.DisplayName}" : $"Crafted {recipe.DisplayName}");
    }

    // ---------------------------------------------------------------- armour (Gear)

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Wear(string itemId)
    {
        var p = SenderPlayer();
        var def = ItemDb.Get(itemId);
        if (p == null || def == null || !def.IsWearable || p.Pack.Count(itemId) < 1) return;
        var worn = new Dictionary<string, string>(p.Worn);
        p.Pack.Remove(itemId, 1);
        if (worn.TryGetValue(def.Slot, out var old)) p.Pack.Add(old, 1);
        worn[def.Slot] = itemId;
        ServerSetWorn(p, worn);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_TakeOff(string slot)
    {
        var p = SenderPlayer();
        if (p == null || !p.Worn.TryGetValue(slot, out var id)) return;
        var worn = new Dictionary<string, string>(p.Worn);
        worn.Remove(slot);
        p.Pack.Add(id, 1); // worn pieces always fit back
        ServerSetWorn(p, worn);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Enchant(string itemId, string enchant, NodePath altarPath)
    {
        var p = SenderPlayer();
        var altar = GetNodeOrNull<CraftingStation>(altarPath);
        var def = ItemDb.Get(itemId);
        if (p == null || def == null || !def.IsWearable || Gear.GetEnchantment(enchant) is not { } e) return;
        if (altar == null || altar.StationId != Gear.AltarStation) return;
        if (!InReach(p, altar)) { ToastTo(p.PeerId, "Get closer to the Rune Altar"); return; }
        if (def.Enchant == enchant) { ToastTo(p.PeerId, $"It's already enchanted {e.Suffix}."); return; }
        string slot = p.Worn.FirstOrDefault(kv => kv.Value == itemId).Key;
        if (slot == null && p.Pack.Count(itemId) < 1) return;
        var cost = new Dictionary<string, int> { [Gear.ShardId] = Gear.ShardCost, ["coin"] = Gear.CoinCost };
        if (!Supplies.TryConsume(p, cost)) { ToastTo(p.PeerId, $"Enchanting costs {Gear.ShardCost} rune shards and {Gear.CoinCost} coins"); return; }
        string made = Gear.Enchanted(itemId, enchant);
        if (slot != null)
        {
            var worn = new Dictionary<string, string>(p.Worn) { [slot] = made };
            ServerSetWorn(p, worn);
        }
        else
        {
            p.Pack.Remove(itemId, 1);
            p.Pack.Add(made, 1);
        }
        ToastTo(p.PeerId, $"Enchanted: {ItemDb.NameOf(made)}");
        Journal.Fact("enchanted");
        EnchantFx(altar.GlobalPosition);
    }

    /// <summary>Server: change what a player wears, here and on every machine.</summary>
    public void ServerSetWorn(PlayerController p, IReadOnlyDictionary<string, string> worn)
    {
        p.SetWorn(worn);
        var d = Gear.ToDict(p.Worn);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Worn, p.PeerId, d);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Worn(int id, GDict worn) => Gs.GetPlayer(id)?.SetWorn(Gear.FromDict(worn));

    private void EnchantFx(Vector3 at)
    {
        if (!IsDedicated) C_EnchantFx(at);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_EnchantFx, at);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_EnchantFx(Vector3 at)
    {
        Sfx.At("heal", at, -4f);
        Fx.Sparks(this, at + Vector3.Up * 1.1f, new Color(0.5f, 0.85f, 1f), 40, 3f, 0.5f, 1.2f);
        Fx.Ring(this, at + Vector3.Up * 0.1f, new Color(0.5f, 0.85f, 1f), 1.8f, 0.7f);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Equip(string itemId)
    {
        var p = SenderPlayer();
        if (p == null || itemId == p.Equipped) return;
        if (itemId != "")
        {
            var def = ItemDb.Get(itemId);
            if (def == null || !def.IsWeapon || !Gs.Inventory.Remove(itemId, 1))
            {
                ToastTo(p.PeerId, "That weapon isn't in the stockpile (someone else may be holding it)");
                return;
            }
        }
        if (p.Equipped != "") Gs.Inventory.Add(p.Equipped, 1);
        SetEquipped(p, itemId);
        ToastTo(p.PeerId, $"Equipped {(itemId == "" ? "Fists" : ItemDb.NameOf(itemId))}");
    }

    /// <summary>Server: change what a player holds and tell everyone. Doesn't touch the stockpile.</summary>
    public void SetEquipped(PlayerController p, string itemId)
    {
        p.Equipped = itemId;
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Equipped, p.PeerId, itemId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Equipped(int id, string itemId)
    {
        var p = Gs.GetPlayer(id);
        if (p != null) p.Equipped = itemId;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Interact(NodePath path)
    {
        var p = SenderPlayer();
        var node = GetNodeOrNull(path);
        if (p == null || node is not IServerInteractable target) return;
        if (!InReach(p, node as Node3D)) return;
        target.ServerInteract(p);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Travel(NodePath fromPath, string toName)
    {
        var p = SenderPlayer();
        var from = GetNodeOrNull<BlossomShrine>(fromPath);
        if (p == null || from == null) return;
        if (!InReach(p, from)) { ToastTo(p.PeerId, "Stand by a sanctuary's tree to travel."); return; }
        string why = from.ServerTravel(p, BlossomShrine.Find(toName));
        if (why != "") ToastTo(p.PeerId, why);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Wake()
    {
        var p = SenderPlayer();
        if (p == null || !p.Sleeping) return;
        SetSleeping(p, false);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Attack(bool heavy, Vector3 aim)
    {
        var p = SenderPlayer();
        if (p == null || p.Health.IsDead) return;
        foreach (var peer in _joined)
            if (peer != p.PeerId) RpcId(peer, MethodName.C_AttackAnim, p.PeerId, heavy);
        p.Combat.ServerAttack(heavy, aim.Normalized());
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_AttackAnim(int id, bool heavy) => Gs.GetPlayer(id)?.Combat.PlayRemoteAttack(heavy);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_FallDamage(float amount)
    {
        var p = SenderPlayer();
        if (p != null) p.Health.TakeDamage(Mathf.Clamp(amount, 0f, 1000f), null);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_FellOut()
    {
        var p = SenderPlayer();
        if (p != null) ServerRespawn(p, "You fell into the clouds... the blossom tree carries you home.");
    }

    // ================================================================== player death / respawn

    /// <summary>Server: a player's Health hit zero.</summary>
    public void ServerPlayerDied(PlayerController p, Node source)
    {
        int lost = Supplies.LoseOnDeath(p);
        if (lost > 0) ToastTo(p.PeerId, $"You dropped {lost} things from your pack.");
        if (p.Buffs.Count > 0) ServerClearBuffs(p);
        if (source is PlayerController killer && killer != p)
        {
            ToastTo(killer.PeerId, $"You defeated {p.DisplayName}");
            ServerRespawn(p, $"{killer.DisplayName} defeated you. The blossom tree restores you.");
        }
        else if (source is Wolf) ServerRespawn(p, "The wolves overwhelmed you. The blossom tree restores you.");
        else if (source is Bandit) ServerRespawn(p, "The bandits beat you down. The blossom tree restores you.");
        else ServerRespawn(p, "You collapsed. The blossom tree restores you.");
    }

    private void ServerRespawn(PlayerController p, string message)
    {
        if (p.Sleeping) SetSleeping(p, false);
        p.Health.Reset();
        ToPeer(p.PeerId, MethodName.C_Respawn, p.SpawnPoint, message);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Respawn(Vector3 pos, string message)
    {
        var p = Gs.LocalPlayer;
        if (p == null) return;
        p.Teleport(pos);
        p.RestoreStamina();
        if (message != "") Hud.Toast(message);
    }

    /// <summary>Server: heal a player fully and refill their stamina (bed, shrine).</summary>
    public void ServerRestore(PlayerController p, float heal = -1f)
    {
        if (heal < 0) p.Health.Reset();
        else p.Health.Heal(heal);
        ToPeer(p.PeerId, MethodName.C_RestoreStamina);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_RestoreStamina() => Gs.LocalPlayer?.RestoreStamina();

    /// <summary>Server: move a player (their own machine owns their position).</summary>
    public void ServerTeleport(PlayerController p, Vector3 pos) => ToPeer(p.PeerId, MethodName.C_Respawn, pos, "");

    // ================================================================== sleeping

    public void SetSleeping(PlayerController p, bool sleeping)
    {
        p.Sleeping = sleeping;
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Sleep, p.PeerId, sleeping);
        UpdateSleep();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Sleep(int id, bool sleeping)
    {
        var p = Gs.GetPlayer(id);
        if (p != null) p.Sleeping = sleeping;
    }

    /// <summary>Server: recount the sleep vote. Everyone connected asleep = skip to dawn.</summary>
    private void UpdateSleep()
    {
        if (!IsServer) return;
        int total = Gs.Players.Count;
        int asleep = Gs.Players.Values.Count(p => p.Sleeping);
        if (total > 0 && asleep == total)
        {
            InteractHelpers.SkipToMorning();
            foreach (var p in Gs.Players.Values.ToList())
            {
                p.Sleeping = false;
                foreach (var peer in _joined) RpcId(peer, MethodName.C_Sleep, p.PeerId, false);
                ServerRestore(p);
            }
            asleep = 0;
            SyncTime();
            ToastAll(total > 1 ? $"Everyone slept soundly. Day {Gs.Day} begins." : $"You slept soundly. Day {Gs.Day} begins.");
            Journal.Fact("slept");
            Gs.SaveGame(silent: true);
        }
        SleepingCount = asleep;
        SleepTotal = total;
        foreach (var peer in _joined) RpcId(peer, MethodName.C_SleepStatus, asleep, total);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_SleepStatus(int asleep, int total)
    {
        SleepingCount = asleep;
        SleepTotal = total;
    }

    // ================================================================== replication (server -> clients)

    /// <summary>Server: a Health changed (see Health.cs). Clients mirror it and fire the same events.</summary>
    public void BroadcastHealth(Health h, Health.ChangeKind kind, float amount, Node source)
    {
        if (_joined.Count == 0 || !h.IsInsideTree()) return;
        int src = (source as PlayerController)?.PeerId ?? 0;
        var path = h.GetPath();
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Health, path, h.Current, amount, (int)kind, src);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Health(NodePath path, float current, float amount, int kind, int src) =>
        GetNodeOrNull<Health>(path)?.ApplyRemote(current, amount, (Health.ChangeKind)kind, src != 0 ? Gs.GetPlayer(src) : null);

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Inventory(GDict items) => Gs.Inventory.ReplaceAll(items);

    /// <summary>Server: rain starts or stops (Weather).</summary>
    public void ServerSetRain(bool raining)
    {
        Gs.SetRaining(raining);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Rain, raining);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Rain(bool raining) => Gs.SetRaining(raining);

    private void SyncTime()
    {
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Time, Gs.TimeOfDay, Gs.Day);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    private void C_Time(float time, int day)
    {
        Gs.TimeOfDay = time;
        Gs.Day = day;
    }

    // ---------------------------------------------------------------- toasts / feedback

    /// <summary>Show a message to one player (prints on a dedicated server).</summary>
    public void ToastTo(int peer, string message)
    {
        if (peer == LocalId && !IsDedicated) Hud.Toast(message);
        else if (_joined.Contains(peer)) RpcId(peer, MethodName.C_Toast, message);
    }

    public void ToastAll(string message, int except = 0)
    {
        if (IsDedicated) GD.Print($"[Server] {message}");
        else if (except != LocalId) Hud.Toast(message);
        foreach (var peer in _joined)
            if (peer != except) RpcId(peer, MethodName.C_Toast, message);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Toast(string message) => Hud.Toast(message);

    // ---------------------------------------------------------------- journal (goals, errands)

    /// <summary>Server: send everyone the journal; completed = the goal just finished ("" if none).</summary>
    public void BroadcastJournal(string completed)
    {
        var state = Journal.Instance.ToDict();
        if (!IsDedicated) Journal.Instance.Apply(state, completed);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Journal, state, completed);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void C_Journal(GDict state, string completed) => Journal.Instance?.Apply(state, completed);

    /// <summary>Local player: hand the trader what the errand asks for.</summary>
    public void RequestErrand(Trader trader) => ToServer(MethodName.S_Errand, trader.GetPath());

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Errand(NodePath traderPath)
    {
        var p = SenderPlayer();
        var trader = GetNodeOrNull<Trader>(traderPath);
        if (p == null || trader == null) return;
        if (!InReach(p, trader)) { ToastTo(p.PeerId, "Get closer to the trader"); return; }
        Journal.Instance.ServerHandIn(p);
    }

    public void HitMarkerTo(int peer) => ToPeer(peer, MethodName.C_HitMarker);

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_HitMarker() => Hud.Instance?.FlashHitMarker();

    // ================================================================== per frame (server)

    public override void _Process(double delta)
    {
        if (!InSession || !IsServer) return;
        float dt = (float)delta;

        if (_inventoryDirty)
        {
            _inventoryDirty = false;
            if (_joined.Count > 0)
            {
                var items = Gs.Inventory.ToDictionary();
                foreach (var peer in _joined) RpcId(peer, MethodName.C_Inventory, items);
            }
        }

        if (_packDirty.Count > 0)
        {
            foreach (int id in _packDirty)
            {
                var p = Gs.GetPlayer(id);
                if (p != null && id != LocalId && _joined.Contains(id)) RpcId(id, MethodName.C_Pack, p.Pack.ToDictionary());
            }
            _packDirty.Clear();
        }

        _timeSyncTimer -= dt;
        if (_timeSyncTimer <= 0)
        {
            _timeSyncTimer = TimeSyncInterval;
            SyncTime();
        }

        if (IsMultiplayer && Config.AutosaveMinutes > 0)
        {
            _autosaveTimer -= dt;
            if (_autosaveTimer <= 0)
            {
                _autosaveTimer = Config.AutosaveMinutes * 60f;
                if (Gs.Players.Count > 0) Gs.SaveGame(silent: !IsDedicated);
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && InSession && IsServer) Gs.SaveGame(silent: true);
    }

    // ================================================================== helpers

    private int Sender
    {
        get
        {
            int s = Multiplayer.GetRemoteSenderId();
            return s == 0 ? LocalId : s;
        }
    }

    private PlayerController SenderPlayer()
    {
        if (!IsServer || !InSession) return null;
        int id = Sender;
        if (id != LocalId && !_joined.Contains(id)) return null;
        return Gs.GetPlayer(id);
    }

    private static bool InReach(PlayerController p, Node3D target) =>
        target == null || p.GlobalPosition.DistanceTo(target.GlobalPosition) <= ServerReach;

    /// <summary>Run a server handler: directly when we are the server, otherwise over the network.</summary>
    private void ToServer(StringName method, params Variant[] args)
    {
        if (!InSession) return;
        if (IsServer) Call(method, args);
        else RpcId(1, method, args);
    }

    /// <summary>Run a client handler on one player's machine (directly if that's us).</summary>
    private void ToPeer(int peer, StringName method, params Variant[] args)
    {
        if (peer == LocalId) { if (!IsDedicated) Call(method, args); }
        else if (_joined.Contains(peer)) RpcId(peer, method, args);
    }
}
