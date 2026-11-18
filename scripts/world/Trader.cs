using Godot;

namespace KatrinasGame;

/// <summary>A village trader. Prices live on the items (ItemDef.BuyPrice / SellPrice); trades go
/// through the server (Net.RequestTrade) and use the shared stockpile's coins. GDD §11.</summary>
public partial class Trader : StaticBody3D, IInteractable
{
    [Export] public string TraderName { get; set; } = "Trader";

    public string InteractPrompt => $"Trade with {TraderName}";

    public void Interact(PlayerController player) => Hud.Instance?.OpenTrade(this);
}
