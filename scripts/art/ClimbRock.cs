using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The look of a climbable face (the pale rock you walk into to climb). Gameplay still
/// uses a plain box collider; this only replaces the box you see with a faceted, bulging sheet of
/// rock: a jittered grid pushed in and out by noise, with ledge-like strata, its edges folded back
/// into the cliff behind it and ragged, so it reads as a pale band in the rock rather than a slab.
/// Flat-shaded, colour in the vertices (paler on the bulges, darker in the hollows).</summary>
public static class ClimbRock
{
    /// <summary>A rock face filling an axis-aligned box <paramref name="size"/> (centred on the
    /// origin) whose climbable side faces <paramref name="outward"/> (±X or ±Z).
    /// <paramref name="rust"/>: optional streaks in face coordinates (u along the face, v up,
    /// both from the centre) to tint with <paramref name="rustColor"/>.</summary>
    public static MeshInstance3D Make(Vector3 size, Vector3 outward, Color pale, Color dark, int seed,
        IList<Rect2> rust = null, Color rustColor = default)
    {
        bool faceZ = Mathf.Abs(outward.Z) > Mathf.Abs(outward.X);
        float w = faceZ ? size.X : size.Z, d = faceZ ? size.Z : size.X, h = size.Y;
        var rng = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
        var noise = new FastNoiseLite { Seed = seed, Frequency = 0.35f, FractalOctaves = 2 };

        int nu = Mathf.Max(3, Mathf.CeilToInt(w / 0.8f)), nv = Mathf.Max(3, Mathf.CeilToInt(h / 0.8f));
        float cu = w / nu, cv = h / nv;
        var pts = new Vector3[nu + 1, nv + 1];
        var cols = new Color[nu + 1, nv + 1];
        // Strata: a few horizontal ledges at random heights.
        var ledges = new List<float>();
        for (float y = -h / 2f + rng.RandfRange(0.6f, 1.6f); y < h / 2f; y += rng.RandfRange(1.2f, 2.4f)) ledges.Add(y);

        for (int i = 0; i <= nu; i++)
            for (int j = 0; j <= nv; j++)
            {
                float u = -w / 2f + cu * i, v = -h / 2f + cv * j;
                bool edgeU = i == 0 || i == nu, edgeV = j == 0 || j == nv;
                if (!edgeU) u += rng.RandfRange(-0.3f, 0.3f) * cu;
                if (!edgeV) v += rng.RandfRange(-0.3f, 0.3f) * cv;
                float n = noise.GetNoise2D(u, v);                       // -1..1
                float z = d / 2f + n * 0.13f - 0.03f;
                foreach (float l in ledges)                             // a lip just above each ledge line
                {
                    float k = v - l;
                    if (k > 0f && k < 0.45f) z += 0.09f * (1f - k / 0.45f);
                }
                if (edgeU || edgeV)
                {
                    // Fold the rim back into the cliff and rag the outline.
                    z = -d / 2f - 0.15f;
                    if (edgeU) u += Mathf.Sign(u) * rng.RandfRange(0f, 0.35f);
                    if (edgeV) v += Mathf.Sign(v) * rng.RandfRange(0f, 0.25f);
                    if (edgeU && !edgeV) v += rng.RandfRange(-0.3f, 0.3f) * cv;
                    if (edgeV && !edgeU) u += rng.RandfRange(-0.3f, 0.3f) * cu;
                }
                else if (i == 1 || i == nu - 1 || j == 1 || j == nv - 1)
                    z -= 0.08f;                                         // round the shoulders a little
                pts[i, j] = new Vector3(u, v, z);
                float shade = Mathf.Clamp(0.62f + n * 0.3f + rng.RandfRange(-0.08f, 0.08f), 0f, 1f);
                var c = dark.Lerp(pale, shade);
                if (rust != null)
                    foreach (var r in rust)
                        if (r.Grow(0.15f).HasPoint(new Vector2(u, v))) { c = c.Lerp(rustColor, r.HasPoint(new Vector2(u, v)) ? 0.75f : 0.35f); break; }
                cols[i, j] = c;
            }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(int ai, int aj, int bi, int bj, int ci, int cj)
        {
            Vector3 a = pts[ai, aj], b = pts[bi, bj], c = pts[ci, cj];
            // Godot's front faces wind clockwise: (c - a) × (b - a) is the front normal. Keep it facing out.
            var nrm = (c - a).Cross(b - a).Normalized();
            if (nrm.Z < 0f) { (b, c) = (c, b); nrm = -nrm; }
            var col = (cols[ai, aj] + cols[bi, bj] + cols[ci, cj]) / 3f;
            foreach (var p in new[] { a, b, c })
            {
                st.SetNormal(nrm);
                st.SetColor(col);
                st.AddVertex(p);
            }
        }
        for (int i = 0; i < nu; i++)
            for (int j = 0; j < nv; j++)
            {
                if (((i + j) & 1) == 0)
                {
                    Tri(i, j, i + 1, j + 1, i + 1, j);
                    Tri(i, j, i, j + 1, i + 1, j + 1);
                }
                else
                {
                    Tri(i, j, i, j + 1, i + 1, j);
                    Tri(i + 1, j, i, j + 1, i + 1, j + 1);
                }
            }

        var mat = StyleMaterials.Toon(Colors.White, 0.3f);
        mat.VertexColorUseAsAlbedo = true;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return new MeshInstance3D
        {
            Name = "ClimbRock",
            Mesh = st.Commit(),
            MaterialOverride = mat,
            // Canonical frame faces +Z with u along +X; turn it to face outward.
            Rotation = new Vector3(0, Mathf.Atan2(outward.X, outward.Z), 0),
        };
    }

    private static ArrayMesh _hold;

    /// <summary>A handhold: a small faceted knob of darker rock (shared mesh, about 0.45 × 0.16 × 0.24 m,
    /// long along X; rotate it to lie along the face).</summary>
    public static ArrayMesh Hold()
    {
        if (_hold != null) return _hold;
        var rng = new RandomNumberGenerator { Seed = 4242 };
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // A squashed, jittered octahedron-ish lump: a ring of 6 points and a top and bottom.
        var ring = new Vector3[6];
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.Tau / 6f;
            ring[k] = new Vector3(Mathf.Cos(a) * 0.23f, rng.RandfRange(-0.02f, 0.02f), Mathf.Sin(a) * 0.12f) * rng.RandfRange(0.85f, 1.1f);
        }
        var top = new Vector3(rng.RandfRange(-0.05f, 0.05f), 0.08f, 0.03f);
        var bottom = new Vector3(0, -0.07f, -0.02f);
        void T(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = (c - a).Cross(b - a).Normalized();
            if (n.Dot(a + b + c) < 0f) { (b, c) = (c, b); n = -n; }   // face away from the middle
            st.SetNormal(n); st.AddVertex(a);
            st.SetNormal(n); st.AddVertex(b);
            st.SetNormal(n); st.AddVertex(c);
        }
        for (int k = 0; k < 6; k++)
        {
            var a = ring[k]; var b = ring[(k + 1) % 6];
            T(top, b, a);
            T(bottom, a, b);
        }
        return _hold = st.Commit();
    }
}
