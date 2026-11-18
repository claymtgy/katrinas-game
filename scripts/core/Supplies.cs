using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>What a player can spend and where gathered things go (GDD §16, carried packs).
///
/// Gathered items and loot go into the player's own pack (PackCapacity items; coins weigh
/// nothing). A pack is emptied into the shared stockpile at a storage chest. Spending (building,
/// crafting, planting, eating, the cottage) takes from the pack first, then from the stockpile,
/// but the stockpile can only be reached at home (inside the land claim). Trading uses the pack
/// only: you carry goods and coins to Ashby and back.
///
/// The server decides (Give / TryConsume). Clients use Count / Has for prompts and menus with
/// their mirrored pack.</summary>
public static class Supplies
{
    public const int PackCapacity = 40;
    /// <summary>Share of each pack stack lost when a player is defeated (rounded down).</summary>
    public const float LossOnDeath = 0.5f;
    /// <summary>How far past the claim edge still counts as home for the stockpile.</summary>
    private const float HomeMargin = 6f;

    /// <summary>Never refused for being heavy (quest items).</summary>
    private static readonly HashSet<string> AlwaysFits = new() { "coin", "cottage_blueprint", "glider" };

    private static GameState Gs => GameState.Instance;

    /// <summary>Close enough to home to reach the shared stockpile.</summary>
    public static bool AtHome(PlayerController p)
    {
        if (p == null) return true;
        var d = new Vector2(p.GlobalPosition.X - Gs.ClaimCenter.X, p.GlobalPosition.Z - Gs.ClaimCenter.Z);
        return d.Length() <= Gs.ClaimRadius + HomeMargin;
    }

    /// <summary>How many this player can use right now (pack, plus the stockpile at home).</summary>
    public static int Count(PlayerController p, string id) =>
        (p?.Pack.Count(id) ?? 0) + (AtHome(p) ? Gs.Inventory.Count(id) : 0);

    public static bool Has(PlayerController p, IReadOnlyDictionary<string, int> cost) =>
        cost.All(kv => Count(p, kv.Key) >= kv.Value);

    public static Dictionary<string, int> Missing(PlayerController p, IReadOnlyDictionary<string, int> cost) =>
        cost.Where(kv => Count(p, kv.Key) < kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value - Count(p, kv.Key));

    /// <summary>Server: pay a cost, pack first. Nothing is taken unless all of it can be.</summary>
    public static bool TryConsume(PlayerController p, IReadOnlyDictionary<string, int> cost)
    {
        if (!Has(p, cost)) return false;
        foreach (var kv in cost)
        {
            int need = kv.Value;
            int fromPack = Mathf.Min(need, p?.Pack.Count(kv.Key) ?? 0);
            if (fromPack > 0) p.Pack.Remove(kv.Key, fromPack);
            need -= fromPack;
            if (need > 0) Gs.Inventory.Remove(kv.Key, need);
        }
        return true;
    }

    public static bool TryConsume(PlayerController p, string id, int amount = 1) =>
        TryConsume(p, new Dictionary<string, int> { [id] = amount });

    /// <summary>Server: gathered / looted items into a player's pack (the stockpile if nobody, e.g.
    /// a wolf killed by a fall). Returns how many fit; the rest are lost, with a warning.</summary>
    public static int Give(PlayerController p, string id, int amount)
    {
        if (amount <= 0) return 0;
        if (p == null || !GodotObject.IsInstanceValid(p))
        {
            Gs.Inventory.Add(id, amount);
            return amount;
        }
        int fits = AlwaysFits.Contains(id) || (ItemDb.Get(id)?.IsPersonal ?? false) ? amount : Mathf.Clamp(PackCapacity - p.Pack.Load, 0, amount);
        if (fits > 0) p.Pack.Add(id, fits);
        if (fits < amount)
            Net.Instance.ToastTo(p.PeerId, $"Your pack is full ({PackCapacity}). Empty it into a storage chest at home.");
        return fits;
    }

    /// <summary>Server: move everything in the pack into the shared stockpile (personal gear, like
    /// the glider and armour, stays with you).</summary>
    public static int Deposit(PlayerController p)
    {
        int moved = 0;
        foreach (var kv in p.Pack.Items.ToList())
        {
            if (ItemDb.Get(kv.Key)?.IsPersonal ?? false) continue;
            Gs.Inventory.Add(kv.Key, kv.Value);
            p.Pack.Remove(kv.Key, kv.Value);
            moved += kv.Value;
        }
        if (moved > 0) Journal.Fact("deposited");
        return moved;
    }

    /// <summary>Server: a defeated player drops part of what they carried.</summary>
    public static int LoseOnDeath(PlayerController p)
    {
        int lost = 0;
        foreach (var kv in p.Pack.Items.ToList())
        {
            if (ItemDb.Get(kv.Key)?.IsPersonal ?? false) continue; // your own gear is never lost
            int n = (int)(kv.Value * LossOnDeath);
            if (n <= 0) continue;
            p.Pack.Remove(kv.Key, n);
            lost += n;
        }
        return lost;
    }
}
