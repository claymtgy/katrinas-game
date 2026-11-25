using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The country south of the plateau: the vale and the rolling hills round it (see
/// <see cref="Terrain"/>), with a track out of Ashby to the Old Waystone on the downs, groves,
/// boulders and long grass. Built last with its own random numbers (resource names carry on from
/// the rest of the world), then <see cref="RaiseTerrain"/> makes the hills and sets everything
/// that was laid out at y = 0 down on them.</summary>
public partial class HomeCliffs
{
    /// <summary>A ring of standing stones on the downs, at the end of the track south from Ashby.</summary>
    public static readonly Vector3 Waystone = InVale(78, 0, 478);

    /// <summary>The track south out of Ashby (y ignored).</summary>
    private static Vector3[] SouthTrack => new[]
    {
        AshbyCenter + new Vector3(15.5f, 0, 3), AshbyCenter + new Vector3(30, 0, 16), InVale(46, 0, 252), InVale(38, 0, 312),
        InVale(60, 0, 372), InVale(56, 0, 428), Waystone + new Vector3(-6, 0, -10),
    };

    private RandomNumberGenerator _crng;
    private Node3D _country;
    private int _countryCount;
    private FastNoiseLite _groves;

    private void BuildCountry()
    {
        _crng = new RandomNumberGenerator { Seed = (ulong)Seed * 131 + 7 };
        _country = new Node3D { Name = "Country" };
        AddChild(_country);

        // Roads first: the hills keep level ground under every one of them.
        Trail(0.022f, 3.2f, SouthTrack, ruts: false, tone: Dirt.Darkened(0.05f));
        WaysideTracks();
        ConfigureTerrain();
        BuildWaystone();
        ScatterCountry();
        BuildWayside(); // after the scatter, so its resource names don't shift
    }

    /// <summary>Where the hills are level, and where other ground lies.</summary>
    private void ConfigureTerrain()
    {
        Terrain.Reset(Seed);
        // Regions that still have flat ground of their own.
        FallsTerrain(); // Falls Valley's floor is hills too
        Terrain.Hole(Regions.Move(new Rect2(-150, 166, 90, 68), Regions.West)); // the forest floor
        Terrain.Hole(new Rect2(MineBox.Position.X, MineBox.Position.Y, 96, 86)); // the mine
        // The plateau's own slab meets the hills along z = 90.
        Terrain.FlattenRect(new Rect2(-60, -30, 120, 120), 22f);
        // Every road and track runs on level ground along the valley floors.
        foreach (var s in Segs) Terrain.FlattenLine(s.A, s.B, s.HalfWidth + 3f, s.HalfWidth + 24f);
        foreach (var (c, h) in Patches) Terrain.FlattenDisc(new Vector3(c.X, 0, c.Y), h.Length() + 1f, h.Length() + 14f);
        Terrain.FlattenDisc(AshbyCenter, 44f, 78f);
        foreach (var camp in Wildlife.CampSites) Terrain.FlattenDisc(camp, 22f, 46f);
        Terrain.FlattenDisc(ClayPond, 12f, 30f);
        Terrain.FlattenDisc(Waystone, 14f, 34f);
        WaysideFlats();
        FoothillsTerrain(); // the Cinder Foothills: an island of their own out in the clouds
        // The north edge rises into mountains, except where it meets the plateau and Falls Valley.
        Terrain.RimEdge(new Vector2(Terrain.Domain.Position.X, 90), new Vector2(-104, 90));
        Terrain.RimEdge(new Vector2(232, 90), new Vector2(Terrain.Domain.End.X, 90));
        Terrain.Lock();
        FindWatchtowerHill();
    }

    /// <summary>Build the hills, then set everything laid out at y = 0 down on them.</summary>
    private void RaiseTerrain()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var terrain = new Terrain { Name = "Terrain" };
        AddChild(terrain);
        terrain.Build(GroundMaterial);
        GD.Print($"[Terrain] {terrain.GetChildCount()} chunks built in {watch.ElapsedMilliseconds} ms");

        foreach (var child in GetChildren())
        {
            if (child is not Node3D n || n == terrain || n.Name == "Buildings") continue;
            if (n == _green || n == _country)
                foreach (var g in n.GetChildren()) { if (g is Node3D gn) Seat(gn); }
            else if (!_unseated.Contains(n)) Seat(n);
        }
    }

    private static void Seat(Node3D n)
    {
        float h = Terrain.HeightAt(n.Position);
        if (h > 0.001f) n.Position += Vector3.Up * h;
    }

    /// <summary>Free every child added since <paramref name="count"/> (pieces made only so the
    /// random numbers come out the same).</summary>
    private void DropChildrenFrom(int count)
    {
        while (GetChildCount() > count)
        {
            var n = GetChild(GetChildCount() - 1);
            RemoveChild(n);
            n.Free();
        }
    }

    // ================================================================== the waystone

    private void BuildWaystone()
    {
        PathPatch(Waystone + new Vector3(0, 0.03f, 0), new Vector2(15, 15), Dirt.Darkened(0.15f), Dirt.Darkened(0.02f));
        for (int i = 0; i < 9; i++)
        {
            float a = Mathf.Tau * i / 9f + _crng.RandfRange(-0.12f, 0.12f);
            var at = Waystone + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 9.5f;
            if (i == 6) continue; // a fallen gap on the side the track comes in
            Crag(at + Vector3.Down * 0.3f, _crng.RandfRange(1.4f, 2f), _crng.RandfRange(3.2f, 5.2f), blade: true, collide: true);
        }
        Crag(Waystone + Vector3.Down * 0.4f, 2.6f, 2.2f, collide: true); // the cairn in the middle
    }

    // ================================================================== groves and boulders

    /// <summary>How much dresses the country (spread over the whole of the hills).</summary>
    private const int MaxResTrees = 200, MaxResRocks = 60, MaxGroveTrees = 560, MaxBushes = 260, MaxTufts = 850, MaxBoulders = 48;

    private void ScatterCountry()
    {
        _groves = new FastNoiseLite { Seed = Seed * 31 + 5, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 95f };
        var tree = GD.Load<PackedScene>("res://scenes/props/Tree.tscn");
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var placed = new List<Vector3>();
        string[] small = { "Grass_Common_Tall", "Grass_Wispy_Tall", "Grass_Wispy_Short", "Grass_Common_Short", "Flower_3_Group", "Flower_4_Group", "Clover_2", "Fern_1", "Plant_7" };

        int resTrees = 0, resRocks = 0, trees = 0, bushes = 0, tufts = 0, boulders = 0;
        for (int guard = 0; guard < 80000; guard++)
        {
            if (resTrees >= MaxResTrees && resRocks >= MaxResRocks && trees >= MaxGroveTrees && bushes >= MaxBushes && tufts >= MaxTufts && boulders >= MaxBoulders) break;
            var p = new Vector3(_crng.RandfRange(Terrain.Domain.Position.X, Terrain.Domain.End.X), 0,
                _crng.RandfRange(Terrain.Domain.Position.Y, Terrain.Domain.End.Y));
            float roll = _crng.Randf();
            if (!CountryFree(p)) continue;
            float grove = _groves.GetNoise2D(p.X, p.Z);

            if (grove > 0.28f && (trees < MaxGroveTrees || resTrees < MaxResTrees))
            {
                if (resTrees < MaxResTrees && roll < 0.4f && Spaced(p, placed, 4.5f)) { Spawn(tree, p, placed); resTrees++; }
                else if (trees < MaxGroveTrees && Spaced(p, placed, 3.2f))
                {
                    var t = Prop(Models.Nature($"CommonTree_{_crng.RandiRange(1, 5)}"), p, _crng.RandfRange(0, Mathf.Tau), _crng.RandfRange(1.6f, 2.6f), _country);
                    t.Name = $"Country_{_countryCount++}";
                    CountryTrunk(p);
                    placed.Add(p);
                    trees++;
                }
            }
            else if (roll < 0.05f && resRocks < MaxResRocks && Spaced(p, placed, 6f)) { Spawn(rock, p, placed); resRocks++; }
            else if (roll < 0.08f && boulders < MaxBoulders && Spaced(p, placed, 10f))
            {
                Crag(p + Vector3.Down * 0.6f, _crng.RandfRange(3f, 6f), _crng.RandfRange(2f, 4.5f), collide: true);
                placed.Add(p);
                boulders++;
            }
            else if (roll < 0.3f && bushes < MaxBushes && (grove > 0.05f || roll < 0.12f))
            {
                var b = Prop(Models.Nature(Bushes[_crng.RandiRange(0, Bushes.Length - 1)]), p, _crng.RandfRange(0, Mathf.Tau), _crng.RandfRange(2f, 3.4f), _country);
                b.Name = $"Country_{_countryCount++}";
                bushes++;
            }
            else if (tufts < MaxTufts)
            {
                var g = Prop(Models.Nature(small[_crng.RandiRange(0, small.Length - 1)]), p, _crng.RandfRange(0, Mathf.Tau), _crng.RandfRange(1f, 1.7f), _country);
                g.Name = $"Country_{_countryCount++}";
                tufts++;
            }
        }
        GD.Print($"[Country] {resTrees} trees and {resRocks} rocks to gather, {trees} grove trees, {bushes} bushes, {tufts} tufts, {boulders} boulders");
    }

    /// <summary>Open country to dress: on the hills, off roads, out of the vale (dressed already),
    /// the village, the waystone ring and the steep mountainsides.</summary>
    private bool CountryFree(Vector3 p)
    {
        if (!Terrain.Covers(p.X, p.Z)) return false;
        if (Mathf.Abs(p.X - Regions.Vale.X) < 62 && p.Z > Regions.Vale.Z + 96 && p.Z < Regions.Vale.Z + 250) return false; // the vale (dressed already)
        if (p.DistanceTo(AshbyCenter) < 52f || p.DistanceTo(Waystone) < 16f || InWaysideClearing(p)) return false;
        foreach (var off in new[] { Vector3.Zero, new Vector3(3, 0, 0), new Vector3(-3, 0, 0), new Vector3(0, 0, 3), new Vector3(0, 0, -3) })
            if (OnPath(p + off)) return false;
        float h = Terrain.HeightAt(p);
        if (h > 42f) return false;                                                // up the mountains
        float gx = Terrain.HeightAt(p.X + 1f, p.Z) - Terrain.HeightAt(p.X - 1f, p.Z);
        float gz = Terrain.HeightAt(p.X, p.Z + 1f) - Terrain.HeightAt(p.X, p.Z - 1f);
        return new Vector2(gx, gz).Length() / 2f < 0.6f;                          // not too steep
    }

    private static bool Spaced(Vector3 p, List<Vector3> placed, float spacing)
    {
        foreach (var q in placed) if (new Vector2(p.X - q.X, p.Z - q.Z).LengthSquared() < spacing * spacing) return false;
        return true;
    }

    /// <summary>A trunk to bump into (the tree models have no collision).</summary>
    private void CountryTrunk(Vector3 at)
    {
        var body = new StaticBody3D { Name = $"Trunk_{_countryCount}", Position = at, CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.45f, Height = 3f }, Position = new Vector3(0, 1.5f, 0) });
        _country.AddChild(body);
    }
}
