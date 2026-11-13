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
            Items[item.Id] = item;

        Recipes.AddRange(LoadAll<RecipeDef>("res://data/recipes").OrderBy(r => r.SortOrder));
        Buildings.AddRange(LoadAll<BuildingDef>("res://data/buildings").OrderBy(b => b.SortOrder));

        GD.Print($"[ItemDb] {Items.Count} items, {Recipes.Count} recipes, {Buildings.Count} buildings");
    }

    public static ItemDef Get(string id) => id != null && Items.TryGetValue(id, out var d) ? d : null;

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
