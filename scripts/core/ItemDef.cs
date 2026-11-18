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

    [ExportGroup("Food")]
    /// <summary>Health restored when eaten (F). 0 = not food.</summary>
    [Export] public float FoodHeal { get; set; } = 0f;
    /// <summary>Timed buff when eaten: "stamina" (regen +Amount×100%) or "vigor" (+Amount max health). "" = none.</summary>
    [Export] public string BuffKind { get; set; } = "";
    [Export] public float BuffAmount { get; set; } = 0f;
    /// <summary>Real seconds the buff lasts.</summary>
    [Export] public float BuffSeconds { get; set; } = 0f;

    [ExportGroup("Trade")]
    /// <summary>Coins a trader asks for one (0 = traders don't sell it).</summary>
    [Export] public int BuyPrice { get; set; } = 0;
    /// <summary>Coins a trader pays for one (0 = traders won't buy it).</summary>
    [Export] public int SellPrice { get; set; } = 0;

    public bool IsWeapon => Category == "weapon";
    public bool HasBuff => BuffKind != "" && BuffSeconds > 0;
    public bool IsFood => FoodHeal > 0 || HasBuff;

    /// <summary>"+25 max health", "Stamina regen +50%".</summary>
    public string BuffText => BuffKind switch
    {
        PlayerController.BuffVigor => $"+{BuffAmount:0} max health",
        PlayerController.BuffStamina => $"Stamina regen +{BuffAmount * 100f:0}%",
        _ => "",
    };
}
