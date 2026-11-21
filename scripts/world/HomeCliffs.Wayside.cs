using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Places to find in the country between the plateau and Ashby, so the long road has
/// things along it and the hills have reasons to leave it:
/// <list type="bullet">
/// <item>the Old Watchtower, a ruin on the highest hilltop east of the road, seen for miles: climb
/// its walls for the lookout's strongbox;</item>
/// <item>the Elder Oak, one huge tree on a meadow beside the road;</item>
/// <item>the Wayside Blossom, a sanctuary halfway along (respawn, healing, fast travel);</item>
/// <item>the Gate Stones, two pillars and a lintel the road runs under;</item>
/// <item>an overturned wagon by the road, its crates still there to search;</item>
/// <item>the Hermit's Hut, up a side track to the west, with a note pointing on;</item>
/// <item>the Old Quarry, off the roads south-east, a pit with copper and tin in its walls.</item>
/// </list>
/// Each is a <see cref="Landmark"/> (discovered when anyone comes near: a toast and XP for
/// everyone) and most have a <see cref="LootCache"/> (searched once per world). The roadside
/// ones sit on level ground (flatten discs in <see cref="ConfigureTerrain"/>); the tower stands
/// on its hill with its foundations sunk into the slope. Built after the country is dressed,
/// so the scatter's resource names don't shift.</summary>
public partial class HomeCliffs
{
    public static readonly Vector3 ElderOak = new(38, 0, 214);
    public static readonly Vector3 WaysideShrinePos = new(-26, 0, 292);
    public static readonly Vector3 WreckedWagon = new(8, 0, 168);
    public static readonly Vector3 HermitHut = new(-118, 0, 388);
    public static readonly Vector3 OldQuarry = new(150, 0, 420);

    /// <summary>Where the road runs under the Gate Stones (halfway between two of its bends).</summary>
    public static Vector3 GateStones => (MainRoad[7] + MainRoad[8]) / 2f;

    /// <summary>The highest good hilltop east of the road (found once the hills are known).</summary>
    public static Vector3 OldWatchtower { get; private set; } = new(95, 0, 300);

    /// <summary>The side tracks: to the sanctuary, and up to the hermit's hut.</summary>
    private static Vector3[] ShrineTrack => new[] { new Vector3(-3.6f, 0, 290), new Vector3(-14, 0, 291), WaysideShrinePos + new Vector3(4, 0, 0) };
    private static Vector3[] HermitTrack => new[] { MainRoad[7], new Vector3(-46, 0, 340), new Vector3(-84, 0, 368), HermitHut + new Vector3(5, 0, -1) };

    /// <summary>Where the scatter keeps off (centre, radius).</summary>
    private static IEnumerable<(Vector3 At, float Radius)> WaysideClearings => new[]
    {
        (ElderOak, 12f), (WaysideShrinePos, 10f), (WreckedWagon, 8f), (HermitHut, 13f), (OldQuarry, 21f),
        (OldWatchtower, 10f), (GateStones, 9f),
    };

    private static bool InWaysideClearing(Vector3 p)
    {
        foreach (var (at, r) in WaysideClearings)
            if (new Vector2(p.X - at.X, p.Z - at.Z).LengthSquared() < r * r) return true;
        return false;
    }

    /// <summary>Lay the side tracks (before the hills are shaped: they keep level ground).</summary>
    private void WaysideTracks()
    {
        Trail(0.024f, 2.4f, ShrineTrack, ruts: false, tone: Dirt.Darkened(0.08f));
        Trail(0.026f, 2.2f, HermitTrack, ruts: false, tone: Dirt.Darkened(0.12f));
    }

    /// <summary>Level ground under the roadside places (before Terrain.Lock).</summary>
    private static void WaysideFlats()
    {
        Terrain.FlattenDisc(ElderOak, 10f, 26f);
        Terrain.FlattenDisc(WaysideShrinePos, 12f, 30f);
        Terrain.FlattenDisc(HermitHut, 10f, 26f);
        Terrain.FlattenDisc(OldQuarry, 15f, 26f); // a level floor cut into the hills: the pit
        Terrain.FlattenDisc(GateStones, 10f, 24f);
    }

    /// <summary>Find the tower's hilltop: the highest spot east of the road that's broad enough to
    /// stand a tower on, well off the road and away from the other places (after Terrain.Lock).</summary>
    private static void FindWatchtowerHill()
    {
        float best = -1f;
        var pick = OldWatchtower;
        var road = MainRoad;
        for (float x = 40; x <= 160; x += 3f)
        for (float z = 230; z <= 390; z += 3f)
        {
            var p = new Vector3(x, 0, z);
            float h = Terrain.HeightAt(p);
            if (h < 5f || h > 38f || h <= best) continue;
            if (DistanceToLine(p, road) < 35f || p.DistanceTo(ElderOak) < 40f || p.DistanceTo(OldQuarry) < 50f) continue;
            float lo = h, hi = h;
            for (int k = 0; k < 8; k++)
            {
                float a = Mathf.Tau * k / 8f;
                float s = Terrain.HeightAt(x + Mathf.Cos(a) * 4.5f, z + Mathf.Sin(a) * 4.5f);
                lo = Mathf.Min(lo, s);
                hi = Mathf.Max(hi, s);
            }
            if (hi - lo > 3f) continue;
            best = h;
            pick = p;
        }
        OldWatchtower = pick;
    }

    private static float DistanceToLine(Vector3 p, Vector3[] line)
    {
        var q = new Vector2(p.X, p.Z);
        float best = float.MaxValue;
        for (int i = 0; i < line.Length - 1; i++)
        {
            var a = new Vector2(line[i].X, line[i].Z);
            var b = new Vector2(line[i + 1].X, line[i + 1].Z);
            var ab = b - a;
            float t = ab.LengthSquared() < 0.001f ? 0f : Mathf.Clamp((q - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            best = Mathf.Min(best, q.DistanceTo(a + ab * t));
        }
        return best;
    }

    private void BuildWayside()
    {
        BuildWatchtower();
        BuildElderOak();
        BuildWaysideShrine();
        BuildGateStones();
        BuildWreckedWagon();
        BuildHermitHut();
        BuildQuarry();
    }

    // ================================================================== helpers

    private static readonly Color Stonework = new(0.55f, 0.52f, 0.47f);

    /// <summary>A place's root: built at y = 0 and lifted onto the hills with everything else.</summary>
    private Node3D WaysideRoot(string name, Vector3 at, float yaw = 0f)
    {
        var root = new Node3D { Name = name, Position = new Vector3(at.X, 0, at.Z), Rotation = new Vector3(0, yaw, 0) };
        AddChild(root);
        return root;
    }

    private static void AddLandmark(Node3D root, string title, float radius, Vector3? offset = null) =>
        root.AddChild(new Landmark { Name = "Landmark_" + root.Name.ToString().Replace("Poi_", ""), Title = title, Radius = radius, Position = offset ?? Vector3.Zero });

    /// <summary>A box to search, once: <paramref name="loot"/> is item id → count.</summary>
    private LootCache Cache(Node3D root, string name, string cacheName, Vector3 at, Vector3 size, Color color, float yaw, params (string Id, int N)[] loot)
    {
        var cache = new LootCache
        {
            Name = name, CacheName = cacheName, Position = at, Rotation = new Vector3(0, yaw, 0),
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        foreach (var (id, n) in loot) cache.Loot[id] = n;
        root.AddChild(cache);
        cache.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = new Vector3(0, size.Y / 2f, 0) });
        Visual(new BoxMesh { Size = size }, Mat(color), new Vector3(0, size.Y / 2f, 0), Vector3.Zero, cache);
        // Iron bands.
        foreach (float x in new[] { -size.X * 0.3f, size.X * 0.3f })
            Visual(new BoxMesh { Size = new Vector3(0.06f, size.Y + 0.02f, size.Z + 0.02f) }, Mat(new Color(0.22f, 0.2f, 0.2f)), new Vector3(x, size.Y / 2f, 0), Vector3.Zero, cache);
        return cache;
    }

    /// <summary>How high the ground is at a point beside a place's root, relative to the root.</summary>
    private static float RelGround(Node3D root, Vector3 local)
    {
        var w = root.Position + root.Basis * local;
        return Terrain.HeightAt(w.X, w.Z) - Terrain.HeightAt(root.Position.X, root.Position.Z);
    }

    private void InfoBoard(Node3D root, string name, string title, string message, Vector3 at, float yaw)
    {
        var sign = new InfoSign
        {
            Name = name, Title = title, Message = message, Position = at, Rotation = new Vector3(0, yaw, 0),
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        root.AddChild(sign);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 1.4f, 0.3f) }, Position = new Vector3(0, 0.7f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.12f, 1.3f, 0.12f) }, Mat(WoodDark), new Vector3(0, 0.65f, 0), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(0.8f, 0.5f, 0.06f) }, Mat(new Color(0.82f, 0.76f, 0.62f)), new Vector3(0, 1.1f, 0.08f), new Vector3(0.05f, 0, 0.04f), sign);
    }

    // ================================================================== the Old Watchtower

    /// <summary>A square stone tower, its top broken off, on a hilltop. The walls are climbable rock;
    /// over the lowest one (the west, toward the road) is the old lookout floor and the strongbox.
    /// Its foundations go well down so it stands true on the slope.</summary>
    private void BuildWatchtower()
    {
        var root = WaysideRoot("Poi_OldWatchtower", OldWatchtower);
        AddLandmark(root, "the Old Watchtower", 18f);
        const float half = 3.5f, t = 0.9f, sink = 5f, floor = 8f;
        // Wall tops: west (toward the road) lowest, so there's a way in over it.
        float north = 11.5f, east = 10.2f, south = 12.2f, west = 9.2f;
        Node3D Wall(Vector3 center, Vector3 size)
        {
            var w = Block(center, size, Vector3.Zero, Stonework, root);
            w.AddToGroup(GameState.ClimbGroup);
            return w;
        }
        Wall(new Vector3(0, (north - sink) / 2f, -half + t / 2f), new Vector3(half * 2, north + sink, t));
        Wall(new Vector3(0, (south - sink) / 2f, half - t / 2f), new Vector3(half * 2, south + sink, t));
        Wall(new Vector3(half - t / 2f, (east - sink) / 2f, 0), new Vector3(t, east + sink, half * 2 - t * 2));
        // The west wall has the door in it (toward the road).
        float side = (half * 2 - t * 2 - 1.6f) / 2f;
        Wall(new Vector3(-half + t / 2f, (west - sink) / 2f, -0.8f - side / 2f), new Vector3(t, west + sink, side));
        Wall(new Vector3(-half + t / 2f, (west - sink) / 2f, 0.8f + side / 2f), new Vector3(t, west + sink, side));
        Wall(new Vector3(-half + t / 2f, (west + 2.7f) / 2f, 0), new Vector3(t, west - 2.7f, 1.6f));
        // Merlons left on the high walls, and a bite out of the east one.
        foreach (float x in new[] { -2.6f, 0f, 2.6f })
        {
            Wall(new Vector3(x, north + 0.5f, -half + t / 2f), new Vector3(1.1f, 1f, t));
            if (x != 0f) Wall(new Vector3(x, south + 0.5f, half - t / 2f), new Vector3(1.1f, 1f, t));
        }
        // The lookout floor, and a wooden landing just inside the low wall to step down onto.
        Block(new Vector3(0, floor, 0), new Vector3(half * 2 - t * 2, 0.3f, half * 2 - t * 2), Vector3.Zero, WoodDark, root);
        foreach (float x in new[] { -1.6f, 0f, 1.6f })
            Visual(new BoxMesh { Size = new Vector3(0.22f, 0.22f, half * 2 - t * 2) }, Mat(Wood), new Vector3(x, floor - 0.26f, 0), Vector3.Zero, root);
        // The packed earth inside, level with the doorway.
        Block(new Vector3(0, -0.1f, 0), new Vector3(half * 2 - t * 2, 0.2f, half * 2 - t * 2), Vector3.Zero, Dirt.Darkened(0.3f), root);

        Cache(root, "Cache_Watchtower", "lookout's strongbox", new Vector3(1.4f, floor + 0.15f, -1.3f), new Vector3(0.9f, 0.55f, 0.6f),
            new Color(0.36f, 0.24f, 0.17f), 0.2f, ("coin", 40), ("rune_shard", 2), ("iron_ingot", 2));

        // A tattered banner on the north wall: what you see from the road.
        Visual(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 4.5f, RadialSegments = 6 }, Mat(WoodDark),
            new Vector3(0, north + 1f + 2.25f, -half + t / 2f), Vector3.Zero, root);
        Visual(new BoxMesh { Size = new Vector3(1.8f, 1.1f, 0.04f) }, Mat(new Color(0.62f, 0.17f, 0.15f)),
            new Vector3(0.95f, north + 4.6f, -half + t / 2f), new Vector3(0, 0.15f, -0.06f), root);

        // Fallen stones down the slope.
        var fallen = new[] { new Vector3(5.2f, 0, 1.8f), new Vector3(-1.5f, 0, 5.4f), new Vector3(5.4f, 0, -3.4f), new Vector3(-5.6f, 0, -4f), new Vector3(2.5f, 0, 6.2f) };
        for (int i = 0; i < fallen.Length; i++)
        {
            var f = fallen[i];
            float s = 0.8f + 0.25f * (i % 3);
            Block(new Vector3(f.X, RelGround(root, f) + s * 0.3f, f.Z), new Vector3(s * 1.3f, s * 0.8f, s), new Vector3(0.2f * (i % 2), i * 1.3f, 0.15f), Stonework.Darkened(0.08f), root);
        }
    }

    // ================================================================== the Elder Oak

    private void BuildElderOak()
    {
        var root = WaysideRoot("Poi_ElderOak", ElderOak);
        AddLandmark(root, "the Elder Oak", 18f);
        var tree = Prop(Models.Nature("TwistedTree_3"), Vector3.Zero, 0.7f, 1f, root);
        Models.FitHeight(tree, 21f);
        var trunk = new StaticBody3D { Name = "ElderOakTrunk", CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        trunk.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1.4f, Height = 7f }, Position = new Vector3(0, 3.5f, 0) });
        root.AddChild(trunk);
        // A ring of mushrooms and flowers in its shade, and a bench for travellers.
        for (int i = 0; i < 14; i++)
        {
            float a = i * 0.95f, r = 3.2f + 2.2f * ((i * 7) % 5) / 5f;
            string kind = i % 3 == 0 ? "Mushroom_Common" : i % 3 == 1 ? "Flower_3_Group" : "Flower_4_Group";
            Prop(Models.Nature(kind), new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), a, i % 3 == 0 ? 1.4f : 1.2f, root);
        }
        var bench = new Vector3(-4.2f, 0, 2.4f);
        Block(bench + new Vector3(0, 0.45f, 0), new Vector3(2.2f, 0.12f, 0.5f), new Vector3(0, 0.5f, 0), Wood, root);
        foreach (float x in new[] { -0.8f, 0.8f })
            Visual(new BoxMesh { Size = new Vector3(0.14f, 0.45f, 0.4f) }, Mat(WoodDark), bench + new Vector3(Mathf.Cos(0.5f) * x, 0.22f, -Mathf.Sin(0.5f) * x), new Vector3(0, 0.5f, 0), root);
    }

    // ================================================================== the Wayside Blossom

    private void BuildWaysideShrine()
    {
        PathPatch(WaysideShrinePos + new Vector3(0, 0.05f, 0), new Vector2(13, 13), Dirt.Darkened(0.1f), Dirt.Darkened(0.02f));
        var shrine = new BlossomShrine
        {
            Name = "WaysideShrine", ShrineName = "Wayside Blossom", SafeRadius = 8f, Position = WaysideShrinePos,
            Rotation = new Vector3(0, -Mathf.Pi / 2f, 0), // faces the track (east): you arrive on its road side
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        AddChild(shrine);
        shrine.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.6f, Height = 4f }, Position = new Vector3(0, 2, 0) });
        var tree = Models.Instance(Models.CherryTree);
        tree.Scale = Vector3.One * 1.1f;
        shrine.AddChild(tree);
        shrine.AddChild(new OmniLight3D { Position = new Vector3(0, 4.5f, 1.5f), LightColor = Blush, LightEnergy = 1.6f, OmniRange = 9f });
        shrine.AddChild(new Label3D
        {
            Text = "Wayside Blossom", Position = new Vector3(0, 4.4f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 56, PixelSize = 0.01f, OutlineSize = 12, Modulate = Blush,
        });
        // A low stone kerb round it.
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12f;
            Visual(new BoxMesh { Size = new Vector3(0.9f, 0.3f, 0.5f) }, Mat(Stonework), WaysideShrinePos + new Vector3(Mathf.Cos(a) * 3.6f, 0.15f, Mathf.Sin(a) * 3.6f),
                new Vector3(0, -a, 0));
        }
    }

    // ================================================================== the Gate Stones

    /// <summary>Two tall stones either side of the road and a slab laid across them, old as the hills.</summary>
    private void BuildGateStones()
    {
        var a = MainRoad[7];
        var b = MainRoad[8];
        var along = new Vector3(b.X - a.X, 0, b.Z - a.Z).Normalized();
        var across = new Vector3(along.Z, 0, -along.X);
        float yaw = Mathf.Atan2(-across.Z, across.X); // the root's +X runs across the road
        var root = WaysideRoot("Poi_GateStones", GateStones, yaw);
        AddLandmark(root, "the Gate Stones", 14f);
        foreach (float side in new[] { -1f, 1f })
        {
            var pillar = Models.RockBody(Models.Columns[side < 0 ? 0 : 2], new Vector3(2.4f, 8.2f, 2.4f));
            pillar.Position = new Vector3(side * 5.4f, -0.4f, 0);
            pillar.Rotation = new Vector3(0, side * 0.4f, side * -0.03f);
            root.AddChild(pillar);
        }
        var lintel = Models.RockBody(Models.Slabs[0], new Vector3(13.5f, 1.6f, 2.6f));
        lintel.Position = new Vector3(0, 7.4f, 0);
        lintel.Rotation = new Vector3(0, 0, 0.03f);
        root.AddChild(lintel);
    }

    // ================================================================== the overturned wagon

    private void BuildWreckedWagon()
    {
        var root = WaysideRoot("Poi_Wagon", WreckedWagon, 0.6f);
        AddLandmark(root, "an overturned wagon", 10f);
        var wagon = Prop(Models.Village("Prop_Wagon"), Vector3.Zero, 0f, 1f, root);
        Models.FitHeight(wagon, 2.2f);
        wagon.Rotation = new Vector3(0.05f, 0, 1.15f); // over on its side
        wagon.Position = new Vector3(0.6f, 0.2f, 0);
        var body = new StaticBody3D { Name = "WagonBody", CollisionLayer = GameState.LayerWorld, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.8f, 1.6f, 3.6f) }, Position = new Vector3(0, 0.8f, 0) });
        root.AddChild(body);
        // A wheel off in the grass, and spilled crates.
        Visual(new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.14f, RadialSegments = 12 }, Mat(WoodDark),
            new Vector3(-2.6f, 0.08f, 2.4f), Vector3.Zero, root);
        var crates = new[] { new Vector3(2.4f, 0, -1.4f), new Vector3(3.1f, 0, 0.6f), new Vector3(2.0f, 0, 2.2f) };
        for (int i = 0; i < crates.Length; i++)
        {
            var c = Prop(Models.Village("Prop_Crate"), crates[i], i * 0.9f, 1f, root);
            Models.FitHeight(c, 0.8f);
            if (i == 2) c.Rotation = new Vector3(0, 0.4f, 1.57f);
        }
        Cache(root, "Cache_Wagon", "spilled crates", new Vector3(2.7f, 0, -0.4f), new Vector3(0.9f, 0.7f, 0.7f), Wood, 0.3f,
            ("coin", 25), ("bread", 3), ("iron_nails", 6), ("planks", 4));
    }

    // ================================================================== the Hermit's Hut

    private void BuildHermitHut()
    {
        var root = WaysideRoot("Poi_HermitHut", HermitHut);
        AddLandmark(root, "the Hermit's Hut", 14f);
        const float w = 5f, d = 4f, h = 2.6f, t = 0.22f;
        Block(new Vector3(0, 0.06f, 0), new Vector3(w, 0.12f, d), Vector3.Zero, WoodDark, root);
        Block(new Vector3(-w / 2f, h / 2f, 0), new Vector3(t, h, d), Vector3.Zero, Wood, root);
        Block(new Vector3(0, h / 2f, -d / 2f), new Vector3(w, h, t), Vector3.Zero, Wood, root);
        Block(new Vector3(0, h / 2f, d / 2f), new Vector3(w, h, t), Vector3.Zero, Wood, root);
        // The east wall, with the door toward the track.
        float side = (d - 1.2f) / 2f;
        Block(new Vector3(w / 2f, h / 2f, -0.6f - side / 2f), new Vector3(t, h, side), Vector3.Zero, Wood, root);
        Block(new Vector3(w / 2f, h / 2f, 0.6f + side / 2f), new Vector3(t, h, side), Vector3.Zero, Wood, root);
        Block(new Vector3(w / 2f, h - 0.25f, 0), new Vector3(t, 0.5f, 1.2f), Vector3.Zero, Wood, root);
        // Roof: two pitches meeting over the middle, and the gable ends filled in.
        foreach (float s in new[] { -1f, 1f })
            Block(new Vector3(0, 3.3f, s * 1.1f), new Vector3(w + 0.6f, 0.15f, 2.7f), new Vector3(s * 0.5f, 0, 0), WoodDark.Darkened(0.15f), root);
        foreach (float x in new[] { -w / 2f, w / 2f })
        {
            Visual(new BoxMesh { Size = new Vector3(t, 0.6f, 3.2f) }, Mat(Wood), new Vector3(x, h + 0.3f, 0), Vector3.Zero, root);
            Visual(new BoxMesh { Size = new Vector3(t, 0.5f, 1.4f) }, Mat(Wood), new Vector3(x, h + 0.8f, 0), Vector3.Zero, root);
        }
        // Inside: a cot, a table, and the chest.
        Visual(new BoxMesh { Size = new Vector3(1.9f, 0.4f, 0.9f) }, Mat(new Color(0.5f, 0.42f, 0.3f)), new Vector3(-1.3f, 0.32f, -1.35f), Vector3.Zero, root);
        Visual(new BoxMesh { Size = new Vector3(0.9f, 0.75f, 0.6f) }, Mat(Wood), new Vector3(1f, 0.5f, -1.4f), Vector3.Zero, root);
        Cache(root, "Cache_Hermit", "hermit's chest", new Vector3(-1.6f, 0.12f, 1.3f), new Vector3(0.9f, 0.55f, 0.55f), WoodDark, 0f,
            ("coin", 15), ("roast_turnip", 4), ("wolf_pelt", 2));
        InfoBoard(root, "HermitNote", "A note pinned by the door",
            "Gone up to the high country for the summer. If you're reading this, take what you need from the chest and leave the cot for the next one.\n\n" +
            "The old watchtower on the eastern hill: the lookout's strongbox is still up on his floor, and nobody's had the nerve to climb for it.\n\n" +
            "South-east, where the hills turn grey, there's the old quarry. Copper still shows in its walls, and a little tin, if you bring a pick.",
            new Vector3(w / 2f + 0.9f, 0, 1.4f), Mathf.Pi / 2f);
        // A fire ring and a woodpile outside.
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8f;
            Visual(new BoxMesh { Size = new Vector3(0.35f, 0.22f, 0.3f) }, Mat(Stonework.Darkened(0.2f)), new Vector3(5.2f + Mathf.Cos(a) * 0.8f, 0.11f, 2.6f + Mathf.Sin(a) * 0.8f), new Vector3(0, a, 0), root);
        }
        for (int i = 0; i < 6; i++)
            Visual(new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 1.2f, RadialSegments = 6 }, Mat(Wood),
                new Vector3(-w / 2f - 0.4f, 0.13f + 0.25f * (i / 3), -0.8f + 0.28f * (i % 3) + 0.14f * (i / 3)), new Vector3(Mathf.Pi / 2f, 0, 0), root);
    }

    // ================================================================== the Old Quarry

    /// <summary>A pit cut into the hills (the flatten disc does the cutting): rock faces round it,
    /// stone, copper and tin to break, an old hoist and the foreman's box.</summary>
    private void BuildQuarry()
    {
        var root = WaysideRoot("Poi_OldQuarry", OldQuarry);
        AddLandmark(root, "the Old Quarry", 20f);
        // Rock faces round the pit, open to the west (the way in).
        for (int i = 0; i < 13; i++)
        {
            float a = -Mathf.Pi * 0.65f + Mathf.Pi * 1.3f * i / 12f; // facing west: the gap
            var at = OldQuarry + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 15.5f;
            Crag(new Vector3(at.X, -0.6f, at.Z), _rng.RandfRange(3.5f, 5.5f), _rng.RandfRange(4f, 7.5f), blade: i % 3 == 0, collide: true);
        }
        // Stone to gather on the floor.
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var placed = new List<Vector3>();
        foreach (var off in new[] { new Vector3(-4, 0, -6), new Vector3(3, 0, 7), new Vector3(7, 0, -2) }) Spawn(rock, OldQuarry + off, placed);
        // Copper and tin in the walls.
        var copperRock = StyleMaterials.Rock(new Color(0.22f, 0.2f, 0.2f), new Color(0.3f, 0.5f, 0.42f), 0.6f, 0.4f);
        var copperBits = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.5f, 0.28f), Metallic = 0.6f, Roughness = 0.35f, EmissionEnabled = true, Emission = new Color(0.4f, 0.16f, 0.05f), EmissionEnergyMultiplier = 0.6f };
        var tinRock = StyleMaterials.Rock(new Color(0.24f, 0.24f, 0.26f), new Color(0.5f, 0.52f, 0.55f), 0.6f, 0.4f);
        var tinBits = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.84f, 0.86f), Metallic = 0.7f, Roughness = 0.3f, EmissionEnabled = true, Emission = new Color(0.25f, 0.27f, 0.3f), EmissionEnergyMultiplier = 0.6f };
        QuarryVein("copper_ore", new Vector3(10.5f, 0, 6f), 0, copperRock, copperBits, root);
        QuarryVein("copper_ore", new Vector3(11.5f, 0, -5f), 1, copperRock, copperBits, root);
        QuarryVein("tin_ore", new Vector3(4f, 0, 11.5f), 0, tinRock, tinBits, root);
        // The hoist: two legs and a beam with a rope down.
        foreach (float z in new[] { -1.6f, 1.6f })
            Visual(new BoxMesh { Size = new Vector3(0.25f, 5f, 0.25f) }, Mat(WoodDark), new Vector3(-2f, 2.5f, z), new Vector3(z > 0 ? -0.15f : 0.15f, 0, 0), root);
        Visual(new BoxMesh { Size = new Vector3(0.25f, 0.25f, 3.8f) }, Mat(WoodDark), new Vector3(-2f, 4.9f, 0), Vector3.Zero, root);
        Visual(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 3.2f, RadialSegments = 4 }, Mat(new Color(0.6f, 0.52f, 0.38f)), new Vector3(-2f, 3.2f, 0), Vector3.Zero, root);
        Visual(new BoxMesh { Size = new Vector3(0.8f, 0.5f, 0.8f) }, Mat(Wood), new Vector3(-2f, 1.45f, 0), new Vector3(0, 0.3f, 0), root);
        Cache(root, "Cache_Quarry", "foreman's box", new Vector3(1.5f, 0, 2.5f), new Vector3(0.9f, 0.6f, 0.6f), WoodDark, -0.4f,
            ("coin", 20), ("iron_nails", 8), ("rune_shard", 1));
    }

    private void QuarryVein(string id, Vector3 at, int i, Material body, Material nugget, Node3D root)
    {
        var n = GD.Load<PackedScene>("res://scenes/props/Rock.tscn").Instantiate<Harvestable>();
        n.Name = $"QuarryVein_{id}_{i}";
        n.DropItemId = id;
        n.Tool = "pick";
        n.RequiresTool = true;
        n.DamagePerDrop = 20f;
        n.BonusOnBreak = 2;
        n.RespawnSeconds = 300f;
        n.AddToGroup(id);
        root.AddChild(n);
        n.Position = at;
        n.Rotation = new Vector3(0, i * 2.1f + 0.5f, 0);
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
}
