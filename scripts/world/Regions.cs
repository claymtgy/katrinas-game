using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Where each region of the world sits. A region's places (its public positions in
/// HomeCliffs and Wildlife, and the literal positions in its builder) are written relative to its
/// origin, so moving a region is changing one number here. The home plateau (and the pass in its
/// east wall) never moves: saved bases sit on it.
///
/// The map: the plateau on its cliff over the clouds, Falls Valley through the pass east of it and
/// the Spire Cliffs off the valley's north rim (both tied to the plateau, at zero). South of the
/// plateau, a long road winds through open hill country to Ashby in the vale (about 520 m from the
/// shack); the Forest Road runs on west from Ashby to the mine, and a track south to the Old
/// Waystone on the downs. The hills (Terrain.Domain) cover all of it.
///
/// Origins are horizontal (y = 0): every region's ground is at y = 0. For a check that everything
/// follows its region, <c>--region-shift=x,0,z</c> moves every region by that much (see <see cref="WorldDump"/>).</summary>
public static class Regions
{
    /// <summary>The Village of Ashby with the vale round it: the bandit camp and the clay pond.</summary>
    public static readonly Vector3 Vale;
    /// <summary>Falls Valley east of the pass: the mesa, the river, the Great Falls, the roost.</summary>
    public static readonly Vector3 Falls;
    /// <summary>The Spire Cliffs, off Falls Valley's north rim (they move with the valley).</summary>
    public static readonly Vector3 Spires;
    /// <summary>The Forest Road and the Hollow Mine.</summary>
    public static readonly Vector3 West;
    /// <summary>The Cinder Foothills: a burnt highland in the cloud sea north-west of the spires,
    /// at the volcano's feet, reached over the Warlord's rope bridge from the Tall Spire (it moves
    /// with the spires, so the bridge always meets).</summary>
    public static readonly Vector3 Foothills;

    static Regions()
    {
        var shift = Vector3.Zero;
        var arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--region-shift="));
        if (arg != null)
        {
            var p = arg.Substring("--region-shift=".Length).Split(',');
            if (p.Length == 3) shift = new Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
            shift.Y = 0f; // regions move across the land, never up or down
            GD.Print($"[Regions] every region shifted by {shift}");
        }
        Vale = new Vector3(0, 0, 360) + shift;
        Falls = shift;
        Spires = Falls;
        West = new Vector3(-150, 0, 360) + shift;
        Foothills = new Vector3(110, 0, -290) + shift;
    }

    /// <summary>A rectangle (x, z) moved with a region.</summary>
    public static Rect2 Move(Rect2 r, Vector3 origin) => new(r.Position + new Vector2(origin.X, origin.Z), r.Size);
}
