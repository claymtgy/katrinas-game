using Godot;

namespace KatrinasGame;

/// <summary>Lie down to sleep. The night is skipped (full heal, autosave) only once every
/// connected player is asleep; until then you wait in bed. E or moving gets you up.</summary>
public partial class Bed : StaticBody3D, IInteractable, IServerInteractable
{
    public string InteractPrompt =>
        Net.Instance.IsMultiplayer && GameState.Instance.Players.Count > 1
            ? "Sleep (the night passes when everyone is asleep)"
            : "Sleep until dawn (saves the game)";

    public void Interact(PlayerController player) => Net.Instance.RequestInteract(this);

    public void ServerInteract(PlayerController player)
    {
        if (player.Sleeping) return;
        Net.Instance.SetSleeping(player, true);
        if (player.Sleeping && GameState.Instance.Players.Count > 1)
            Net.Instance.ToastAll($"{player.DisplayName} is asleep ({Net.Instance.SleepingCount}/{Net.Instance.SleepTotal})");
    }
}
