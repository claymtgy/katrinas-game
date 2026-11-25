using System.Collections.Generic;
using System.Linq;
using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>Armour and other worn gear (GDD §6, "Armour, gear and enchantments").
///
/// A player wears up to one piece per <see cref="Slots"/> slot (PlayerController.Worn, mirrored on
/// every machine). Pieces are ordinary items with a Slot (ItemDef); an enchanted piece is the
/// same item with "@enchantment" on its id ("leather_cap@thorns"), so packs, saving and the
/// network carry it like any other item. Everything here is a pure function of what's worn.</summary>
public static class Gear
{
    public const string Head = "head", Body = "body", Hands = "hands", Feet = "feet", Cloak = "cloak", Charm = "charm", OffHand = "offhand", Bow = "bow";
    public static readonly string[] Slots = { Head, Body, Hands, Feet, Cloak, Charm, OffHand, Bow };
    /// <summary>The armour slots (everything but the off hand).</summary>
    public static readonly string[] ArmourSlots = { Head, Body, Hands, Feet, Cloak, Charm };

    /// <summary>Armour A cuts damage by A / (A + ArmorScale).</summary>
    public const float ArmorScale = 40f;
    public const float HeavyRegenPenalty = 0.05f;

    // Perks on the base items (ItemDef.Perk / PerkAmount).
    public const string PerkGrip = "grip", PerkRegen = "regen", PerkFeather = "feather", PerkMight = "might", PerkVigor = "vigor", PerkGuard = "guard", PerkHeatward = "heatward";

    /// <summary>A shield (perk "guard", amount = share off the guard's stamina cost) also widens the parry window by this.</summary>
    public const float ShieldParryBonus = 0.12f;

    /// <summary>Enchanting at a Rune Altar.</summary>
    public const string AltarStation = "rune_altar";
    public const string ShardId = "rune_shard";
    public const int ShardCost = 2, CoinCost = 10;

    public sealed record Enchantment(string Id, string Name, string Suffix, string Text);

    public const string Ward = "ward", Vigor = "vigor", Swift = "swift", Feather = "feather", Thorns = "thorns", Leech = "leech";
    public static readonly Enchantment[] Enchantments =
    {
        new(Ward, "Warding", "of Warding", "+4 armour"),
        new(Vigor, "Vigor", "of Vigor", "+12 max health"),
        new(Swift, "Swiftness", "of Swiftness", "+5% move speed"),
        new(Feather, "the Feather", "of the Feather", "−30% fall damage"),
        new(Thorns, "Thorns", "of Thorns", "Melee attackers take 20% of the damage they dealt you"),
        new(Leech, "Leeching", "of Leeching", "Heal 6% of the melee damage you deal"),
    };

    public static Enchantment GetEnchantment(string id) => Enchantments.FirstOrDefault(e => e.Id == id);

    public static string SlotName(string slot) => slot switch
    {
        Head => "Head", Body => "Body", Hands => "Hands", Feet => "Feet", Cloak => "Cloak", Charm => "Charm", OffHand => "Off hand", Bow => "Bow", _ => slot,
    };

    // ------------------------------------------------------------------ ids

    /// <summary>"leather_cap@thorns" → ("leather_cap", "thorns").</summary>
    public static (string Base, string Enchant) Split(string id)
    {
        if (string.IsNullOrEmpty(id)) return ("", "");
        int at = id.IndexOf('@');
        return at < 0 ? (id, "") : (id[..at], id[(at + 1)..]);
    }

    public static string Enchanted(string id, string enchant) => $"{Split(id).Base}@{enchant}";

    // ------------------------------------------------------------------ weapons

    /// <summary>Every archer has at least this bow (a new archer is handed one).</summary>
    public const string StarterBow = "hunting_bow";

    /// <summary>The bow an archer shoots with: the one worn in the bow slot, else the starter bow's
    /// numbers (so an old save's archer, or one who took theirs off, still shoots as before).</summary>
    public static ItemDef BowOf(PlayerController p) =>
        p != null && p.Worn.TryGetValue(Bow, out var id) && ItemDb.Get(id) is { } d ? d : ItemDb.Get(StarterBow);

    /// <summary>How strong the held weapon makes class spells and heals (a staff's firebolt and
    /// meteor, a mace's Mend): ItemDef.Power, 1 with no weapon.</summary>
    public static float AbilityPower(PlayerController p) =>
        p != null && p.Equipped != "" && ItemDb.Get(p.Equipped) is { } d && d.Power > 0f ? d.Power : 1f;

    // ------------------------------------------------------------------ stats

    private static IEnumerable<ItemDef> WornDefs(PlayerController p) =>
        p == null ? Enumerable.Empty<ItemDef>() : p.Worn.Values.Select(ItemDb.Get).Where(d => d != null);

    public static int EnchantCount(PlayerController p, string enchant) => WornDefs(p).Count(d => d.Enchant == enchant);

    public static float PerkTotal(PlayerController p, string perk) => WornDefs(p).Where(d => d.Perk == perk).Sum(d => d.PerkAmount);

    public static bool HasPerk(PlayerController p, string perk) => WornDefs(p).Any(d => d.Perk == perk);

    public static float Armor(PlayerController p) => WornDefs(p).Sum(d => d.Armor) + 4f * EnchantCount(p, Ward)
        + (Talents.Has(p, Talents.IronSkin) ? Talents.IronSkinArmor : 0f);

    public static float Reduction(PlayerController p)
    {
        float a = Armor(p);
        return a <= 0 ? 0f : a / (a + ArmorScale);
    }

    public static float MaxHealthBonus(PlayerController p) => 12f * EnchantCount(p, Vigor) + PerkTotal(p, PerkVigor);

    public static float MoveSpeedMult(PlayerController p) => 1f + 0.05f * EnchantCount(p, Swift);

    public static float StaminaRegenMult(PlayerController p) =>
        (1f + PerkTotal(p, PerkRegen)) * Mathf.Max(0.5f, 1f - HeavyRegenPenalty * WornDefs(p).Count(d => d.Heavy));

    public static float FallDamageMult(PlayerController p) =>
        Mathf.Max(0f, 1f - PerkTotal(p, PerkFeather)) * Mathf.Pow(0.7f, EnchantCount(p, Feather));

    /// <summary>Climbing stamina cost multiplier, and whether wet rock is no worse than dry (grip boots).</summary>
    public static (float Cost, bool IgnoreWet) Climb(PlayerController p)
    {
        float grip = PerkTotal(p, PerkGrip);
        return (Mathf.Max(0.2f, 1f - grip), grip > 0);
    }

    public static float MeleeDamageMult(PlayerController p) => 1f + PerkTotal(p, PerkMight);

    /// <summary>Shield: blocks cost this much of their usual stamina.</summary>
    public static float GuardCostMult(PlayerController p) => Mathf.Max(0.3f, 1f - PerkTotal(p, PerkGuard));

    /// <summary>Shield: extra seconds in which a raised guard parries.</summary>
    public static float ParryBonus(PlayerController p) => HasPerk(p, PerkGuard) ? ShieldParryBonus : 0f;

    /// <summary>How fast the Cinder Foothills' heat builds (an Ember-glass Charm slows it).</summary>
    public static float HeatGainMult(PlayerController p) => Mathf.Max(0.3f, 1f - PerkTotal(p, PerkHeatward));

    public static float ThornsShare(PlayerController p) => 0.2f * EnchantCount(p, Thorns);

    public static float LeechShare(PlayerController p) => 0.06f * EnchantCount(p, Leech);

    /// <summary>A short summary for the character sheet.</summary>
    public static List<string> Summary(PlayerController p)
    {
        var lines = new List<string>();
        float armor = Armor(p);
        lines.Add($"Armour {armor:0}  (−{Reduction(p) * 100f:0}% damage)");
        float hp = MaxHealthBonus(p);
        if (hp > 0) lines.Add($"+{hp:0} max health");
        float move = MoveSpeedMult(p) - 1f;
        if (move > 0.001f) lines.Add($"+{move * 100f:0}% move speed");
        float regen = StaminaRegenMult(p) - 1f;
        if (Mathf.Abs(regen) > 0.001f) lines.Add($"{(regen > 0 ? "+" : "−")}{Mathf.Abs(regen) * 100f:0}% stamina regen");
        float fall = 1f - FallDamageMult(p);
        if (fall > 0.001f) lines.Add($"−{fall * 100f:0}% fall damage");
        var (climb, dry) = Climb(p);
        if (climb < 0.999f) lines.Add($"Climbing costs {(1f - climb) * 100f:0}% less{(dry ? ", even wet" : "")}");
        float might = MeleeDamageMult(p) - 1f;
        if (might > 0.001f) lines.Add($"+{might * 100f:0}% melee damage");
        if (HasPerk(p, PerkGuard)) lines.Add($"Blocks cost {(1f - GuardCostMult(p)) * 100f:0}% less, parry window +{ShieldParryBonus * 1000f:0} ms");
        if (ThornsShare(p) > 0) lines.Add($"Thorns {ThornsShare(p) * 100f:0}%");
        if (LeechShare(p) > 0) lines.Add($"Leeching {LeechShare(p) * 100f:0}%");
        if (HeatGainMult(p) < 0.999f) lines.Add($"Heat builds {(1f - HeatGainMult(p)) * 100f:0}% slower");
        return lines;
    }

    // ------------------------------------------------------------------ looks

    /// <summary>How a player looks with their gear on: body armour recolours the outfit and trim,
    /// head armour puts on a helmet (or a cap on the Rogue), a cloak puts the cape on.</summary>
    public static Appearance Dress(Appearance look, IReadOnlyDictionary<string, string> worn)
    {
        var a = look.Clone();
        a.HeadGear = "";
        string Hex(Color c) => "#" + c.ToHtml(false);
        if (worn.TryGetValue(Body, out var body) && ItemDb.Get(body) is { } b)
        {
            if (b.WornColor.A > 0) a.Outfit = Hex(b.WornColor);
            if (b.WornTrim.A > 0) a.Trim = Hex(b.WornTrim);
        }
        if (worn.TryGetValue(Head, out var head) && ItemDb.Get(head) is { } h)
        {
            if (a.Body == Appearance.Knight) a.Helmet = true;
            else a.HeadGear = Hex(h.WornColor.A > 0 ? h.WornColor : h.IconColor);
        }
        if (worn.TryGetValue(Cloak, out var cloak) && ItemDb.Get(cloak) is { } c)
        {
            a.Cape = true;
            if (c.WornColor.A > 0 && a.Body == Appearance.Knight) a.Outfit = Hex(c.WornColor);
            a.CapeColor = c.WornColor.A > 0 ? Hex(c.WornColor) : "";
        }
        a.Shield = worn.TryGetValue(OffHand, out var shield) && ItemDb.Get(shield) is { } sh ? Hex(sh.WornColor.A > 0 ? sh.WornColor : sh.IconColor) : "";
        return a;
    }

    // ------------------------------------------------------------------ wire format

    public static GDict ToDict(IReadOnlyDictionary<string, string> worn)
    {
        var d = new GDict();
        foreach (var kv in worn) d[kv.Key] = kv.Value;
        return d;
    }

    /// <summary>Read (and sanitise: it may come from another machine or an old save) worn gear.</summary>
    public static Dictionary<string, string> FromDict(GDict d)
    {
        var worn = new Dictionary<string, string>();
        if (d == null) return worn;
        foreach (var kv in d)
        {
            string slot = kv.Key.AsString(), id = kv.Value.AsString();
            if (Slots.Contains(slot) && ItemDb.Get(id) is { } def && def.Slot == slot) worn[slot] = id;
        }
        return worn;
    }
}
