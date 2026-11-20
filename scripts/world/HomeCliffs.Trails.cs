using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Trails: unbroken dirt roads joining every area (home → Ashby, the bandit camp track, and
/// east through the pass to Falls Valley). Each trail is a polyline of road strips with a patch over
/// every bend, so there are no gaps or hard corners. Every strip and patch is also recorded so the
/// player walks faster on paths (<see cref="OnPath"/>), and trees or rocks the scatter dropped on a
/// trail are nudged off it.</summary>
public partial class HomeCliffs
{
    /// <summary>Walking or running on a path is this much faster (GDD §8).</summary>
    public const float PathSpeedMult = 1.2f;

    private readonly struct PathSeg
    {
        public readonly Vector2 A, B;
        public readonly float HalfWidth;
        public PathSeg(Vector3 a, Vector3 b, float halfWidth) { A = new Vector2(a.X, a.Z); B = new Vector2(b.X, b.Z); HalfWidth = halfWidth; }

        public float Distance(Vector2 p)
        {
            var ab = B - A;
            float t = ab.LengthSquared() < 1e-4f ? 0f : Mathf.Clamp((p - A).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            return p.DistanceTo(A + ab * t);
        }
    }

    private static readonly List<PathSeg> Segs = new();
    /// <summary>Patches (yards, squares, bends): centre and half size on x/z.</summary>
    private static readonly List<(Vector2 Center, Vector2 Half)> Patches = new();

    /// <summary>True when the point (x/z) is on a road, track, yard or square.</summary>
    public static bool OnPath(Vector3 p)
    {
        var q = new Vector2(p.X, p.Z);
        foreach (var s in Segs)
            if (s.Distance(q) <= s.HalfWidth) return true;
        foreach (var (c, h) in Patches)
        {
            // The patch edge is ragged and rounded: count the ellipse inside it.
            var d = (q - c) / h;
            if (d.LengthSquared() <= 0.8f) return true;
        }
        return false;
    }

    /// <summary>Every recorded path point (segment ends, patch centres), for WorldDump.</summary>
    public static IEnumerable<Vector3> PathPoints()
    {
        foreach (var s in Segs) { yield return new Vector3(s.A.X, 0, s.A.Y); yield return new Vector3(s.B.X, 0, s.B.Y); }
        foreach (var (c, _) in Patches) yield return new Vector3(c.X, 0, c.Y);
    }

    private static void ClearPathRecords()
    {
        Segs.Clear();
        Patches.Clear();
    }

    /// <summary>A trail through <paramref name="points"/> (y is ignored; laid at <paramref name="y"/>).
    /// Strips overlap a little at each joint and a round patch covers the bend.</summary>
    private void Trail(float y, float width, Vector3[] points, bool ruts = true, Color? tone = null)
    {
        var dirt = tone ?? Dirt;
        for (int i = 0; i < points.Length - 1; i++)
        {
            var a = new Vector3(points[i].X, 0, points[i].Z);
            var b = new Vector3(points[i + 1].X, 0, points[i + 1].Z);
            var d = b - a;
            float len = d.Length();
            // Alternate heights a hair apart so overlapping strips don't flicker.
            Road(new Vector3((a.X + b.X) / 2f, y + (i % 2) * 0.004f, (a.Z + b.Z) / 2f), len + width * 0.4f,
                Mathf.Atan2(d.X, d.Z), width, ruts, dirt);
            Segs.Add(new PathSeg(a, b, width / 2f));
            if (i > 0)
                PathPatch(new Vector3(a.X, y + 0.008f, a.Z), new Vector2(width * 1.5f, width * 1.5f),
                    dirt.Darkened(0.12f), dirt.Lightened(0.06f));
        }
    }

    /// <summary>The roads between every area. The main road runs from the yard south through the
    /// gap in the plateau wall and the vale to Ashby; a footpath branches east to the bandit camp;
    /// another road leaves the main one east through the pass, crosses the river on the stepping
    /// stones and ends at the foot of the mesa's climbing face.</summary>
    /// <summary>The main road, home → Ashby: down the plateau, out through the gap in its south
    /// edge, a long meander along the valley floors of the country between, and up the vale.</summary>
    public static Vector3[] MainRoad => new[]
    {
        ShackPos + new Vector3(0, 0, 7), new Vector3(5, 0, 4), new Vector3(2, 0, 40), new Vector3(0.6f, 0, 82),
        // The country between the plateau and the vale (neither region's: it stays put).
        new Vector3(-5, 0, 140), new Vector3(12, 0, 205), new Vector3(5, 0, 265), new Vector3(-16, 0, 325),
        new Vector3(-9, 0, 390), new Vector3(1, 0, 430),
        InVale(0.4f, 0, 100), InVale(1, 0, 134), AshbyCenter + new Vector3(1, 0, -12),
    };

    private void BuildTrails()
    {
        // Home → Ashby (the yard patch and the village square cover each end).
        Trail(0.02f, 4.8f, MainRoad);
        // Main road → the bandit camp, a narrow footpath with no cart ruts.
        var camp = Wildlife.CampSites[0];
        Trail(0.03f, 2.6f, new[] { InVale(1, 0, 118), new Vector3(camp.X - 2f, 0, camp.Z) }, ruts: false, tone: Dirt.Darkened(0.1f));
        // Main road → Falls Valley: east through the pass, over the stepping stones, to the mesa.
        float z = FallsPathZ;
        Trail(0.025f, 4f, new[]
        {
            new Vector3(2.4f, 0, 36), new Vector3(30, 0, 41), PassPos, // the pass is the plateau's; the river crossing is the valley's
            new Vector3(RiverBend.X - RiverHalfWidth - 0.8f, 0, z),
        });
        Trail(0.025f, 4f, new[]
        {
            new Vector3(RiverBend.X + RiverHalfWidth + 0.8f, 0, z), InFalls(122, 0, 34), MesaClimbFoot + new Vector3(-2.5f, 0, 0),
        });
    }

    /// <summary>After everything is placed: push trees, rocks and plants that landed on a trail
    /// off to its side. Positions only depend on the world seed, so every machine agrees.</summary>
    private void ClearTrailsOfScatter()
    {
        foreach (var child in GetChildren())
        {
            if (child is not Node3D n) continue;
            bool scatter = n.Name.ToString().StartsWith("Res_") || n.SceneFilePath.Contains("/nature/");
            if (!scatter || n.IsInGroup("iron_ore")) continue;
            var q = new Vector2(n.Position.X, n.Position.Z);
            foreach (var s in Segs)
            {
                float clear = s.HalfWidth + (n.Name.ToString().StartsWith("Res_") ? 1.6f : 0.4f);
                if (s.Distance(q) >= clear) continue;
                var ab = (s.B - s.A).Normalized();
                var side = new Vector2(-ab.Y, ab.X);
                float along = (q - s.A).Dot(ab);
                float off = (q - s.A).Dot(side);
                float push = off >= 0 ? clear + 0.3f : -(clear + 0.3f);
                var moved = s.A + ab * along + side * push;
                n.Position = new Vector3(moved.X, n.Position.Y, moved.Y);
                q = moved;
            }
        }
    }
}
