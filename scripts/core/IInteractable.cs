namespace KatrinasGame;

/// <summary>Anything the player can press E on: stations, beds, signs, shrines.</summary>
public interface IInteractable
{
    /// <summary>Short verb phrase shown as "[E] ...".</summary>
    string InteractPrompt { get; }
    void Interact(PlayerController player);
}
