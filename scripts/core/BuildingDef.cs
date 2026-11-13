using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>A placeable building. One .tres per building in res://data/buildings.</summary>
[GlobalClass]
public partial class BuildingDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Description { get; set; } = "";
    [Export] public PackedScene Scene { get; set; }
    /// <summary>item id -> count</summary>
    [Export] public Godot.Collections.Dictionary Cost { get; set; } = new();
    /// <summary>Bounding box (meters) used for overlap checks while placing.</summary>
    [Export] public Vector3 Footprint { get; set; } = Vector3.One;
    [Export] public int SortOrder { get; set; } = 0;

    public Dictionary<string, int> GetCost() => ItemDb.ToCost(Cost);
}
