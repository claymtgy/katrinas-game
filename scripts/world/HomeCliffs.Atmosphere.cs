using Godot;

namespace KatrinasGame;

/// <summary>Art pass, atmosphere (the concept art's small living touches): smoke curling from
/// chimneys, windows that glow warm at dusk, billowing spray where waterfalls land, a faint
/// rainbow in the Great Falls' mist by day, and a market in Ashby's square with coloured
/// awnings and hanging lanterns. Scenery only.</summary>
public partial class HomeCliffs
{
    private static readonly Color AwningTeal = new(0.24f, 0.52f, 0.54f);
    private static readonly Color AwningMustard = new(0.86f, 0.64f, 0.24f);
    private static readonly Color AwningPlum = new(0.46f, 0.3f, 0.56f);
    private static readonly Color LanternGlow = new(1f, 0.68f, 0.34f);

    // ================================================================== chimney smoke

    private static StandardMaterial3D _smokeMat;

    /// <summary>A thin plume of smoke rising from <paramref name="localPos"/> (in <paramref name="parent"/>),
    /// swelling and fading as it drifts off with the wind.</summary>
    private void Smoke(Node3D parent, Vector3 localPos)
    {
        _smokeMat ??= new StandardMaterial3D
        {
            // Lit (not unshaded), so it greys down at dusk instead of glowing.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            Roughness = 1f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoTexture = SoftDot,
        };
        var fade = new Gradient
        {
            Offsets = new[] { 0f, 0.15f, 0.6f, 1f },
            Colors = new[] { new Color(0.86f, 0.84f, 0.86f, 0f), new Color(0.84f, 0.82f, 0.86f, 0.5f), new Color(0.8f, 0.8f, 0.86f, 0.28f), new Color(0.8f, 0.8f, 0.88f, 0f) },
        };
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.3f));
        grow.AddPoint(new Vector2(1f, 1f));
        var smoke = new GpuParticles3D
        {
            Name = "ChimneySmoke",
            Amount = 22,
            Lifetime = 7f,
            Preprocess = 7f,
            Position = localPos,
            DrawPass1 = new QuadMesh { Size = Vector2.One * 2.2f, Material = _smokeMat },
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 8f,
                InitialVelocityMin = 0.8f,
                InitialVelocityMax = 1.2f,
                Gravity = new Vector3(0.35f, 0.05f, 0.12f),   // drifts off on the breeze
                DampingMin = 0.05f,
                DampingMax = 0.15f,
                ScaleMin = 0.8f,
                ScaleMax = 1.2f,
                ScaleCurve = new CurveTexture { Curve = grow },
                ColorRamp = new GradientTexture1D { Gradient = fade },
            },
            VisibilityAabb = new Aabb(new Vector3(-8, -2, -8), new Vector3(20, 22, 20)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(smoke);
    }

    // ================================================================== window glow

    private static ShaderMaterial _windowMat;

    /// <summary>A warm pane set into a window hole (inside the wall's thickness, so it only shows
    /// through the opening): dim by day, glowing from dusk.</summary>
    private void WindowGlow(Node3D wallPiece)
    {
        _windowMat ??= new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type spatial;
render_mode unshaded, cull_disabled;
global uniform float night;
global uniform vec3 sun_dir;
uniform vec3 glow : source_color = vec3(1.0, 0.66, 0.32);
void fragment() {
    // A cross of mullions, and the light pooled toward the bottom.
    vec2 g = abs(UV - 0.5);
    float bar = step(min(g.x, g.y), 0.025);
    float dusk = clamp(night + (1.0 - smoothstep(0.0, 0.35, normalize(sun_dir).y)) * 0.8, 0.0, 1.0);
    vec3 lit = glow * (0.9 + 0.4 * UV.y);
    vec3 col = mix(vec3(0.12, 0.1, 0.1), lit * 2.2, dusk);
    ALBEDO = mix(col, vec3(0.1, 0.07, 0.05), bar);
}",
            },
        };
        wallPiece.AddChild(new MeshInstance3D
        {
            Name = "WindowGlow", Mesh = new QuadMesh { Size = new Vector2(1.5f, 1.5f) }, MaterialOverride = _windowMat,
            Position = new Vector3(0, 1.75f, -0.1f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // ================================================================== waterfall spray and the rainbow

    /// <summary>Big soft billows where a fall lands, so it fades into its own mist (concept 1).</summary>
    private void Spray(Vector3 basePos, float width)
    {
        Particles(28, 6f, new Vector3(width * 0.8f, 1.5f, 2.5f), basePos + Vector3.Up * 2.5f,
            new Color(1f, 1f, 1f, 0.2f), 4f, 9f, new Vector3(0, 0.8f, 0)).Name = "Spray";
        Particles(40, 1.6f, new Vector3(width * 0.6f, 0.5f, 1.5f), basePos + Vector3.Up * 0.8f,
            new Color(0.95f, 0.98f, 1f, 0.55f), 0.1f, 0.25f, new Vector3(0, 3.5f, 0)).Name = "Droplets";
    }

    /// <summary>A faint rainbow standing in the Great Falls' mist, by day (concept 4).</summary>
    private void Rainbow(Vector3 center, float width, float yaw)
    {
        var mat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled;
global uniform float night;
global uniform vec3 sun_dir;
global uniform float wetness;
vec3 hue(float h) { return clamp(abs(mod(h * 6.0 + vec3(0.0, 4.0, 2.0), 6.0) - 3.0) - 1.0, 0.0, 1.0); }
void fragment() {
    float d = length((UV - vec2(0.5, 1.0)) * vec2(1.0, 2.0));
    float band = smoothstep(0.62, 0.66, d) * (1.0 - smoothstep(0.86, 0.9, d));
    float h = clamp((d - 0.64) / 0.24, 0.0, 1.0) * 0.8;
    float ends = smoothstep(0.0, 0.25, UV.x) * smoothstep(1.0, 0.75, UV.x) * smoothstep(1.0, 0.7, UV.y);
    float day = (1.0 - night) * smoothstep(0.05, 0.3, normalize(sun_dir).y) * (1.0 - 0.6 * wetness);
    ALBEDO = hue(h) * band * ends * day * 0.3;
}",
            },
        };
        AddChild(new MeshInstance3D
        {
            Name = "Rainbow", Mesh = new QuadMesh { Size = new Vector2(width, width * 0.5f) }, MaterialOverride = mat,
            Position = center, Rotation = new Vector3(0, yaw, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // ================================================================== Ashby's market

    /// <summary>Market stalls round the well (village-local): coloured awnings, goods, a lantern each.</summary>
    private void MarketStalls(Node3D village)
    {
        Stall(village, new Vector3(9, 0, -1), AwningTeal, 0);
        Stall(village, new Vector3(-8.5f, 0, 1.5f), AwningMustard, 1);
        Stall(village, new Vector3(4, 0, 9.5f), AwningPlum, 2);
    }

    private void Stall(Node3D village, Vector3 at, Color awning, int seed)
    {
        var stall = new Node3D { Name = $"MarketStall{seed}", Position = at };
        village.AddChild(stall);
        var to = -at; // face the well
        stall.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);

        // Counter (solid), four posts, and a sloping cloth awning with a scalloped front edge.
        var counter = new StaticBody3D { Position = new Vector3(0, 0.45f, 0.5f), CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        counter.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.4f, 0.9f, 0.8f) } });
        counter.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.4f, 0.9f, 0.8f) }, MaterialOverride = Mat(Wood) });
        stall.AddChild(counter);
        foreach (var (x, z, h) in new[] { (-1.2f, 0.9f, 2.3f), (1.2f, 0.9f, 2.3f), (-1.2f, -0.9f, 2.7f), (1.2f, -0.9f, 2.7f) })
            Visual(new BoxMesh { Size = new Vector3(0.1f, h, 0.1f) }, Mat(WoodDark), new Vector3(x, h / 2f, z), Vector3.Zero, stall);
        var cloth = StyleMaterials.Toon(awning, 0.3f);
        Visual(new BoxMesh { Size = new Vector3(2.8f, 0.05f, 2.3f) }, cloth, new Vector3(0, 2.55f, 0.05f), new Vector3(-0.2f, 0, 0), stall);
        for (int i = 0; i < 6; i++)
            Visual(new CylinderMesh { TopRadius = 0.24f, BottomRadius = 0.24f, Height = 0.04f, RadialSegments = 10 }, cloth,
                new Vector3(-1.17f + i * 0.47f, 2.25f, 1.2f), new Vector3(Mathf.Pi / 2f, 0, 0), stall);
        // Goods on the counter and a crate or two.
        var goods = new[] { new Color(0.9f, 0.45f, 0.2f), new Color(0.55f, 0.7f, 0.3f), new Color(0.95f, 0.8f, 0.35f), new Color(0.7f, 0.25f, 0.25f) };
        for (int i = 0; i < 4; i++)
            Visual(new SphereMesh { Radius = 0.16f, Height = 0.26f }, StyleMaterials.Toon(goods[(i + seed) % goods.Length], 0.2f),
                new Vector3(-0.8f + i * 0.53f, 0.98f, 0.45f), Vector3.Zero, stall);
        Prop(Models.Village("Prop_Crate"), new Vector3(1.6f, 0, -0.2f), 0.4f + seed, 0.7f, stall);

        // A lantern hanging from the front post: a warm pool of light at night.
        var glow = new StandardMaterial3D { AlbedoColor = LanternGlow, EmissionEnabled = true, Emission = LanternGlow, EmissionEnergyMultiplier = 3f };
        Visual(new BoxMesh { Size = new Vector3(0.22f, 0.28f, 0.22f) }, glow, new Vector3(1.32f, 1.95f, 1.0f), Vector3.Zero, stall);
        Visual(new BoxMesh { Size = new Vector3(0.28f, 0.05f, 0.28f) }, Mat(WoodDark), new Vector3(1.32f, 2.12f, 1.0f), Vector3.Zero, stall);
        stall.AddChild(new OmniLight3D { Position = new Vector3(1.32f, 1.9f, 1.2f), LightColor = LanternGlow, LightEnergy = 1.1f, OmniRange = 6f });
    }
}
