using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Columnar basalt (the concept art's cliffs): a cluster of tall hexagonal columns of
/// different heights, packed together, each with a slightly tilted, bevelled top that catches
/// the light. Built in code as a few shared mesh variants, flat-shaded, in a unit box
/// (x, z −0.5..0.5, y −0.5..0.5) like the rock models, so it drops in wherever a big rock went.
/// Per-column shade goes in the vertex colour; <see cref="StyleMaterials.CliffShader"/> does the rest.</summary>
public static class Basalt
{
    public const int Variants = 6;

    private static readonly ArrayMesh[] _meshes = new ArrayMesh[Variants];
    private static readonly Vector3[][] _points = new Vector3[Variants][];

    /// <summary>Tall rocks become basalt clusters; boulders, slabs and buttresses stay rock models.</summary>
    public static bool Suits(Vector3 size) => size.Y >= 12f && size.Y >= 0.8f * Mathf.Max(size.X, size.Z);

    /// <summary>Which variant a rock at this spot gets (stable: the same every run and on every screen).</summary>
    public static int VariantAt(Vector3 pos) => (int)((uint)GD.Hash(new Vector3I(Mathf.RoundToInt(pos.X), Mathf.RoundToInt(pos.Y), Mathf.RoundToInt(pos.Z))) % Variants);

    /// <summary>A basalt cluster filling the box <paramref name="size"/>, centred on its origin.
    /// collide: a static body with a convex collider (layer as given), else just the mesh.</summary>
    public static Node3D Make(int variant, Vector3 size, bool collide, uint layer = GameState.LayerWorld)
    {
        var mesh = Mesh(variant);
        var mi = new MeshInstance3D { Name = "Basalt", Mesh = mesh, MaterialOverride = StyleMaterials.Cliff, Scale = size };
        if (!collide) return mi;
        var body = new StaticBody3D { CollisionLayer = layer, CollisionMask = 0 };
        body.AddChild(mi);
        body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = _points[variant].Select(p => p * size).ToArray() } });
        return body;
    }

    public static ArrayMesh Mesh(int variant)
    {
        variant = Mathf.PosMod(variant, Variants);
        if (_meshes[variant] != null) return _meshes[variant];
        var rng = new RandomNumberGenerator { Seed = (ulong)(9173 + variant * 7919) };
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        var hull = new List<Vector3>();

        // Columns on a jittered hex grid inside an ellipse; tallest toward the middle and back.
        float r = rng.RandfRange(0.1f, 0.13f);
        float dx = r * 1.75f, dz = r * 1.5f;
        var peak = new Vector2(rng.RandfRange(-0.15f, 0.15f), rng.RandfRange(-0.2f, 0.05f));
        for (int row = -5; row <= 5; row++)
            for (int col = -5; col <= 5; col++)
            {
                var c = new Vector2(col * dx + (row % 2 != 0 ? dx / 2f : 0f), row * dz)
                        + new Vector2(rng.RandfRange(-0.2f, 0.2f), rng.RandfRange(-0.2f, 0.2f)) * r;
                float e = new Vector2(c.X / 0.5f, c.Y / 0.5f).Length();
                if (e > 0.92f) continue;
                float radius = r * rng.RandfRange(0.8f, 1.02f);
                // Height falls off from the peak, with a few columns standing proud or broken short.
                float fall = c.DistanceTo(peak) / 0.7f;
                float top = 0.5f - fall * fall * 0.75f - rng.RandfRange(0f, 0.12f);
                if (rng.Randf() < 0.12f) top -= rng.RandfRange(0.1f, 0.25f);
                top = Mathf.Clamp(top, -0.35f, 0.5f);
                float shade = rng.RandfRange(0.84f, 1.1f);
                Column(st, hull, c, radius, -0.5f, top, rng.RandfRange(0, Mathf.Tau), shade, rng);
            }

        var mesh = st.Commit();
        _meshes[variant] = mesh;
        _points[variant] = hull.ToArray();
        return mesh;
    }

    /// <summary>A flat-topped block of columns in real metres (a spire, a mesa): a square footprint
    /// <paramref name="size"/>.X × .Z centred on the origin, from −Y/2 to +Y/2. Interior columns
    /// stand flush with the top (you walk on their caps, over the box collider that goes with it);
    /// edge columns drop a little lower, so the rim reads broken. No collider: pair it with one.</summary>
    public static MeshInstance3D Plateau(Vector3 size, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(31337 + seed * 104729) };
        var st = new SurfaceTool();
        st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        var hull = new List<Vector3>();
        float r = Mathf.Clamp(Mathf.Min(size.X, size.Z) / 9f, 0.8f, 1.4f);
        float dx = r * 1.72f, dz = r * 1.49f;
        float hx = size.X / 2f, hz = size.Z / 2f, top = size.Y / 2f, bottom = -size.Y / 2f;
        int nx = Mathf.CeilToInt(hx / dx) + 1, nz = Mathf.CeilToInt(hz / dz) + 1;
        for (int row = -nz; row <= nz; row++)
            for (int col = -nx; col <= nx; col++)
            {
                var c = new Vector2(col * dx + (Mathf.PosMod(row, 2) == 1 ? dx / 2f : 0f), row * dz)
                        + new Vector2(rng.RandfRange(-0.15f, 0.15f), rng.RandfRange(-0.15f, 0.15f)) * r;
                // Keep every column inside the footprint (so the collider covers what you see).
                if (Mathf.Abs(c.X) > hx - r * 0.95f || Mathf.Abs(c.Y) > hz - r * 0.95f) continue;
                bool edge = Mathf.Abs(c.X) > hx - r * 2.2f || Mathf.Abs(c.Y) > hz - r * 2.2f;
                float t = top - 0.12f - rng.RandfRange(0f, 0.08f) - (edge ? rng.RandfRange(0f, 0.35f) : 0f);
                Column(st, hull, c, r * rng.RandfRange(0.92f, 1.04f), bottom, t, rng.RandfRange(0, Mathf.Tau), rng.RandfRange(0.86f, 1.08f), rng, capTilt: 0.12f);
            }
        // Fill the faces between the edge columns and the core with a slightly recessed wall, so no
        // gaps show through to the sky.
        var shade = new Color(0.72f, 0.72f, 0.72f);
        float ix = hx - r * 0.6f, iz = hz - r * 0.6f, wt = top - 0.5f;
        var p = new[] { new Vector3(-ix, bottom, -iz), new Vector3(ix, bottom, -iz), new Vector3(ix, bottom, iz), new Vector3(-ix, bottom, iz) };
        var q = p.Select(v => new Vector3(v.X, wt, v.Z)).ToArray();
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Quad(st, p[i], p[j], q[j], q[i], shade);
        }
        Quad(st, q[0], q[1], q[2], q[3], shade);
        return new MeshInstance3D { Name = "Basalt", Mesh = st.Commit(), MaterialOverride = StyleMaterials.Cliff };
    }

    /// <summary>One hexagonal column from <paramref name="bottom"/> to <paramref name="top"/>, with a
    /// tilted cap and a bevel ring round it.</summary>
    private static void Column(SurfaceTool st, List<Vector3> hull, Vector2 c, float radius, float bottom, float top, float spin, float shade,
        RandomNumberGenerator rng, float capTilt = 0.35f)
    {
        const int Sides = 6;
        var tilt = new Vector2(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f)) * radius * capTilt; // cap slope
        float bevel = radius * 0.22f;
        var color = new Color(shade, shade, shade);
        var ring = new Vector3[Sides];      // top of the sides
        var inner = new Vector3[Sides];     // the cap, inside the bevel
        var foot = new Vector3[Sides];
        for (int i = 0; i < Sides; i++)
        {
            float a = spin + Mathf.Tau * i / Sides;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * (1f + 0.08f * Mathf.Sin(a * 3f + spin));
            float slope = (d.X * tilt.X + d.Y * tilt.Y) / radius;
            foot[i] = new Vector3(c.X + d.X, bottom, c.Y + d.Y);
            ring[i] = new Vector3(c.X + d.X, top + slope - bevel * 0.6f, c.Y + d.Y);
            var di = d * 0.72f;
            inner[i] = new Vector3(c.X + di.X, top + slope * 0.72f, c.Y + di.Y);
        }
        var capMid = new Vector3(c.X, top, c.Y);
        hull.Add(capMid);
        for (int i = 0; i < Sides; i++)
        {
            int j = (i + 1) % Sides;
            Quad(st, foot[i], foot[j], ring[j], ring[i], color);
            Quad(st, ring[i], ring[j], inner[j], inner[i], color * 1.04f);  // the bevel
            Tri(st, inner[i], inner[j], capMid, color * 1.06f);
            hull.Add(inner[i]);
            hull.Add(foot[i]);
        }
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Tri(st, a, b, c, color);
        Tri(st, a, c, d, color);
    }

    /// <summary>One flat-shaded triangle, wound so its face points outward (Godot: clockwise).</summary>
    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        var n = (c - a).Cross(b - a).Normalized();
        if (n.LengthSquared() < 0.5f) return;
        foreach (var v in new[] { a, b, c })
        {
            st.SetNormal(n);
            st.SetColor(new Color(Mathf.Min(color.R, 1.2f), Mathf.Min(color.G, 1.2f), Mathf.Min(color.B, 1.2f)));
            st.AddVertex(v);
        }
    }
}
