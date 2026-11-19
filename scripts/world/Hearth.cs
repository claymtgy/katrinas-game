using Godot;

namespace KatrinasGame;

/// <summary>The fireplace in your home: a stone firebox with a mantel, logs and a fire. It works
/// like the campfire (rest to heal, cook, smelt iron ore into ingots) from indoors. Built in code
/// so the shack and the cottage share it; the shack's chimney stack (Blender) sits on its mantel.
/// Local layout: the fire opens toward +Z; the hearth is <see cref="Width"/> wide and
/// <see cref="Depth"/> deep, its back on z = -Depth/2.</summary>
public partial class Hearth : Campfire
{
    public const float Width = 1.1f, Depth = 0.7f, MantelHeight = 1.3f;

    public Hearth()
    {
        StationName = "Hearth";
        HealAmount = 50f;
    }

    public override string InteractPrompt => "Rest, cook or smelt at the hearth";

    private static readonly Color Stone = new(0.5f, 0.48f, 0.47f);
    private static readonly Color StoneDark = new(0.36f, 0.34f, 0.34f);
    private static readonly Color Soot = new(0.09f, 0.07f, 0.07f);
    private static readonly Color Wood = new(0.42f, 0.27f, 0.15f);

    /// <summary>A hearth with its stones, fire and collider, ready to add to a home.</summary>
    public static Hearth Create(string name = "Hearth")
    {
        var h = new Hearth { Name = name, CollisionLayer = GameState.LayerWorld | GameState.LayerProps, CollisionMask = 0 };
        h.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(Width, MantelHeight + 0.1f, Depth) },
            Position = new Vector3(0, (MantelHeight + 0.1f) / 2f, 0),
        });
        h.BuildLooks();
        return h;
    }

    /// <summary>A stone chimney stack from the mantel up to <paramref name="top"/> (local height):
    /// for homes whose model has none of its own (the cottage).</summary>
    public void AddStack(float top)
    {
        int k = 0;
        for (float y = MantelHeight; y < top; y += 0.42f, k++)
            Box(new Vector3(0.62f, 0.4f, 0.62f), k % 2 == 0 ? Stone : Stone.Darkened(0.08f), new Vector3(0, y + 0.2f, -0.02f), k % 2 == 0 ? 3f : -3f);
    }

    private MeshInstance3D Box(Vector3 size, Color color, Vector3 pos, float yawDeg = 0f, float rim = 0.2f)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size }, MaterialOverride = StyleMaterials.Toon(color, rim),
            Position = pos, RotationDegrees = new Vector3(0, yawDeg, 0),
        };
        AddChild(mi);
        return mi;
    }

    private void BuildLooks()
    {
        float hw = Width / 2f, hd = Depth / 2f;
        const float jamb = 0.24f, opening = 0.85f;
        // Hearthstone in front, raised a little off the floor.
        Box(new Vector3(Width + 0.3f, 0.08f, 0.45f), StoneDark, new Vector3(0, 0.04f, hd + 0.2f));
        // Side jambs in rough courses, so it reads as laid stone, not one block.
        for (int side = -1; side <= 1; side += 2)
            for (int k = 0; k < 4; k++)
            {
                float h = opening / 4f;
                Box(new Vector3(jamb - (k % 2) * 0.03f, h - 0.02f, Depth - (k % 2) * 0.04f), k % 2 == 0 ? Stone : Stone.Darkened(0.08f),
                    new Vector3(side * (hw - jamb / 2f), h * k + h / 2f, 0), (k % 2 == 0 ? 3f : -4f) * side);
            }
        // Back of the firebox and its sooty inside.
        Box(new Vector3(Width - 2 * jamb, opening, 0.14f), StoneDark, new Vector3(0, opening / 2f, -hd + 0.07f));
        Box(new Vector3(Width - 2 * jamb - 0.02f, opening - 0.05f, 0.02f), Soot, new Vector3(0, opening / 2f, -hd + 0.15f), 0, 0f);
        Box(new Vector3(Width - 2 * jamb, 0.04f, Depth - 0.14f), Soot, new Vector3(0, 0.02f, 0.05f), 0, 0f);
        // Lintel stone over the opening, then the mantel shelf.
        Box(new Vector3(Width, 0.3f, Depth), Stone.Lightened(0.05f), new Vector3(0, opening + 0.15f, 0));
        Box(new Vector3(Width + 0.16f, 0.07f, 0.2f), Wood, new Vector3(0, opening + 0.33f, hd - 0.02f));
        // Under the stack.
        Box(new Vector3(Width - 0.2f, MantelHeight - opening - 0.37f, Depth - 0.08f), Stone, new Vector3(0, (opening + 0.37f + MantelHeight) / 2f, -0.02f));

        // Logs crossed on the floor of the firebox.
        var log = StyleMaterials.Toon(Wood.Darkened(0.2f), 0.1f);
        foreach (var (yaw, x) in new[] { (70f, -0.08f), (-65f, 0.08f) })
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.06f, Height = 0.5f, RadialSegments = 6 },
                MaterialOverride = log, Position = new Vector3(x, 0.08f, 0.02f), RotationDegrees = new Vector3(0, yaw, 90),
            });
        // The fire (Campfire flickers the nodes named Flame and Light).
        var flameMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.55f, 0.15f),
            EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f), EmissionEnergyMultiplier = 4f,
        };
        var flame = new Node3D { Name = "Flame", Position = new Vector3(0, 0.1f, 0.02f) };
        AddChild(flame);
        foreach (var (x, h, r) in new[] { (0f, 0.5f, 0.17f), (-0.12f, 0.32f, 0.1f), (0.13f, 0.36f, 0.11f) })
            flame.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = r, Height = h, RadialSegments = 5 },
                MaterialOverride = flameMat, Position = new Vector3(x, h / 2f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        AddChild(new OmniLight3D
        {
            Name = "Light", Position = new Vector3(0, 0.5f, hd + 0.45f), LightColor = new Color(1f, 0.6f, 0.3f),
            LightEnergy = 2.2f, OmniRange = 7f, ShadowEnabled = true,
        });
    }
}
