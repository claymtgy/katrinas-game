using Godot;

namespace KatrinasGame;

/// <summary>Sacred cherry-blossom sanctuary: sets your respawn point and heals you.
/// Respawn points are personal. Enemies won't enter its light. Later: fast travel, blessings.</summary>
public partial class BlossomShrine : StaticBody3D, IInteractable, IServerInteractable
{
    [Export] public string ShrineName { get; set; } = "Cliffside Blossom";
    /// <summary>Enemies won't come within this many meters of the tree.</summary>
    [Export] public float SafeRadius { get; set; } = 9f;

    public override void _Ready() => GameState.Instance.SafeZones.Add((GlobalPosition, SafeRadius));

    public string InteractPrompt => $"Pray at the {ShrineName}";

    public void Interact(PlayerController player) => Net.Instance.RequestInteract(this);

    public void ServerInteract(PlayerController player)
    {
        player.SpawnPoint = GlobalPosition - GlobalBasis.Z * 3f + Vector3.Up * 0.2f;
        Net.Instance.ServerRestore(player);
        Journal.Fact($"prayed:{Name}");
        Net.Instance.ToastTo(player.PeerId, $"The {ShrineName} glows warmly. Respawn point set, wounds healed.");
    }
}
