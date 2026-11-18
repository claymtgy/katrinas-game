using Godot;

namespace KatrinasGame;

/// <summary>A well (Ashby's, or one you build) or the pool under the west waterfall: E fills your
/// pack with Water for the farm plots. GDD §4 farming.
/// A built well (BuildVisual) makes its own stone-ring model.</summary>
public partial class WaterSource : StaticBody3D, IInteractable, IServerInteractable
{
    [Export] public string SourceName { get; set; } = "well";
    /// <summary>Water drawn per use.</summary>
    [Export] public int Draw { get; set; } = 4;
    /// <summary>Build the well model in code (the buildable Well).</summary>
    [Export] public bool BuildVisual { get; set; }

    public string InteractPrompt => $"Draw water from the {SourceName} (+{Draw} Water)";

    public override void _Ready()
    {
        if (BuildVisual) BuildWell(this);
    }

    public void Interact(PlayerController player)
    {
        Sfx.At("water", GlobalPosition + Vector3.Up * 0.5f);
        Net.Instance.RequestInteract(this);
    }

    public void ServerInteract(PlayerController player)
    {
        int got = Supplies.Give(player, "water", Draw);
        if (got > 0) Net.Instance.ToastTo(player.PeerId, $"+{got} Water");
    }

    /// <summary>Stone ring, water, two posts, a roof and a hanging bucket (about 1.8 m across).</summary>
    public static void BuildWell(Node3D root)
    {
        var stone = StyleMaterials.Rock(new Color(0.28f, 0.27f, 0.32f), new Color(0.42f, 0.41f, 0.46f), 0.6f, 0.3f);
        var wood = StyleMaterials.Toon(new Color(0.36f, 0.23f, 0.15f));
        var roof = StyleMaterials.Toon(new Color(0.42f, 0.2f, 0.15f));
        var water = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.18f, 0.38f, 0.5f), Roughness = 0.05f, Metallic = 0.3f,
            EmissionEnabled = true, Emission = new Color(0.05f, 0.15f, 0.22f),
        };
        // Faceted stone ring: 8 blocks around the rim.
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.62f, 0.8f, 0.3f) },
                MaterialOverride = stone,
                Position = new Vector3(Mathf.Sin(a) * 0.75f, 0.4f, Mathf.Cos(a) * 0.75f),
                Rotation = new Vector3(0, a, 0),
            });
        }
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.66f, BottomRadius = 0.66f, Height = 0.05f, RadialSegments = 10 },
            MaterialOverride = water, Position = new Vector3(0, 0.62f, 0),
        });
        foreach (float x in new[] { -0.82f, 0.82f })
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.14f, 1.9f, 0.14f) }, MaterialOverride = wood, Position = new Vector3(x, 1.25f, 0) });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1.7f, RadialSegments = 6 },
            MaterialOverride = wood, Position = new Vector3(0, 1.75f, 0), Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
        });
        root.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(2.1f, 0.6f, 1.5f) }, MaterialOverride = roof, Position = new Vector3(0, 2.5f, 0) });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.12f, Height = 0.24f, RadialSegments = 7 },
            MaterialOverride = wood, Position = new Vector3(0, 1.25f, 0),
        });
    }
}
