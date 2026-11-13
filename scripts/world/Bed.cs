using Godot;

namespace KatrinasGame;

/// <summary>Sleep until dawn: full heal, full stamina, autosave.</summary>
public partial class Bed : StaticBody3D, IInteractable
{
    public string InteractPrompt => "Sleep until dawn (saves the game)";

    public void Interact(PlayerController player)
    {
        InteractHelpers.SkipToMorning();
        player.Health.Reset();
        player.RestoreStamina();
        Hud.Toast($"You slept soundly. Day {GameState.Instance.Day} begins.");
        GameState.Instance.SaveGame();
    }
}
