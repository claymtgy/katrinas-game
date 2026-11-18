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
    public const float CaptainHealth = 160f;
    public const float CaptainDamage = 18f;
    public const int GruntCoins = 6;
    public const int CaptainCoins = 25;

    public bool Captain { get; private set; }

    public override string Kind => Captain ? "captain" : "bandit";
    protected override float WalkSpeed => 2.4f;
    protected override float RunSpeed => Captain ? 4.6f : 5.4f;
    protected override float AggroRange => 15f;
    protected override float LeashRange => 30f;
    protected override float HomeLeash => 30f;
    protected override float AttackRange => Captain ? 2.2f : 1.9f;
    protected override float AttackReach => Captain ? 3.0f : 2.6f;
    protected override float AttackWindup => Captain ? 0.75f : 0.55f;
    protected override float AttackCooldown => Captain ? 2.0f : 1.6f;
    protected override float HitStagger => Captain ? 0.15f : 0.35f;
    protected override float HitKnockback => Captain ? 1.5f : 4f;
    protected override float WanderRadius => 5f;

    private HumanoidModel _model;

    public static Bandit Create(int netId, int camp, Vector3 position, float yaw, float maxHealth, float damage, bool captain)
    {
        var b = new Bandit { Name = $"Bandit_{netId}", Captain = captain, Damage = damage };
        float height = captain ? 2.3f : 1.8f;
        b.Init(netId, camp, position, yaw, maxHealth, new Vector3(0.7f, height, 0.7f));
        var hide = captain
            ? new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "2H_Sword" }
            : new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "Spike_Shield", "2H_Sword", "Knight_Helmet", "Knight_Cape" };
        var tint = captain ? new Color(0.25f, 0.02f, 0.05f, 0.45f) : new Color(0.6f, 0.1f, 0.06f, 0.42f);
        b._model = HumanoidModel.Create("res://assets/thirdparty/kaykit/Knight.glb", height, hide, tint);
        b.AddChild(b._model);
        if (captain)
            b.AddChild(new Label3D
            {
                Text = "Bandit Captain", Position = new Vector3(0, height + 0.4f, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = 0.006f, OutlineSize = 10,
                Modulate = new Color(1f, 0.45f, 0.35f),
            });
        return b;
    }

    public override void ServerDropLoot(Node killer)
    {
        var gs = GameState.Instance;
        int coins = Captain ? CaptainCoins : GruntCoins;
        var looter = killer as PlayerController;
        Supplies.Give(looter, "coin", coins);
        string msg = $"+{coins} Coins";
        bool haveBlueprint = gs.Inventory.Count("cottage_blueprint") > 0 || gs.Players.Values.Any(pl => pl.Pack.Count("cottage_blueprint") > 0);
        if (Captain && gs.HomeTier < 2 && !haveBlueprint)
        {
            Supplies.Give(looter, "cottage_blueprint", 1);
            msg += ", Cottage Blueprint!";
            Net.Instance.ToastAll("The bandit captain carried plans for a proper cottage. Check the board by your shack.");
        }
        if (killer is PlayerController p) Net.Instance.ToastTo(p.PeerId, msg);
    }

    public override void PlayAttack()
    {
        Sfx.At("swing_heavy", GlobalPosition + Vector3.Up * 1.2f, -2f);
        if (Current == State.Dead) return;
        // Slow the swing so the blow lands when the server resolves it (after the windup).
        _model.OneShot(Captain ? "1H_Melee_Attack_Chop" : "1H_Melee_Attack_Slice_Diagonal", Captain ? 0.85f : 1.0f);
    }

    public override void PlayStun() => _model.OneShot("Hit_B", 0.6f);

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
