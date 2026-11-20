using Godot;

namespace KatrinasGame;

/// <summary>Wild boar on the Forest Road (GDD §11, step 1). Grazes in its clearing day and night
/// and leaves you alone unless you come close or hit it; then it lowers its tusks and charges.
/// Tough hide (a little armour). Drops a boar hide, which backs bronze armour at the anvil.
/// Brain, targeting and replication: Enemy / Wildlife.</summary>
public partial class Boar : Enemy
{
    public const float MaxHealth = 110f;
    public const float GoreDamage = 16f;
    public const float Armor = 3f;
    public const float ChargeWindup = 0.45f;
    public const float Size = 1.15f;

    public override string Kind => "boar";
    protected override float WalkSpeed => 1.6f;
    protected override float RunSpeed => 7.4f;
    protected override float AggroRange => 7f;
    protected override float LeashRange => 24f;
    protected override float HomeLeash => 20f;
    protected override float AttackRange => 2.1f;
    protected override float AttackReach => 2.8f;
    protected override float AttackWindup => ChargeWindup;
    protected override float AttackCooldown => 1.6f;
    protected override float HitKnockback => 3f;
    protected override float WanderRadius => 5f;
    protected override Rect2 Bounds => HomeCliffs.ForestBounds;

    private Node3D _visual, _body, _head;
    private Node3D[] _legs;
    private ShaderMaterial _hide;
    private float _gait, _time;
    private Tween _tween;

    private static readonly Color HideColor = new(0.36f, 0.26f, 0.2f);
    private static readonly Color HideDark = new(0.2f, 0.14f, 0.11f);
    private static readonly Color Bristle = new(0.14f, 0.11f, 0.1f);
    private static readonly Color Tusk = new(0.92f, 0.88f, 0.76f);

    public static Boar Create(int netId, int group, Vector3 position, float yaw, float maxHealth = MaxHealth, float damage = GoreDamage)
    {
        var b = new Boar { Name = $"Boar_{netId}", Damage = damage };
        b.Init(netId, group, position, yaw, maxHealth, new Vector3(0.6f, 0.8f, 1.2f) * Size);
        b.Health.Armor = Armor;
        b.BuildVisual();
        return b;
    }

    public override void ServerDropLoot(Node killer)
    {
        var p = killer as PlayerController;
        if (Supplies.Give(p, "boar_hide", 1) > 0 && p != null) Net.Instance.ToastTo(p.PeerId, "+1 Boar Hide");
    }

    protected override void FlashHit()
    {
        if (_hide == null) return;
        var t = CreateTween();
        t.TweenProperty(_hide, "shader_parameter/tint", new Color(0.85f, 0.3f, 0.3f), 0.05);
        t.TweenProperty(_hide, "shader_parameter/tint", HideColor, 0.25);
        Sfx.At(Health.IsDead ? "wolf_yelp" : "hit_flesh", GlobalPosition + Vector3.Up * 0.6f, Health.IsDead ? 0f : -1f);
    }

    protected override void PlayDeath()
    {
        if (_visual == null) return;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_visual, "rotation:z", Mathf.Pi / 2f, 0.5).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "position:y", -0.15f, 0.5);
    }

    /// <summary>Everyone: paw the ground, then a short, hard rush with the tusks.</summary>
    public override void PlayAttack()
    {
        if (_visual == null || Current == State.Dead) return;
        Sfx.At("wolf_snarl", GlobalPosition + Vector3.Up * 0.5f, -2f, 0.25f);
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "position", new Vector3(0, -0.05f, 0.2f) * Size, ChargeWindup * 0.8).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "position", new Vector3(0, 0.05f, -0.6f) * Size, 0.1).SetTrans(Tween.TransitionType.Expo);
        _tween.TweenProperty(_visual, "position", Vector3.Zero, 0.35).SetTrans(Tween.TransitionType.Sine);
    }

    public override void PlayStun()
    {
        if (_visual == null || Current == State.Dead) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:z", 0.3f, 0.12);
        _tween.TweenProperty(_visual, "rotation:z", -0.2f, 0.25);
        _tween.TweenProperty(_visual, "rotation:z", 0f, 0.3);
        _tween.Parallel().TweenProperty(_visual, "position", Vector3.Zero, 0.3);
    }

    protected override void Animate(float dt)
    {
        _time += dt;
        if (_legs == null || Current == State.Dead) return;
        float sp = SpeedSmoothed;
        _gait += sp * dt * 4.2f / Size;
        float amp = Mathf.Clamp(sp / 2.5f, 0f, 1f) * 0.55f;
        for (int i = 0; i < 4; i++)
            _legs[i].Rotation = new Vector3(Mathf.Sin(_gait + (i is 0 or 3 ? 0f : Mathf.Pi)) * amp, 0, 0);
        float idle = 1f - Mathf.Clamp(sp / 1f, 0f, 1f);
        _body.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(_gait * 2f)) * amp * 0.04f + Mathf.Sin(_time * 2.4f) * 0.006f * idle, 0) * Size;
        // Rooting about with its snout while it grazes.
        float root = idle * Mathf.SmoothStep(0.3f, 1f, Mathf.Max(0f, Mathf.Sin(_time * 0.6f + NetId)));
        _head.Rotation = new Vector3(0.35f * root + Mathf.Sin(_time * 9f) * 0.04f * root, Mathf.Sin(_time * 0.5f + NetId) * 0.25f * idle, 0);
    }

    private void BuildVisual()
    {
        _hide = StyleMaterials.CharacterFlat(HideColor);
        var dark = StyleMaterials.CharacterFlat(HideDark);
        var bristle = StyleMaterials.CharacterFlat(Bristle);
        var tusk = StyleMaterials.CharacterFlat(Tusk);
        var eyes = EnemyMesh.Glow(new Color(0.9f, 0.3f, 0.15f), 1.5f);

        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);
        _visual.AddChild(_body = new Node3D { Name = "Body" });
        _body.Scale = Vector3.One * Size;

        // Barrel body, high in the shoulders, with a bristly ridge down the back.
        EnemyMesh.Seg(_body, new Vector3(0, 0.55f, -0.38f), new Vector3(0, 0.5f, 0.42f), 0.3f, 0.24f, _hide, 6);
        EnemyMesh.Seg(_body, new Vector3(0, 0.82f, -0.36f), new Vector3(0, 0.72f, 0.3f), 0.09f, 0.05f, bristle, 5);
        EnemyMesh.Seg(_body, new Vector3(0, 0.36f, -0.2f), new Vector3(0, 0.38f, 0.3f), 0.18f, 0.14f, dark, 5);

        _head = new Node3D { Name = "Head", Position = new Vector3(0, 0.56f, -0.62f) };
        _body.AddChild(_head);
        EnemyMesh.Seg(_head, new Vector3(0, 0.02f, 0.12f), new Vector3(0, -0.1f, -0.28f), 0.2f, 0.1f, _hide, 6);
        EnemyMesh.Seg(_head, new Vector3(0, -0.1f, -0.28f), new Vector3(0, -0.12f, -0.36f), 0.1f, 0.09f, dark, 6);     // snout
        foreach (float side in new[] { -1f, 1f })
        {
            EnemyMesh.Seg(_head, new Vector3(side * 0.08f, -0.14f, -0.26f), new Vector3(side * 0.14f, -0.02f, -0.34f), 0.025f, 0.008f, tusk, 4);
            _head.AddChild(new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(0.09f, 0.12f, 0.04f) }, MaterialOverride = dark,
                Position = new Vector3(side * 0.12f, 0.16f, 0.06f), Rotation = new Vector3(-0.3f, 0, side * -0.4f),
            });
            EnemyMesh.Box(_head, new Vector3(0.035f, 0.025f, 0.02f), new Vector3(side * 0.1f, 0.06f, -0.12f), Vector3.Zero, eyes);
        }
        EnemyMesh.Seg(_body, new Vector3(0, 0.56f, 0.45f), new Vector3(0, 0.4f, 0.55f), 0.025f, 0.015f, dark, 4); // tail

        _legs = new Node3D[4];
        var hips = new[] { new Vector3(-0.16f, 0.4f, -0.3f), new Vector3(0.16f, 0.4f, -0.3f), new Vector3(-0.16f, 0.4f, 0.32f), new Vector3(0.16f, 0.4f, 0.32f) };
        for (int i = 0; i < 4; i++)
        {
            _legs[i] = new Node3D { Name = $"Leg{i}", Position = hips[i] };
            _body.AddChild(_legs[i]);
            EnemyMesh.Seg(_legs[i], Vector3.Zero, new Vector3(0, -0.34f, 0), 0.08f, 0.05f, _hide, 5);
            EnemyMesh.Box(_legs[i], new Vector3(0.08f, 0.06f, 0.1f), new Vector3(0, -0.37f, -0.01f), Vector3.Zero, bristle);
        }
    }
}
