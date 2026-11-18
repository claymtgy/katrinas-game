using Godot;

namespace KatrinasGame;

/// <summary>Empty your pack into the shared stockpile (GDD §16). One stands by the shack; more can
/// be built. Builds its own model.</summary>
public partial class StorageChest : StaticBody3D, IInteractable, IServerInteractable
{
    public string InteractPrompt
    {
        get
        {
            int n = GameState.Instance.LocalPlayer?.Pack.Items.Count ?? 0;
            return n > 0 ? "Empty your pack into the stockpile" : "Storage chest (your pack is empty)";
        }
    }

    public override void _Ready() => BuildChest(this);

    public void Interact(PlayerController player)
    {
        if (player.Pack.Items.Count == 0) { Hud.Toast("Your pack is empty. Gathered things go into your pack until you bring them here."); return; }
        Sfx.At("chest", GlobalPosition + Vector3.Up * 0.5f);
        Net.Instance.RequestInteract(this);
    }

    public void ServerInteract(PlayerController player)
    {
        int n = Supplies.Deposit(player);
        if (n > 0) Net.Instance.ToastTo(player.PeerId, $"Stored {n} items in the stockpile");
    }

    private static void BuildChest(Node3D root)
    {
        var wood = StyleMaterials.Toon(new Color(0.45f, 0.28f, 0.16f));
        var dark = StyleMaterials.Toon(new Color(0.22f, 0.13f, 0.09f));
        var iron = StyleMaterials.Toon(new Color(0.32f, 0.3f, 0.34f), 0.4f);
        var brass = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.7f, 0.3f), Metallic = 0.6f, Roughness = 0.35f };
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.2f, 0.55f, 0.75f) }, MaterialOverride = wood, Position = new Vector3(0, 0.3f, 0) });
        // Rounded lid from a half cylinder.
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.38f, BottomRadius = 0.38f, Height = 1.22f, RadialSegments = 8 },
            MaterialOverride = dark, Position = new Vector3(0, 0.5f, 0), Rotation = new Vector3(0, 0, Mathf.Pi / 2f),
        });
        foreach (float x in new[] { -0.45f, 0.45f })
            root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.95f, 0.8f) }, MaterialOverride = iron, Position = new Vector3(x, 0.42f, 0) });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.2f, 0.06f) }, MaterialOverride = brass, Position = new Vector3(0, 0.55f, 0.4f) });
    }
}
