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
        BuildLava();
        ScatterProps();
        ScatterFoliage();
    }

    // ================================================================== terrain

    private void BuildGround()
    {
        // Main plateau: x -60..60, z -30..90, top at y = 0.
        var ground = Block(new Vector3(0, -6, 30), new Vector3(120, 12, 120), Vector3.Zero, Grass);
        ((MeshInstance3D)ground.GetChild(0)).MaterialOverride =
            StyleMaterials.Rock(new Color(0.11f, 0.1f, 0.14f), new Color(0.27f, 0.36f, 0.28f), 0.7f, 0.25f);
        // Dirt yard around the shack.
        Visual(new BoxMesh { Size = new Vector3(14, 0.05f, 12) }, StyleMaterials.Toon(Dirt.Darkened(0.25f), 0), ShackPos + new Vector3(0, 0.02f, 4), Vector3.Zero);
    }

    private void BuildCliffFace()
    {
        // Jagged dark volcanic rock along the north edge (z = -30), falling into the clouds.
        for (float x = -64; x <= 64; x += 7)
        {
            float h = _rng.RandfRange(30, 55);
            var size = new Vector3(_rng.RandfRange(8, 12), h, _rng.RandfRange(5, 9));
            var pos = new Vector3(x + _rng.RandfRange(-2, 2), -h / 2f - _rng.RandfRange(0, 6), -31 - _rng.RandfRange(0, 3));
            Rock(pos, size, new Vector3(_rng.RandfRange(-0.15f, 0.25f), _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-0.1f, 0.1f)),
                _rng.Randf() < 0.5f ? Obsidian : Slate);
        }
        // Overhang ledges just below the lip.
        for (int i = 0; i < 6; i++)
        {
            var pos = new Vector3(_rng.RandfRange(-50, 50), _rng.RandfRange(-14, -5), -35 - _rng.RandfRange(0, 4));
            Rock(pos, new Vector3(_rng.RandfRange(6, 12), 2.5f, _rng.RandfRange(4, 7)),
                new Vector3(_rng.RandfRange(-0.2f, 0.2f), _rng.RandfRange(0, 3), _rng.RandfRange(-0.15f, 0.15f)), Slate);
        }
        // Spires rising out of the cloud sea.
        for (int i = 0; i < 9; i++)
        {
            float h = _rng.RandfRange(50, 110);
            var pos = new Vector3(_rng.RandfRange(-160, 160), -70 + h / 2f, _rng.RandfRange(-70, -260));
            Rock(pos, new Vector3(_rng.RandfRange(10, 24), h, _rng.RandfRange(10, 22)),
                new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.RandfRange(0, 3), _rng.RandfRange(-0.12f, 0.12f)),
                _rng.Randf() < 0.5f ? Obsidian : Slate, collide: false);
            Crag(pos + new Vector3(0, h / 2f - 4f, 0), _rng.RandfRange(12, 20), _rng.RandfRange(14, 28));
        }
        // Obsidian blades along the western cliff lip: the jagged silhouette left of the hero shot.
        for (int i = 0; i < 9; i++)
        {
            var pos = new Vector3(_rng.RandfRange(-62, -26), -1.5f, _rng.RandfRange(-36, -28));
            bool blade = _rng.Randf() < 0.45f;
            Crag(pos, blade ? _rng.RandfRange(2.5f, 4.5f) : _rng.RandfRange(5, 9),
                blade ? _rng.RandfRange(7, 14) : _rng.RandfRange(5, 10), blade, collide: true);
        }
    }

    /// <summary>Volcanic crag or single blade standing on basePos (models have their origin at the base).</summary>
    private Node3D Crag(Vector3 basePos, float width, float height, bool blade = false, bool collide = false)
    {
        var set = blade ? Models.Shards : Models.Crags;
        var path = set[_rng.RandiRange(0, set.Length - 1)];
        var size = new Vector3(width, height, width * _rng.RandfRange(0.8f, 1.1f));
        Node3D node = collide ? Models.RockBody(path, size) : Models.Instance(path);
        if (!collide) node.Scale = size;
        node.Position = basePos;
        node.Rotation = new Vector3(_rng.RandfRange(-0.08f, 0.08f), _rng.RandfRange(0, Mathf.Tau), _rng.RandfRange(-0.08f, 0.08f));
        AddChild(node);
        return node;
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
                Rock(pos, size, new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.RandfRange(-0.4f, 0.4f), side * _rng.RandfRange(0.05f, 0.2f)),
                    _rng.Randf() < 0.35f ? SlateLight : Slate);
                if (_rng.Randf() < 0.6f)
                    Crag(pos + new Vector3(0, h / 2f - 3f, 0), size.X * 0.8f, _rng.RandfRange(8, 18));
            }
        }
        // South wall with a gap for the road (x in -5..5).
        for (float x = -60; x <= 60; x += 10)
        {
            if (Mathf.Abs(x) < 8) continue;
            float h = _rng.RandfRange(10, 22);
            Rock(new Vector3(x, h / 2f - 2, 94 + _rng.RandfRange(0, 4)), new Vector3(12, h, 10),
                new Vector3(0, _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-0.1f, 0.1f)), Slate);
        }
    }

    private CloudSea _clouds;

    private void BuildCloudSea()
    {
        // Deep base under the puffs so no gaps show through to the void.
        var basePlane = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 0.24f, 0.42f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        Visual(new PlaneMesh { Size = new Vector2(4000, 4000) }, basePlane, new Vector3(0, -45, 0), Vector3.Zero);
        _clouds = new CloudSea { Name = "CloudSea" };
        AddChild(_clouds);
        _clouds.Build(_rng);

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
        var wood = StyleMaterials.Toon(Wood);
        // Model (Blender) + invisible box colliders (door gap on the south side facing the plateau).
        root.AddChild(Models.Instance(Models.Shack));
        Block(new Vector3(0, 0.1f, 0), new Vector3(4.4f, 0.2f, 4.4f), Vector3.Zero, WoodDark, root, visible: false);
        Block(new Vector3(0, 1.4f, -2.1f), new Vector3(4.4f, 2.8f, 0.2f), Vector3.Zero, Wood, root, visible: false);      // back
        Block(new Vector3(-2.1f, 1.4f, 0), new Vector3(0.2f, 2.8f, 4.4f), Vector3.Zero, Wood, root, visible: false);      // west
        Block(new Vector3(2.1f, 1.4f, 0), new Vector3(0.2f, 2.8f, 4.4f), Vector3.Zero, Wood, root, visible: false);       // east
        Block(new Vector3(-1.4f, 1.4f, 2.1f), new Vector3(1.6f, 2.8f, 0.2f), Vector3.Zero, Wood, root, visible: false);   // front-left
        Block(new Vector3(1.4f, 1.4f, 2.1f), new Vector3(1.6f, 2.8f, 0.2f), Vector3.Zero, Wood, root, visible: false);    // front-right
        Block(new Vector3(0, 2.6f, 2.1f), new Vector3(1.2f, 0.4f, 0.2f), Vector3.Zero, Wood, root, visible: false);      // lintel
        // Yard props from the Medieval Village kit.
        Prop(Models.Village("Prop_Crate"), ShackPos + new Vector3(3.2f, 0, 1.2f), 0.4f, 0.9f);
        Prop(Models.Village("Prop_Crate"), ShackPos + new Vector3(3.3f, 0, 2.4f), 1.2f, 0.75f);
        Prop(Models.Village("Prop_Wagon"), ShackPos + new Vector3(6.5f, 0, -1f), 1.9f, 1f);
        for (int f = 0; f < 4; f++)
            Prop(Models.Village("Prop_WoodenFence_Single"), ShackPos + new Vector3(-7f + f * 2f, 0, 9.5f), 0f, 1f);
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

        // Gnarled cherry tree modelled in Blender (leans toward local +Z, out over the drop).
        var tree = Models.Instance(Models.CherryTree);
        tree.Scale = Vector3.One * 1.3f;
        shrine.AddChild(tree);

        shrine.AddChild(new OmniLight3D { Position = new Vector3(0.4f, 5.8f, 2.4f), LightColor = Blush, LightEnergy = 2.2f, OmniRange = 14f });

        // Drifting petals blown off the cliff edge.
        var petals = Particles(160, 9f, new Vector3(3, 1.2f, 2.5f), BlossomPos + new Vector3(0, 6f, -2.5f),
            new Color(1f, 0.6f, 0.75f), 0.08f, 0.16f, new Vector3(0.35f, -0.25f, -0.5f));
        petals.Name = "Petals";

        var label = new Label3D
        {
            Text = "Sacred Blossom",
            Position = new Vector3(0, 9.5f, 2f),
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

    private const string LavaShader = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform vec3 hot : source_color = vec3(1.0, 0.86, 0.4);
uniform vec3 cool : source_color = vec3(0.95, 0.24, 0.04);
uniform float speed = 0.45;
uniform float columns = 7.0;
uniform float intensity = 3.2;
float hash(float n) { return fract(sin(n) * 43758.5453); }
void fragment() {
    float col = floor(UV.x * columns);
    float flow = fract(UV.y * (1.5 + hash(col) * 1.5) - TIME * speed * (0.7 + hash(col + 5.0) * 0.6) + hash(col + 2.0) * 10.0);
    float streak = smoothstep(0.35, 1.0, flow);
    float center = 1.0 - abs(UV.x * 2.0 - 1.0);
    vec3 c = mix(cool, hot, clamp(streak * 0.6 + center * 0.55 - UV.y * 0.25, 0.0, 1.0));
    ALBEDO = c * intensity;
    ALPHA = smoothstep(0.0, 0.45, center) * (1.0 - smoothstep(0.8, 1.0, UV.y));
}";

    private void BuildLava()
    {
        var lavaMat = new ShaderMaterial { Shader = new Shader { Code = LavaShader } };
        var magmaLight = new Color(1f, 0.45f, 0.15f);

        void Fall(Vector3 top, float width, float length, float rotY = 0f)
        {
            var quad = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(width, length) },
                MaterialOverride = lavaMat,
                Position = top - new Vector3(0, length / 2f, 0),
                Rotation = new Vector3(0, rotY, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(quad);
            // Glowing lip where it spills out of the rock.
            AddChild(new OmniLight3D { Position = top + new Vector3(0, -1, 1.5f), LightColor = magmaLight, LightEnergy = 3f, OmniRange = 14f });
            AddChild(new OmniLight3D { Position = top + new Vector3(0, -length * 0.6f, 2f), LightColor = magmaLight, LightEnergy = 4f, OmniRange = 30f });
            _clouds?.LavaPoints.Add(new Vector4(top.X, top.Y - length, top.Z, 26f));
        }

        // Under the blossom tree: two falls pouring out of the cliff face into the clouds.
        Fall(new Vector3(-12f, -5f, -38.5f), 2.6f, 42f, 0.1f);
        Fall(new Vector3(-21f, -9f, -38f), 1.6f, 36f, -0.15f);
        Fall(new Vector3(-3.5f, -14f, -39f), 1.2f, 30f, 0.05f);

        // Lava spires out in the cloud sea (right side of the classic view, like the concept art).
        foreach (var (pos, height, width) in new[]
                 {
                     (new Vector3(14f, -36f, -80f), 50f, 3.6f),
                     (new Vector3(40f, -38f, -115f), 64f, 4.5f),
                     (new Vector3(-48f, -40f, -150f), 58f, 4f),
                 })
        {
            var spire = Models.Instance(Models.Columns[_rng.RandiRange(0, Models.Columns.Length - 1)]);
            spire.Scale = new Vector3(16f, height, 14f);
            spire.Position = pos + new Vector3(0, height / 2f - 6f, 0);
            spire.Rotation = new Vector3(0, _rng.RandfRange(0, Mathf.Tau), 0);
            AddChild(spire);
            Crag(pos + new Vector3(0, height - 9f, 0), 15f, _rng.RandfRange(12, 20));
            Fall(pos + new Vector3(0, height - 8f, 7.5f), width, height - 4f, _rng.RandfRange(-0.2f, 0.2f));
        }
        _clouds?.ApplyLava();

        // Embers drifting up past the cliff lip.
        var embers = Particles(220, 9f, new Vector3(45, 14, 10), new Vector3(-10, -8, -40),
            new Color(1f, 0.42f, 0.08f), 0.05f, 0.12f, new Vector3(0.4f, 1.6f, 0.2f), emissive: 1.6f);
        embers.Name = "Embers";
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

    private void ScatterFoliage()
    {
        string[] small =
        {
            "Grass_Common_Short", "Grass_Common_Tall", "Grass_Wispy_Short", "Grass_Wispy_Tall", "Grass_Common_Short",
            "Flower_3_Group", "Flower_4_Group", "Flower_3_Single", "Clover_1", "Clover_2", "Fern_1",
            "Mushroom_Common", "Pebble_Round_1", "Pebble_Square_2", "Plant_1", "Plant_7",
        };
        int placedCount = 0, guard = 0;
        while (placedCount < 260 && guard++ < 4000)
        {
            var p = new Vector3(_rng.RandfRange(-56, 56), 0, _rng.RandfRange(-28, 88));
            if (Mathf.Abs(p.X - 2) < 2.5f && p.Z > 0) continue;                       // road
            if (p.DistanceTo(PoolPos) < 9 || p.DistanceTo(ShackPos) < 4) continue;     // pool, shack
            Prop(Models.Nature(small[_rng.RandiRange(0, small.Length - 1)]), p, _rng.RandfRange(0, Mathf.Tau), _rng.RandfRange(0.8f, 1.4f));
            placedCount++;
        }
        for (int i = 0; i < 14; i++)
        {
            var p = new Vector3(_rng.RandfRange(-55, 55), 0, _rng.RandfRange(-20, 85));
            if (p.DistanceTo(ShackPos) < 7 || (Mathf.Abs(p.X - 2) < 4 && p.Z > 0)) continue;
            Prop(Models.Nature(_rng.Randf() < 0.6f ? "Bush_Common" : "Bush_Common_Flowers"), p, _rng.RandfRange(0, Mathf.Tau), _rng.RandfRange(0.9f, 1.4f));
        }
        // Wind-bent twisted trees along the cliff lip, and a few dead ones toward the volcano side.
        foreach (var x in new[] { -46f, -30f, 22f, 36f, 50f })
            Prop(Models.Nature("TwistedTree_" + _rng.RandiRange(1, 5)), new Vector3(x, 0, -27.5f), _rng.RandfRange(0, Mathf.Tau), _rng.RandfRange(0.9f, 1.3f));
        foreach (var p in new[] { new Vector3(-52, 0, 30), new Vector3(-50, 0, 60), new Vector3(48, 0, 70) })
            Prop(Models.Nature("DeadTree_" + _rng.RandiRange(1, 5)), p, _rng.RandfRange(0, Mathf.Tau), 1.1f);
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
        bool collide = true, uint layer = GameState.LayerWorld, bool visible = true)
    {
        parent ??= this;
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Mat(color), Visible = visible };
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

    /// <summary>Faceted rock model (Blender kit) scaled to roughly fill the given box.</summary>
    private Node3D Rock(Vector3 pos, Vector3 size, Vector3 rot, Color unused, Node3D parent = null, bool collide = true)
    {
        parent ??= this;
        var path = Models.AllRocks[_rng.RandiRange(0, Models.AllRocks.Length - 1)];
        size *= 1.15f;
        Node3D node = collide ? Models.RockBody(path, size) : Models.Instance(path);
        if (!collide) node.Scale = size;
        node.Position = pos;
        node.Rotation = rot;
        parent.AddChild(node);
        return node;
    }

    /// <summary>Decorative model without collision.</summary>
    private Node3D Prop(string path, Vector3 pos, float rotY, float scale, Node3D parent = null)
    {
        var node = Models.Instance(path);
        node.Position = pos;
        node.Rotation = new Vector3(0, rotY, 0);
        node.Scale = Vector3.One * scale;
        (parent ?? this).AddChild(node);
        return node;
    }

    private MeshInstance3D Visual(Mesh mesh, Material mat, Vector3 pos, Vector3 rot, Node3D parent = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot };
        (parent ?? this).AddChild(mi);
        return mi;
    }

    private static GradientTexture2D _softDot;

    /// <summary>Round, soft-edged sprite so particles read as motes, not squares.</summary>
    private static GradientTexture2D SoftDot => _softDot ??= new GradientTexture2D
    {
        Width = 64,
        Height = 64,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.35f, 1f },
            Colors = new[] { Colors.White, new Color(1, 1, 1, 0.75f), new Color(1, 1, 1, 0) },
        },
    };

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
            AlbedoTexture = SoftDot,
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
