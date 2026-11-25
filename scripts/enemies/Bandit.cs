using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Bandits hold a camp off the Ashby road, day and night. Grunts are small enemies: the
/// camp gets more of them with more players. The captain is a big monster: one of them, with
/// health and damage scaled by player count (Difficulty). Slow, readable swings: block them, or
/// parry (raise the guard just before the blow lands) to stagger them. GDD §9.</summary>
public partial class Bandit : Enemy
{
    public const float GruntHealth = 60f;
    public const float GruntDamage = 10f;
    public const float CaptainHealth = 240f;
    public const float CaptainDamage = 34f;
    /// <summary>Mail: flat damage off every hit. A wooden sword barely scratches the captain; take iron.</summary>
    public const float GruntArmor = 5f, CaptainArmor = 14f;
    public const int GruntCoins = 6;
    public const int CaptainCoins = 25;
    public const float RaiderHealth = 55f;
    public const float RaiderDamage = 10f;

    /// <summary>The Cinder Foothills (GDD §11, step 3): the Warlord's fort is held by veterans in
    /// soot-grey mail, and the Warlord himself, a giant with a greatsword who slams the ground.</summary>
    public enum Rank { Camp, Fort, Warlord }
    public const float FortHealth = 120f, FortDamage = 17f, FortArmor = 9f;
    public const int FortCoins = 10;
    public const float WarlordHealth = 1100f, WarlordDamage = 38f, WarlordArmor = 18f;
    public const int WarlordCoins = 60;
    /// <summary>The slam: every SlamInterval s he raises the greatsword (SlamWindup s: the tell),
    /// then brings it down: everyone within SlamRadius takes SlamDamage and is thrown back (roll
    /// through it; a raised guard halves it).</summary>
    public const float SlamInterval = 12f, SlamWindup = 1.0f, SlamRadius = 6.5f, SlamDamage = 22f;
    /// <summary>Below half health he bellows for this many fort guards (once).</summary>
    public const int WarlordGuards = 2;

    public Rank Grade { get; private set; }
    public bool Warlord => Grade == Rank.Warlord;
    public bool Fort => Grade == Rank.Fort;
    /// <summary>The captain and the Warlord: slow, heavy blows with long reach.</summary>
    private bool Big => Captain || Warlord;

    public bool Captain { get; private set; }
    /// <summary>Part of a raid on the base (Wildlife.StartRaid): goes for buildings and the stockpile.</summary>
    public bool Raider { get; private set; }
    /// <summary>Server: what this raider stole from the stockpile (recovered if it's killed).</summary>
    public readonly Dictionary<string, int> Carried = new();
    public int CarriedCount => Carried.Values.Sum();

    public override string Kind => Warlord ? "warlord" : Fort ? "fort_bandit" : Raider ? "raider" : Captain ? "captain" : "bandit";
    protected override float WalkSpeed => 2.4f;
    protected override float RunSpeed => Warlord ? 4.2f : Captain ? 4.6f : 5.4f;
    protected override float AggroRange => Raider ? 11f : Fort ? 17f : 15f;
    protected override float LeashRange => Raider ? 22f : Warlord ? 36f : 30f;
    protected override float HomeLeash => Raider ? 0f : Warlord ? 26f : 30f;
    protected override Rect2 Bounds => Grade != Rank.Camp ? Wildlife.FoothillsBounds : base.Bounds;
    /// <summary>Raiders slink off in the morning, with whatever they stole.</summary>
    protected override bool ShouldFlee => Raider && GameState.Instance.TimeOfDay >= 0.24f && GameState.Instance.TimeOfDay < 0.5f;
    protected override float AttackRange => Warlord ? 3.6f : Captain ? 3.2f : 1.9f;
    protected override float AttackReach => Warlord ? 4.8f : Captain ? 4.4f : 2.6f;
    protected override float AttackWindup => Warlord ? 0.8f : Captain ? 0.75f : 0.55f;
    protected override float AttackCooldown => Warlord ? 2.2f : Captain ? 2.0f : Fort ? 1.4f : 1.6f;
    protected override float HitStagger => Warlord ? 0.08f : Captain ? 0.15f : 0.35f;
    protected override float HitKnockback => Warlord ? 0.6f : Captain ? 1.5f : 4f;
    protected override float WanderRadius => 5f;

    private HumanoidModel _model;

    public static Bandit Create(int netId, int camp, Vector3 position, float yaw, float maxHealth, float damage, bool captain, bool raider = false, Rank rank = Rank.Camp)
    {
        var b = new Bandit { Name = $"Bandit_{netId}", Captain = captain, Raider = raider, Damage = damage, Grade = rank };
        bool warlord = rank == Rank.Warlord, fort = rank == Rank.Fort;
        float height = warlord ? 2.6f : captain ? 2.3f : fort ? 1.9f : 1.8f;
        b.Init(netId, camp, position, yaw, maxHealth, new Vector3(warlord ? 0.9f : 0.7f, height, warlord ? 0.9f : 0.7f));
        b.Health.Armor = warlord ? WarlordArmor : fort ? FortArmor : captain ? CaptainArmor : raider ? 0f : GruntArmor;   // raiders travel light (and the watchtower must still bring them down)
        var hide = captain || warlord
            ? new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "1H_Sword" } // a greatsword: his long reach
            : fort ? new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Spike_Shield", "2H_Sword", "Knight_Cape" } // helmet and a round shield
            : new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "Spike_Shield", "2H_Sword", "Knight_Helmet", "Knight_Cape" };
        var tint = warlord ? new Color(0.12f, 0.06f, 0.04f, 0.55f)
            : fort ? new Color(0.22f, 0.2f, 0.2f, 0.5f)
            : captain ? new Color(0.25f, 0.02f, 0.05f, 0.45f)
            : raider ? new Color(0.55f, 0.3f, 0.05f, 0.45f)
            : new Color(0.6f, 0.1f, 0.06f, 0.42f);
        b._model = HumanoidModel.Create("res://assets/thirdparty/kaykit/Knight.glb", height, hide, tint);
        b.AddChild(b._model);
        if (raider)
        {
            // A burning torch held high: you see the raid coming in the dusk.
            var torch = new Node3D { Name = "Torch", Position = new Vector3(0.45f, 1.9f, -0.1f) };
            torch.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.03f, Height = 0.7f, RadialSegments = 5 },
                MaterialOverride = StyleMaterials.Toon(new Color(0.3f, 0.2f, 0.12f)), Position = new Vector3(0, -0.3f, 0),
            });
            var flame = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.6f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f),
                EmissionEnergyMultiplier = 5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            torch.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.11f, Height = 0.3f, RadialSegments = 6, Rings = 3 }, MaterialOverride = flame, Position = new Vector3(0, 0.1f, 0) });
            torch.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 2.2f, OmniRange = 7f, Position = new Vector3(0, 0.2f, 0) });
            torch.AddChild(Fx.Trail(new Color(1f, 0.5f, 0.15f, 0.7f)));
            b.AddChild(torch);
        }
        if (captain || warlord)
            b.AddChild(new Label3D
            {
                Text = warlord ? "The Bandit Warlord" : "Bandit Captain", Position = new Vector3(0, height + 0.4f, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = warlord ? 48 : 40, PixelSize = 0.006f, OutlineSize = 10,
                Modulate = warlord ? new Color(1f, 0.55f, 0.2f) : new Color(1f, 0.45f, 0.35f),
            });
        if (warlord)
        {
            // Embers smoulder in his mail: you see him across the fort in the haze.
            b.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.45f, 0.15f), LightEnergy = 1.2f, OmniRange = 5f, Position = new Vector3(0, 1.6f, 0) });
        }
        return b;
    }

    public override void ServerDropLoot(Node killer)
    {
        var gs = GameState.Instance;
        var looter = killer as PlayerController;
        if (Grade != Rank.Camp) { FoothillsLoot(looter); return; }
        int coins = Captain ? CaptainCoins : GruntCoins;
        if (Talents.Has(looter, Talents.Pickpocket)) coins = Mathf.RoundToInt(coins * Talents.PickpocketCoins);
        Supplies.Give(looter, "coin", coins);
        string msg = $"+{coins} Coins";
        if (Carried.Count > 0)
        {
            // Stolen goods: back into the stockpile (they were taken from it).
            int n = CarriedCount;
            foreach (var kv in Carried) gs.Inventory.Add(kv.Key, kv.Value);
            Carried.Clear();
            msg += $", recovered {n} stolen items";
        }
        // Rune shards for enchanting (GDD §6): raiders carry one, the captain a few.
        int shards = Captain ? 3 : Raider ? 1 : 0;
        if (shards > 0 && Supplies.Give(looter, Gear.ShardId, shards) > 0) msg += $", +{shards} Rune Shard{(shards > 1 ? "s" : "")}";
        bool haveBlueprint = gs.Inventory.Count("cottage_blueprint") > 0 || gs.Players.Values.Any(pl => pl.Pack.Count("cottage_blueprint") > 0);
        if (Captain && gs.HomeTier < 2 && !haveBlueprint)
        {
            Supplies.Give(looter, "cottage_blueprint", 1);
            msg += ", Cottage Blueprint!";
            Net.Instance.ToastAll("The bandit captain carried plans for a proper cottage. Check the board by your shack.");
        }
        if (killer is PlayerController p) Net.Instance.ToastTo(p.PeerId, msg);
    }

    /// <summary>Server: the fort's spoils. The Warlord carries the Keep's plans and the key to the
    /// Ember Pass (both once: nobody needs two).</summary>
    private void FoothillsLoot(PlayerController looter)
    {
        var gs = GameState.Instance;
        int coins = Warlord ? WarlordCoins : FortCoins;
        if (Talents.Has(looter, Talents.Pickpocket)) coins = Mathf.RoundToInt(coins * Talents.PickpocketCoins);
        Supplies.Give(looter, "coin", coins);
        string msg = $"+{coins} Coins";
        int shards = Warlord ? 5 : Rng.Randf() < 0.35f ? 1 : 0;
        if (shards > 0 && Supplies.Give(looter, Gear.ShardId, shards) > 0) msg += $", +{shards} Rune Shard{(shards > 1 ? "s" : "")}";
        if (!Warlord && Supplies.Give(looter, "coal", 1) > 0) msg += ", +1 Coal";
        if (Warlord)
        {
            bool Held(string id) => gs.Inventory.Count(id) > 0 || gs.Players.Values.Any(pl => pl.Pack.Count(id) > 0);
            if (gs.HomeTier < HomeUpgradeBoard.MaxTier && !Held("keep_blueprint"))
            {
                Supplies.Give(looter, "keep_blueprint", 1);
                msg += ", Keep Blueprint!";
                Net.Instance.ToastAll("Rolled up in the Warlord's war-chest: plans for a stone keep. Check the board by your home.");
            }
            if (!Held("ember_pass_key") && !(Journal.Instance?.Facts.Contains("ember_pass_opened") ?? false))
            {
                Supplies.Give(looter, "ember_pass_key", 1);
                msg += ", Ember Pass Key!";
                Net.Instance.ToastAll("On a chain round the Warlord's neck: a heavy iron key, warm to the touch. The Ember Pass gate…");
            }
        }
        if (looter != null) Net.Instance.ToastTo(looter.PeerId, msg);
    }

    public override void PlayAttack()
    {
        Sfx.At("swing_heavy", GlobalPosition + Vector3.Up * 1.2f, Warlord ? 2f : -2f, Warlord ? 0.02f : 0.08f);
        if (Current == State.Dead) return;
        // Slow the swing so the blow lands when the server resolves it (after the windup).
        if (Warlord) _model.OneShot("2H_Melee_Attack_Spin", 1.0f);
        else _model.OneShot(Captain ? "1H_Melee_Attack_Chop" : "1H_Melee_Attack_Slice_Diagonal", Captain ? 0.85f : 1.0f);
    }

    // ================================================================== the Warlord

    private float _slamIn = SlamInterval * 0.5f, _slamTime;
    private bool _calledGuard;

    protected override void Think(float dt)
    {
        if (Warlord && WarlordThink(dt)) return;
        base.Think(dt);
    }

    /// <summary>Server: the Warlord's slam and his call for help. True while the slam's windup
    /// holds him in place (skip the usual brain).</summary>
    private bool WarlordThink(float dt)
    {
        if (Current is State.Dead or State.Stagger or State.Prowl || Target == null) { _slamTime = 0; return false; }
        if (!_calledGuard && Health.Fraction < 0.5f)
        {
            _calledGuard = true;
            for (int i = 0; i < WarlordGuards; i++) Wildlife.Instance.SpawnWarlordGuard(this);
            Net.Instance.ToastAll("The Warlord bellows, and the fort's gate-guard come running…");
        }
        if (_slamTime > 0)
        {
            _slamTime -= dt;
            var v = Velocity;
            v.X = Mathf.Lerp(v.X, 0, Mathf.Min(1f, 8f * dt));
            v.Z = Mathf.Lerp(v.Z, 0, Mathf.Min(1f, 8f * dt));
            v.Y = IsOnFloor() ? -0.5f : v.Y - Gravity * dt;
            Velocity = v;
            MoveAndSlide();
            if (_slamTime <= 0) Slam();
            return true;
        }
        _slamIn -= dt;
        if (_slamIn <= 0 && Current == State.Chase && Flat(Target.GlobalPosition - GlobalPosition).Length() < SlamRadius - 1f)
        {
            _slamIn = SlamInterval;
            _slamTime = SlamWindup;
            Wildlife.Instance.BroadcastAnim(this, Wildlife.AnimSpecial);
        }
        return false;
    }

    /// <summary>Server: the greatsword hits the ground.</summary>
    private void Slam()
    {
        var net = Net.Instance;
        foreach (var p in GameState.Instance.Players.Values)
        {
            if (!CanHunt(p)) continue;
            var off = p.GlobalPosition - GlobalPosition;
            if (Flat(off).Length() > SlamRadius || Mathf.Abs(off.Y) > 3f) continue;
            var result = net.ServerDamagePlayer(p, Difficulty.BigMonsterDamage(SlamDamage), this);
            if (result is Net.HitResult.Dodged or Net.HitResult.Parried) continue;
            var away = Flat(off);
            away = away.LengthSquared() < 0.01f ? -GlobalBasis.Z : away.Normalized();
            float k = result == Net.HitResult.Blocked ? 0.5f : 1f;
            net.ServerShovePlayer(p, (away * 11f + Vector3.Up * 4f) * k);
        }
        AttackCooldownLeft = Mathf.Max(AttackCooldownLeft, 1.2f);
    }

    /// <summary>Everyone: the greatsword goes up over his head (the tell), then crashes down in a
    /// ring of cinders.</summary>
    public override void PlaySpecial()
    {
        if (!Warlord || Current == State.Dead) return;
        Sfx.At("raid_horn", GlobalPosition + Vector3.Up * 2f, -6f, 0.05f, 60f);
        _model.OneShot("2H_Melee_Attack_Chop", 0.75f);
        GetTree().CreateTimer(SlamWindup).Timeout += () =>
        {
            if (!IsInstanceValid(this) || Current == State.Dead) return;
            Fx.Ring(GetParent(), GlobalPosition + Vector3.Up * 0.1f, new Color(1f, 0.5f, 0.15f, 0.7f), SlamRadius, 0.5f);
            Sfx.At("explode", GlobalPosition, 2f, 0.1f, 70f);
        };
    }

    public override void PlayStun() => _model.OneShot("Hit_B", 0.6f);

    // ================================================================== raids

    private float _stuckTimer;
    private Vector3 _stuckFrom;

    /// <summary>Raiders with nobody to fight march on the base: the nearest building or chest
    /// (chests first: they want the stockpile). A wall in the way gets hacked down.</summary>
    protected override bool Objective(float dt, Vector3 pos, ref Vector3 desired, ref float speed, ref float? faceYaw)
    {
        if (!Raider) return false;
        var raid = Wildlife.Instance;
        if (!raid.ValidObjective(ObjectiveNode)) ObjectiveNode = raid.PickObjective(pos);

        // Not getting anywhere? Something's in the way: break it.
        _stuckTimer += dt;
        if (_stuckTimer > 1.5f)
        {
            if (Flat(pos - _stuckFrom).Length() < 0.6f && raid.NearestBuilding(pos, 3.5f) is { } wall) ObjectiveNode = wall;
            _stuckFrom = pos;
            _stuckTimer = 0;
        }

        var goal = ObjectiveNode?.GlobalPosition ?? GameState.Instance.ClaimCenter;
        float reach = raid.ObjectiveRadius(ObjectiveNode) + AttackRange;
        var to = Flat(goal - pos);
        if (ObjectiveNode != null && to.Length() < reach)
        {
            faceYaw = YawOf(to);
            if (AttackCooldownLeft <= 0)
            {
                Enter(State.Windup);
                raid.BroadcastAnim(this, Wildlife.AnimAttack);
            }
            return true;
        }
        desired = Steer(pos, goal, dt);
        speed = RunSpeed * 0.75f;
        return true;
    }

    protected override void StrikeObjective()
    {
        if (!Wildlife.Instance.ValidObjective(ObjectiveNode)) return; // already wrecked by another raider
        if (Flat(ObjectiveNode.GlobalPosition - GlobalPosition).Length() > Wildlife.Instance.ObjectiveRadius(ObjectiveNode) + AttackReach) return;
        Wildlife.Instance.RaidStrike(this, ObjectiveNode);
    }

    protected override void FlashHit()
    {
        Sfx.At("hit_flesh", GlobalPosition + Vector3.Up * 1.1f, 0f, 0.12f);
        _model.Flash(new Color(1f, 0.85f, 0.85f, 0.6f));
        if (!_model.Busy) _model.OneShot("Hit_A", 1.4f);
    }

    protected override void PlayDeath() => _model.OneShot("Death_A", 1f, hold: true);

    protected override void Animate(float dt)
    {
        if (Current == State.Dead) return;
        if (SpeedSmoothed > 3.6f) _model.Loop("Running_A", 0.2, SpeedSmoothed / 5.5f);
        else if (SpeedSmoothed > 0.5f) _model.Loop("Walking_A", 0.2, Mathf.Clamp(SpeedSmoothed / 2.4f, 0.7f, 1.5f));
        else _model.Loop("Idle", 0.25);
    }
}
