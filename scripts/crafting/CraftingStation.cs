using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>A building you can craft at. Shows every RecipeDef whose Station matches StationId.
/// The menu is local; crafting asks the server, which pays from the shared stockpile.</summary>
public partial class CraftingStation : StaticBody3D, IInteractable
{
    [Export] public string StationId { get; set; } = "workbench";
    [Export] public string StationName { get; set; } = "Workbench";

    public virtual string InteractPrompt => $"Use {StationName}";

    public void Interact(PlayerController player) => Hud.Instance?.OpenCrafting(this);

    public IEnumerable<RecipeDef> Recipes => ItemDb.Recipes.Where(r => r.Station == StationId);

    public void RequestCraft(RecipeDef recipe) => Net.Instance.RequestCraft(recipe.Id, this);
}
