using Godot;

namespace KatrinasGame;

/// <summary>Art pass, greenery (concept art 3 and 4): lush clumps of round bushes and leafy trees
/// banked against the feet of the cliffs in the vale and Falls Valley, a few out in the open,
/// and blossom trees round Ashby. Scenery only (trees have a trunk you bump into). Built last,
/// with its own random numbers, and kept off roads, water, camps, the village and the home claim.</summary>
public partial class HomeCliffs
{
    private RandomNumberGenerator _grng;
    private Node3D _green;
    private int _greenCount;

    private static readonly string[] Bushes = { "Bush_Common", "Bush_Common", "Bush_Common_Flowers" };

    private void BuildGreenery()
    {
        _grng = new RandomNumberGenerator { Seed = (ulong)Seed * 97 + 13 };
        _green = new Node3D { Name = "Greenery" };
        AddChild(_green);

        // The vale: banked along both walls (not across the gap to the Forest Road), and the far end.
        for (float z = 106; z <= 236; z += 9f)
        {
            float wz = Regions.Vale.Z + z;
            if (_grng.Randf() < 0.8f && !(wz > ForestGapMinZ - 6 && wz < ForestGapMaxZ + 6))
                Clump(InVale(_grng.RandfRange(-56, -49), 0, z + _grng.RandfRange(-3, 3)), _grng.RandiRange(5, 9), 7f);
            if (_grng.Randf() < 0.8f)
                Clump(InVale(_grng.RandfRange(49, 56), 0, z + _grng.RandfRange(-3, 3)), _grng.RandiRange(5, 9), 7f);
        }
        for (float x = -44; x <= 44; x += 11f)
            if (_grng.Randf() < 0.7f) Clump(InVale(x + _grng.RandfRange(-3, 3), 0, _grng.RandfRange(234, 240)), _grng.RandiRange(5, 8), 7f);
        // A few out in the open vale.
        for (int i = 0; i < 7; i++)
            Clump(InVale(_grng.RandfRange(-42, 42), 0, _grng.RandfRange(110, 230)), _grng.RandiRange(2, 4), 3.5f);

        // Falls Valley: under the east cliffs (either side of the falls) and along the south wall.
        for (float z = -12; z <= 100; z += 9f)
            if (_grng.Randf() < 0.75f) Clump(InFalls(_grng.RandfRange(184, 193), 0, z + _grng.RandfRange(-3, 3)), _grng.RandiRange(5, 8), 7f);
        for (float x = 92; x <= 180; x += 10f)
            if (_grng.Randf() < 0.7f) Clump(InFalls(x + _grng.RandfRange(-3, 3), 0, _grng.RandfRange(101, 108)), _grng.RandiRange(5, 8), 7f);

        // The home plateau's flanks, well clear of where you build.
        for (float z = 4; z <= 84; z += 10f)
        {
            if (_grng.Randf() < 0.6f) Clump(new Vector3(_grng.RandfRange(-54, -47), 0, z), _grng.RandiRange(2, 5), 4f);
            if (_grng.Randf() < 0.6f) Clump(new Vector3(_grng.RandfRange(47, 54), 0, z), _grng.RandiRange(2, 5), 4f);
        }

        // Blossom trees round Ashby, just outside the houses (concept 3).
        foreach (float a in new[] { 0.5f, 1.2f, 2.3f, 3.9f, 4.6f, 5.5f })
        {
            var p = AshbyCenter + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * _grng.RandfRange(31, 36);
            if (!GreenFree(p, ashbyRing: true)) continue;
            var tree = Models.Instance(Models.CherryTree);
            tree.Name = $"Green_{_greenCount++}";
            tree.Position = p;
            tree.Rotation = new Vector3(0, _grng.RandfRange(0, Mathf.Tau), 0);
            tree.Scale = Vector3.One * _grng.RandfRange(0.5f, 0.62f);
            _green.AddChild(tree);
            Trunk(p, 0.35f);
        }
    }

    /// <summary>A clump of bushes with a leafy tree or two in it.</summary>
    private void Clump(Vector3 center, int count, float radius)
    {
        int trees = 0;
        for (int i = 0; i < count; i++)
        {
            var p = center + new Vector3(_grng.RandfRange(-radius, radius), 0, _grng.RandfRange(-radius, radius));
            bool tree = trees < 3 && _grng.Randf() < 0.4f;
            if (!GreenFree(p)) continue;
            string model = tree ? $"CommonTree_{_grng.RandiRange(1, 5)}" : Bushes[_grng.RandiRange(0, Bushes.Length - 1)];
            var node = Prop(Models.Nature(model), p, _grng.RandfRange(0, Mathf.Tau), tree ? _grng.RandfRange(1.5f, 2.3f) : _grng.RandfRange(2f, 3.4f), _green);
            node.Name = $"Green_{_greenCount++}";
            if (tree) { Trunk(p, 0.45f); trees++; }
        }
    }

    /// <summary>A trunk to bump into (the models have no collision).</summary>
    private void Trunk(Vector3 at, float radius)
    {
        var body = new StaticBody3D { Name = $"Trunk_{_greenCount}", Position = at, CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = radius, Height = 3f }, Position = new Vector3(0, 1.5f, 0) });
        _green.AddChild(body);
    }

    private bool GreenFree(Vector3 p, bool ashbyRing = false)
    {
        var gs = GameState.Instance;
        foreach (var off in new[] { Vector3.Zero, new Vector3(1.5f, 0, 0), new Vector3(-1.5f, 0, 0), new Vector3(0, 0, 1.5f), new Vector3(0, 0, -1.5f) })
            if (OnPath(p + off)) return false;
        if (!float.IsNaN(gs.WaterSurfaceAt(p + Vector3.Up * 0.2f))) return false;
        if (gs.CurrentAt(p + Vector3.Up * 0.1f, RiverHalfWidth + 2.5f) != Vector3.Zero) return false;
        if (p.DistanceTo(ShackPos) < 48f) return false;                       // the home claim (it grows)
        if (p.DistanceTo(AshbyCenter) < (ashbyRing ? 29f : 38f)) return false; // the village
        foreach (var camp in Wildlife.CampSites) if (p.DistanceTo(camp) < 16f) return false;
        if (Mathf.Abs(p.X - Regions.Vale.X - 1) < 6f && p.Z > Regions.Vale.Z + 90) return false; // the vale road
        if (Mathf.Abs(p.X - MesaCenter.X) < MesaHalf + 5 && Mathf.Abs(p.Z - MesaCenter.Z) < MesaHalf + 5) return false;
        if (p.DistanceTo(GreatFallsPool) < 15f) return false;
        foreach (var r in Wildlife.RoostSites) if (p.DistanceTo(r) < 9f) return false;
        if (Mathf.Abs(p.Z - PassPos.Z) < 7f && p.X > 55 && p.X < PassPos.X + 12) return false;     // the pass (the plateau's)
        if (Mathf.Abs(p.Z - FallsPathZ) < 7f && p.X > Regions.Falls.X + 55 && p.X < Regions.Falls.X + 115) return false; // the road on into the valley
        return true;
    }
}
