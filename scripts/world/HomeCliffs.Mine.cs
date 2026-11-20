using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The Forest Road and the Hollow Mine (GDD §11, step 1: bronze). West out of Ashby
/// through a gap in the vale wall, a short forest walk with wild boars, to an old mine dug into
/// the rock. Inside it's dark: torches you build and a worn Lantern are the only light, and cave
/// crawlers keep out of it. An upper gallery with copper and tin veins, a sunlit shaft where the
/// Mine Blossom grows, a deep gallery with more ore and a locked iron gate, and the Crawler
/// Mother's lair at the bottom.
///
/// The mine is one floor, carved out of a solid block of rock: <see cref="MineRooms"/> are the
/// hollows, everything else in <see cref="MineBox"/> is wall, under one roof with a hole for
/// the shaft. Built after everything else with its own random numbers, so the rest of the world
/// comes out exactly as before.</summary>
public partial class HomeCliffs
{
    /// <summary>The vale's west wall is open between these (z), west of Ashby.</summary>
    public const float ForestGapMinZ = 190f, ForestGapMaxZ = 212f;
    /// <summary>The forest floor between the vale and the mine (enemies there stay inside it).</summary>
    public static readonly Rect2 ForestBounds = new(-148, 172, 82, 54);
    public static readonly Vector3 ForestRoadStart = new(-15.5f, 0, 199);
    public static readonly Vector3 MineMouth = new(-150, 0, 200);

    /// <summary>The whole mine (x, z): solid rock except for the rooms.</summary>
    public static readonly Rect2 MineBox = new(-246, 176, 96, 86);
    /// <summary>Enemies in the mine are kept inside this.</summary>
    public static readonly Rect2 MineBounds = new(-244, 178, 94, 82);
    /// <summary>Floor to the underside of the roof.</summary>
    public const float MineCeiling = 5f;
    private const float MineRoofTop = 7f, MineCell = 2f;

    public static readonly Rect2 MineEntrance = new(-172, 197, 22, 6);
    public static readonly Rect2 UpperGallery = new(-196, 184, 24, 30);
    public static readonly Rect2 ShaftHall = new(-198, 232, 26, 22);
    public static readonly Rect2 DeepGallery = new(-240, 226, 24, 32);
    public static readonly Rect2 MotherLair = new(-240, 180, 30, 26);

    /// <summary>The hollows of the mine, in order from the mouth.</summary>
    public static readonly Rect2[] MineRooms =
    {
        MineEntrance,
        UpperGallery,
        new(-186, 214, 6, 18),    // down to the shaft
        ShaftHall,
        new(-216, 240, 18, 6),    // west to the deep gallery
        DeepGallery,
        new(-246, 247, 6, 6),     // the recess behind the iron gate
        new(-231, 206, 6, 20),    // north to the lair
        MotherLair,
    };

    /// <summary>The collapsed shaft that lets daylight in, and the Mine Blossom under it.</summary>
    public static readonly Vector3 MineShaft = new(-185, 0, 243);
    public const float ShaftHalf = 4f;
    public static readonly Vector3 MineGate = new(-240, 0, 250);

    public static readonly Vector3[] CopperSpots =
    {
        new(-194.5f, 0, 190), new(-188, 0, 212.5f), new(-174, 0, 186), new(-194.5f, 0, 206),
        new(-238.5f, 0, 232), new(-222, 0, 256.5f), new(-212, 0, 182),
    };
    public static readonly Vector3[] TinSpots =
    {
        new(-173.5f, 0, 209), new(-182, 0, 185.5f),
        new(-238.5f, 0, 240), new(-228, 0, 227.5f), new(-217.5f, 0, 250),
    };

    /// <summary>True inside the mine (under its roof): the sky's light doesn't reach here.</summary>
    public static bool InMine(Vector3 p)
    {
        if (p.Y > MineCeiling + 0.5f || !MineBox.HasPoint(new Vector2(p.X, p.Z))) return false;
        // The first few metres of the entrance still catch some daylight.
        return p.X < MineMouth.X - 3f;
    }

    public static bool InMineRoom(Vector3 p)
    {
        var q = new Vector2(p.X, p.Z);
        foreach (var r in MineRooms) if (r.HasPoint(q)) return true;
        return false;
    }

    private RandomNumberGenerator _mrng;
    private SpotLight3D _shaftSun;
    private OmniLight3D _shaftFill;
    private int _forestCount;

    private void BuildForestAndMine()
    {
        _mrng = new RandomNumberGenerator { Seed = (ulong)Seed * 53 + 11 };
        BuildForest();
        BuildMine();
    }

    /// <summary>Called every frame: daylight down the shaft follows the time of day.</summary>
    private void TickMine()
    {
        if (_shaftSun == null) return;
        float t = GameState.Instance.TimeOfDay;
        float elevation = Mathf.Sin((t - 0.25f) * Mathf.Tau);
        float day = Mathf.Clamp(elevation * 3f, 0f, 1f);
        float wet = Weather.Wetness;
        _shaftSun.LightEnergy = Mathf.Lerp(0.6f, 5f, day) * (1f - 0.5f * wet);
        _shaftSun.LightColor = new Color(0.6f, 0.68f, 1f).Lerp(new Color(1f, 0.93f, 0.8f), day);
        _shaftFill.LightEnergy = Mathf.Lerp(0.25f, 1.1f, day);
    }

    // ================================================================== the forest

    private void BuildForest()
    {
        // Floor: x -150..-60 (it meets the vale's at x -60), z 166..234.
        var ground = Block(new Vector3(-105, -6, 200), new Vector3(90, 12, 68), Vector3.Zero, Grass);
        ground.Name = "Ground";
        ((MeshInstance3D)ground.GetChild(0)).MaterialOverride = GroundMaterial;

        // Rock walls north and south.
        foreach (float z in new[] { 165f, 235f })
            for (float x = -146; x <= -68; x += 9)
            {
                float h = _mrng.RandfRange(16, 30);
                var size = new Vector3(_mrng.RandfRange(11, 15), h, _mrng.RandfRange(10, 14));
                var pos = new Vector3(x + _mrng.RandfRange(-2, 2), h / 2f - 4, z + _mrng.RandfRange(-1.5f, 1.5f));
                MRock(pos, size, new Vector3(_mrng.RandfRange(-0.1f, 0.1f), _mrng.RandfRange(-0.4f, 0.4f), _mrng.RandfRange(-0.1f, 0.1f)));
            }

        // The road: out of Ashby's square, through the gap and the trees to the mine mouth.
        Trail(0.022f, 3.6f, new[]
        {
            ForestRoadStart, new Vector3(-40, 0, 200.5f), new Vector3(-66, 0, 200), new Vector3(-92, 0, 194),
            new Vector3(-118, 0, 205), new Vector3(-140, 0, 200), MineMouth,
        }, tone: Dirt.Darkened(0.08f));

        MineSign("ForestSign", new Vector3(-22, 0, 196.6f), Mathf.Pi / 2f, "The Forest Road", "MINE →",
            "West through the trees: the old Hollow Mine. Mind the wild boars. Inside it's pitch dark: bring torches (build mode, " +
            "they go up anywhere) and a Lantern (workbench), and the crawlers will keep their distance.");

        // Dense pines and broadleaves, kept off the road and out of the boars' clearings.
        var tree = GD.Load<PackedScene>("res://scenes/props/Tree.tscn");
        var placed = new List<Vector3> { MineMouth };
        int trees = 0, guard = 0;
        while (trees < 46 && guard++ < 3000)
        {
            var p = new Vector3(_mrng.RandfRange(-146, -70), 0, _mrng.RandfRange(172, 228));
            if (OnPath(p) || (p.X < -142 && Mathf.Abs(p.Z - MineMouth.Z) < 12)) continue;
            bool clear = true;
            foreach (var q in placed)
                if (p.DistanceTo(q) < 4.5f) { clear = false; break; }
            foreach (var q in Wildlife.HerdSites)
                if (p.DistanceTo(q) < 9f) { clear = false; break; }
            if (!clear) continue;
            var n = tree.Instantiate<Node3D>();
            n.Name = $"Res_Forest_{_forestCount++}"; // stable name: the network refers to it by path
            AddChild(n);
            n.Position = p;
            n.Rotation = new Vector3(0, _mrng.RandfRange(0, Mathf.Tau), 0);
            n.Scale = Vector3.One * _mrng.RandfRange(0.9f, 1.4f);
            placed.Add(p);
            trees++;
        }
        string[] small = { "Fern_1", "Fern_1", "Mushroom_Common", "Mushroom_Laetiporus", "Grass_Wispy_Tall", "Plant_1", "Plant_7", "Bush_Common", "Clover_2", "Pebble_Round_3" };
        for (int i = 0, n = 0; n < 170 && i < 3000; i++)
        {
            var p = new Vector3(_mrng.RandfRange(-147, -64), 0, _mrng.RandfRange(170, 230));
            if (OnPath(p)) continue;
            Prop(Models.Nature(small[_mrng.RandiRange(0, small.Length - 1)]), p, _mrng.RandfRange(0, Mathf.Tau), _mrng.RandfRange(0.8f, 1.5f));
            n++;
        }
        foreach (var p in new[] { new Vector3(-80, 0, 176), new Vector3(-132, 0, 224), new Vector3(-108, 0, 214) })
            Prop(Models.Nature("DeadTree_" + _mrng.RandiRange(1, 5)), p, _mrng.RandfRange(0, Mathf.Tau), 1.2f);
        // Dappled light and drifting motes under the canopy.
        Particles(70, 8f, new Vector3(40, 3, 26), new Vector3(-105, 3, 200), new Color(1f, 0.95f, 0.7f, 0.5f), 0.04f, 0.08f,
            new Vector3(0.1f, 0.05f, 0.05f), emissive: 1.5f);
    }

    // ================================================================== the mine

    private Material _mineRock, _mineFloor;

    private void BuildMine()
    {
        _mineRock = StyleMaterials.Rock(new Color(0.16f, 0.15f, 0.17f), new Color(0.3f, 0.28f, 0.27f), 0.75f, 0.25f);
        _mineFloor = StyleMaterials.Rock(new Color(0.2f, 0.18f, 0.17f), new Color(0.27f, 0.24f, 0.21f), 0.5f, 0.1f);

        var root = new Node3D { Name = "HollowMine" };
        AddChild(root);

        // Floor under the whole block.
        var floor = Block(new Vector3(MineBox.GetCenter().X, -6, MineBox.GetCenter().Y), new Vector3(MineBox.Size.X, 12, MineBox.Size.Y), Vector3.Zero, Slate, root);
        floor.Name = "MineFloor";
        ((MeshInstance3D)floor.GetChild(0)).MaterialOverride = _mineFloor;

        CarveMine(root);
        BuildMineRoof(root);
        BuildMineFacade();
        BuildMineSupports(root);
        BuildShaft(root);
        BuildMineGate(root);
        BuildMineVeins();
        DecorateMine(root);

        // A worn track from the mouth into the upper gallery.
        Trail(0.02f, 2.2f, new[] { MineMouth, new Vector3(-172, 0, 200), new Vector3(-184, 0, 199) }, ruts: true, tone: new Color(0.3f, 0.25f, 0.2f));
        MineSign("MineSign", MineMouth + new Vector3(4, 0, -5), Mathf.Pi / 2f, "The Hollow Mine", "HOLLOW MINE",
            "Dug for copper and tin long ago, and left to the dark. Scratched under the name: 'Carry light. The crawlers fear it.'");
    }

    /// <summary>Everything in the mine's block that isn't a room is rock: one row of cells at a
    /// time, each run of solid cells one block.</summary>
    private void CarveMine(Node3D root)
    {
        int cols = (int)(MineBox.Size.X / MineCell), rows = (int)(MineBox.Size.Y / MineCell);
        for (int r = 0; r < rows; r++)
        {
            float z = MineBox.Position.Y + (r + 0.5f) * MineCell;
            int start = -1;
            for (int c = 0; c <= cols; c++)
            {
                bool solid = c < cols && !InMineRoom(new Vector3(MineBox.Position.X + (c + 0.5f) * MineCell, 0, z));
                if (solid && start < 0) start = c;
                if (!solid && start >= 0)
                {
                    float x0 = MineBox.Position.X + start * MineCell, x1 = MineBox.Position.X + c * MineCell;
                    var wall = Block(new Vector3((x0 + x1) / 2f, MineRoofTop / 2f - 0.1f, z), new Vector3(x1 - x0, MineRoofTop + 0.2f, MineCell),
                        Vector3.Zero, Slate, root);
                    ((MeshInstance3D)wall.GetChild(0)).MaterialOverride = _mineRock;
                    start = -1;
                }
            }
        }
    }

    /// <summary>One roof over the whole mine, open over the shaft.</summary>
    private void BuildMineRoof(Node3D root)
    {
        float x0 = MineBox.Position.X, x1 = MineBox.End.X, z0 = MineBox.Position.Y, z1 = MineBox.End.Y;
        float hx0 = MineShaft.X - ShaftHalf, hx1 = MineShaft.X + ShaftHalf, hz0 = MineShaft.Z - ShaftHalf, hz1 = MineShaft.Z + ShaftHalf;
        float y = (MineCeiling + MineRoofTop) / 2f, h = MineRoofTop - MineCeiling;
        void Piece(float ax, float bx, float az, float bz)
        {
            var b = Block(new Vector3((ax + bx) / 2f, y, (az + bz) / 2f), new Vector3(bx - ax, h, bz - az), Vector3.Zero, Slate, root);
            b.Name = "MineRoof";
            ((MeshInstance3D)b.GetChild(0)).MaterialOverride = _mineRock;
        }
        Piece(x0, hx0, z0, z1);
        Piece(hx1, x1, z0, z1);
        Piece(hx0, hx1, z0, hz0);
        Piece(hx0, hx1, hz1, z1);
        // Rock heaped on the roof, so from outside it's a hill, not a slab.
        for (int i = 0; i < 18; i++)
        {
            var p = new Vector3(_mrng.RandfRange(x0 + 6, x1 - 6), MineRoofTop - 1f, _mrng.RandfRange(z0 + 6, z1 - 6));
            if (new Vector2(p.X - MineShaft.X, p.Z - MineShaft.Z).Length() < ShaftHalf + 7) continue;
            float s = _mrng.RandfRange(12, 22);
            MRock(p + Vector3.Up * s * 0.2f, new Vector3(s, s * _mrng.RandfRange(0.4f, 0.8f), s), new Vector3(0, _mrng.RandfRange(0, 3), 0), collide: false);
        }
    }

    /// <summary>The rock face the mouth is cut into, seen from the forest, and its timber frame.</summary>
    private void BuildMineFacade()
    {
        foreach (float z in new[] { 172f, 180f, 188f, 213f, 221f, 229f })
        {
            float h = _mrng.RandfRange(12, 20);
            MRock(new Vector3(MineMouth.X + _mrng.RandfRange(-2, 0.5f), h / 2f - 2, z + _mrng.RandfRange(-1, 1)), new Vector3(_mrng.RandfRange(8, 11), h, 9),
                new Vector3(0, _mrng.RandfRange(-0.4f, 0.4f), _mrng.RandfRange(-0.08f, 0.08f)));
        }
        // The forest's north-west corner, where the mine's block doesn't reach: keep everyone on the ground.
        Block(new Vector3(-154, 4, 170.5f), new Vector3(8, 12, 11), Vector3.Zero, Slate, visible: false);
        // Over the mouth: a lintel rock that leaves the doorway clear.
        MRock(MineMouth + new Vector3(-1.5f, MineCeiling + 4f, 0), new Vector3(9, 7, 12), new Vector3(0, 0.2f, 0));
        var wood = Mat(WoodDark);
        foreach (float side in new[] { -1f, 1f })
            Block(MineMouth + new Vector3(0.5f, 2.2f, side * 3.2f), new Vector3(0.45f, 4.4f, 0.45f), Vector3.Zero, WoodDark);
        Visual(new BoxMesh { Size = new Vector3(0.5f, 0.5f, 7.4f) }, wood, MineMouth + new Vector3(0.5f, 4.45f, 0), Vector3.Zero);
    }

    /// <summary>Timber posts and beams along the tunnels: the mine was worked once.</summary>
    private void BuildMineSupports(Node3D root)
    {
        var wood = Mat(new Color(0.32f, 0.22f, 0.14f));
        void Frame(Vector3 at, bool alongX, float width)
        {
            var side = alongX ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
            foreach (float s in new[] { -1f, 1f })
                Block(at + side * s * (width / 2f - 0.3f) + Vector3.Up * 2.4f, new Vector3(0.3f, 4.8f, 0.3f), Vector3.Zero, WoodDark, root);
            Visual(new BoxMesh { Size = alongX ? new Vector3(0.35f, 0.35f, width) : new Vector3(width, 0.35f, 0.35f) }, wood,
                at + Vector3.Up * (MineCeiling - 0.25f), Vector3.Zero, root);
        }
        foreach (float x in new[] { -156f, -164f }) Frame(new Vector3(x, 0, 200), true, 6);
        foreach (float z in new[] { 219f, 227f }) Frame(new Vector3(-183, 0, z), false, 6);
        foreach (float x in new[] { -203f, -211f }) Frame(new Vector3(x, 0, 243), true, 6);
        foreach (float z in new[] { 211f, 219f }) Frame(new Vector3(-228, 0, z), false, 6);
    }

    /// <summary>The collapsed shaft: a square of sky, daylight pouring down on the Mine Blossom,
    /// which grows up toward it. A sanctuary like the others, so crawlers keep away.</summary>
    private void BuildShaft(Node3D root)
    {
        // The chimney above the hole.
        float top = 22f, h = top - MineRoofTop, w = ShaftHalf * 2f;
        foreach (var (off, size) in new[]
                 {
                     (new Vector3(-ShaftHalf - 1, 0, 0), new Vector3(2, h, w + 4)), (new Vector3(ShaftHalf + 1, 0, 0), new Vector3(2, h, w + 4)),
                     (new Vector3(0, 0, -ShaftHalf - 1), new Vector3(w, h, 2)), (new Vector3(0, 0, ShaftHalf + 1), new Vector3(w, h, 2)),
                 })
        {
            var b = Block(MineShaft + off + Vector3.Up * (MineRoofTop + h / 2f), size, Vector3.Zero, Slate, root);
            ((MeshInstance3D)b.GetChild(0)).MaterialOverride = _mineRock;
        }
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2f + 0.4f;
            MRock(MineShaft + new Vector3(Mathf.Cos(a) * 8f, top - 2f, Mathf.Sin(a) * 8f), new Vector3(8, 6, 8), new Vector3(0, a, 0), collide: false);
        }

        _shaftSun = new SpotLight3D
        {
            Name = "ShaftSun", Position = MineShaft + Vector3.Up * (top - 1f), Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0),
            SpotRange = top + 4f, SpotAngle = 24f, SpotAttenuation = 0.6f, ShadowEnabled = true, LightEnergy = 4f,
        };
        root.AddChild(_shaftSun);
        _shaftFill = new OmniLight3D { Name = "ShaftFill", Position = MineShaft + Vector3.Up * 3f, LightColor = new Color(1f, 0.85f, 0.9f), OmniRange = 11f, LightEnergy = 1f };
        root.AddChild(_shaftFill);
        // Motes in the beam, and petals.
        Particles(90, 10f, new Vector3(ShaftHalf - 1, 6, ShaftHalf - 1), MineShaft + Vector3.Up * 6f, new Color(1f, 0.97f, 0.85f, 0.55f), 0.03f, 0.06f,
            new Vector3(0, -0.08f, 0.03f), emissive: 2f);
        Particles(40, 9f, new Vector3(3, 2, 3), MineShaft + Vector3.Up * 5f, new Color(1f, 0.6f, 0.75f), 0.08f, 0.15f, new Vector3(0.1f, -0.3f, 0.1f));

        var shrine = new BlossomShrine
        {
            Name = "MineShrine", ShrineName = "Mine Blossom", SafeRadius = 7f, Position = MineShaft,
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        AddChild(shrine);
        shrine.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.6f, Height = 4f }, Position = new Vector3(0, 2, 0) });
        var tree = Models.Instance(Models.CherryTree);
        tree.Scale = Vector3.One * 1.05f;
        shrine.AddChild(tree);
        shrine.AddChild(new OmniLight3D { Position = new Vector3(0, 4.5f, 1.5f), LightColor = Blush, LightEnergy = 1.6f, OmniRange = 9f });
        shrine.AddChild(new Label3D
        {
            Text = "Mine Blossom", Position = new Vector3(0, 4.4f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 56, PixelSize = 0.01f, OutlineSize = 12, Modulate = Blush,
        });
        // A ring of moss where the light falls.
        Visual(new CylinderMesh { TopRadius = ShaftHalf + 0.5f, BottomRadius = ShaftHalf + 0.5f, Height = 0.03f, RadialSegments = 16 },
            Mat(new Color(0.3f, 0.42f, 0.26f)), MineShaft + Vector3.Up * 0.02f, Vector3.Zero, root);

        // The daylight keeps crawlers off as a light does.
        var lightNode = new Node3D { Name = "ShaftLight", Position = MineShaft };
        root.AddChild(lightNode);
        LightSources.Register(lightNode, ShaftHalf + 3f);
    }

    /// <summary>A locked iron gate in the deep gallery's west wall, with a note toward the spires.</summary>
    private void BuildMineGate(Node3D root)
    {
        var iron = StyleMaterials.Toon(new Color(0.3f, 0.2f, 0.16f), 0.3f);
        var gate = Block(MineGate + Vector3.Up * 2.4f, new Vector3(0.3f, 4.8f, 6f), Vector3.Zero, Slate, root, visible: false);
        gate.Name = "IronGate";
        for (float z = -2.75f; z <= 2.76f; z += 0.5f)
            Visual(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 4.8f, RadialSegments = 6 }, iron,
                MineGate + new Vector3(0, 2.4f, z), Vector3.Zero, root);
        foreach (float y in new[] { 0.6f, 2.6f, 4.4f })
            Visual(new BoxMesh { Size = new Vector3(0.12f, 0.14f, 6f) }, iron, MineGate + new Vector3(0, y, 0), Vector3.Zero, root);
        Visual(new BoxMesh { Size = new Vector3(0.25f, 0.4f, 0.35f) }, StyleMaterials.Toon(new Color(0.5f, 0.42f, 0.25f)), MineGate + new Vector3(0.18f, 2.2f, 0.6f), Vector3.Zero, root);

        var sign = new InfoSign
        {
            Name = "GateNote", Title = "A locked iron gate",
            Message = "Rusted iron bars, locked fast, and cold air beyond. Scratched into the frame: 'What iron shut, iron opens. " +
                      "The good seams are in the spires past Falls Valley.'",
            Position = MineGate + new Vector3(1.4f, 0, -3.6f), CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        root.AddChild(sign);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 1.4f, 0.4f) }, Position = new Vector3(0, 0.7f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.1f, 1.2f, 0.1f) }, Mat(WoodDark), new Vector3(0, 0.6f, 0), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(0.06f, 0.45f, 0.7f) }, Mat(new Color(0.78f, 0.7f, 0.55f)), new Vector3(0, 1.15f, 0), Vector3.Zero, sign);
    }

    /// <summary>Copper (green-crusted) and tin (pale grey) veins: a pickaxe breaks them.</summary>
    private void BuildMineVeins()
    {
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        void Vein(string id, Vector3 at, int i, Material body, Material nugget)
        {
            var n = rock.Instantiate<Harvestable>();
            n.Name = $"Vein_{id}_{i}";
            n.DropItemId = id;
            n.Tool = "pick";
            n.RequiresTool = true;
            n.DamagePerDrop = 20f;
            n.BonusOnBreak = 2;
            n.RespawnSeconds = 300f;
            n.AddToGroup(id);
            AddChild(n);
            n.Position = at;
            n.Rotation = new Vector3(0, i * 2.1f, 0);
            var visual = n.GetNode<Node3D>("Visual");
            foreach (var m in visual.FindChildren("*", "MeshInstance3D", true, false))
                ((MeshInstance3D)m).MaterialOverride = body;
            for (int k = 0; k < 8; k++)
            {
                float a = k * 2.4f + i, r = 0.5f + 0.1f * (k % 3);
                Visual(new BoxMesh { Size = Vector3.One * (0.15f + 0.05f * (k % 2)) }, nugget,
                    new Vector3(Mathf.Cos(a) * r, 0.3f + 0.22f * (k % 3), Mathf.Sin(a) * r), new Vector3(a, a * 0.7f, 0.4f), visual);
            }
        }
        var copperRock = StyleMaterials.Rock(new Color(0.22f, 0.2f, 0.2f), new Color(0.3f, 0.5f, 0.42f), 0.6f, 0.4f);
        var copperBits = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.5f, 0.28f), Metallic = 0.6f, Roughness = 0.35f, EmissionEnabled = true, Emission = new Color(0.4f, 0.16f, 0.05f), EmissionEnergyMultiplier = 0.6f };
        var tinRock = StyleMaterials.Rock(new Color(0.24f, 0.24f, 0.26f), new Color(0.5f, 0.52f, 0.55f), 0.6f, 0.4f);
        var tinBits = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.84f, 0.86f), Metallic = 0.7f, Roughness = 0.3f, EmissionEnabled = true, Emission = new Color(0.25f, 0.27f, 0.3f), EmissionEnergyMultiplier = 0.6f };
        for (int i = 0; i < CopperSpots.Length; i++) Vein("copper_ore", CopperSpots[i], i, copperRock, copperBits);
        for (int i = 0; i < TinSpots.Length; i++) Vein("tin_ore", TinSpots[i], i, tinRock, tinBits);
    }

    /// <summary>Rubble, an old cart, and glowing fungus (just enough to see the walls by, not
    /// enough to count as light).</summary>
    private void DecorateMine(Node3D root)
    {
        // Rubble along the walls.
        foreach (var room in MineRooms)
        {
            int n = (int)(room.Size.X * room.Size.Y / 60f);
            for (int i = 0; i < n; i++)
            {
                bool alongX = _mrng.Randf() < 0.5f;
                var p = alongX
                    ? new Vector3(_mrng.RandfRange(room.Position.X + 1, room.End.X - 1), 0, _mrng.Randf() < 0.5f ? room.Position.Y + 0.7f : room.End.Y - 0.7f)
                    : new Vector3(_mrng.Randf() < 0.5f ? room.Position.X + 0.7f : room.End.X - 0.7f, 0, _mrng.RandfRange(room.Position.Y + 1, room.End.Y - 1));
                if (new Vector2(p.X - MineShaft.X, p.Z - MineShaft.Z).Length() < 3f) continue;
                float s = _mrng.RandfRange(0.8f, 1.8f);
                MRock(p, new Vector3(s, s * 0.7f, s), new Vector3(0, _mrng.RandfRange(0, 3), 0), collide: false, parent: root);
            }
        }

        // An abandoned ore cart in the upper gallery.
        var cart = new Node3D { Name = "OreCart", Position = new Vector3(-184, 0, 194), Rotation = new Vector3(0, 0.3f, 0) };
        root.AddChild(cart);
        var ironM = Mat(new Color(0.2f, 0.2f, 0.22f));
        Block(new Vector3(0, 0.75f, 0), new Vector3(1.2f, 0.8f, 1.8f), Vector3.Zero, new Color(0.34f, 0.23f, 0.15f), cart);
        Visual(new BoxMesh { Size = new Vector3(1.0f, 0.2f, 1.6f) }, Mat(new Color(0.75f, 0.45f, 0.3f)), new Vector3(0, 1.2f, 0), Vector3.Zero, cart);
        foreach (float sx in new[] { -0.62f, 0.62f })
            foreach (float sz in new[] { -0.6f, 0.6f })
                Visual(new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.26f, Height = 0.1f, RadialSegments = 10 }, ironM,
                    new Vector3(sx, 0.26f, sz), new Vector3(0, 0, Mathf.Pi / 2f), cart);

        // Glowing fungus in clumps.
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.9f, 0.85f), EmissionEnabled = true, Emission = new Color(0.2f, 0.85f, 0.8f),
            EmissionEnergyMultiplier = 1.6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var stalk = Mat(new Color(0.55f, 0.6f, 0.55f));
        foreach (var room in MineRooms)
        {
            if (room.Size.X < 10 && room.Size.Y < 10) continue;
            for (int c = 0; c < 3; c++)
            {
                var at = new Vector3(_mrng.RandfRange(room.Position.X + 1, room.End.X - 1), 0, _mrng.Randf() < 0.5f ? room.Position.Y + 0.9f : room.End.Y - 0.9f);
                for (int k = 0; k < 5; k++)
                {
                    var p = at + new Vector3(_mrng.RandfRange(-0.6f, 0.6f), 0, _mrng.RandfRange(-0.4f, 0.4f));
                    float hgt = _mrng.RandfRange(0.12f, 0.3f);
                    Visual(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.025f, Height = hgt, RadialSegments = 4 }, stalk, p + Vector3.Up * hgt / 2f, Vector3.Zero, root);
                    var cap = Visual(new SphereMesh { Radius = 0.07f + hgt * 0.15f, Height = 0.06f, RadialSegments = 6, Rings = 2, IsHemisphere = true }, glow, p + Vector3.Up * hgt, Vector3.Zero, root);
                    cap.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
            }
        }

        // Drips of dust in the dark.
        Particles(60, 9f, new Vector3(20, 2, 20), new Vector3(-205, 2.5f, 220), new Color(0.6f, 0.7f, 0.8f, 0.25f), 0.03f, 0.05f, new Vector3(0, -0.05f, 0));
    }

    // ================================================================== helpers

    /// <summary>Rock with the mine's own random numbers.</summary>
    private Node3D MRock(Vector3 pos, Vector3 size, Vector3 rot, bool collide = true, Node3D parent = null)
    {
        var path = Models.AllRocks[_mrng.RandiRange(0, Models.AllRocks.Length - 1)];
        size *= 1.15f;
        Node3D node = collide ? Models.RockBody(path, size) : Models.Instance(path);
        if (!collide) node.Scale = size;
        node.Position = pos;
        node.Rotation = rot;
        (parent ?? this).AddChild(node);
        return node;
    }

    private void MineSign(string name, Vector3 pos, float rotY, string title, string label, string message)
    {
        var sign = new InfoSign { Name = name, Title = title, Message = message, Position = pos, CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        AddChild(sign);
        sign.Rotation = new Vector3(0, rotY, 0);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 2.2f, 0.4f) }, Position = new Vector3(0, 1.1f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(WoodDark), new Vector3(0, 1, -0.115f), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(1.5f, 0.5f, 0.08f) }, Mat(Wood), new Vector3(0, 1.8f, 0), Vector3.Zero, sign);
        sign.AddChild(new Label3D { Text = label, Position = new Vector3(0, 1.8f, 0.06f), FontSize = 40, PixelSize = 0.006f, Modulate = new Color(0.15f, 0.1f, 0.08f) });
    }
}
