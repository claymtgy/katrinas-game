using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>One continuous landscape of rolling hills, south of the home plateau: the vale, the
/// country round it and the mountains that ring it. It replaces the flat slab of ground the vale
/// used to be and the rock walls that boxed it in.
///
/// The ground's height is one deterministic function of x and z (<see cref="HeightAt(float, float)"/>),
/// so every machine builds the same hills and anything can ask how high the ground is somewhere.
/// The world is still laid out at y = 0, so the hills flatten out to 0 under everything that
/// needs flat ground (<see cref="FlattenDisc"/>, <see cref="FlattenLine"/>, <see cref="FlattenRect"/>):
/// roads run along the valley floors, and villages, camps and ponds sit on level ground. Other
/// regions that still have ground of their own (the forest, the mine) are <see cref="Hole"/>s:
/// the hills ease down to 0 at their edges and skip them.
///
/// Besides the country (<see cref="Domain"/>) there can be other stretches of land (<see cref="Land"/>):
/// Falls Valley's floor is one, its hills gentler (<see cref="Calm"/>) and rising into scree at the
/// foot of its cliffs (<see cref="Wall"/>). It's all one height function, so where two stretches
/// meet the ground runs on without a seam.
///
/// Built in chunks (<see cref="ChunkCells"/> cells of <see cref="Cell"/> metres), each a mesh in the
/// painterly ground material and a trimesh collider on the world layer (enemies' navmesh too).</summary>
public partial class Terrain : Node3D
{
    public const float Cell = 2f;
    public const int ChunkCells = 32;
    private const float ChunkSize = Cell * ChunkCells;

    /// <summary>The land the hills cover (x, z). The north edge meets the home plateau's slab.</summary>
    public static readonly Rect2 Domain = new(-480, 90, ChunkSize * 15, ChunkSize * 16);

    /// <summary>How high the mountains round the edge get, and how far in they reach.</summary>
    private const float RimHeight = 110f, RimReach = 80f;
    /// <summary>Hill height near roads and villages, and far out in the country.</summary>
    private const float NearAmp = 6f, FarAmp = 42f;
    /// <summary>Over this distance from the nearest road or village, hills grow from near to far.</summary>
    private const float AmpNear = 18f, AmpFar = 170f;

    private static readonly List<Rect2> Holes = new();
    /// <summary>Stretches of land besides the country (Falls Valley's floor).</summary>
    private static readonly List<Rect2> Lands = new();
    /// <summary>Where the hills are lower (scale), easing back to full height 30 m outside.</summary>
    private static readonly List<(Rect2 R, float Scale)> Calms = new();
    /// <summary>Ground that rises against a cliff along a line: height at the line, and how far out.</summary>
    private static readonly List<(Vector2 A, Vector2 B, float Height, float Reach)> Walls = new();
    private static readonly List<(Vector2 C, float Inner, float Outer)> Discs = new();
    private static readonly List<(Vector2 A, Vector2 B, float Inner, float Outer)> Lines = new();
    private static readonly List<(Rect2 R, float Outer)> Rects = new();
    /// <summary>Stretches of the north edge that rise into mountains (the rest meets other ground).</summary>
    private static readonly List<(Vector2 A, Vector2 B)> RimEdges = new();

    /// <summary>Land of its own shape and height out in the cloud sea (the Cinder Foothills):
    /// inside the rectangle the height comes from the island's own function, its ground is
    /// wherever <c>Inside</c> says, and it has its own material.</summary>
    private static readonly List<(Rect2 R, System.Func<float, float, float> Height, System.Func<float, float, bool> Inside, Material Material)> Islands = new();

    private static FastNoiseLite _hills, _detail;
    private static bool _ready;

    /// <summary>Forget every flat zone and hole (the world is being built again).</summary>
    public static void Reset(int seed)
    {
        Holes.Clear(); Discs.Clear(); Lines.Clear(); Rects.Clear(); RimEdges.Clear(); Lands.Clear(); Calms.Clear(); Walls.Clear(); Islands.Clear();
        _hills = new FastNoiseLite
        {
            Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 210f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4, FractalGain = 0.45f,
        };
        _detail = new FastNoiseLite { Seed = seed * 7 + 3, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f / 47f };
        _ready = false;
    }

    /// <summary>Other ground lies here (x, z): no hills, and they ease down to 0 at its edge.</summary>
    public static void Hole(Rect2 r) => Holes.Add(r);
    /// <summary>Level ground (y = 0) within <paramref name="inner"/> of a point, hills again by <paramref name="outer"/>.</summary>
    public static void FlattenDisc(Vector3 c, float inner, float outer) => Discs.Add((new Vector2(c.X, c.Z), inner, outer));
    /// <summary>Level ground along a segment (a road): flat within <paramref name="inner"/>.</summary>
    public static void FlattenLine(Vector3 a, Vector3 b, float inner, float outer) => Lines.Add((new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), inner, outer));
    public static void FlattenLine(Vector2 a, Vector2 b, float inner, float outer) => Lines.Add((a, b, inner, outer));
    /// <summary>Level ground over a rectangle (x, z) and <paramref name="outer"/> beyond it.</summary>
    public static void FlattenRect(Rect2 r, float outer) => Rects.Add((r, outer));
    /// <summary>This stretch of the domain's north edge rises into mountains.</summary>
    public static void RimEdge(Vector2 a, Vector2 b) => RimEdges.Add((a, b));
    /// <summary>More land for the hills to cover (x, z), besides <see cref="Domain"/>.</summary>
    public static void Land(Rect2 r) => Lands.Add(r);
    /// <summary>Lower hills over a rectangle: <paramref name="scale"/> of their height.</summary>
    public static void Calm(Rect2 r, float scale) => Calms.Add((r, scale));
    /// <summary>The ground banks up against a cliff along a - b: <paramref name="height"/> at the
    /// line, down to nothing <paramref name="reach"/> away (roads and flat zones still win).</summary>
    public static void Wall(Vector2 a, Vector2 b, float height, float reach) => Walls.Add((a, b, height, reach));

    /// <summary>An island of its own (see <see cref="Islands"/>), before Lock.</summary>
    public static void Island(Rect2 r, System.Func<float, float, float> height, System.Func<float, float, bool> inside, Material material) =>
        Islands.Add((r, height, inside, material));

    private static bool OnLand(Vector2 p)
    {
        if (Domain.HasPoint(p)) return true;
        foreach (var l in Lands) if (l.HasPoint(p)) return true;
        return false;
    }

    /// <summary>Every flat zone and hole is in: heights can be asked for from now on.</summary>
    public static void Lock() => _ready = true;

    /// <summary>How high the ground is here (0 outside the hills, or before they're laid out).</summary>
    public static float HeightAt(Vector3 p) => HeightAt(p.X, p.Z);

    public static float HeightAt(float x, float z) => _ready ? RawHeight(x, z) : 0f;

    /// <summary>True when (x, z) is on the hills' own ground (not outside them, or in a hole).</summary>
    public static bool Covers(float x, float z)
    {
        foreach (var isl in Islands)
            if (isl.R.HasPoint(new Vector2(x, z))) return isl.Inside(x, z);
        if (!OnLand(new Vector2(x, z))) return false;
        foreach (var h in Holes) if (h.HasPoint(new Vector2(x, z))) return false;
        return true;
    }

    private static float RawHeight(float x, float z)
    {
        var p = new Vector2(x, z);
        foreach (var isl in Islands)
            if (isl.R.HasPoint(p)) return isl.Height(x, z);
        if (!OnLand(p)) return 0f;
        foreach (var h in Holes) if (h.HasPoint(p)) return 0f;

        // How flat (0) or free (1) the ground is here, and how far the nearest road or village is.
        float free = 1f, near = float.MaxValue;
        foreach (var (c, inner, outer) in Discs)
        {
            float d = p.DistanceTo(c);
            free = Mathf.Min(free, Smooth(inner, outer, d));
            near = Mathf.Min(near, Mathf.Max(0f, d - inner));
        }
        foreach (var (a, b, inner, outer) in Lines)
        {
            float d = SegDistance(p, a, b);
            free = Mathf.Min(free, Smooth(inner, outer, d));
            near = Mathf.Min(near, Mathf.Max(0f, d - inner));
        }
        foreach (var (r, outer) in Rects)
        {
            float d = RectDistance(p, r);
            free = Mathf.Min(free, Smooth(0f, outer, d));
            near = Mathf.Min(near, d);
        }
        foreach (var h in Holes) free = Mathf.Min(free, Smooth(1f, 26f, RectDistance(p, h)));
        if (free <= 0f) return 0f;

        // Rolling hills, low by the roads and taller out in the country.
        float amp = Mathf.Lerp(NearAmp, FarAmp, Smooth(AmpNear, AmpFar, near));
        foreach (var (r, scale) in Calms) amp *= Mathf.Lerp(scale, 1f, Smooth(0f, 30f, RectDistance(p, r)));
        float n = _hills.GetNoise2D(x, z) * 0.5f + 0.5f;           // 0..1
        float hills = amp * Mathf.Pow(Mathf.Clamp(n * 1.3f - 0.12f, 0f, 1f), 1.35f);
        hills += _detail.GetNoise2D(x, z) * 1.2f * Smooth(0.2f, 1f, free);

        // The mountains that ring the land, so its edge never shows.
        float edge = Mathf.Min(Mathf.Min(x - Domain.Position.X, Domain.End.X - x), Domain.End.Y - z);
        foreach (var (a, b) in RimEdges) edge = Mathf.Min(edge, SegDistance(p, a, b));
        float rim = 1f - Smooth(0f, RimReach, edge);
        // Ridged noise breaks the ring into peaks and saddles instead of one smooth bank.
        float ridge = 1f - Mathf.Abs(_detail.GetNoise2D(x * 0.35f, z * 0.35f));
        float mountains = RimHeight * rim * rim * (0.45f + 0.75f * ridge * ridge);
        // Scree banked against cliffs.
        foreach (var (a, b, height, reach) in Walls)
        {
            float bank = 1f - Smooth(0f, reach, SegDistance(p, a, b));
            mountains = Mathf.Max(mountains, height * bank * bank * (0.55f + 0.6f * ridge));
        }

        return Mathf.Max(0f, hills + mountains) * free;
    }

    private static float Smooth(float a, float b, float x) => x <= a ? 0f : x >= b ? 1f : Mathf.SmoothStep(a, b, x);

    private static float SegDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.LengthSquared() < 1e-4f ? 0f : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
        return p.DistanceTo(a + ab * t);
    }

    private static float RectDistance(Vector2 p, Rect2 r)
    {
        float dx = Mathf.Max(Mathf.Max(r.Position.X - p.X, 0f), p.X - r.End.X);
        float dz = Mathf.Max(Mathf.Max(r.Position.Y - p.Y, 0f), p.Y - r.End.Y);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ================================================================== building

    /// <summary>Make every chunk (after all the flat zones and holes are in).</summary>
    public void Build(Material material)
    {
        if (!_ready) Lock();
        // Chunks on the country's grid, out as far as every stretch of land goes.
        var all = Domain;
        foreach (var l in Lands) all = all.Merge(l);
        foreach (var isl in Islands) all = all.Merge(isl.R);
        int x0 = Mathf.FloorToInt((all.Position.X - Domain.Position.X) / ChunkSize), x1 = Mathf.CeilToInt((all.End.X - Domain.Position.X) / ChunkSize);
        int z0 = Mathf.FloorToInt((all.Position.Y - Domain.Position.Y) / ChunkSize), z1 = Mathf.CeilToInt((all.End.Y - Domain.Position.Y) / ChunkSize);
        for (int cz = z0; cz < z1; cz++)
            for (int cx = x0; cx < x1; cx++)
            {
                var area = new Rect2(Domain.Position.X + cx * ChunkSize, Domain.Position.Y + cz * ChunkSize, ChunkSize, ChunkSize);
                if (!area.Intersects(Domain) && !Lands.Exists(l => l.Intersects(area)) && !Islands.Exists(i => i.R.Intersects(area))) continue;
                // An island's chunks wear its own ground.
                var mat = material;
                foreach (var isl in Islands) if (isl.R.Intersects(area)) mat = isl.Material;
                BuildChunk(cx, cz, mat);
            }
    }

    private void BuildChunk(int cx, int cz, Material material)
    {
        float ox = Domain.Position.X + cx * ChunkSize, oz = Domain.Position.Y + cz * ChunkSize;
        int n = ChunkCells + 1, m = n + 2;
        // Heights with a one-cell border, so normals at the chunk's edge see past it (no seams).
        var ring = new float[m * m];
        for (int j = 0; j < m; j++)
            for (int i = 0; i < m; i++)
                ring[j * m + i] = RawHeight(ox + (i - 1) * Cell, oz + (j - 1) * Cell);
        float H(int i, int j) => ring[(j + 1) * m + i + 1];
        var heights = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                heights[j * n + i] = H(i, j);

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var index = new Dictionary<int, int>();
        var tris = new List<int>();
        var faces = new List<Vector3>();

        int Vert(int i, int j)
        {
            int key = j * n + i;
            if (index.TryGetValue(key, out int at)) return at;
            verts.Add(new Vector3(i * Cell, heights[key], j * Cell));
            // Smooth normals across neighbouring grid points (per metre of slope).
            float hx = (H(i + 1, j) - H(i - 1, j)) / (2f * Cell);
            float hz = (H(i, j + 1) - H(i, j - 1)) / (2f * Cell);
            normals.Add(new Vector3(-hx, 1f, -hz).Normalized());
            index[key] = verts.Count - 1;
            return verts.Count - 1;
        }

        for (int j = 0; j < ChunkCells; j++)
            for (int i = 0; i < ChunkCells; i++)
            {
                float mx = ox + (i + 0.5f) * Cell, mz = oz + (j + 0.5f) * Cell;
                if (!Covers(mx, mz)) continue;
                int a = Vert(i, j), b = Vert(i + 1, j), c = Vert(i, j + 1), d = Vert(i + 1, j + 1);
                // Split each cell along the diagonal that follows the ground best.
                bool flip = Mathf.Abs(heights[j * n + i] - heights[(j + 1) * n + i + 1]) > Mathf.Abs(heights[j * n + i + 1] - heights[(j + 1) * n + i]);
                int[] q = flip ? new[] { a, b, c, b, d, c } : new[] { a, b, d, a, d, c };
                foreach (int k in q) { tris.Add(k); faces.Add(verts[k]); }
            }
        // (a, b, d) runs clockwise seen from above: Godot's front face, so the top faces up.
        if (tris.Count == 0) return;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = tris.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var body = new StaticBody3D
        {
            Name = $"Chunk_{cx}_{cz}",
            Position = new Vector3(ox, 0, oz),
            CollisionLayer = GameState.LayerWorld,
            CollisionMask = 0,
        };
        body.AddChild(new MeshInstance3D { Name = "Mesh", Mesh = mesh, MaterialOverride = material });
        var shape = new ConcavePolygonShape3D { BackfaceCollision = false };
        shape.SetFaces(faces.ToArray());
        body.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(body);
    }
}
