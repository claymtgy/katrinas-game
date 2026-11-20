using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>The planning board by the shack: pay for the next home tier from the shared
/// stockpile. Tier 2 = the cottage (two beds, bigger build claim). Tier 3 = the Longhouse (four
/// beds, the widest claim yet, bunks kept for workers), from the Storm Harpy Queen's blueprint.
/// GDD §5, §11.</summary>
public partial class HomeUpgradeBoard : StaticBody3D, IInteractable, IServerInteractable
{
    public static readonly Dictionary<string, int> CottageCost = new()
    {
        ["wood"] = 40, ["stone"] = 20, ["iron_nails"] = 6, ["wolf_pelt"] = 3, ["cottage_blueprint"] = 1,
    };

    public static readonly Dictionary<string, int> LonghouseCost = new()
    {
        ["planks"] = 40, ["bricks"] = 20, ["stone"] = 30, ["iron_nails"] = 12, ["goat_hide"] = 4, ["longhouse_blueprint"] = 1,
    };

    public const int MaxTier = 3;

    /// <summary>Where the cottage stands (world space, centre and half size on the ground).</summary>
    public static readonly Vector3 CottageCenter = new(10, 0, -21);
    public static readonly Vector3 CottageHalfSize = new(3.4f, 0, 3.4f);
    /// <summary>The Longhouse stands on the same spot, longer east to west (8 × 6 m).</summary>
    public static readonly Vector3 LonghouseHalfSize = new(4.4f, 0, 3.4f);

    public static string TierName(int tier) => tier >= 3 ? "Longhouse" : tier == 2 ? "Cottage" : "Shack";

    private static Dictionary<string, int> NextCost(int tier) => tier < 2 ? CottageCost : LonghouseCost;

    public string InteractPrompt
    {
        get
        {
            var gs = GameState.Instance;
            if (gs.HomeTier >= MaxTier) return "Home: Longhouse (fully upgraded for now)";
            string next = TierName(gs.HomeTier + 1), from = TierName(gs.HomeTier).ToLowerInvariant();
            var missing = Missing();
            return missing.Count == 0
                ? $"Upgrade the {from} to a {next} ({ItemDb.FormatCost(NextCost(gs.HomeTier))})"
                : $"{next} needs: {ItemDb.FormatCost(missing)}";
        }
    }

    private static Dictionary<string, int> Missing(PlayerController p = null) =>
        Supplies.Missing(p ?? GameState.Instance.LocalPlayer, NextCost(GameState.Instance.HomeTier));

    public void Interact(PlayerController player)
    {
        if (GameState.Instance.HomeTier >= MaxTier) { Hud.Toast("The longhouse is finished. The Keep comes later."); return; }
        Net.Instance.RequestInteract(this);
    }

    public void ServerInteract(PlayerController player)
    {
        var gs = GameState.Instance;
        var net = Net.Instance;
        int tier = gs.HomeTier;
        if (tier >= MaxTier) return;
        if (gs.Players.Values.Any(p => p.Sleeping)) { net.ToastTo(player.PeerId, "Not while someone is asleep."); return; }
        var cost = NextCost(tier);
        if (!Supplies.TryConsume(player, cost))
        {
            net.ToastTo(player.PeerId, $"Still short (pack + stockpile): {ItemDb.FormatCost(Missing(player))}");
            return;
        }
        // Anything built where the new home goes is packed up and its materials returned.
        foreach (var b in gs.PlacedBuildings.Where(b => InHome(tier + 1, b.Position, 1.2f)).ToList())
            net.ServerRemoveBuilding(b, refund: true);
        net.ServerSetHomeTier(tier + 1);
        if (tier + 1 == 2)
            net.ToastAll($"{player.DisplayName} raised the cottage! Two beds, and you can build farther from home (42 m).");
        else
        {
            net.ToastAll($"{player.DisplayName} raised the Longhouse! Four beds, and you can build farther still (54 m). Its spare bunks wait for workers.");
            Journal.Fact("longhouse");
        }
        net.ServerAwardXpAll(Levels.HomeUpgrade, tier + 1 == 2 ? "raised the cottage" : "raised the Longhouse");
        gs.SaveGame(silent: true);
    }

    /// <summary>Is a point inside the cottage footprint (plus a margin)?</summary>
    public static bool InCottage(Vector3 p, float margin = 0f) => InHome(2, p, margin);

    /// <summary>Inside the footprint of the home at this tier (plus a margin)?</summary>
    public static bool InHome(int tier, Vector3 p, float margin = 0f)
    {
        var half = tier >= 3 ? LonghouseHalfSize : CottageHalfSize;
        return Mathf.Abs(p.X - CottageCenter.X) < half.X + margin && Mathf.Abs(p.Z - CottageCenter.Z) < half.Z + margin;
    }

    /// <summary>Kept clear for the next home tier (nothing may be built there).</summary>
    public static bool KeptForNextHome(Vector3 p, float margin = 0f)
    {
        int tier = GameState.Instance.HomeTier;
        return tier < MaxTier && InHome(tier + 1, p, margin);
    }
}
