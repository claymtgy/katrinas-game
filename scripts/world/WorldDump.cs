using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

namespace KatrinasGame;

/// <summary>A check that every region builds where its origin says (<see cref="Regions"/>).
/// <c>--dump-world=path</c> writes where every piece of the world ended up (x, z) and every place
/// the world registered (water, updrafts, currents, safe zones, paths), then quits. Dump once as
/// is and once with <c>--region-shift=x,0,z</c>, then compare (tools/compare_world_dumps.py):
/// every piece should either stay put (the home plateau) or move by exactly the shift.
/// Runs before the country is dressed (its scatter depends on where the holes in the hills are).</summary>
public static class WorldDump
{
    public static string Path => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--dump-world="))?.Substring("--dump-world=".Length);

    /// <summary>Containers whose children are placed one by one (the rest move as a whole).</summary>
    private static readonly string[] Open = { "HollowMine", "SpireCliffs", "Greenery" };

    public static void Write(Node3D world, string path)
    {
        var sb = new StringBuilder();
        void Row(string kind, Vector3 p) =>
            sb.Append(kind).Append('\t').Append(p.X.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t')
              .Append(p.Z.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
        string Kind(Node n) => n.Name.ToString().StartsWith("@") ? n.GetClass() : n.Name.ToString();

        foreach (var child in world.GetChildren())
        {
            if (child is not Node3D n) continue;
            Row(Kind(n), n.GlobalPosition);
            if (!Open.Contains(n.Name.ToString())) continue;
            foreach (var g in n.GetChildren())
                if (g is Node3D gn) Row(n.Name + "/" + Kind(gn), gn.GlobalPosition);
        }
        var gs = GameState.Instance;
        foreach (var (min, max) in gs.Waters) { Row("water.min", min); Row("water.max", max); }
        foreach (var (b, _, _) in gs.Updrafts) Row("updraft", b);
        foreach (var (from, to, _, _) in gs.Currents) { Row("current.from", from); Row("current.to", to); }
        foreach (var (c, _) in gs.SafeZones) Row("safe", c);
        foreach (var p in HomeCliffs.PathPoints()) Row("path", p);
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        f.StoreString(sb.ToString());
        GD.Print($"[WorldDump] wrote {path}");
    }
}
