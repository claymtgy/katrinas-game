using Godot;

namespace KatrinasGame;

/// <summary>A sign post: E shows its message.</summary>
public partial class InfoSign : StaticBody3D, IInteractable
{
    [Export] public string Title { get; set; } = "Sign";
    [Export(PropertyHint.MultilineText)] public string Message { get; set; } = "";

    public string InteractPrompt => $"Read: {Title}";

    public void Interact(PlayerController player) => Hud.Toast(Message);
}
