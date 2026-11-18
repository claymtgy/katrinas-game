using Godot;

namespace KatrinasGame;

/// <summary>Night predator. A small animal: it never gets tougher with more players; the pack
/// gets bigger instead (Difficulty.PackSize). Flees at dawn. Greybox body built from boxes.
/// Brain, targeting and replication: Enemy / Wildlife.</summary>
public partial class Wolf : Enemy
{
    public const float MaxHealth = 40f;
    public const float BiteDamage = 8f;
    public const float Run = 7.2f;   // a sprinting player (8 m/s) can just outrun it
    public const float BiteWindup = 0.35f;

    public override string Kind => "wolf";
    protected override float WalkSpeed => 3.2f;
    protected override float RunSpeed => Run;
    protected override float AggroRange => 22f;
    protected override float LeashRange => 34f;
    protected override float AttackRange => 1.8f;
    protected override float AttackReach => 2.4f;
    protected override float AttackWindup => BiteWindup;
    protected override float AttackCooldown => 1.4f;
    protected override bool FleesByDay => true;

    private Node3D _visual, _head, _tail;
    private Node3D[] _legs;
    private StandardMaterial3D _fur;
    private float _gait, _time;
    private Tween _tween;

    private static readonly Color FurColor = new(0.3f, 0.29f, 0.35f);
    private static readonly Color FurLight = new(0.52f, 0.5f, 0.55f);
    private static readonly Color FurDark = new(0.16f, 0.15f, 0.2f);
    private static readonly Color EyeColor = new(1f, 0.62f, 0.18f);

    /// <summary>Build a wolf (call before adding it to the tree so its path and Health exist).</summary>
    public static Wolf Create(int netId, int pack, Vector3 position, float yaw, float maxHealth = MaxHealth, float damage = BiteDamage)
    {
        var w = new Wolf { Name = $"Wolf_{netId}", Damage = damage };
        w.Init(netId, pack, position, yaw, maxHealth, new Vector3(0.5f, 0.8f, 1.2f));
        w.BuildVisual();
        return w;
    }

    public override void ServerDropLoot(Node killer)
    {
        GameState.Instance.Inventory.Add("wolf_pelt", 1);
        if (killer is PlayerController p) Net.Instance.ToastTo(p.PeerId, "+1 Wolf Pelt");
    }

    // ================================================================== visual

    protected override void FlashHit()
    {
        if (_fur == null) return;
        var t = CreateTween();
        t.TweenProperty(_fur, "albedo_color", new Color(0.85f, 0.3f, 0.3f), 0.05);
        t.TweenProperty(_fur, "albedo_color", FurColor, 0.25);
    }

    protected override void PlayDeath()
    {
        if (_visual == null) return;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_visual, "rotation:z", Mathf.Pi / 2f, 0.45).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "position:y", -0.2f, 0.45);
    }

    /// <summary>Everyone: the bite lunge.</summary>
    public override void PlayAttack()
    {
        if (_visual == null || Current == State.Dead) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "position", new Vector3(0, -0.06f, 0.15f), BiteWindup * 0.7).SetEase(Tween.EaseType.Out); // crouch back
        _tween.TweenProperty(_visual, "position", new Vector3(0, 0.08f, -0.45f), 0.09).SetTrans(Tween.TransitionType.Expo);
        _tween.TweenProperty(_visual, "position", Vector3.Zero, 0.3).SetTrans(Tween.TransitionType.Sine);
    }

    public override void PlayStun()
    {
        if (_visual == null || Current == State.Dead) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:z", 0.35f, 0.12);
        _tween.TweenProperty(_visual, "rotation:z", -0.25f, 0.25);
        _tween.TweenProperty(_visual, "rotation:z", 0f, 0.3);
        _tween.Parallel().TweenProperty(_visual, "position", Vector3.Zero, 0.3);
    }

    protected override void Animate(float dt)
    {
        _time += dt;
        if (_legs == null || Current == State.Dead) return;

        _gait += SpeedSmoothed * dt * 3.4f;
        float amp = Mathf.Clamp(SpeedSmoothed / Run, 0f, 1f) * 0.75f;
        for (int i = 0; i < 4; i++)
        {
            float phase = (i == 0 || i == 3) ? 0f : Mathf.Pi; // trot: diagonal pairs
            _legs[i].Rotation = new Vector3(Mathf.Sin(_gait + phase) * amp, 0, 0);
        }
        _tail.Rotation = new Vector3(0.5f + amp * 0.4f, Mathf.Sin(_time * (2f + SpeedSmoothed)) * 0.35f, 0);
        _head.Rotation = new Vector3(Mathf.Sin(_gait * 2f) * amp * 0.08f - (SpeedSmoothed > 5f ? 0.15f : 0f), 0, 0);
    }

    private void BuildVisual()
    {
        _fur = StyleMaterials.Toon(FurColor, 0.35f);
        var light = StyleMaterials.Toon(FurLight, 0.3f);
        var dark = StyleMaterials.Toon(FurDark, 0.2f);
        var eyes = new StandardMaterial3D
        {
            AlbedoColor = EyeColor, EmissionEnabled = true, Emission = EyeColor, EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);
        // Forward is -Z. Chunky faceted shapes: torso, chest ruff, head, snout, ears, tail.
        Box(_visual, new Vector3(0.42f, 0.4f, 1.0f), new Vector3(0, 0.66f, 0.05f), Vector3.Zero, _fur);
        Box(_visual, new Vector3(0.5f, 0.5f, 0.42f), new Vector3(0, 0.72f, -0.36f), new Vector3(-0.15f, 0, 0), light);
        Box(_visual, new Vector3(0.34f, 0.12f, 0.8f), new Vector3(0, 0.5f, 0.05f), Vector3.Zero, light); // belly

        _head = new Node3D { Name = "Head", Position = new Vector3(0, 0.88f, -0.58f) };
        _visual.AddChild(_head);
        Box(_head, new Vector3(0.34f, 0.3f, 0.34f), new Vector3(0, 0.02f, -0.1f), Vector3.Zero, _fur);
        Box(_head, new Vector3(0.18f, 0.14f, 0.3f), new Vector3(0, -0.05f, -0.38f), new Vector3(0.12f, 0, 0), light);
        Box(_head, new Vector3(0.07f, 0.06f, 0.06f), new Vector3(0, -0.01f, -0.54f), Vector3.Zero, dark); // nose
        Box(_head, new Vector3(0.09f, 0.16f, 0.06f), new Vector3(-0.11f, 0.22f, -0.04f), new Vector3(0, 0, 0.2f), dark);
        Box(_head, new Vector3(0.09f, 0.16f, 0.06f), new Vector3(0.11f, 0.22f, -0.04f), new Vector3(0, 0, -0.2f), dark);
        Box(_head, new Vector3(0.06f, 0.04f, 0.02f), new Vector3(-0.09f, 0.07f, -0.27f), Vector3.Zero, eyes);
        Box(_head, new Vector3(0.06f, 0.04f, 0.02f), new Vector3(0.09f, 0.07f, -0.27f), Vector3.Zero, eyes);

        _tail = new Node3D { Name = "Tail", Position = new Vector3(0, 0.78f, 0.55f) };
        _visual.AddChild(_tail);
        Box(_tail, new Vector3(0.13f, 0.13f, 0.55f), new Vector3(0, 0, 0.27f), Vector3.Zero, dark);

        _legs = new Node3D[4];
        var hips = new[] { new Vector3(-0.15f, 0.55f, -0.36f), new Vector3(0.15f, 0.55f, -0.36f), new Vector3(-0.15f, 0.55f, 0.38f), new Vector3(0.15f, 0.55f, 0.38f) };
        for (int i = 0; i < 4; i++)
        {
            _legs[i] = new Node3D { Name = $"Leg{i}", Position = hips[i] };
            _visual.AddChild(_legs[i]);
            Box(_legs[i], new Vector3(0.12f, 0.55f, 0.13f), new Vector3(0, -0.27f, 0), Vector3.Zero, i < 2 ? light : _fur);
        }
    }

    private static void Box(Node3D parent, Vector3 size, Vector3 pos, Vector3 rot, Material mat) =>
        parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, Rotation = rot, MaterialOverride = mat });
}
