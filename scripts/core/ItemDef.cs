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
    /// <summary>A tool as well as a weapon: "axe" chops trees, "pick" mines rock and ore.</summary>
    [Export] public string ToolKind { get; set; } = "";
    /// <summary>With the right tool, each swing does this many drops' worth to a resource node.</summary>
    [Export] public float ToolPower { get; set; } = 0f;
    /// <summary>Class spells and heals while held (a staff's firebolt, a mace's Mend): × this.</summary>
    [Export] public float Power { get; set; } = 1f;

    [ExportGroup("Bow")]
    /// <summary>A bow (Slot "bow", worn): Damage is per arrow, AttackCooldown the draw between
    /// shots, and arrows fly this many times the base speed (flatter, further).</summary>
    [Export] public float ArrowSpeed { get; set; } = 1f;

    [ExportGroup("Food")]
    /// <summary>Health restored when eaten (F). 0 = not food.</summary>
    [Export] public float FoodHeal { get; set; } = 0f;
    /// <summary>Timed buff when eaten: "stamina" (regen +Amount×100%) or "vigor" (+Amount max health). "" = none.</summary>
    [Export] public string BuffKind { get; set; } = "";
    [Export] public float BuffAmount { get; set; } = 0f;
    /// <summary>Real seconds the buff lasts.</summary>
    [Export] public float BuffSeconds { get; set; } = 0f;

    [ExportGroup("Armour")]
    /// <summary>Where it's worn (Gear.Slots: head, body, hands, feet, cloak, charm). "" = not wearable.</summary>
    [Export] public string Slot { get; set; } = "";
    [Export] public float Armor { get; set; } = 0f;
    /// <summary>Metal: slows stamina regeneration a little (Gear.HeavyRegenPenalty).</summary>
    [Export] public bool Heavy { get; set; } = false;
    /// <summary>A built-in bonus (Gear.Perk*: grip, regen, feather, might, vigor) and its size.</summary>
    [Export] public string Perk { get; set; } = "";
    [Export] public float PerkAmount { get; set; } = 0f;
    [Export(PropertyHint.MultilineText)] public string PerkText { get; set; } = "";
    /// <summary>Colours it gives the body when worn (alpha 0 = leave the player's own).</summary>
    [Export] public Color WornColor { get; set; } = new(0, 0, 0, 0);
    [Export] public Color WornTrim { get; set; } = new(0, 0, 0, 0);

    [ExportGroup("Trade")]
    /// <summary>Coins a trader asks for one (0 = traders don't sell it).</summary>
    [Export] public int BuyPrice { get; set; } = 0;
    /// <summary>Coins a trader pays for one (0 = traders won't buy it).</summary>
    [Export] public int SellPrice { get; set; } = 0;

    /// <summary>The enchantment on this copy (an "id@enchant" variant, see ItemDb.Get), or "".</summary>
    public string Enchant { get; set; } = "";
    /// <summary>The item this is an enchanted copy of (its own id if it isn't one).</summary>
    public string BaseId { get; set; } = "";
    /// <summary>Which picture to show: res://assets/ui/icons/&lt;Icon&gt;.png ("" = the base id).</summary>
    [Export] public string Icon { get; set; } = "";

    public bool IsWeapon => Category == "weapon";
    public bool IsTool => ToolKind != "";
    public bool IsWearable => Slot != "";
    /// <summary>Stays with its owner: crafted into their pack, kept at a chest, never lost on defeat.</summary>
    public bool IsPersonal => Category is "gear" or "armor" or "bow";
    public bool IsBow => Slot == Gear.Bow;
    public bool HasBuff => BuffKind != "" && BuffSeconds > 0;
    public bool IsFood => FoodHeal > 0 || HasBuff;

    /// <summary>Several lines for a tooltip: what it is and what it does.</summary>
    public string Describe()
    {
        var lines = new System.Collections.Generic.List<string> { DisplayName };
        if (IsBow)
        {
            lines.Add($"Bow · {Damage:0} per arrow · a shot every {AttackCooldown:0.##} s" + (ArrowSpeed > 1.05f ? " · flat, fast arrows" : ArrowSpeed < 0.95f ? " · slower arrows" : ""));
            lines.Add("Archers shoot with it (wear it in the bow slot)");
            if (Gear.GetEnchantment(Enchant) is { } e) lines.Add($"Enchanted: {e.Text}");
        }
        else if (IsWearable)
        {
            lines.Add($"{Gear.SlotName(Slot)} armour" + (Armor > 0 ? $" · {Armor:0} armour" : "") + (Heavy ? " · heavy" : ""));
            if (PerkText != "") lines.Add(PerkText);
            if (Gear.GetEnchantment(Enchant) is { } e) lines.Add($"Enchanted: {e.Text}");
        }
        else if (IsTool) lines.Add($"{(ToolKind == "axe" ? "Axe: chops trees" : "Pickaxe: mines rock and ore")} · ×{ToolPower:0.#} per swing · {Damage:0} damage");
        else if (IsWeapon)
        {
            lines.Add($"Weapon · {Damage:0} damage · {Reach:0.0} m reach");
            if (Power > 1.001f) lines.Add($"Spells and heals +{(Power - 1f) * 100f:0}%");
        }
        else if (IsFood) lines.Add($"Food" + (FoodHeal > 0 ? $" · heals {FoodHeal:0}" : "") + (HasBuff ? $" · {BuffText}" : ""));
        else if (Category == "gear" && Id == "glider") lines.Add("Gear · Space in the air to glide");
        if (SellPrice > 0) lines.Add($"Sells for {SellPrice} coins");
        return string.Join('\n', lines);
    }

    /// <summary>"+25 max health", "Stamina regen +50%".</summary>
    public string BuffText => BuffKind switch
    {
        PlayerController.BuffVigor => $"+{BuffAmount:0} max health",
        PlayerController.BuffStamina => $"Stamina regen +{BuffAmount * 100f:0}%",
        _ => "",
    };
}
