using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Greybox of the starting zone, generated in code so it's easy to iterate.
/// North (-Z) is the cliff edge over a sea of clouds; the shack and the sacred blossom
/// tree sit there. East/west are high rock walls with waterfalls. A road leads south
/// toward the Village of Ashby. The volcano glows far to the north-west.
/// Later this gets replaced piece by piece with sculpted art in the editor.</summary>
public partial class HomeCliffs : Node3D
{
    [Export] public int Seed { get; set; } = 7;

    // Palette.
    private static readonly Color Obsidian = new(0.09f, 0.08f, 0.12f);
    private static readonly Color Slate = new(0.24f, 0.25f, 0.33f);
    private static readonly Color SlateLight = new(0.34f, 0.35f, 0.44f);
    private static readonly Color Grass = new(0.33f, 0.45f, 0.32f);
    private static readonly Color Dirt = new(0.5f, 0.4f, 0.3f);
    private static readonly Color Wood = new(0.45f, 0.3f, 0.2f);
    private static readonly Color WoodDark = new(0.28f, 0.18f, 0.13f);
    private static readonly Color Magma = new(1f, 0.42f, 0.1f);
    private static readonly Color Blush = new(1f, 0.62f, 0.76f);

    private static readonly Vector3 ShackPos = new(9, 0, -21);
    private static readonly Vector3 BlossomPos = new(-7, 0, -25);
    private static readonly Vector3 PoolPos = new(-44, 0, -4);

    private RandomNumberGenerator _rng;
    private readonly Dictionary<Color, StandardMaterial3D> _mats = new();

    public override void _Ready()
    {
        _rng = new RandomNumberGenerator { Seed = (ulong)Seed };
        var gs = GameState.Instance;

        AddChild(new DayNight { Name = "DayNight" });

        var buildings = new Node3D { Name = "Buildings" };
        AddChild(buildings);
        gs.BuildingsRoot = buildings;
        gs.ClaimCenter = ShackPos;
        gs.ClaimRadius = 30f;
        gs.SpawnPoint = BlossomPos + new Vector3(2.5f, 0.2f, 3.5f);

        BuildGround();
        BuildCliffFace();
        BuildSideWalls();
        BuildCloudSea();
        BuildShack();
        BuildBlossomTree();
        BuildWaterfalls();
        BuildRoadAndSign();
        BuildVolcano();
        ScatterProps();
    }

    // ================================================================== terrain

    private void BuildGround()
    {
        // Main plateau: x -60..60, z -30..90, top at y = 0.
        Block(new Vector3(0, -6, 30), new Vector3(120, 12, 120), Vector3.Zero, Grass);
        // Dirt yard around the shack.
        Visual(new BoxMesh { Size = new Vector3(14, 0.05f, 12) }, Mat(Dirt), ShackPos + new Vector3(0, 0.02f, 4), Vector3.Zero);
    }

    private void BuildCliffFace()
    {
        // Jagged dark volcanic rock along the north edge (z = -30), falling into the clouds.
        for (float x = -64; x <= 64; x += 7)
        {
            float h = _rng.RandfRange(30, 55);
            var size = new Vector3(_rng.RandfRange(8, 12), h, _rng.RandfRange(5, 9));
            var pos = new Vector3(x + _rng.RandfRange(-2, 2), -h / 2f - _rng.RandfRange(0, 6), -31 - _rng.RandfRange(0, 3));
            Block(pos, size, new Vector3(_rng.RandfRange(-0.15f, 0.25f), _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-0.1f, 0.1f)),
                _rng.Randf() < 0.5f ? Obsidian : Slate);
        }
        // Overhang ledges just below the lip.
        for (int i = 0; i < 6; i++)
        {
            var pos = new Vector3(_rng.RandfRange(-50, 50), _rng.RandfRange(-14, -5), -35 - _rng.RandfRange(0, 4));
            Block(pos, new Vector3(_rng.RandfRange(6, 12), 2.5f, _rng.RandfRange(4, 7)),
                new Vector3(_rng.RandfRange(-0.2f, 0.2f), _rng.RandfRange(0, 3), _rng.RandfRange(-0.15f, 0.15f)), Slate);
        }
        // Spires rising out of the cloud sea.
        for (int i = 0; i < 9; i++)
        {
            float h = _rng.RandfRange(50, 110);
            var pos = new Vector3(_rng.RandfRange(-160, 160), -70 + h / 2f, _rng.RandfRange(-70, -260));
            Block(pos, new Vector3(_rng.RandfRange(10, 24), h, _rng.RandfRange(10, 22)),
                new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.RandfRange(0, 3), _rng.RandfRange(-0.12f, 0.12f)),
                _rng.Randf() < 0.5f ? Obsidian : Slate, collide: false);
        }
    }

    private void BuildSideWalls()
    {
        // West and east: tall angular walls that enclose the plateau.
        foreach (float side in new[] { -1f, 1f })
        {
            for (float z = -34; z <= 92; z += 9)
            {
                float h = _rng.RandfRange(18, 42) + (z < 0 ? 12 : 0);
                var size = new Vector3(_rng.RandfRange(12, 18), h, _rng.RandfRange(10, 14));
                var pos = new Vector3(side * (64 + _rng.RandfRange(0, 6)), h / 2f - 4, z + _rng.RandfRange(-2, 2));
                Block(pos, size, new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.RandfRange(-0.4f, 0.4f), side * _rng.RandfRange(0.05f, 0.2f)),
                    _rng.Randf() < 0.35f ? SlateLight : Slate);
            }
        }
        // South wall with a gap for the road (x in -5..5).
        for (float x = -60; x <= 60; x += 10)
        {
            if (Mathf.Abs(x) < 8) continue;
            float h = _rng.RandfRange(10, 22);
            Block(new Vector3(x, h / 2f - 2, 94 + _rng.RandfRange(0, 4)), new Vector3(12, h, 10),
                new Vector3(0, _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-0.1f, 0.1f)), Slate);
        }
    }

    private void BuildCloudSea()
    {
        var layer1 = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.92f, 0.8f, 0.88f, 0.95f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f,
            RimEnabled = true, Rim = 0.6f,
        };
        Visual(new PlaneMesh { Size = new Vector2(4000, 4000) }, layer1, new Vector3(0, -62, 0), Vector3.Zero);
        var layer2 = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.9f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        Visual(new PlaneMesh { Size = new Vector2(4000, 4000) }, layer2, new Vector3(0, -52, 0), Vector3.Zero);

        // Slow drifting mist puffs just below the lip.
        var mist = Particles(140, 14f, new Vector3(90, 4, 40), new Vector3(0, -48, -90),
            new Color(1f, 0.9f, 0.95f, 0.25f), 10f, 22f, new Vector3(0.6f, 0.05f, 0));
        mist.Name = "CloudMist";
    }

    // ================================================================== landmarks

    private void BuildShack()
    {
        var root = new Node3D { Name = "Shack", Position = ShackPos };
        AddChild(root);
        var wood = Mat(Wood);
        var dark = Mat(WoodDark);
        // Floor, walls (door gap on the south side facing the plateau), roof.
        Block(new Vector3(0, 0.1f, 0), new Vector3(4.4f, 0.2f, 4.4f), Vector3.Zero, WoodDark, root);
        Block(new Vector3(0, 1.4f, -2.1f), new Vector3(4.4f, 2.8f, 0.2f), Vector3.Zero, Wood, root);      // back
        Block(new Vector3(-2.1f, 1.4f, 0), new Vector3(0.2f, 2.8f, 4.4f), Vector3.Zero, Wood, root);      // west
        Block(new Vector3(2.1f, 1.4f, 0), new Vector3(0.2f, 2.8f, 4.4f), Vector3.Zero, Wood, root);       // east
        Block(new Vector3(-1.4f, 1.4f, 2.1f), new Vector3(1.6f, 2.8f, 0.2f), Vector3.Zero, Wood, root);   // front-left
        Block(new Vector3(1.4f, 1.4f, 2.1f), new Vector3(1.6f, 2.8f, 0.2f), Vector3.Zero, Wood, root);    // front-right
        Block(new Vector3(0, 2.55f, 2.1f), new Vector3(1.2f, 0.5f, 0.2f), Vector3.Zero, Wood, root);      // lintel
        var roof = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(5.2f, 1.8f, 5.2f) },
            MaterialOverride = dark,
            Position = new Vector3(0, 3.7f, 0),
        };
        root.AddChild(roof);
        // Window glow (warm, cozy).
        var glow = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.7f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.3f), EmissionEnergyMultiplier = 2.5f };
        Visual(new BoxMesh { Size = new Vector3(0.05f, 0.6f, 0.8f) }, glow, new Vector3(-2.22f, 1.6f, 0), Vector3.Zero, root);
        root.AddChild(new OmniLight3D { Position = new Vector3(0, 2.2f, 0), LightColor = new Color(1f, 0.7f, 0.45f), LightEnergy = 1.2f, OmniRange = 6f });

        // Bed inside.
        var bed = new Bed { Name = "Bed", Position = new Vector3(-1.2f, 0.2f, -1.1f), CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        root.AddChild(bed);
        bed.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.0f, 0.5f, 2.0f) }, Position = new Vector3(0, 0.25f, 0) });
        Visual(new BoxMesh { Size = new Vector3(1.0f, 0.4f, 2.0f) }, wood, new Vector3(0, 0.2f, 0), Vector3.Zero, bed);
        Visual(new BoxMesh { Size = new Vector3(0.9f, 0.15f, 1.5f) }, Mat(new Color(0.55f, 0.25f, 0.3f)), new Vector3(0, 0.47f, 0.2f), Vector3.Zero, bed);
        Visual(new BoxMesh { Size = new Vector3(0.7f, 0.15f, 0.4f) }, Mat(new Color(0.9f, 0.85f, 0.78f)), new Vector3(0, 0.47f, -0.7f), Vector3.Zero, bed);

        // Training dummy in the yard.
        var dummy = GD.Load<PackedScene>("res://scenes/enemies/TrainingDummy.tscn").Instantiate<Node3D>();
        dummy.Position = ShackPos + new Vector3(-4, 0, 7);
        AddChild(dummy);
    }

    private void BuildBlossomTree()
    {
        var shrine = new BlossomShrine
        {
            Name = "BlossomShrine",
            Position = BlossomPos,
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        AddChild(shrine);
        shrine.Rotation = new Vector3(0, Mathf.DegToRad(200), 0); // local +Z leans out over the cliff.
        shrine.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.6f, Height = 4f }, Position = new Vector3(0, 2, 0) });

        // Gnarled, wind-swept trunk: tilted segments leaning out over the cliff.
        var bark = Mat(new Color(0.16f, 0.1f, 0.12f));
        var segs = new (Vector3 pos, Vector3 rot, float r0, float r1, float h)[]
        {
            (new Vector3(0, 1.2f, 0), new Vector3(-0.25f, 0, 0.1f), 0.32f, 0.5f, 2.6f),
            (new Vector3(0.2f, 3.0f, 0.55f), new Vector3(-0.7f, 0, 0.35f), 0.22f, 0.32f, 2.2f),
            (new Vector3(-0.4f, 3.4f, 0.9f), new Vector3(-0.4f, 0, -0.8f), 0.12f, 0.2f, 2.4f),
            (new Vector3(0.9f, 4.2f, 1.6f), new Vector3(-1.1f, 0, 0.5f), 0.1f, 0.18f, 2.6f),
        };
        foreach (var s in segs)
            Visual(new CylinderMesh { TopRadius = s.r0, BottomRadius = s.r1, Height = s.h, RadialSegments = 6 }, bark, s.pos, s.rot, shrine);

        // Glowing backlit canopy (StandardMaterial3D backlight ≈ cheap subsurface scattering).
        var canopy = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.5f, 0.68f),
            EmissionEnabled = true,
            Emission = new Color(0.9f, 0.3f, 0.55f),
            EmissionEnergyMultiplier = 0.45f,
            BacklightEnabled = true,
            Backlight = new Color(1f, 0.55f, 0.7f),
            RimEnabled = true, Rim = 0.8f, RimTint = 0.6f,
            Roughness = 1f,
        };
        var blobs = new (Vector3 p, float r)[]
        {
            (new Vector3(0.5f, 4.8f, 1.8f), 2.0f), (new Vector3(-1.4f, 4.6f, 1.2f), 1.6f),
            (new Vector3(1.8f, 4.4f, 2.6f), 1.5f), (new Vector3(0.2f, 5.6f, 0.6f), 1.5f),
            (new Vector3(-0.6f, 4.2f, 2.8f), 1.3f), (new Vector3(2.6f, 5.0f, 1.2f), 1.2f),
        };
        foreach (var b in blobs)
            Visual(new SphereMesh { Radius = b.r, Height = b.r * 1.6f, RadialSegments = 10, Rings = 5 }, canopy, b.p, Vector3.Zero, shrine);

        shrine.AddChild(new OmniLight3D { Position = new Vector3(0.5f, 4.5f, 1.8f), LightColor = Blush, LightEnergy = 1.4f, OmniRange = 12f });

        // Drifting petals blown off the cliff edge.
        var petals = Particles(160, 9f, new Vector3(3, 1.2f, 2.5f), BlossomPos + new Vector3(0, 4.8f, -1.5f),
            new Color(1f, 0.6f, 0.75f), 0.08f, 0.16f, new Vector3(0.35f, -0.25f, -0.5f));
        petals.Name = "Petals";

        var label = new Label3D
        {
            Text = "Sacred Blossom",
            Position = new Vector3(0, 7.2f, 1.2f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64, PixelSize = 0.01f, OutlineSize = 12,
            Modulate = Blush,
        };
        shrine.AddChild(label);
    }

    private void BuildWaterfalls()
    {
        // 1) West wall falls into a pool on the plateau...
        Waterfall(new Vector3(-56.5f, 16, -4), new Vector2(7, 34), new Vector3(0, Mathf.Pi / 2f, 0));
        var water = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.6f, 0.75f, 0.8f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.2f, Roughness = 0.05f,
        };
        Visual(new BoxMesh { Size = new Vector3(14, 0.3f, 12) }, water, PoolPos + new Vector3(-2, 0.05f, 0), Vector3.Zero);
        Block(PoolPos + new Vector3(-2, -0.4f, 6.5f), new Vector3(15, 1.2f, 1), Vector3.Zero, Slate);
        Block(PoolPos + new Vector3(-2, -0.4f, -6.5f), new Vector3(15, 1.2f, 1), Vector3.Zero, Slate);
        // ...a stream runs to the edge...
        Visual(new BoxMesh { Size = new Vector3(3, 0.12f, 24) }, water, new Vector3(-40, 0.06f, -18), new Vector3(0, 0.15f, 0));
        // ...and spills over the cliff.
        Waterfall(new Vector3(-38, -26, -31.5f), new Vector2(4, 52), Vector3.Zero);

        // 2) East: a tall fall straight off the high wall past the cliff.
        Waterfall(new Vector3(52, -5, -36), new Vector2(6, 85), new Vector3(0, -0.3f, 0));
        // 3) A distant fall on one of the spires.
        Waterfall(new Vector3(-90, -20, -150), new Vector2(8, 70), new Vector3(0, 0.4f, 0), collideMist: false);
        // 4) A thin ribbon behind the shack.
        Waterfall(new Vector3(24, -20, -33), new Vector2(2.2f, 40), new Vector3(0, -0.1f, 0));
    }

    private void Waterfall(Vector3 center, Vector2 size, Vector3 rotation, bool collideMist = true)
    {
        var shader = new Shader
        {
            Code = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform vec4 base_color : source_color = vec4(0.72, 0.88, 1.0, 0.85);
uniform vec4 streak_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float speed = 1.3;
uniform float columns = 24.0;
float hash(float n) { return fract(sin(n) * 43758.5453); }
void fragment() {
    float col = floor(UV.x * columns);
    float flow = fract(UV.y * (3.0 + hash(col) * 3.0) - TIME * speed * (0.7 + hash(col + 7.0) * 0.6) + hash(col + 3.0) * 10.0);
    float streak = smoothstep(0.5, 1.0, flow);
    float edge = smoothstep(0.0, 0.12, UV.x) * smoothstep(1.0, 0.88, UV.x);
    float foam = smoothstep(0.85, 1.0, UV.y);
    ALBEDO = mix(base_color.rgb, streak_color.rgb, clamp(streak * 0.8 + foam, 0.0, 1.0)) * 1.15;
    ALPHA = base_color.a * edge * (0.65 + streak * 0.35);
}",
        };
        var mat = new ShaderMaterial { Shader = shader };
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = size },
            MaterialOverride = mat,
            Position = center,
            Rotation = rotation,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(quad);

        // Mist at the base (and a soft updraft column for later glider gameplay).
        var basePos = center - new Vector3(0, size.Y / 2f, 0);
        Particles(60, 4f, new Vector3(size.X * 0.6f, 1, 2), basePos + Vector3.Up,
            new Color(0.95f, 0.97f, 1f, 0.35f), 1.5f, 3.5f, new Vector3(0, 0.6f, 0));
    }

    private void BuildRoadAndSign()
    {
        // Dirt road from the yard south through the gap in the wall.
        var road = Mat(Dirt.Darkened(0.05f));
        Visual(new BoxMesh { Size = new Vector3(4, 0.04f, 80) }, road, new Vector3(2, 0.02f, 40), new Vector3(0, -0.08f, 0));
        Visual(new BoxMesh { Size = new Vector3(4, 0.04f, 22) }, road, new Vector3(6, 0.02f, -4), new Vector3(0, 0.35f, 0));

        // Fallen-log barrier where the greybox ends.
        Block(new Vector3(0, 0.4f, 91), new Vector3(10, 0.8f, 0.8f), new Vector3(0, 0.1f, 0), WoodDark, layer: GameState.LayerProps);

        var sign = new InfoSign
        {
            Name = "AshbySign",
            Title = "Road to Ashby",
            Message = "Village of Ashby — 10 minutes south. (The bridge is out: the village arrives in the next update!)",
            Position = new Vector3(5, 0, 86),
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        AddChild(sign);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 2.2f, 0.4f) }, Position = new Vector3(0, 1.1f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(WoodDark), new Vector3(0, 1, 0), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(1.5f, 0.5f, 0.08f) }, Mat(Wood), new Vector3(0, 1.8f, 0), Vector3.Zero, sign);
        sign.AddChild(new Label3D { Text = "ASHBY →", Position = new Vector3(0, 1.8f, -0.06f), FontSize = 48, PixelSize = 0.006f, Rotation = new Vector3(0, Mathf.Pi, 0), Modulate = new Color(0.15f, 0.1f, 0.08f) });

        var homeSign = new InfoSign
        {
            Name = "HomeSign",
            Title = "Your shack",
            Message = "Home, such as it is. Build within 30 m of the shack. Sleep in the bed to skip the night and save.",
            Position = ShackPos + new Vector3(3, 0, 4),
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        AddChild(homeSign);
        homeSign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 1.4f, 0.3f) }, Position = new Vector3(0, 0.7f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.1f, 1.2f, 0.1f) }, Mat(WoodDark), new Vector3(0, 0.6f, 0), Vector3.Zero, homeSign);
        Visual(new BoxMesh { Size = new Vector3(0.7f, 0.4f, 0.06f) }, Mat(Wood), new Vector3(0, 1.15f, 0), Vector3.Zero, homeSign);
    }

    private void BuildVolcano()
    {
        var root = new Node3D { Name = "Volcano", Position = new Vector3(-380, -70, -950) };
        AddChild(root);
        Visual(new CylinderMesh { TopRadius = 35, BottomRadius = 340, Height = 380, RadialSegments = 9, Rings = 1 },
            Mat(Obsidian), new Vector3(0, 190, 0), Vector3.Zero, root);
        var lava = new StandardMaterial3D
        {
            AlbedoColor = Magma,
            EmissionEnabled = true,
            Emission = Magma,
            EmissionEnergyMultiplier = 6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        Visual(new CylinderMesh { TopRadius = 30, BottomRadius = 33, Height = 6, RadialSegments = 9 }, lava, new Vector3(0, 380, 0), Vector3.Zero, root);
        // Lava rivulets down the flank (emissive streaks).
        for (int i = 0; i < 5; i++)
        {
            float ang = Mathf.DegToRad(150 + i * 22 + _rng.RandfRange(-6, 6));
            var dir = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang));
            var mid = dir * 160 + new Vector3(0, 220, 0);
            var streak = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(_rng.RandfRange(4, 9), 1, 260) },
                MaterialOverride = lava,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(streak);
            streak.Position = mid;
            streak.LookAt(root.GlobalPosition + mid + dir * 10 + Vector3.Down * 12.5f, Vector3.Up);
        }
        root.AddChild(new OmniLight3D { Position = new Vector3(0, 400, 0), LightColor = Magma, LightEnergy = 8f, OmniRange = 400f });
        // Smoke column.
        Particles(40, 20f, new Vector3(20, 5, 20), root.Position + new Vector3(0, 390, 0),
            new Color(0.25f, 0.2f, 0.25f, 0.5f), 40f, 90f, new Vector3(3, 8, 0));
    }

    private void ScatterProps()
    {
        var tree = GD.Load<PackedScene>("res://scenes/props/Tree.tscn");
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var placed = new List<Vector3> { ShackPos, BlossomPos, PoolPos, ShackPos + new Vector3(-4, 0, 7) };

        // A few guaranteed trees & rocks close to home so the first loop is quick.
        Spawn(tree, ShackPos + new Vector3(8, 0, 6), placed);
        Spawn(tree, ShackPos + new Vector3(-10, 0, 10), placed);
        Spawn(tree, ShackPos + new Vector3(12, 0, 14), placed);
        Spawn(rock, ShackPos + new Vector3(6, 0, 12), placed);
        Spawn(rock, ShackPos + new Vector3(-14, 0, 3), placed);

        int trees = 0, rocks = 0, guard = 0;
        while ((trees < 30 || rocks < 12) && guard++ < 2000)
        {
            var p = new Vector3(_rng.RandfRange(-54, 54), 0, _rng.RandfRange(-22, 86));
            if (Mathf.Abs(p.X - 2) < 5 && p.Z > 0) continue;          // road
            if (p.DistanceTo(PoolPos) < 10) continue;                     // pool
            bool clear = true;
            foreach (var q in placed) if (p.DistanceTo(q) < 5.5f) { clear = false; break; }
            if (!clear) continue;
            if (trees < 30) { Spawn(tree, p, placed); trees++; }
            else { Spawn(rock, p, placed); rocks++; }
        }
    }

    private void Spawn(PackedScene scene, Vector3 pos, List<Vector3> placed)
    {
        var n = scene.Instantiate<Node3D>();
        AddChild(n);
        n.Position = pos;
        n.Rotation = new Vector3(0, _rng.RandfRange(0, Mathf.Tau), 0);
        float s = _rng.RandfRange(0.85f, 1.3f);
        n.Scale = new Vector3(s, s, s);
        placed.Add(pos);
    }

    // ================================================================== helpers

    private StandardMaterial3D Mat(Color c)
    {
        if (_mats.TryGetValue(c, out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = c, Roughness = 1f };
        _mats[c] = m;
        return m;
    }

    /// <summary>Box with collision (StaticBody3D). Pass collide:false for distant scenery.</summary>
    private Node3D Block(Vector3 pos, Vector3 size, Vector3 rot, Color color, Node3D parent = null,
        bool collide = true, uint layer = GameState.LayerWorld)
    {
        parent ??= this;
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Mat(color) };
        if (!collide)
        {
            mesh.Position = pos;
            mesh.Rotation = rot;
            parent.AddChild(mesh);
            return mesh;
        }
        var body = new StaticBody3D { Position = pos, Rotation = rot, CollisionLayer = layer, CollisionMask = 0 };
        body.AddChild(mesh);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
        return body;
    }

    private MeshInstance3D Visual(Mesh mesh, Material mat, Vector3 pos, Vector3 rot, Node3D parent = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot };
        (parent ?? this).AddChild(mi);
        return mi;
    }

    private GpuParticles3D Particles(int amount, float lifetime, Vector3 extents, Vector3 position, Color color,
        float sizeMin, float sizeMax, Vector3 drift, float emissive = 0f)
    {
        var quadMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = emissive > 0 ? new Color(emissive, emissive, emissive) : Colors.White,
        };
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = extents,
            Direction = drift.LengthSquared() > 0 ? drift.Normalized() : Vector3.Up,
            Spread = 25f,
            InitialVelocityMin = drift.Length() * 0.6f,
            InitialVelocityMax = drift.Length() * 1.4f,
            Gravity = Vector3.Zero,
            ScaleMin = sizeMin / sizeMax,
            ScaleMax = 1f,
            Color = color,
            AngularVelocityMin = -90f,
            AngularVelocityMax = 90f,
        };
        var p = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            Preprocess = lifetime,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One * sizeMax, Material = quadMat },
            Position = position,
            VisibilityAabb = new Aabb(-extents - Vector3.One * 60, (extents + Vector3.One * 60) * 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        return p;
    }
}
