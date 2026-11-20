using Godot;

namespace KatrinasGame;

/// <summary>Cliff goat (GDD §11, Spire Cliffs). Grazes on a spire top and minds its own business
/// until you come close or hit it; then it lowers its horns, paws the rock, and butts. A butt that
/// lands hurls you back and up, which on a spire top usually means off the edge: roll through it
/// (the roll's dodge window), or brace behind a raised guard (half the shove); a parry stuns it.
/// Drops a goat hide, which lines iron gear at the Bellows. Brain and replication: Enemy / Wildlife.</summary>
public partial class Goat : Enemy
{
    public const float MaxHealth = 70f;
    public const float ButtDamage = 8f;
    public const float Armor = 2f;
    public const float ButtWindup = 0.6f;
    /// <summary>How hard a butt that lands throws you (m/s, away and up).</summary>
    public const float ShoveAway = 11f, ShoveUp = 4.5f;

    public override string Kind => "goat";
    protected override float WalkSpeed => 1.4f;
    protected override float RunSpeed => 6.2f;
    protected override float AggroRange => 6f;
    protected override float LeashRange => 18f;
    protected override float HomeLeash => 14f;
    protected override float AttackRange => 2.3f;
    protected override float AttackReach => 3.0f;
    protected override float AttackWindup => ButtWindup;
    protected override float AttackCooldown => 2.2f;
    protected override float HitKnockback => 2.5f;
    protected override float WanderRadius => 3f;
    protected override float MinY => -35f;
    /// <summary>The top of its own spire (it never steps off).</summary>
    protected override Rect2 Bounds => Wildlife.GoatBounds(Group);

    private Node3D _visual, _body, _head;
    private Node3D[] _legs;
    private ShaderMaterial _coat;
    private float _gait, _time;
    private Tween _tween;

    private static readonly Color CoatColor = new(0.86f, 0.84f, 0.78f);
    private static readonly Color CoatDark = new(0.55f, 0.5f, 0.45f);
    private static readonly Color HornColor = new(0.38f, 0.32f, 0.26f);
    private static readonly Color Hoof = new(0.16f, 0.13f, 0.12f);

    public static Goat Create(int netId, int group, Vector3 position, float yaw, float maxHealth = MaxHealth, float damage = ButtDamage)
    {
        var g = new Goat { Name = $"Goat_{netId}", Damage = damage };
        g.Init(netId, group, position, yaw, maxHealth, new Vector3(0.55f, 0.9f, 1.0f));
        g.Health.Armor = Armor;
        g.BuildVisual();
        return g;
    }

    /// <summary>Server: the butt landed. A hit throws you; a raised guard holds you to half; a roll or a parry, nothing.</summary>
    public override void OnStrike(PlayerController p, Net.HitResult result)
    {
        if (result is Net.HitResult.Dodged or Net.HitResult.Parried) return;
        var away = Flat(p.GlobalPosition - GlobalPosition);
        away = away.LengthSquared() < 0.01f ? -GlobalBasis.Z : away.Normalized();
        float k = result == Net.HitResult.Blocked ? 0.5f : 1f;
        Net.Instance.ServerShovePlayer(p, (away * ShoveAway + Vector3.Up * ShoveUp) * k);
    }

    public override void ServerDropLoot(Node killer)
    {
        var p = killer as PlayerController;
        if (Supplies.Give(p, "goat_hide", 1) > 0 && p != null) Net.Instance.ToastTo(p.PeerId, "+1 Goat Hide");
    }

    protected override void FlashHit()
    {
        if (_coat == null) return;
        var t = CreateTween();
        t.TweenProperty(_coat, "shader_parameter/tint", new Color(0.9f, 0.4f, 0.4f), 0.05);
        t.TweenProperty(_coat, "shader_parameter/tint", CoatColor, 0.25);
        Sfx.At(Health.IsDead ? "wolf_yelp" : "hit_flesh", GlobalPosition + Vector3.Up * 0.7f, Health.IsDead ? 3f : 1f, 0.25f);
    }

    protected override void PlayDeath()
    {
        if (_visual == null) return;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_visual, "rotation:z", Mathf.Pi / 2f, 0.5).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "position:y", -0.1f, 0.5);
    }

    /// <summary>Everyone: rear up and drop the horns, then a short hard lunge.</summary>
    public override void PlayAttack()
    {
        if (_visual == null || Current == State.Dead) return;
        Sfx.At("wolf_snarl", GlobalPosition + Vector3.Up * 0.7f, 2f, 0.3f);
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:x", -0.35f, ButtWindup * 0.6).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "rotation:x", 0.25f, ButtWindup * 0.3).SetTrans(Tween.TransitionType.Expo);
        _tween.Parallel().TweenProperty(_visual, "position", new Vector3(0, 0, -0.7f), ButtWindup * 0.3);
        _tween.TweenProperty(_visual, "rotation:x", 0f, 0.3);
        _tween.Parallel().TweenProperty(_visual, "position", Vector3.Zero, 0.3);
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
        _tween.Parallel().TweenProperty(_visual, "rotation:x", 0f, 0.3);
    }

    protected override void Animate(float dt)
    {
        _time += dt;
        if (_legs == null || Current == State.Dead) return;
        float sp = SpeedSmoothed;
        _gait += sp * dt * 4.6f;
        float amp = Mathf.Clamp(sp / 2.5f, 0f, 1f) * 0.6f;
        for (int i = 0; i < 4; i++)
            _legs[i].Rotation = new Vector3(Mathf.Sin(_gait + (i is 0 or 3 ? 0f : Mathf.Pi)) * amp, 0, 0);
        float idle = 1f - Mathf.Clamp(sp, 0f, 1f);
        _body.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(_gait * 2f)) * amp * 0.05f, 0);
        // Head down to crop the grass now and then, chewing; up and looking round otherwise.
        float graze = idle * Mathf.SmoothStep(0.2f, 1f, Mathf.Max(0f, Mathf.Sin(_time * 0.45f + NetId)));
        _head.Rotation = new Vector3(0.8f * graze + Mathf.Sin(_time * 7f) * 0.03f * graze, Mathf.Sin(_time * 0.6f + NetId) * 0.4f * idle, 0);
    }

    private void BuildVisual()
    {
        _coat = StyleMaterials.CharacterFlat(CoatColor);
        var dark = StyleMaterials.CharacterFlat(CoatDark);
        var horn = StyleMaterials.CharacterFlat(HornColor);
        var hoof = StyleMaterials.CharacterFlat(Hoof);
        var eyes = EnemyMesh.Glow(new Color(0.95f, 0.75f, 0.3f), 1.2f);

        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);
        _visual.AddChild(_body = new Node3D { Name = "Body" });

        // A shaggy pale body on long legs, darker down the spine and under the belly.
        EnemyMesh.Seg(_body, new Vector3(0, 0.72f, -0.32f), new Vector3(0, 0.7f, 0.36f), 0.24f, 0.2f, _coat, 6);
        EnemyMesh.Seg(_body, new Vector3(0, 0.92f, -0.3f), new Vector3(0, 0.88f, 0.3f), 0.07f, 0.05f, dark, 5);
        EnemyMesh.Seg(_body, new Vector3(0, 0.56f, -0.22f), new Vector3(0, 0.56f, 0.26f), 0.14f, 0.12f, _coat, 5);
        EnemyMesh.Seg(_body, new Vector3(0, 0.78f, 0.4f), new Vector3(0, 0.9f, 0.48f), 0.04f, 0.02f, _coat, 4); // tail

        _head = new Node3D { Name = "Head", Position = new Vector3(0, 0.92f, -0.42f) };
        _body.AddChild(_head);
        EnemyMesh.Seg(_head, new Vector3(0, -0.08f, 0.1f), new Vector3(0, 0.08f, -0.06f), 0.12f, 0.1f, _coat, 6);  // neck
        EnemyMesh.Seg(_head, new Vector3(0, 0.1f, -0.06f), new Vector3(0, -0.02f, -0.3f), 0.1f, 0.06f, _coat, 6);  // muzzle
        EnemyMesh.Seg(_head, new Vector3(0, -0.04f, -0.24f), new Vector3(0, -0.2f, -0.22f), 0.035f, 0.01f, dark, 4); // beard
        foreach (float side in new[] { -1f, 1f })
        {
            // Horns sweeping back and curling down.
            EnemyMesh.Seg(_head, new Vector3(side * 0.05f, 0.17f, -0.08f), new Vector3(side * 0.11f, 0.3f, 0.06f), 0.035f, 0.028f, horn, 5);
            EnemyMesh.Seg(_head, new Vector3(side * 0.11f, 0.3f, 0.06f), new Vector3(side * 0.16f, 0.22f, 0.2f), 0.028f, 0.02f, horn, 5);
            EnemyMesh.Seg(_head, new Vector3(side * 0.16f, 0.22f, 0.2f), new Vector3(side * 0.15f, 0.1f, 0.14f), 0.02f, 0.008f, horn, 4);
            _head.AddChild(new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(0.06f, 0.12f, 0.03f) }, MaterialOverride = _coat,
                Position = new Vector3(side * 0.12f, 0.12f, -0.02f), Rotation = new Vector3(0, 0, side * 1.2f),
            });
            EnemyMesh.Box(_head, new Vector3(0.03f, 0.02f, 0.02f), new Vector3(side * 0.08f, 0.1f, -0.14f), Vector3.Zero, eyes);
        }

        _legs = new Node3D[4];
        var hips = new[] { new Vector3(-0.13f, 0.6f, -0.28f), new Vector3(0.13f, 0.6f, -0.28f), new Vector3(-0.13f, 0.6f, 0.3f), new Vector3(0.13f, 0.6f, 0.3f) };
        for (int i = 0; i < 4; i++)
        {
            _legs[i] = new Node3D { Name = $"Leg{i}", Position = hips[i] };
            _body.AddChild(_legs[i]);
            EnemyMesh.Seg(_legs[i], Vector3.Zero, new Vector3(0, -0.55f, 0), 0.06f, 0.035f, i < 2 ? _coat : dark, 5);
            EnemyMesh.Box(_legs[i], new Vector3(0.06f, 0.05f, 0.08f), new Vector3(0, -0.58f, -0.01f), Vector3.Zero, hoof);
        }
    }
}
