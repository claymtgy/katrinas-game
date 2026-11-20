using Godot;

namespace KatrinasGame;

/// <summary>Bits for building low-poly faceted creatures in code (boars, cave crawlers).</summary>
public static class EnemyMesh
{
    /// <summary>A tapered n-sided segment from a to b (radius ra at a, rb at b).</summary>
    public static MeshInstance3D Seg(Node3D parent, Vector3 a, Vector3 b, float ra, float rb, Material mat, int sides)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.001f) return null;
        var y = d / len;
        var x = Mathf.Abs(y.Dot(Vector3.Forward)) > 0.95f ? y.Cross(Vector3.Right).Normalized() : y.Cross(Vector3.Forward).Normalized();
        var z = x.Cross(y);
        var mi = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = rb, BottomRadius = ra, Height = len, RadialSegments = sides, Rings = 1 },
            MaterialOverride = mat,
            Transform = new Transform3D(new Basis(x, y, z), (a + b) / 2f),
        };
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Vector3 rot, Material mat)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = pos, Rotation = rot, MaterialOverride = mat };
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>A faceted ball (few segments), squashed by <paramref name="scale"/>.</summary>
    public static MeshInstance3D Ball(Node3D parent, float radius, Vector3 pos, Vector3 scale, Material mat, int segments = 6)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2f, RadialSegments = segments, Rings = Mathf.Max(2, segments / 2) },
            MaterialOverride = mat, Position = pos, Scale = scale,
        };
        parent.AddChild(mi);
        return mi;
    }

    public static StandardMaterial3D Glow(Color c, float energy = 3f) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
