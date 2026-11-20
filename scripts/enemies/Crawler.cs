using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Cave crawler (GDD §11, Hollow Mine): a low-poly faceted spider the size of a dog.
/// It hates light. It won't step into the circle of a torch, a fire, the daylight shaft or a
/// worn lantern, and it only strikes from the dark: it waits at the edge of the light and
/// lunges in from there. A lantern's circle is small enough that it still reaches you, so
/// lighting the mine with torches is how you clear it.
///
/// The Crawler Mother (<see cref="Mother"/>) is the boss at the bottom of the mine: big, slow,
/// armoured, not put off by light at all, and she calls her brood to her while she fights. She
/// carries the Bellows Plans. Brain, targeting and replication: Enemy / Wildlife.</summary>
public partial class Crawler : Enemy
{
    public const float MaxHealth = 55f;
    /// <summary>The Mother's brood: a little weaker, since more keep coming.</summary>
    public const float BroodHealth = 40f;
    public const float BiteDamage = 13f;
    public const float Armor = 2f;
    public const float LungeWindup = 0.4f;

    public const float MotherHealth = 650f;
    public const float MotherDamage = 26f;
    public const float MotherArmor = 5f;
    /// <summary>Seconds between calls to her brood while she fights, and how many at most at once.</summary>
    public const float BroodInterval = 11f;
    public const int MaxBrood = 3;
    public const int MotherCoins = 30, MotherShards = 3;

    /// <summary>How far a crawler keeps out past the edge of a light.</summary>
    public const float LightMargin = 0.6f;

    public bool Mother { get; private set; }
    private float Size => Mother ? 2.3f : 1f;

    public override string Kind => Mother ? "crawler_mother" : "crawler";
    protected override float WalkSpeed => Mother ? 1.8f : 2.6f;
    protected override float RunSpeed => Mother ? 4.8f : 6.8f;
    protected override float AggroRange => Mother ? 18f : 15f;
    protected override float LeashRange => Mother ? 32f : 28f;
    protected override float HomeLeash => Mother ? 20f : 30f;
    /// <summary>Starts a lunge from here: just past the edge of a lantern's circle (radius + margin).</summary>
    protected override float AttackRange => Mother ? 3.8f : LightSources.LanternRadius + LightMargin + 0.3f;
    /// <summary>The lunge: a crawler bites from outside a lantern's circle, not from deep in a torch's.</summary>
    protected override float AttackReach => Mother ? 4.8f : 5f;
    protected override float AttackWindup => Mother ? 0.65f : LungeWindup;
    protected override float AttackCooldown => Mother ? 1.8f : 1.4f;
    protected override float HitStagger => Mother ? 0.12f : 0.35f;
    protected override float HitKnockback => Mother ? 1f : 5f;
    protected override float WanderRadius => Mother ? 4f : 6f;
    protected override Rect2 Bounds => HomeCliffs.MineBounds;

    /// <summary>Out of the light (the Mother doesn't care).</summary>
    protected override Vector3 ExtraPush(Vector3 pos) =>
        Mother ? Vector3.Zero : LightSources.Push(pos, LightMargin) * 1.6f;

    /// <summary>Only strikes from the dark.</summary>
    protected override bool MayAttack => Mother || !LightSources.Lit(GlobalPosition);

    private float _broodIn = BroodInterval * 0.5f;

    protected override void Think(float dt)
    {
        base.Think(dt);
        if (!Mother || Current is State.Dead or State.Prowl || Target == null) return;
        _broodIn -= dt;
        if (_broodIn > 0) return;
        _broodIn = BroodInterval;
        var wl = Wildlife.Instance;
        int brood = wl.Crawlers.Count(c => c != this && c.Group == Group && c.Current != State.Dead);
        if (brood >= MaxBrood) return;
        wl.SpawnBrood(this);
        wl.BroadcastAnim(this, Wildlife.AnimStun); // rears up and shrieks
        Net.Instance.ToastAll("The Crawler Mother shrieks, and something skitters out of the dark…");
    }

    private Node3D _visual, _body, _abdomen;
    private Node3D[] _legs, _shins;
    private ShaderMaterial _shell;
    private StandardMaterial3D _eyes;
    private float _gait, _time;
    private Tween _tween;

    private static readonly Color ShellColor = new(0.2f, 0.18f, 0.24f);
    private static readonly Color ShellLight = new(0.34f, 0.3f, 0.38f);
    private static readonly Color ShellDark = new(0.09f, 0.08f, 0.11f);
    private static readonly Color MotherShell = new(0.26f, 0.15f, 0.22f);
    private static readonly Color EyeColor = new(0.55f, 1f, 0.75f);
    private static readonly Color SacColor = new(0.85f, 0.6f, 0.35f);

    public static Crawler Create(int netId, int group, Vector3 position, float yaw, float maxHealth, float damage, bool mother)
    {
        var c = new Crawler { Name = mother ? $"CrawlerMother_{netId}" : $"Crawler_{netId}", Mother = mother, Damage = damage };
        float s = mother ? 2.3f : 1f;
        c.Init(netId, group, position, yaw, maxHealth, new Vector3(1.0f, 0.5f, 1.1f) * s);
        c.Health.Armor = mother ? MotherArmor : Armor;
        c.BuildVisual();
        return c;
    }

    public override void ServerDropLoot(Node killer)
    {
        var p = killer as PlayerController;
        if (!Mother)
        {
            // Now and then a rune shard, glinting in what's left of the nest.
            if (GD.Randf() < 0.2f && Supplies.Give(p, Gear.ShardId, 1) > 0 && p != null) Net.Instance.ToastTo(p.PeerId, "+1 Rune Shard");
            return;
        }
        var gs = GameState.Instance;
        string msg = $"+{MotherCoins} Coins, +{MotherShards} Rune Shards";
        Supplies.Give(p, "coin", MotherCoins);
        Supplies.Give(p, Gear.ShardId, MotherShards);
        bool havePlans = gs.Inventory.Count("bellows_plans") > 0 || gs.Players.Values.Any(pl => pl.Pack.Count("bellows_plans") > 0)
                         || (Journal.Instance?.Facts.Contains("bellows_plans") ?? false);
        if (!havePlans)
        {
            Supplies.Give(p, "bellows_plans", 1);
            Journal.Fact("bellows_plans");
            msg += ", Bellows Plans!";
            Net.Instance.ToastAll("Tangled in the Mother's nest: old plans for a smelter's bellows, the key to working iron properly.");
        }
        Journal.Fact("mother_slain");
        if (p != null) Net.Instance.ToastTo(p.PeerId, msg);
    }

    // ================================================================== visual

    protected override void FlashHit()
    {
        if (_shell == null) return;
        var t = CreateTween();
        t.TweenProperty(_shell, "shader_parameter/tint", new Color(0.85f, 0.35f, 0.35f), 0.05);
        t.TweenProperty(_shell, "shader_parameter/tint", Mother ? MotherShell : ShellColor, 0.25);
        Sfx.At(Health.IsDead ? "rustle" : "hit_flesh", GlobalPosition + Vector3.Up * 0.4f * Size, Mother ? 2f : -2f, 0.2f);
    }

    protected override void PlayDeath()
    {
        if (_visual == null) return;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        // Flips over, legs curling in.
        _tween.TweenProperty(_visual, "rotation:z", Mathf.Pi * 0.95f, 0.4).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "position:y", 0.45f * Size, 0.4);
        if (_legs != null)
            foreach (var l in _legs) _tween.TweenProperty(l, "rotation:z", l.Position.X < 0 ? -0.9f : 0.9f, 0.5);
        if (_eyes != null) _tween.TweenProperty(_eyes, "emission_energy_multiplier", 0f, 0.8);
    }

    /// <summary>Everyone: rear back, then spring forward with the fangs.</summary>
    public override void PlayAttack()
    {
        if (_visual == null || Current == State.Dead) return;
        Sfx.At("wolf_snarl", GlobalPosition + Vector3.Up * 0.4f * Size, Mother ? 3f : -4f, 0.3f);
        Sfx.At("rustle", GlobalPosition, 0f, 0.2f);
        _tween?.Kill();
        _tween = CreateTween();
        float w = AttackWindup;
        _tween.TweenProperty(_visual, "position", new Vector3(0, 0.12f, 0.35f) * Size, w * 0.85).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_visual, "rotation:x", 0.35f, w * 0.85);
        _tween.TweenProperty(_visual, "position", new Vector3(0, 0.05f, -1.6f) * Size, 0.1).SetTrans(Tween.TransitionType.Expo);
        _tween.Parallel().TweenProperty(_visual, "rotation:x", -0.1f, 0.1);
        _tween.TweenProperty(_visual, "position", Vector3.Zero, 0.4).SetTrans(Tween.TransitionType.Sine);
        _tween.Parallel().TweenProperty(_visual, "rotation:x", 0f, 0.4);
    }

    /// <summary>Stunned (a parry), or the Mother rearing up to call her brood.</summary>
    public override void PlayStun()
    {
        if (_visual == null || Current == State.Dead) return;
        _tween?.Kill();
        _tween = CreateTween();
        if (Mother) Sfx.At("harpy_screech", GlobalPosition + Vector3.Up * 1.5f, 3f, 0.05f, 70f);
        _tween.TweenProperty(_visual, "rotation:x", 0.6f, 0.2);
        _tween.TweenProperty(_visual, "rotation:x", 0f, 0.5).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        _tween.Parallel().TweenProperty(_visual, "position", Vector3.Zero, 0.3);
    }

    protected override void Animate(float dt)
    {
        _time += dt;
        if (_legs == null || Current == State.Dead) return;
        float sp = SpeedSmoothed;
        _gait += sp * dt * 5f / Size;
        float amp = Mathf.Clamp(sp / 2.5f, 0f, 1f) * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            // Alternating tetrapods: legs 0,3,4,7 then 1,2,5,6.
            float a = _gait + ((i is 0 or 3 or 4 or 7) ? 0f : Mathf.Pi);
            float swing = Mathf.Sin(a) * amp;
            float lift = Mathf.Max(0f, Mathf.Cos(a)) * amp;
            float side = _legs[i].Position.X < 0 ? -1f : 1f;
            _legs[i].Rotation = new Vector3(0, swing, side * (0.15f + lift * 0.6f));
            _shins[i].Rotation = new Vector3(0, 0, side * -lift * 0.5f);
        }
        float idle = 1f - Mathf.Clamp(sp / 1f, 0f, 1f);
        _body.Position = new Vector3(0, Mathf.Sin(_time * 3f + NetId) * 0.01f + Mathf.Abs(Mathf.Sin(_gait * 2f)) * amp * 0.03f, 0) * Size;
        // The abdomen pulses; twitchy little turns while it waits.
        float pulse = 1f + Mathf.Sin(_time * (Mother ? 1.6f : 2.6f)) * 0.04f;
        _abdomen.Scale = new Vector3(pulse, pulse * 0.92f, pulse);
        _body.Rotation = new Vector3(0, idle * Mathf.Sin(_time * 1.3f + NetId * 2.1f) * 0.12f, 0);
    }

    private void BuildVisual()
    {
        _shell = StyleMaterials.CharacterFlat(Mother ? MotherShell : ShellColor);
        var light = StyleMaterials.CharacterFlat(ShellLight);
        var dark = StyleMaterials.CharacterFlat(ShellDark);
        _eyes = EnemyMesh.Glow(EyeColor, Mother ? 4f : 3f);

        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);
        _visual.AddChild(_body = new Node3D { Name = "Body" });
        _body.Scale = Vector3.One * Size;

        // Head-and-thorax in front, the big abdomen behind (forward is -Z).
        EnemyMesh.Ball(_body, 0.22f, new Vector3(0, 0.36f, -0.18f), new Vector3(1f, 0.7f, 1.15f), _shell);
        EnemyMesh.Ball(_body, 0.12f, new Vector3(0, 0.34f, -0.42f), new Vector3(1.1f, 0.8f, 0.9f), dark, 5);
        _abdomen = new Node3D { Name = "Abdomen", Position = new Vector3(0, 0.46f, 0.3f) };
        _body.AddChild(_abdomen);
        EnemyMesh.Ball(_abdomen, 0.34f, Vector3.Zero, new Vector3(0.95f, 0.8f, 1.15f), _shell, 7);
        // Pale chevrons down its back.
        for (int k = 0; k < 3; k++)
            EnemyMesh.Box(_abdomen, new Vector3(0.22f - k * 0.05f, 0.03f, 0.05f), new Vector3(0, 0.27f - k * 0.02f, -0.1f + k * 0.13f), new Vector3(0.3f, 0, 0), light);
        if (Mother)
        {
            // An egg sac slung under her, glowing faintly.
            EnemyMesh.Ball(_abdomen, 0.2f, new Vector3(0, -0.2f, 0.2f), new Vector3(1.2f, 0.8f, 1f), EnemyMesh.Glow(SacColor, 0.8f), 6);
            for (int k = 0; k < 5; k++)
                EnemyMesh.Seg(_body, new Vector3(-0.16f + k * 0.08f, 0.5f, -0.3f), new Vector3(-0.2f + k * 0.1f, 0.68f, -0.1f), 0.03f, 0.005f, light, 4); // spines
        }

        // Eyes: a cluster of glints, and the fangs under them.
        foreach (var (ex, ey, r) in new[] { (-0.06f, 0.42f, 0.03f), (0.06f, 0.42f, 0.03f), (-0.12f, 0.4f, 0.02f), (0.12f, 0.4f, 0.02f), (-0.03f, 0.46f, 0.018f), (0.03f, 0.46f, 0.018f) })
            EnemyMesh.Ball(_body, r, new Vector3(ex, ey, -0.5f), Vector3.One, _eyes, 4);
        foreach (float side in new[] { -1f, 1f })
            EnemyMesh.Seg(_body, new Vector3(side * 0.05f, 0.3f, -0.5f), new Vector3(side * 0.03f, 0.18f, -0.56f), 0.025f, 0.006f, light, 4);

        // Eight legs: a high knee, then down to a point.
        _legs = new Node3D[8];
        _shins = new Node3D[8];
        for (int i = 0; i < 8; i++)
        {
            float side = i < 4 ? -1f : 1f;
            int k = i % 4;
            float z = -0.32f + k * 0.13f;
            float spread = (-0.75f + k * 0.5f); // front legs reach forward, back legs back
            _legs[i] = new Node3D { Name = $"Leg{i}", Position = new Vector3(side * 0.16f, 0.36f, z) };
            _body.AddChild(_legs[i]);
            var knee = new Vector3(side * 0.34f, 0.2f, spread * 0.3f);
            var foot = new Vector3(side * 0.62f, -0.36f, spread * 0.55f);
            EnemyMesh.Seg(_legs[i], Vector3.Zero, knee, 0.04f, 0.03f, _shell, 4);
            _shins[i] = new Node3D { Name = "Shin", Position = knee };
            _legs[i].AddChild(_shins[i]);
            EnemyMesh.Seg(_shins[i], Vector3.Zero, foot - knee, 0.03f, 0.008f, dark, 4);
        }
    }
}
