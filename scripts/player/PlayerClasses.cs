using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>One player class: passive tweaks to the shared moveset, plus one ability on R.
/// Every multiplier is 1 (or every bonus 0) for "no change". See GDD §6, Classes.</summary>
public sealed record ClassDef(
    string Id,
    string Name,
    string Blurb,
    string AbilityName,
    string AbilityText,
    float AbilityStamina,
    float AbilityCooldown,
    Color Tint,
    float BonusHealth = 0f,
    float MoveSpeed = 1f,
    float LightDamage = 1f,
    float HeavyDamage = 1f,
    float DodgeCost = 1f,
    float DodgeIFrames = 1f,
    float GuardCost = 1f,
    float StaminaRegen = 1f,
    float FoodHealing = 1f,
    float HealthRegen = 0f);

public static class PlayerClasses
{
    public const string WarriorId = "warrior", RogueId = "rogue", WizardId = "wizard", HealerId = "healer", ArcherId = "archer";

    public static readonly ClassDef Warrior = new(
        WarriorId, "Warrior",
        "Front-line fighter: +30 health, heavy blows hit 25% harder, a blocked hit costs 30% less stamina.",
        "Whirlwind", "Spin with your weapon, striking everything within 3 m.",
        AbilityStamina: 30f, AbilityCooldown: 7f, Tint: new Color(0.9f, 0.45f, 0.3f),
        BonusHealth: 30f, HeavyDamage: 1.25f, GuardCost: 0.7f);

    public static readonly ClassDef Rogue = new(
        RogueId, "Rogue",
        "Quick and slippery: 10% faster, rolls cost 40% less and dodge for longer, light attacks hit 20% harder.",
        "Vanish", "For 4 s enemies lose track of you; your next melee hit deals double damage.",
        AbilityStamina: 25f, AbilityCooldown: 14f, Tint: new Color(0.55f, 0.5f, 0.75f),
        MoveSpeed: 1.1f, LightDamage: 1.2f, DodgeCost: 0.6f, DodgeIFrames: 1.35f);

    public static readonly ClassDef Wizard = new(
        WizardId, "Wizard",
        "Fragile but deadly at range: -20 health, stamina comes back 25% faster.",
        "Firebolt", "Hurl a bolt of fire (22 damage) that bursts on impact, scorching anything within 2.5 m.",
        AbilityStamina: 16f, AbilityCooldown: 0.9f, Tint: new Color(1f, 0.55f, 0.2f),
        BonusHealth: -20f, StaminaRegen: 1.25f);

    public static readonly ClassDef Healer = new(
        HealerId, "Healer",
        "Keeps the group standing: slowly regains health out of combat, food heals 50% more.",
        "Mend", "Heal yourself and every player within 9 m for 30 health.",
        AbilityStamina: 30f, AbilityCooldown: 10f, Tint: new Color(0.45f, 0.9f, 0.55f),
        FoodHealing: 1.5f, HealthRegen: 1.5f);

    public static readonly ClassDef Archer = new(
        ArcherId, "Archer",
        "Strikes from afar. Arrows drop over distance and hit 50% harder past 15 m.",
        "Shoot", "Loose an arrow (16 damage) where you aim.",
        AbilityStamina: 8f, AbilityCooldown: 0.7f, Tint: new Color(0.95f, 0.85f, 0.45f),
        MoveSpeed: 1.04f);

    public static readonly ClassDef[] All = { Warrior, Rogue, Wizard, Healer, Archer };

    public static ClassDef Get(string id) => All.FirstOrDefault(c => c.Id == id) ?? Warrior;

    public static bool IsValid(string id) => All.Any(c => c.Id == id);
}
