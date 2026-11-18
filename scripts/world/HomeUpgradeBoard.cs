using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>The planning board by the shack: pay for the next home tier from the shared
/// stockpile. Tier 2 = the cottage (two beds, bigger build claim). GDD §6.</summary>
public partial class HomeUpgradeBoard : StaticBody3D, IInteractable, IServerInteractable
{
    public static readonly Dictionary<string, int> CottageCost = new()
    {
        ["wood"] = 40, ["stone"] = 20, ["iron_nails"] = 6, ["wolf_pelt"] = 3, ["cottage_blueprint"] = 1,
    };

    /// <summary>Where the cottage stands (world space, centre and half size on the ground).</summary>
    public static readonly Vector3 CottageCenter = new(10, 0, -21);
    public static readonly Vector3 CottageHalfSize = new(3.4f, 0, 3.4f);

    public string InteractPrompt
    {
        get
        {
            var gs = GameState.Instance;
            if (gs.HomeTier >= 2) return "Home: Cottage (fully upgraded for now)";
            var missing = Missing();
            return missing.Count == 0
                ? $"Upgrade the shack to a Cottage ({ItemDb.FormatCost(CottageCost)})"
                : $"Cottage needs: {ItemDb.FormatCost(missing)}";
        }
    }

    private static Dictionary<string, int> Missing(PlayerController p = null) =>
        Supplies.Missing(p ?? GameState.Instance.LocalPlayer, CottageCost);

    public void Interact(PlayerController player)
    {
        if (GameState.Instance.HomeTier >= 2) { Hud.Toast("The cottage is finished. More home tiers come later."); return; }
        Net.Instance.RequestInteract(this);
    }

    public void ServerInteract(PlayerController player)
    {
        var gs = GameState.Instance;
        var net = Net.Instance;
        if (gs.HomeTier >= 2) return;
        if (gs.Players.Values.Any(p => p.Sleeping)) { net.ToastTo(player.PeerId, "Not while someone is asleep in the shack."); return; }
        if (!Supplies.TryConsume(player, CottageCost))
        {
            net.ToastTo(player.PeerId, $"Still short (pack + stockpile): {ItemDb.FormatCost(Missing(player))}");
            return;
        }
        // Anything built where the cottage goes is packed up and its materials returned.
        foreach (var b in gs.PlacedBuildings.Where(b => InCottage(b.Position, 1.2f)).ToList())
            net.ServerRemoveBuilding(b, refund: true);
        net.ServerSetHomeTier(2);
        net.ToastAll($"{player.DisplayName} raised the cottage! Two beds, and you can build farther from home (42 m).");
        gs.SaveGame(silent: true);
    }

    /// <summary>Is a point inside the cottage footprint (plus a margin)?</summary>
    public static bool InCottage(Vector3 p, float margin = 0f) =>
        Mathf.Abs(p.X - CottageCenter.X) < CottageHalfSize.X + margin && Mathf.Abs(p.Z - CottageCenter.Z) < CottageHalfSize.Z + margin;
}
