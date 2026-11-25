using Godot;

namespace KatrinasGame;

/// <summary>Night predator: a big, heavy-shouldered dire wolf that a fresh level-1 player can't
/// fight head on (run, hide or parry). It never gets tougher with more players; the pack gets
/// bigger instead (Difficulty.PackSize). Flees at dawn. Low-poly faceted body built in code,
/// drawn at <see cref="Size"/> times its modelled size. Brain, targeting and replication:
/// Enemy / Wildlife.</summary>
public partial class Wolf : Enemy
{
    public const float MaxHealth = 170f;
    public const float BiteDamage = 24f;
    /// <summary>Thick fur: a flat amount off each hit (Health.Armor), so a wooden sword does ~9.</summary>
    public const float Armor = 6f;
    public const float Run = 7.2f;   // a sprinting player (8 m/s) can just outrun it
    public const float BiteWindup = 0.35f;
    /// <summary>Pelts a kill leaves: a dire wolf is a big hide, and a hard fight.</summary>
    public const int PeltsDropped = 2;
    /// <summary>Scale of the model and collider (the model is built at 1).</summary>
    public const float Size = 1.6f;

    /// <summary>An ash wolf of the Cinder Foothills: soot-black, ember-eyed, and it hunts by day
    /// (the haze there never lets the sun through).</summary>
    public bool Ash { get; private set; }
    public const float AshHealth = 200f, AshDamage = 26f;

    public override string Kind => Ash ? "ash_wolf" : "wolf";
    protected override Rect2 Bounds => Ash ? Wildlife.FoothillsBounds : base.Bounds;
    protected override float WalkSpeed => 3.2f;
    protected override float RunSpeed => Run;
    protected override float AggroRange => 22f;
    protected override float LeashRange => 34f;
    protected override float AttackRange => 2.4f;
    protected override float AttackReach => 3.2f;
    protected override float AttackWindup => BiteWindup;
    protected override float AttackCooldown => 1.1f;
    protected override bool FleesByDay => !Ash;
    protected override bool FearsFire => true;

    private Node3D _visual, _body, _head, _tail;
    private Node3D[] _legs, _shins;
    private ShaderMaterial _fur;
    private float _gait, _time, _idleFor, _howlLeft, _nextHowlRoll = 3f;
    private Tween _tween;

    public const float HowlSeconds = 2.6f;
    /// <summary>True during a (purely cosmetic, per-machine) howl.</summary>
    public bool Howling => _howlLeft > 0;

    private static readonly Color FurColor = new(0.3f, 0.29f, 0.35f);
    private static readonly Color FurLight = new(0.52f, 0.5f, 0.55f);
    private static readonly Color FurDark = new(0.16f, 0.15f, 0.2f);
    private static readonly Color EyeColor = new(1f, 0.62f, 0.18f);
    private static readonly Color AshFur = new(0.17f, 0.15f, 0.16f), AshLight = new(0.42f, 0.36f, 0.33f), AshDark = new(0.08f, 0.07f, 0.08f);
    private static readonly Color AshEye = new(1f, 0.35f, 0.1f);
    private Color Fur => Ash ? AshFur : FurColor;

    /// <summary>Build a wolf (call before adding it to the tree so its path and Health exist).</summary>
    public static Wolf Create(int netId, int pack, Vector3 position, float yaw, float maxHealth = MaxHealth, float damage = BiteDamage, bool ash = false)
    {
        var w = new Wolf { Name = $"Wolf_{netId}", Damage = damage, Ash = ash };
        w.Init(netId, pack, position, yaw, maxHealth, new Vector3(0.5f, 0.8f, 1.2f) * Size);
        w.Health.Armor = Armor;
        w.BuildVisual();
        return w;
    }

    public override void ServerDropLoot(Node killer)
    {
        var p = killer as PlayerController;
        if (Supplies.Give(p, "wolf_pelt", PeltsDropped) > 0 && p != null) Net.Instance.ToastTo(p.PeerId, $"+{PeltsDropped} Wolf Pelts");
        // Ash wolves den in the coal seams: soot and cinders caught in the fur.
        if (Ash && Supplies.Give(p, "coal", 1) > 0 && p != null) Net.Instance.ToastTo(p.PeerId, "+1 Coal");
    }

    // ================================================================== visual

    protected override void FlashHit()
    {
        if (_fur == null) return;
        var t = CreateTween();
        t.TweenProperty(_fur, "shader_parameter/tint", new Color(0.85f, 0.3f, 0.3f), 0.05);
        t.TweenProperty(_fur, "shader_parameter/tint", Fur, 0.25);
        _howlLeft = 0;
        Sfx.At(Health.IsDead ? "wolf_yelp" : "hit_flesh", GlobalPosition + Vector3.Up * 0.6f, Health.IsDead ? 2f : 0f);
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
        _howlLeft = 0;
        Sfx.At("wolf_snarl", GlobalPosition + Vector3.Up * 0.7f, 1f);
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "position", new Vector3(0, -0.06f, 0.15f) * Size, BiteWindup * 0.7).SetEase(Tween.EaseType.Out); // crouch back
        _tween.TweenProperty(_visual, "position", new Vector3(0, 0.08f, -0.45f) * Size, 0.09).SetTrans(Tween.TransitionType.Expo);
        _tween.TweenProperty(_visual, "position", Vector3.Zero, 0.3).SetTrans(Tween.TransitionType.Sine);
    }

    public override void PlayStun()
    {
        if (_visual == null || Current == State.Dead) return;
        _tween?.Kill();
        _tween = CreateTween();
        Sfx.At("wolf_yelp", GlobalPosition + Vector3.Up * 0.6f, -3f, 0.15f);
        _tween.TweenProperty(_visual, "rotation:z", 0.35f, 0.12);
        _tween.TweenProperty(_visual, "rotation:z", -0.25f, 0.25);
        _tween.TweenProperty(_visual, "rotation:z", 0f, 0.3);
        _tween.Parallel().TweenProperty(_visual, "position", Vector3.Zero, 0.3);
    }

    protected override void Animate(float dt)
    {
        _time += dt;
        if (_legs == null || Current == State.Dead) return;

        float sp = SpeedSmoothed;
        bool gallop = sp > 5.2f;
        // Trot: diagonal pairs. Gallop: front pair then hind pair, slightly split, body rocking.
        _gait += sp * dt * (gallop ? 2.3f : 3.4f) / Size;   // longer legs, longer strides
        float amp = Mathf.Clamp(sp / 3.2f, 0f, 1f) * (gallop ? 0.95f : 0.6f);
        for (int i = 0; i < 4; i++)
        {
            float a = _gait + (gallop ? GallopPhase[i] : TrotPhase[i]);
            bool hind = i >= 2;
            _legs[i].Rotation = new Vector3(Mathf.Sin(a) * amp, 0, 0);
            // Fold the lower leg while it swings forward (paw lifted clear of the ground).
            float lift = Mathf.Max(0f, Mathf.Cos(a)) * amp * 1.3f;
            _shins[i].Rotation = new Vector3(hind ? lift * 0.8f : -lift, 0, 0);
        }

        // Idle: breathing, a slow look around, the odd sniff at the ground; at night the occasional howl.
        float idle = 1f - Mathf.Clamp(sp / 1.2f, 0f, 1f);
        _idleFor = idle > 0.9f ? _idleFor + dt : 0f;
        if (_howlLeft > 0) _howlLeft -= dt;
        else if (_idleFor > 3f && (_nextHowlRoll -= dt) <= 0)
        {
            _nextHowlRoll = 4f;
            if (!Wildlife.IsDay(GameState.Instance.TimeOfDay) && GD.Randf() < 0.18f) StartHowl();
        }
        if (sp > 0.6f) _howlLeft = 0;
        float howl = _howlLeft > 0 ? Mathf.Clamp(Mathf.Min(HowlSeconds - _howlLeft, _howlLeft) / 0.35f, 0f, 1f) : 0f;

        float bob = gallop ? Mathf.Abs(Mathf.Sin(_gait)) * 0.07f : Mathf.Abs(Mathf.Sin(_gait * 2f)) * amp * 0.03f;
        float breathe = Mathf.Sin(_time * 2.1f) * 0.008f * idle;
        float pitch = (gallop ? Mathf.Sin(_gait + 0.6f) * 0.07f : 0f) + howl * 0.14f;
        _body.Position = new Vector3(0, bob + breathe - howl * 0.05f, 0) * Size;
        _body.Rotation = new Vector3(pitch, 0, 0);

        float sniff = Mathf.SmoothStep(0.75f, 1f, Mathf.Max(0f, Mathf.Sin(_time * 0.45f + NetId)));
        float headX = Mathf.Sin(_gait * 2f) * amp * 0.08f - (gallop ? 0.2f : 0f)
                      + idle * sniff * (-0.45f + Mathf.Sin(_time * 7f) * 0.03f)
                      + howl * 0.95f;
        float headY = idle * Mathf.Sin(_time * 0.37f + NetId * 1.7f) * 0.45f * (1f - howl);
        _head.Rotation = new Vector3(headX, headY, 0);
        _tail.Rotation = new Vector3(0.5f + amp * 0.4f - idle * 0.3f + howl * 0.2f,
            Mathf.Sin(_time * (1.2f + sp)) * (0.35f - idle * 0.2f), 0);
    }

    private static readonly float[] TrotPhase = { 0f, Mathf.Pi, Mathf.Pi, 0f };
    private static readonly float[] GallopPhase = { 0f, 0.45f, Mathf.Pi * 0.9f, Mathf.Pi * 1.2f };

    /// <summary>Start a howl on this machine (idle at night; also used by the autotest).</summary>
    public void StartHowl()
    {
        _howlLeft = HowlSeconds;
        Sfx.At("wolf_howl", GlobalPosition + Vector3.Up * 1f, 4f, 0.1f, 90f);
    }

    private void BuildVisual()
    {
        // Low-poly faceted wolf (art pass): tapered 5/6-sided segments rather than boxes. Lean,
        // deep-chested, head low, bushy tail. Forward is -Z; legs hang from hip pivots.
        _fur = StyleMaterials.CharacterFlat(Fur);
        var light = StyleMaterials.CharacterFlat(Ash ? AshLight : FurLight);
        var dark = StyleMaterials.CharacterFlat(Ash ? AshDark : FurDark);
        var eye = Ash ? AshEye : EyeColor;
        var eyes = new StandardMaterial3D
        {
            AlbedoColor = eye, EmissionEnabled = true, Emission = eye, EmissionEnergyMultiplier = Ash ? 5f : 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        _visual = new Node3D { Name = "Visual" };   // tweened: lunge, stagger, death
        AddChild(_visual);
        _visual.AddChild(_body = new Node3D { Name = "Body" });   // procedural: bob, gallop rock, howl
        _body.Scale = Vector3.One * Size;

        // Torso: chest to hips, then the shoulder hump and the pale chest ruff and belly.
        Seg(_body, new Vector3(0, 0.74f, -0.38f), new Vector3(0, 0.7f, 0.46f), 0.27f, 0.19f, _fur, 6);
        Seg(_body, new Vector3(0, 0.86f, -0.3f), new Vector3(0, 0.8f, 0.05f), 0.17f, 0.12f, dark, 5);           // hackles
        Seg(_body, new Vector3(0, 0.6f, -0.42f), new Vector3(0, 0.78f, -0.2f), 0.24f, 0.2f, light, 6);          // ruff
        Seg(_body, new Vector3(0, 0.58f, -0.15f), new Vector3(0, 0.6f, 0.3f), 0.15f, 0.1f, light, 5);           // belly

        _head = new Node3D { Name = "Head", Position = new Vector3(0, 0.86f, -0.56f) };
        _body.AddChild(_head);
        Seg(_head, new Vector3(0, -0.1f, 0.18f), new Vector3(0, 0.02f, -0.05f), 0.15f, 0.13f, _fur, 6);            // neck
        _head.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.17f, Height = 0.3f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = _fur, Position = new Vector3(0, 0.03f, -0.1f),
        });
        Seg(_head, new Vector3(0, -0.01f, -0.2f), new Vector3(0, -0.06f, -0.48f), 0.1f, 0.045f, light, 5);       // muzzle
        Seg(_head, new Vector3(0, -0.07f, -0.22f), new Vector3(0, -0.1f, -0.42f), 0.06f, 0.03f, dark, 5);        // jaw
        Box(_head, new Vector3(0.06f, 0.05f, 0.05f), new Vector3(0, -0.03f, -0.49f), Vector3.Zero, dark);        // nose
        foreach (float side in new[] { -1f, 1f })
        {
            _head.AddChild(new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(0.1f, 0.17f, 0.05f) },
                MaterialOverride = dark, Position = new Vector3(side * 0.09f, 0.21f, -0.06f), Rotation = new Vector3(-0.2f, 0, side * -0.25f),
            });
            Box(_head, new Vector3(0.055f, 0.03f, 0.02f), new Vector3(side * 0.075f, 0.06f, -0.235f), new Vector3(0, 0, side * -0.25f), eyes);
        }

        _tail = new Node3D { Name = "Tail", Position = new Vector3(0, 0.78f, 0.56f) };
        _body.AddChild(_tail);
        Seg(_tail, Vector3.Zero, new Vector3(0, -0.05f, 0.3f), 0.07f, 0.11f, _fur, 5);
        Seg(_tail, new Vector3(0, -0.05f, 0.3f), new Vector3(0, -0.12f, 0.6f), 0.11f, 0.03f, dark, 5);

        // Legs: thigh / shin with a backward hock on the hind legs, and a paw.
        _legs = new Node3D[4];
        _shins = new Node3D[4];
        var hips = new[] { new Vector3(-0.13f, 0.66f, -0.36f), new Vector3(0.13f, 0.66f, -0.36f), new Vector3(-0.13f, 0.66f, 0.4f), new Vector3(0.13f, 0.66f, 0.4f) };
        for (int i = 0; i < 4; i++)
        {
            bool hind = i >= 2;
            _legs[i] = new Node3D { Name = $"Leg{i}", Position = hips[i] };
            _body.AddChild(_legs[i]);
            var knee = hind ? new Vector3(0, -0.3f, -0.08f) : new Vector3(0, -0.32f, 0.02f);
            var ankle = hind ? new Vector3(0, -0.5f, 0.06f) : new Vector3(0, -0.58f, 0f);
            Seg(_legs[i], Vector3.Zero, knee, hind ? 0.12f : 0.09f, 0.06f, hind ? _fur : light, 5);
            // Lower leg hangs from a knee (front) / hock (hind) pivot so it can fold.
            _shins[i] = new Node3D { Name = "Shin", Position = knee };
            _legs[i].AddChild(_shins[i]);
            Seg(_shins[i], Vector3.Zero, ankle - knee, 0.055f, 0.045f, hind ? _fur : light, 5);
            if (hind) Seg(_shins[i], ankle - knee, new Vector3(0, -0.63f, 0.02f) - knee, 0.045f, 0.04f, _fur, 5);
            Box(_shins[i], new Vector3(0.1f, 0.05f, 0.14f), new Vector3(0, -0.645f, -0.03f) - knee, Vector3.Zero, dark);
        }
    }

    /// <summary>A tapered n-sided segment from a to b (radius ra at a, rb at b).</summary>
    private static void Seg(Node3D parent, Vector3 a, Vector3 b, float ra, float rb, Material mat, int sides)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.001f) return;
        var y = d / len;
        var x = Mathf.Abs(y.Dot(Vector3.Forward)) > 0.95f ? y.Cross(Vector3.Right).Normalized() : y.Cross(Vector3.Forward).Normalized();
        var z = x.Cross(y);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = rb, BottomRadius = ra, Height = len, RadialSegments = sides, Rings = 1 },
            MaterialOverride = mat,
            Transform = new Transform3D(new Basis(x, y, z), (a + b) / 2f),
        });
    }

    private static void Box(Node3D parent, Vector3 size, Vector3 pos, Vector3 rot, Material mat) =>
        parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, Rotation = rot, MaterialOverride = mat });
}
