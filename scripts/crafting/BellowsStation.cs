using Godot;

namespace KatrinasGame;

/// <summary>The Bellows (GDD §11, step 2): the smelter upgrade the Crawler Mother's plans describe.
/// A great leather bellows on a timber frame, its iron nozzle aimed at the smelter beside it, that
/// works the iron recipes (more ingots from the ore, chain mail, chain gloves and the iron shield).
/// Built from code here so the scene stays a bare station; the leather breathes in and out.</summary>
public partial class BellowsStation : CraftingStation
{
    private Node3D _leather;
    private float _time;

    public override void _Ready()
    {
        var wood = StyleMaterials.Toon(new Color(0.42f, 0.28f, 0.16f), 0.25f);
        var dark = StyleMaterials.Toon(new Color(0.28f, 0.18f, 0.1f), 0.2f);
        var leather = StyleMaterials.Toon(new Color(0.4f, 0.24f, 0.16f), 0.3f);
        var iron = StyleMaterials.Toon(new Color(0.36f, 0.37f, 0.4f), 0.5f);

        void Box(Vector3 size, Material m, Vector3 pos, Vector3 rot = default, Node3D parent = null) =>
            (parent ?? this).AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos, Rotation = rot });

        // A trestle frame.
        foreach (float x in new[] { -0.6f, 0.6f })
            foreach (float z in new[] { -0.4f, 0.4f })
                Box(new Vector3(0.1f, 0.6f, 0.1f), dark, new Vector3(x, 0.3f, z));
        Box(new Vector3(1.4f, 0.08f, 0.95f), wood, new Vector3(0, 0.62f, 0));

        // The bellows: two boards with the leather between, a nozzle forward (-Z), a handle back.
        Box(new Vector3(0.9f, 0.06f, 0.7f), wood, new Vector3(0, 0.7f, 0.05f));
        _leather = new Node3D { Name = "Leather", Position = new Vector3(0, 0.73f, 0.05f) };
        AddChild(_leather);
        Box(new Vector3(0.84f, 0.3f, 0.62f), leather, new Vector3(0, 0.15f, 0), default, _leather);
        Box(new Vector3(0.9f, 0.06f, 0.7f), wood, new Vector3(0, 0.33f, 0), default, _leather);
        Box(new Vector3(0.08f, 0.08f, 0.6f), dark, new Vector3(0, 0.4f, 0.55f), new Vector3(0.3f, 0, 0), _leather);
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.09f, Height = 0.6f, RadialSegments = 8 },
            MaterialOverride = iron, Position = new Vector3(0, 0.85f, -0.55f), Rotation = new Vector3(Mathf.Pi / 2f, 0, 0),
        });
        // A coil of chain and a finished shield leaning on the frame.
        AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.08f, OuterRadius = 0.16f, Rings = 10, RingSegments = 5 }, MaterialOverride = iron,
            Position = new Vector3(0.45f, 0.67f, -0.25f),
        });
        var shield = HeldModels.Shield(new Color(0.45f, 0.3f, 0.2f));
        shield.Position = new Vector3(-0.75f, 0.32f, 0.1f);
        shield.Rotation = new Vector3(0, 0, -1.3f);
        AddChild(shield);
    }

    public override void _Process(double delta)
    {
        if (_leather == null) return;
        _time += (float)delta;
        _leather.Scale = new Vector3(1, 0.75f + 0.25f * Mathf.Sin(_time * 1.6f), 1);
    }
}
