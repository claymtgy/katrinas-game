using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Clay (GDD §11, step 0: fill in tier 1): soft banks of it along the Falls Valley river
/// and round a pond in the Ashby vale, by the road south. Any swing digs it, a pickaxe quickest.
/// The Kiln fires it into bricks.</summary>
public partial class HomeCliffs
{
    public static readonly Vector3 ClayPond = InVale(-30, 0, 160);
    public const float ClayPondRadius = 4.5f;

    /// <summary>Clay banks: six on the valley river's banks, three round the vale pond.</summary>
    public static readonly Vector3[] ClaySpots =
    {
        InFalls(102.4f, 0, 10), InFalls(102.4f, 0, -8), InFalls(113.6f, 0, 2),
        InFalls(132, 0, 63.6f), InFalls(155, 0, 63.6f), InFalls(145, 0, 52.4f),
        ClayPond + new Vector3(5.6f, 0, 1.5f), ClayPond + new Vector3(-4.5f, 0, 3.8f), ClayPond + new Vector3(-1f, 0, -5.7f),
    };

    private static readonly Color ClayColor = new(0.7f, 0.45f, 0.32f);

    private void BuildClay()
    {
        // The pond: a muddy rim and still water. Both stand clear of the ground (the hills are flat
        // at y = 0 here): the rim from 2 cm up, the water's underside inside the rim, so no face
        // sits on the ground's own plane and flickers against it.
        ClearScatterAround(ClayPond, ClayPondRadius + 2.5f);
        var water = StyleMaterials.Water();
        Visual(new CylinderMesh { TopRadius = ClayPondRadius + 0.9f, BottomRadius = ClayPondRadius + 0.9f, Height = 0.06f, RadialSegments = 20 },
            Mat(ClayColor.Darkened(0.35f)), ClayPond + new Vector3(0, 0.05f, 0), Vector3.Zero);
        Visual(new CylinderMesh { TopRadius = ClayPondRadius, BottomRadius = ClayPondRadius, Height = 0.1f, RadialSegments = 20 },
            water, ClayPond + new Vector3(0, 0.09f, 0), Vector3.Zero);
        for (int k = 0; k < 9; k++)
        {
            float a = k * 0.7f + 0.3f;
            var at = ClayPond + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (ClayPondRadius + 0.4f);
            Visual(new BoxMesh { Size = new Vector3(0.06f, 0.9f + 0.15f * (k % 3), 0.06f) }, Mat(new Color(0.42f, 0.55f, 0.28f)),
                at + new Vector3(0, 0.45f, 0), new Vector3(0.12f * (k % 2 == 0 ? 1 : -1), a, 0.1f));
        }

        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var clay = Mat(ClayColor);
        var wet = Mat(ClayColor.Darkened(0.25f));
        for (int i = 0; i < ClaySpots.Length; i++)
        {
            ClearScatterAround(ClaySpots[i], 1.8f);
            var n = rock.Instantiate<Harvestable>();
            n.Name = $"Clay_{i}";
            n.ModelPaths = "";          // keep our own mound, not a boulder
            n.DropItemId = "clay";
            n.Tool = "pick";            // a pickaxe digs it fastest; anything else slowly
            n.DamagePerDrop = 15f;
            n.BonusOnBreak = 2;
            n.RespawnSeconds = 180f;
            n.AddToGroup("clay");
            AddChild(n);
            n.Position = ClaySpots[i];
            n.Rotation = new Vector3(0, i * 1.3f, 0);
            var visual = n.GetNode<Node3D>("Visual");
            foreach (var c in visual.GetChildren()) { visual.RemoveChild(c); c.QueueFree(); }
            Visual(new SphereMesh { Radius = 0.85f, Height = 0.7f, RadialSegments = 10, Rings = 5 }, clay, new Vector3(0, 0.12f, 0), Vector3.Zero, visual);
            Visual(new SphereMesh { Radius = 0.55f, Height = 0.55f, RadialSegments = 8, Rings = 4 }, wet, new Vector3(0.55f, 0.12f, 0.25f), Vector3.Zero, visual);
            Visual(new SphereMesh { Radius = 0.45f, Height = 0.5f, RadialSegments = 8, Rings = 4 }, clay, new Vector3(-0.5f, 0.1f, -0.3f), Vector3.Zero, visual);
            foreach (var s in n.FindChildren("*", "CollisionShape3D", false, false))
            {
                var shape = (CollisionShape3D)s;
                shape.Shape = new BoxShape3D { Size = new Vector3(1.9f, 0.9f, 1.6f) };
                shape.Position = new Vector3(0, 0.45f, 0);
            }
        }
    }

    /// <summary>Push scattered props (trees, rocks, plants) out of a circle.</summary>
    private void ClearScatterAround(Vector3 center, float radius)
    {
        foreach (var child in GetChildren())
        {
            if (child is not Node3D n) continue;
            string name = n.Name.ToString();
            bool scatter = name.StartsWith("Res_") || n.SceneFilePath.Contains("/nature/");
            if (!scatter || n.IsInGroup("iron_ore")) continue;
            var d = new Vector3(n.Position.X - center.X, 0, n.Position.Z - center.Z);
            float clear = radius + (name.StartsWith("Res_") ? 1.2f : 0.3f);
            if (d.Length() >= clear) continue;
            var dir = d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Right;
            var to = center + dir * (clear + 0.3f);
            n.Position = new Vector3(to.X, n.Position.Y, to.Z);
        }
    }
}
