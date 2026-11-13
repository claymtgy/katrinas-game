using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>A building you can craft at. Shows every RecipeDef whose Station matches StationId.</summary>
public partial class CraftingStation : StaticBody3D, IInteractable
{
    [Export] public string StationId { get; set; } = "workbench";
    [Export] public string StationName { get; set; } = "Workbench";

    public string InteractPrompt => $"Use {StationName}";

    public void Interact(PlayerController player) => Hud.Instance?.OpenCrafting(this);

    public IEnumerable<RecipeDef> Recipes => ItemDb.Recipes.Where(r => r.Station == StationId);

    public static bool TryCraft(RecipeDef recipe)
    {
        var gs = GameState.Instance;
        if (!gs.Inventory.TryConsume(recipe.GetInputs())) return false;
        gs.Inventory.Add(recipe.OutputItem, recipe.OutputCount);

        var item = ItemDb.Get(recipe.OutputItem);
        if (item != null && item.IsWeapon)
        {
            gs.Equipped = item.Id;
            int slot = gs.WeaponSlots().IndexOf(item.Id) + 1;
            Hud.Toast($"Equipped {item.DisplayName} (slot {slot})");
        }
        return true;
    }
}
