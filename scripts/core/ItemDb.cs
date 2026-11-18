using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Loads every item / recipe / building definition from res://data at startup.</summary>
public static class ItemDb
{
    public static readonly Dictionary<string, ItemDef> Items = new();
    public static readonly List<RecipeDef> Recipes = new();
    public static readonly List<BuildingDef> Buildings = new();

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        foreach (var item in LoadAll<ItemDef>("res://data/items"))
        {
            item.BaseId = item.Id;
            Items[item.Id] = item;
        }

        Recipes.AddRange(LoadAll<RecipeDef>("res://data/recipes").OrderBy(r => r.SortOrder));
        Buildings.AddRange(LoadAll<BuildingDef>("res://data/buildings").OrderBy(b => b.SortOrder));

        GD.Print($"[ItemDb] {Items.Count} items, {Recipes.Count} recipes, {Buildings.Count} buildings");
    }

    private static readonly Dictionary<string, ItemDef> Variants = new();

    /// <summary>An item by id. "base@enchant" ids (Gear) are enchanted copies of the base item,
    /// made on first use and kept apart from <see cref="Items"/> (so traders don't list them).</summary>
    public static ItemDef Get(string id)
    {
        if (id == null) return null;
        if (Items.TryGetValue(id, out var d)) return d;
        if (Variants.TryGetValue(id, out d)) return d;
        var (baseId, enchant) = Gear.Split(id);
        if (enchant == "" || !Items.TryGetValue(baseId, out var b) || !b.IsWearable || Gear.GetEnchantment(enchant) is not { } e) return null;
        var v = (ItemDef)b.Duplicate();
        v.Id = id;
        v.BaseId = baseId;
        v.Enchant = enchant;
        v.DisplayName = $"{b.DisplayName} {e.Suffix}";
        v.Icon = b.Icon == "" ? baseId : b.Icon;
        v.BuyPrice = 0;
        v.SellPrice = b.SellPrice + 10;
        Variants[id] = v;
        return v;
    }

    public static string NameOf(string id) => Get(id)?.DisplayName ?? id;

    public static BuildingDef GetBuilding(string id) => Buildings.FirstOrDefault(b => b.Id == id);

    public static Dictionary<string, int> ToCost(Godot.Collections.Dictionary dict)
    {
        var result = new Dictionary<string, int>();
        if (dict == null) return result;
        foreach (var kv in dict)
            result[kv.Key.AsString()] = kv.Value.AsInt32();
        return result;
    }

    public static string FormatCost(Dictionary<string, int> cost) =>
        string.Join(", ", cost.Select(kv => $"{kv.Value} {NameOf(kv.Key)}"));

    private static IEnumerable<T> LoadAll<T>(string dir) where T : Resource
    {
        if (!DirAccess.DirExistsAbsolute(dir)) yield break;
        var seen = new HashSet<string>();
        foreach (var file in DirAccess.GetFilesAt(dir))
        {
            // Exported builds rename resources to *.tres.remap.
            var name = file.EndsWith(".remap") ? file[..^".remap".Length] : file;
            if (!name.EndsWith(".tres") || !seen.Add(name)) continue;
            var res = GD.Load<T>($"{dir}/{name}");
            if (res != null) yield return res;
            else GD.PushWarning($"[ItemDb] Could not load {dir}/{name} as {typeof(T).Name}");
        }
    }
}
