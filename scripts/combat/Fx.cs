using Godot;

namespace KatrinasGame;

/// <summary>Small one-off visual effects built in code: expanding rings and bursts, rising
/// sparkles, ember trails. Nothing here touches gameplay.</summary>
public static class Fx
{
    private static StandardMaterial3D Glow(Color c, float energy = 3f) => new()
    {
        AlbedoColor = c,
        EmissionEnabled = true,
        Emission = new Color(c.R, c.G, c.B),
        EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A flat ring on the ground that grows to <paramref name="radius"/> and fades.</summary>
    public static void Ring(Node parent, Vector3 at, Color color, float radius, float seconds)
    {
        if (parent == null) return;
        var mat = Glow(color);
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.88f, OuterRadius = 1f, Rings = 32, RingSegments = 6 },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = new Vector3(0.2f, 0.4f, 0.2f),
        };
        parent.AddChild(ring);
        ring.GlobalPosition = at + Vector3.Up * 0.15f;
        var t = ring.CreateTween().SetParallel();
        t.TweenProperty(ring, "scale", new Vector3(radius, 0.4f, radius), seconds).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        t.TweenProperty(mat, "albedo_color:a", 0f, seconds).SetEase(Tween.EaseType.In);
        t.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    /// <summary>A glowing ball that swells to <paramref name="radius"/> and fades (fire burst).</summary>
    public static void Burst(Node parent, Vector3 at, Color color, float radius, float seconds)
    {
        if (parent == null) return;
        var mat = Glow(color, 5f);
        var ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = Vector3.One * 0.2f,
        };
        parent.AddChild(ball);
        ball.GlobalPosition = at;
        var light = new OmniLight3D { LightColor = color, LightEnergy = 6f, OmniRange = radius * 3f };
        ball.AddChild(light);
        var t = ball.CreateTween().SetParallel();
        t.TweenProperty(ball, "scale", Vector3.One * radius * 0.8f, seconds).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        t.TweenProperty(mat, "albedo_color:a", 0f, seconds);
        t.TweenProperty(light, "light_energy", 0f, seconds);
        t.Chain().TweenCallback(Callable.From(ball.QueueFree));
        Sparks(parent, at, color, 24, 3f);
    }

    /// <summary>A puff of particles that drift up and fade (one shot, frees itself).</summary>
    public static void Sparks(Node parent, Vector3 at, Color color, int amount, float speed, float radius = 0.3f, float lifetime = 0.8f)
    {
        if (parent == null) return;
        var p = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Amount = amount,
            Lifetime = lifetime,
            Explosiveness = 0.9f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, 1.5f, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.2f,
            Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 4, Rings = 2 },
            Color = color,
        };
        var mat = Glow(color, 4f);
        mat.VertexColorUseAsAlbedo = true;
        p.MaterialOverride = mat;
        var ramp = new Gradient();
        ramp.SetColor(0, color);
        ramp.SetColor(1, new Color(color.R, color.G, color.B, 0f));
        p.ColorRamp = ramp;
        parent.AddChild(p);
        p.GlobalPosition = at;
        p.Emitting = true;
        p.Finished += p.QueueFree;
    }

    /// <summary>A continuous ember trail to parent to something moving.</summary>
    public static CpuParticles3D Trail(Color color)
    {
        var p = new CpuParticles3D
        {
            Amount = 30, Lifetime = 0.45f, LocalCoords = false,
            Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.8f,
            Gravity = new Vector3(0, 0.8f, 0),
            Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 4, Rings = 2 },
            ScaleAmountMin = 0.5f, ScaleAmountMax = 1.3f,
        };
        var mat = Glow(color, 5f);
        mat.VertexColorUseAsAlbedo = true;
        p.MaterialOverride = mat;
        var ramp = new Gradient();
        ramp.SetColor(0, color);
        ramp.SetColor(1, new Color(0.3f, 0.1f, 0.05f, 0f));
        p.ColorRamp = ramp;
        return p;
    }
}
