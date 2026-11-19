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
    /// <summary>The home pool's basin (x, z): deep enough to swim in.</summary>
    public static readonly Rect2 HomePool = new(PoolPos.X - 9, PoolPos.Z - 6, 14, 12);
    /// <summary>Swimmable pools: how deep the basin is, and where the water stands (just under the rim).</summary>
    public const float PoolDepth = 2.8f, PoolSurface = -0.25f;

    /// <summary>The painterly grass-and-rock ground shared by the plateau and the vale.</summary>
    private static ShaderMaterial _ground;
    private static ShaderMaterial GroundMaterial => _ground ??= StyleMaterials.Terrain(
        new Color(0.11f, 0.1f, 0.14f), new Color(0.26f, 0.38f, 0.27f), new Color(0.36f, 0.46f, 0.26f), new Color(0.48f, 0.44f, 0.29f));

    private RandomNumberGenerator _rng;
    private int _resourceCount;
    private readonly Dictionary<Color, StandardMaterial3D> _mats = new();

    private GpuParticles3D _fireflies;

    public override void _Process(double delta)
    {
        float t = GameState.Instance.TimeOfDay;
        if (_fireflies != null) _fireflies.Emitting = t < 0.24f || t > 0.77f;
    }

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
        gs.WorldSpawn = BlossomPos + new Vector3(2.5f, 0.2f, 3.5f);

        ClearPathRecords();
        BuildGround();
        BuildCliffFace();
        BuildSideWalls();
        BuildCloudSea();
        BuildYard();
        BuildHome(gs.HomeTier);
        gs.HomeTierChanged += OnHomeTierChanged;
        BuildUpgradeBoard();
        BuildBlossomTree();
        BuildWaterfalls();
        BuildRoadAndSign();
        BuildVolcano();
        BuildLava();
        ScatterProps();
        ScatterFoliage();
        BuildVale();
        BuildAshby();
        BuildBanditCamp();
        BuildHomeChest();
        BuildFallsValley(); // its scatter uses the shared random numbers
        BuildOreVeins();    // after everything else, so the other resource names don't shift
        ClearTrailsOfScatter();

        AddChild(new Weather { Name = "Weather" });
        AddChild(new Ambience { Name = "Ambience" });
        // Enemies path around the world (the AI runs on the server only).
        AddToGroup(WorldNav.SourceGroup);
        if (Net.Instance.IsServer) AddChild(new WorldNav { Name = "WorldNav" });
    }

    /// <summary>Storage chest by the shack: empty your pack into the shared stockpile here.</summary>
    private void BuildHomeChest()
    {
        var chest = new StorageChest
        {
            Name = "HomeChest",
            Position = ShackPos + new Vector3(-6f, 0, 3f),
            Rotation = new Vector3(0, 0.5f, 0),
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        AddChild(chest);
        chest.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.25f, 0.95f, 0.8f) }, Position = new Vector3(0, 0.48f, 0) });
    }

    public override void _ExitTree() => GameState.Instance.HomeTierChanged -= OnHomeTierChanged;

    // ================================================================== terrain

    /// <summary>A slab of ground (the painterly terrain) with a rectangular hole through it for a
    /// pool's basin: up to four blocks around the hole. The terrain shader works in world space, so
    /// the seams don't show.</summary>
    private void GroundAround(Vector3 center, Vector3 size, Rect2 hole)
    {
        float x0 = center.X - size.X / 2f, x1 = center.X + size.X / 2f, z0 = center.Z - size.Z / 2f, z1 = center.Z + size.Z / 2f;
        float hx0 = hole.Position.X, hx1 = hole.End.X, hz0 = hole.Position.Y, hz1 = hole.End.Y;
        void Piece(float ax, float bx, float az, float bz)
        {
            if (bx - ax < 0.01f || bz - az < 0.01f) return;
            var b = Block(new Vector3((ax + bx) / 2f, center.Y, (az + bz) / 2f), new Vector3(bx - ax, size.Y, bz - az), Vector3.Zero, Grass);
            b.Name = "Ground";
            ((MeshInstance3D)b.GetChild(0)).MaterialOverride = GroundMaterial;
        }
        Piece(x0, hx0, z0, z1);
        Piece(hx1, x1, z0, z1);
        Piece(hx0, hx1, z0, hz0);
        Piece(hx0, hx1, hz1, z1);
    }

    /// <summary>A pool you can swim in (GDD §8), sunk into a hole left by <see cref="GroundAround"/>:
    /// a slate floor <see cref="PoolDepth"/> down and the water's surface just under the rim.
    /// Swim into any edge to haul yourself out.</summary>
    private void Pool(Rect2 area)
    {
        var mid = new Vector3(area.GetCenter().X, 0, area.GetCenter().Y);
        Block(mid + Vector3.Down * (PoolDepth + 0.5f), new Vector3(area.Size.X, 1f, area.Size.Y), Vector3.Zero, Slate.Darkened(0.3f));
        Visual(new BoxMesh { Size = new Vector3(area.Size.X, 0.1f, area.Size.Y) }, StyleMaterials.Water(), mid + new Vector3(0, PoolSurface - 0.05f, 0), Vector3.Zero);
        GameState.Instance.Waters.Add((new Vector3(area.Position.X, -PoolDepth, area.Position.Y), new Vector3(area.End.X, PoolSurface, area.End.Y)));
    }

    private void BuildGround()
    {
        // Main plateau: x -60..60, z -30..90, top at y = 0.
        GroundAround(new Vector3(0, -6, 30), new Vector3(120, 12, 120), HomePool);
        // Dirt yard around the shack.
        PathPatch(ShackPos + new Vector3(0, 0.03f, 4), new Vector2(15, 13), Dirt.Darkened(0.3f), Dirt.Darkened(0.15f));
    }

    private void BuildCliffFace()
    {
        // Jagged dark volcanic rock along the north edge (z = -30), falling into the clouds.
        for (float x = -64; x <= 64; x += 5.5f)
        {
            float h = _rng.RandfRange(30, 55);
            var size = new Vector3(_rng.RandfRange(8, 12), h, _rng.RandfRange(5, 9));
            var pos = new Vector3(x + _rng.RandfRange(-2, 2), -h / 2f - _rng.RandfRange(0.4f, 1.8f), -31 - _rng.RandfRange(0, 2));
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
            var body = Rock(pos, new Vector3(_rng.RandfRange(10, 24), h, _rng.RandfRange(10, 22)),
                new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.RandfRange(0, 3), _rng.RandfRange(-0.12f, 0.12f)),
                _rng.Randf() < 0.5f ? Obsidian : Slate, collide: false);
            Crag(pos + new Vector3(0, h / 2f - 4f, 0), _rng.RandfRange(12, 20), _rng.RandfRange(14, 28), on: body);
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

    /// <summary>Volcanic crag or single blade standing on basePos (models have their origin at the base).
    /// Given <paramref name="on"/>, it's a crown for that rock instead: see <see cref="SeatCrown"/>.</summary>
    private Node3D Crag(Vector3 basePos, float width, float height, bool blade = false, bool collide = false, Node3D on = null)
    {
        var set = blade ? Models.Shards : Models.Crags;
        var path = set[_rng.RandiRange(0, set.Length - 1)];
        var size = new Vector3(width, height, width * _rng.RandfRange(0.8f, 1.1f));
        Node3D node = collide ? Models.RockBody(path, size) : Models.Instance(path);
        if (!collide) node.Scale = size;
        node.Position = basePos;
        node.Rotation = new Vector3(_rng.RandfRange(-0.08f, 0.08f), _rng.RandfRange(0, Mathf.Tau), _rng.RandfRange(-0.08f, 0.08f));
        AddChild(node);
        if (on != null && !collide) SeatCrown(node, on, size.Z / Mathf.Max(size.X, 0.01f));
        return node;
    }

    /// <summary>Make a crag a crown that grows out of a rock rather than sitting on it like a hat:
    /// find the rock's real top from its mesh, sink the crown's base well into it, tilt it with the
    /// rock, and make its base a little narrower than the rock is at that depth (a wider crown
    /// overhangs and reads as a separate piece). Keeps the crown's height and spin.</summary>
    private void SeatCrown(Node3D crown, Node3D rock, float depthRatio)
    {
        var mi = Models.FindMesh(rock);
        if (mi?.Mesh == null) return;
        var toHere = RelativeTo(mi, this);
        var verts = mi.Mesh.GetFaces();
        if (verts.Length == 0) return;
        float top = float.MinValue;
        foreach (var v in verts) top = Mathf.Max(top, (toHere * v).Y);

        float height = crown.Scale.Y;
        float sink = Mathf.Max(1.2f, height * 0.38f);
        // The rock's cross-section in the band the crown's base sinks into.
        var up = toHere.Basis.Y.Normalized();
        Vector3 sum = Vector3.Zero;
        int count = 0;
        var band = new System.Collections.Generic.List<Vector3>();
        foreach (var v in verts)
        {
            var p = toHere * v;
            if (p.Y < top - sink) continue;
            band.Add(p);
            sum += p;
            count++;
        }
        if (count == 0) return;
        var centre = sum / count;
        float reach = 0f;
        foreach (var p in band) reach = Mathf.Max(reach, new Vector2(p.X - centre.X, p.Z - centre.Z).Length());
        float width = Mathf.Max(1f, reach * 2f * 0.85f);

        float spin = crown.Rotation.Y;
        var tilt = new Quaternion(Vector3.Up, up);
        crown.Basis = new Basis(tilt * new Quaternion(Vector3.Up, spin)).Scaled(new Vector3(width, height, width * depthRatio));
        crown.Position = new Vector3(centre.X, top - sink, centre.Z);
    }

    private static Transform3D RelativeTo(Node3D node, Node3D root)
    {
        var t = Transform3D.Identity;
        for (Node n = node; n != null && n != root; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
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
                int before = GetChildCount();
                var wall = Rock(pos, size, new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.RandfRange(-0.4f, 0.4f), side * _rng.RandfRange(0.05f, 0.2f)),
                    _rng.Randf() < 0.35f ? SlateLight : Slate);
                if (_rng.Randf() < 0.6f)
                    Crag(pos + new Vector3(0, h / 2f - 3f, 0), size.X * 0.8f, _rng.RandfRange(8, 18), on: wall);
                // The pass to Falls Valley: drop these rocks (made anyway, so the random numbers
                // for everything after come out the same as before the valley existed).
                if (side > 0 && z > PassMinZ - 4 && z < PassMaxZ - 2)
                    while (GetChildCount() > before)
                    {
                        var n = GetChild(GetChildCount() - 1);
                        RemoveChild(n);
                        n.Free();
                    }
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

        // Slow drifting mist just below the lip and out over the near clouds, so the banks fade
        // into each other instead of standing out as separate puffs.
        var mist = Particles(140, 14f, new Vector3(90, 4, 40), new Vector3(0, -48, -90),
            new Color(1f, 0.9f, 0.95f, 0.25f), 10f, 22f, new Vector3(0.6f, 0.05f, 0));
        mist.Name = "CloudMist";
        var veil = Particles(90, 22f, new Vector3(160, 6, 70), new Vector3(0, -30, -110),
            new Color(1f, 0.88f, 0.92f, 0.16f), 30f, 60f, new Vector3(0.8f, 0.02f, 0));
        veil.Name = "CloudVeil";
    }

    // ================================================================== landmarks

    /// <summary>Home tier 1: the starting shack (replaced by the cottage on upgrade).</summary>
    private Node3D BuildShackTier()
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
        // The floor is a 0.2 m step: a shallow ramp at the door lets you walk in instead of jumping.
        // Its top runs from the floor's edge (z 2.2, y 0.2) down to the ground at z 3.0.
        float rampAngle = Mathf.Atan2(0.2f, 0.8f);
        var rampTop = new Vector3(0, 0.1f, 2.6f);
        var rampNormal = new Vector3(0, Mathf.Cos(rampAngle), Mathf.Sin(rampAngle));
        Block(rampTop - rampNormal * 0.05f, new Vector3(1.6f, 0.1f, Mathf.Sqrt(0.8f * 0.8f + 0.2f * 0.2f)),
            new Vector3(rampAngle, 0, 0), WoodDark, root, visible: false);
        // Crates by the door (the cottage takes their spot).
        Prop(Models.Village("Prop_Crate"), new Vector3(3.2f, 0, 1.2f), 0.4f, 0.9f, root);
        Prop(Models.Village("Prop_Crate"), new Vector3(3.3f, 0, 2.4f), 1.2f, 0.75f, root);
        root.AddChild(new OmniLight3D { Position = new Vector3(0, 2.2f, 0), LightColor = new Color(1f, 0.7f, 0.45f), LightEnergy = 1.2f, OmniRange = 6f });

        // The hearth in the back-east corner, opening toward the door, under the model's chimney.
        var hearth = Hearth.Create();
        hearth.Position = new Vector3(1.3f, 0.2f, -2.0f + Hearth.Depth / 2f);
        root.AddChild(hearth);

        // Bed inside.
        var bed = new Bed { Name = "Bed", Position = new Vector3(-1.2f, 0.2f, -1.1f), CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        root.AddChild(bed);
        bed.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.0f, 0.5f, 2.0f) }, Position = new Vector3(0, 0.25f, 0) });
        Visual(new BoxMesh { Size = new Vector3(1.0f, 0.4f, 2.0f) }, wood, new Vector3(0, 0.2f, 0), Vector3.Zero, bed);
        Visual(new BoxMesh { Size = new Vector3(0.9f, 0.14f, 1.38f) }, Mat(new Color(0.55f, 0.25f, 0.3f)), new Vector3(0, 0.465f, 0.26f), Vector3.Zero, bed);
        Visual(new BoxMesh { Size = new Vector3(0.7f, 0.18f, 0.38f) }, Mat(new Color(0.9f, 0.85f, 0.78f)), new Vector3(0, 0.49f, -0.7f), Vector3.Zero, bed);
        return root;
    }

    /// <summary>Around the home: wagon, fence, ledge campfire, training dummy.</summary>
    private void BuildYard()
    {
        Prop(Models.Village("Prop_Wagon"), ShackPos + new Vector3(6.5f, 0, -1f), 1.9f, 1f);
        for (int f = 0; f < 4; f++)
            Prop(Models.Village("Prop_WoodenFence_Single"), ShackPos + new Vector3(-7f + f * 2f, 0, 9.5f), 0f, 1f);

        // Campfire on the ledge between the shack and the drop: a warm pool of light at night.
        var fire = GD.Load<PackedScene>("res://scenes/buildings/Campfire.tscn").Instantiate<Node3D>();
        fire.Name = "LedgeCampfire";
        fire.Position = ShackPos + new Vector3(-4.5f, 0, -3.5f);
        AddChild(fire);

        // Training dummy in the yard.
        var dummy = GD.Load<PackedScene>("res://scenes/enemies/TrainingDummy.tscn").Instantiate<Node3D>();
        dummy.Name = "TrainingDummy";
        dummy.Position = ShackPos + new Vector3(-4, 0, 7);
        AddChild(dummy);
    }

    private void BuildBlossomTree()
    {
        var shrine = new BlossomShrine
        {
            Name = "BlossomShrine",
            Position = BlossomPos,
            StartsAttuned = true,
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

        // Fireflies drifting around the sanctuary; only shown at night (see _Process).
        _fireflies = Particles(70, 7f, new Vector3(12, 2.5f, 8), BlossomPos + new Vector3(6, 2f, 2),
            new Color(1f, 0.85f, 0.35f), 0.1f, 0.17f, new Vector3(0.15f, 0.1f, 0.1f), emissive: 4f);
        _fireflies.Name = "Fireflies";

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
        var water = StyleMaterials.Water();
        Pool(HomePool);
        Block(PoolPos + new Vector3(-2, -0.4f, 6.5f), new Vector3(15, 1.2f, 1), Vector3.Zero, Slate);
        // Draw water at the pool's east edge.
        var pool = new WaterSource
        {
            Name = "PoolWater", SourceName = "pool", Position = PoolPos + new Vector3(5.2f, 0, 0),
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        AddChild(pool);
        pool.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.2f, 0.4f, 10f) }, Position = new Vector3(0, 0.1f, 0) });
        Block(PoolPos + new Vector3(-2, -0.4f, -6.5f), new Vector3(15, 1.2f, 1), Vector3.Zero, Slate);
        // ...a stream runs to the edge...
        Visual(new BoxMesh { Size = new Vector3(3, 0.12f, 20) }, water, new Vector3(-40.3f, 0.06f, -20.2f), new Vector3(0, 0.15f, 0));
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
        // The roar: loudest at the plunge pool, carries further for the big falls.
        Sfx.Loop(this, "amb_waterfall", basePos + Vector3.Up * 2f, Mathf.Lerp(-6f, 4f, Mathf.Clamp(size.X * size.Y / 500f, 0f, 1f)), 40f + size.X * 8f, 3f + size.X);
    }

    private const string LavaShader = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform vec3 hot : source_color = vec3(1.0, 0.86, 0.4);
uniform vec3 cool : source_color = vec3(0.95, 0.24, 0.04);
uniform float speed = 0.45;
uniform float columns = 2.0;
uniform float intensity = 4.0;
float hash(float n) { return fract(sin(n) * 43758.5453); }
void fragment() {
    float col = floor(UV.x * columns);
    float flow = fract(UV.y * (1.5 + hash(col) * 1.5) - TIME * speed * (0.7 + hash(col + 5.0) * 0.6) + hash(col + 2.0) * 10.0);
    float streak = smoothstep(0.35, 1.0, flow);
    float center = 1.0 - abs(UV.x * 2.0 - 1.0);
    // Thin white-hot core inside a soft orange halo: a seam of lava, not a sheet.
    float core = smoothstep(0.55, 0.92, center);
    float halo = smoothstep(0.0, 0.7, center);
    vec3 c = mix(cool, hot, clamp(core * 0.8 + streak * 0.35 * core - UV.y * 0.2, 0.0, 1.0));
    ALBEDO = c * intensity * (0.35 + 0.65 * core + 0.3 * streak * core);
    ALPHA = (halo * 0.45 + core * 0.55) * (1.0 - smoothstep(0.85, 1.0, UV.y));
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
        Fall(new Vector3(-12f, -5f, -38.5f), 1.5f, 42f, 0.1f);
        Fall(new Vector3(-21f, -9f, -38f), 1.1f, 36f, -0.15f);
        Fall(new Vector3(-3.5f, -14f, -39f), 0.9f, 30f, 0.05f);

        // Lava spires out in the cloud sea (right side of the classic view, like the concept art).
        foreach (var (pos, height, width) in new[]
                 {
                     (new Vector3(14f, -36f, -80f), 50f, 1.8f),
                     (new Vector3(40f, -38f, -115f), 64f, 2.2f),
                     (new Vector3(-48f, -40f, -150f), 58f, 2f),
                 })
        {
            var spire = Models.Instance(Models.Columns[_rng.RandiRange(0, Models.Columns.Length - 1)]);
            spire.Scale = new Vector3(16f, height, 14f);
            spire.Position = pos + new Vector3(0, height / 2f - 6f, 0);
            spire.Rotation = new Vector3(0, _rng.RandfRange(0, Mathf.Tau), 0);
            AddChild(spire);
            Crag(pos + new Vector3(0, height - 9f, 0), 15f, _rng.RandfRange(12, 20), on: spire);
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
        // Every road and track, home to Ashby, the camp and Falls Valley (HomeCliffs.Trails).
        BuildTrails();

        var sign = new InfoSign
        {
            Name = "AshbySign",
            Title = "Road to Ashby",
            Message = "Village of Ashby: follow the road south. Mind the bandit camp east of the road past the gap.",
            Position = new Vector3(5, 0, 86),
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        AddChild(sign);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 2.2f, 0.4f) }, Position = new Vector3(0, 1.1f, 0) });
        // The post stands behind the board, so it doesn't cover the lettering.
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(WoodDark), new Vector3(0, 1, 0.115f), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(1.5f, 0.5f, 0.08f) }, Mat(Wood), new Vector3(0, 1.8f, 0), Vector3.Zero, sign);
        sign.AddChild(new Label3D { Text = "ASHBY →", Position = new Vector3(0, 1.8f, -0.06f), FontSize = 48, PixelSize = 0.006f, Rotation = new Vector3(0, Mathf.Pi, 0), Modulate = new Color(0.15f, 0.1f, 0.08f) });

        var homeSign = new InfoSign
        {
            Name = "HomeSign",
            Title = "Your shack",
            Message = "Home, such as it is. What you gather rides in your pack: empty it into the chest by the door so it's safe and everyone can use it. Build within 30 m (42 m once it's a cottage). Sleep in the bed to skip the night and save. In co-op, the night passes once everyone is in bed, so build a bedroll each. The plans board shows what the cottage needs.",
            Position = ShackPos + new Vector3(3, 0, 4),
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        AddChild(homeSign);
        homeSign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 1.4f, 0.3f) }, Position = new Vector3(0, 0.7f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.1f, 1.2f, 0.1f) }, Mat(WoodDark), new Vector3(0, 0.6f, 0), Vector3.Zero, homeSign);
        Visual(new BoxMesh { Size = new Vector3(0.7f, 0.4f, 0.06f) }, Mat(Wood), new Vector3(0, 1.15f, 0), Vector3.Zero, homeSign);
    }

    private void ScatterProps()
    {
        var tree = GD.Load<PackedScene>("res://scenes/props/Tree.tscn");
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var placed = new List<Vector3> { ShackPos, BlossomPos, PoolPos, ShackPos + new Vector3(-4, 0, 7), ShackPos + new Vector3(-6, 0, 3) };

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
        n.Name = $"Res_{_resourceCount++}"; // stable name: the network refers to it by path
        AddChild(n);
        n.Position = pos;
        n.Rotation = new Vector3(0, _rng.RandfRange(0, Mathf.Tau), 0);
        float s = _rng.RandfRange(0.85f, 1.3f);
        n.Scale = new Vector3(s, s, s);
        placed.Add(pos);
    }

    // ================================================================== iron ore

    /// <summary>Where iron ore crops out: around the foot of the mesa in Falls Valley, and one by
    /// at the west end of the home plateau. Only a pickaxe breaks it.</summary>
    public static readonly Vector3[] OreSpots =
    {
        new(128, 0, 6), new(164, 0, 4), new(138, 0, 42), new(117, 0, 24), new(-50, 0, -22),
    };

    private void BuildOreVeins()
    {
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var ore = StyleMaterials.Rock(new Color(0.3f, 0.23f, 0.22f), new Color(0.58f, 0.33f, 0.2f), 0.55f, 0.5f);
        var nugget = StyleMaterials.Toon(new Color(0.85f, 0.52f, 0.28f), 0.6f);
        for (int i = 0; i < OreSpots.Length; i++)
        {
            var n = rock.Instantiate<Harvestable>();
            n.Name = $"Res_{_resourceCount++}";
            n.DropItemId = "iron_ore";
            n.RequiresTool = true;
            n.DamagePerDrop = 20f;
            n.BonusOnBreak = 2;
            n.RespawnSeconds = 300f;
            n.AddToGroup("iron_ore");
            AddChild(n);
            n.Position = OreSpots[i];
            n.Rotation = new Vector3(0, i * 1.9f, 0);
            var visual = n.GetNode<Node3D>("Visual");
            foreach (var m in visual.FindChildren("*", "MeshInstance3D", true, false))
                ((MeshInstance3D)m).MaterialOverride = ore;
            // Rusty nuggets poking out of the rock: tells it apart from plain stone.
            for (int k = 0; k < 7; k++)
            {
                float a = k * 2.4f + i, r = 0.55f + 0.1f * (k % 3);
                Visual(new BoxMesh { Size = Vector3.One * (0.16f + 0.05f * (k % 2)) }, nugget,
                    new Vector3(Mathf.Cos(a) * r, 0.35f + 0.22f * (k % 3), Mathf.Sin(a) * r), new Vector3(a, a * 0.7f, 0.4f), visual);
            }
        }
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

    /// <summary>A dirt road or track: a flat strip <paramref name="length"/> long along its yaw, with
    /// ragged grassy edges, ruts and pebbles (<see cref="StyleMaterials.PathShader"/>). Give
    /// overlapping strips slightly different heights so they don't flicker.</summary>
    private MeshInstance3D Road(Vector3 center, float length, float yaw, float width, bool ruts = true, Color? tone = null, Node3D parent = null)
    {
        var dirt = tone ?? Dirt;
        var size = new Vector2(width, length);
        var mi = Visual(new PlaneMesh { Size = size }, StyleMaterials.Path(size, dirt.Darkened(0.12f), dirt.Lightened(0.06f), ruts: ruts),
            center, new Vector3(0, yaw, 0), parent);
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return mi;
    }

    /// <summary>Trodden ground with a rounded, ragged edge (a yard, a square, a camp).</summary>
    private MeshInstance3D PathPatch(Vector3 center, Vector2 size, Color dirtA, Color dirtB, Node3D parent = null)
    {
        var mi = Visual(new PlaneMesh { Size = size }, StyleMaterials.Path(size, dirtA, dirtB, patch: true), center, Vector3.Zero, parent);
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var at = center + (parent?.Position ?? Vector3.Zero);
        Patches.Add((new Vector2(at.X, at.Z), size / 2f));
        return mi;
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
