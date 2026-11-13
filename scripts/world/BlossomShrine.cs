using Godot;

namespace KatrinasGame;

/// <summary>Sacred cherry-blossom sanctuary: sets your respawn point and heals you.
/// Later: fast travel, blessings, enemy-free zone.</summary>
public partial class BlossomShrine : StaticBody3D, IInteractable
{
    [Export] public string ShrineName { get; set; } = "Cliffside Blossom";

    public string InteractPrompt => $"Pray at the {ShrineName}";

    public void Interact(PlayerController player)
    {
        var gs = GameState.Instance;
        gs.SpawnPoint = GlobalPosition - GlobalBasis.Z * 3f + Vector3.Up * 0.2f;
        player.Health.Reset();
        player.RestoreStamina();
        Hud.Toast($"The {ShrineName} glows warmly. Respawn point set, wounds healed.");
    }
}
