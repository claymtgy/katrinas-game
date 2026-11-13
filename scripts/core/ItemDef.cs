using Godot;

namespace KatrinasGame;

/// <summary>An item type: raw resource, refined good or weapon. One .tres per item in res://data/items.</summary>
[GlobalClass]
public partial class ItemDef : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    /// <summary>"resource", "material", "weapon", "food"...</summary>
    [Export] public string Category { get; set; } = "resource";
    [Export] public Color IconColor { get; set; } = Colors.White;

    [ExportGroup("Weapon")]
    [Export] public float Damage { get; set; } = 0f;
    [Export] public float Reach { get; set; } = 1.8f;
    /// <summary>Seconds between light attacks.</summary>
    [Export] public float AttackCooldown { get; set; } = 0.5f;
    /// <summary>Size of the greybox weapon mesh held in hand.</summary>
    [Export] public Vector3 WeaponSize { get; set; } = new(0.08f, 0.08f, 0.9f);

    public bool IsWeapon => Category == "weapon";
}
