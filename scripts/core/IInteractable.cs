using Godot;

namespace KatrinasGame;

/// <summary>Anything the player can press E on: stations, beds, signs, shrines.
/// Interact runs on the machine of the player who pressed E (open a menu, show a sign, or ask
/// the server to do something with Net.Instance.RequestInteract).</summary>
public interface IInteractable
{
    /// <summary>Short verb phrase shown as "[E] ...".</summary>
    string InteractPrompt { get; }
    void Interact(PlayerController player);
}

/// <summary>The server-side half of an interaction (sleep, pray, rest). Reached through
/// Net.Instance.RequestInteract(this); runs on the server with the player who asked.</summary>
public interface IServerInteractable
{
    void ServerInteract(PlayerController player);
}

/// <summary>A placed building with state worth saving and replicating (a farm plot's crop).
/// Server changes it, then calls Net.BroadcastBuildingState.</summary>
public interface INetState
{
    Variant GetState();
    void ApplyState(Variant state);
}
