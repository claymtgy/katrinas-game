using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Turns inputs into an output at a crafting station. One .tres per recipe in res://data/recipes.</summary>
[GlobalClass]
public partial class RecipeDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>Station id that can craft this ("workbench", "forge", ...).</summary>
    [Export] public string Station { get; set; } = "workbench";
    /// <summary>item id -> count</summary>
    [Export] public Godot.Collections.Dictionary Inputs { get; set; } = new();
    [Export] public string OutputItem { get; set; } = "";
    [Export] public int OutputCount { get; set; } = 1;
    [Export] public int SortOrder { get; set; } = 0;

    public Dictionary<string, int> GetInputs() => ItemDb.ToCost(Inputs);
}
