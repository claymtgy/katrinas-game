using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>One talent: a class passive you pick at <see cref="Level"/> (GDD §6, *Levels and abilities*).</summary>
public sealed record TalentDef(string Id, string ClassId, int Level, string Name, string Text);

/// <summary>Talents: at levels 3, 8 and 15 each class picks one of two passives. A pick is remembered
/// per class (changing class and back keeps it). The first pick at a level can be made anywhere;
/// changing it later is free but only at home, like changing class. Picks are saved with the
/// character and every machine knows them (some apply where you play: fall damage, sprinting,
/// parry stamina; the rest on the server).</summary>
public static class Talents
{
    public static readonly int[] Levels = { 3, 8, 15 };

    // Ids (used in code to check for a talent).
    public const string Bulwark = "bulwark", Brute = "brute", IronSkin = "iron_skin", Cleave = "cleave";
    public const string LightFooted = "light_footed", Pickpocket = "pickpocket", Ambush = "ambush", Fleet = "fleet";
    public const string Kindling = "kindling", Focus = "focus", Pyromancer = "pyromancer", Glacial = "glacial";
    public const string Herbalist = "herbalist", SteadyHand = "steady_hand", Lifebloom = "lifebloom", Lifeline = "lifeline";
    public const string LongDraw = "long_draw", QuickNock = "quick_nock", Piercing = "piercing", Fleetfoot = "fleetfoot";
    // Level 15: each class has one that keeps the Cinder Foothills' heat off.
    public const string Emberhide = "emberhide", Unbreakable = "unbreakable", Ashwalker = "ashwalker", Phantom = "phantom";
    public const string Fireborn = "fireborn", Starfall = "starfall", Wellspring = "wellspring", Evergreen = "evergreen";
    public const string CinderRunner = "cinder_runner", ArrowStorm = "arrow_storm";

    // Numbers.
    public const float BulwarkStamina = 10f, BruteHeavy = 1.15f, IronSkinArmor = 5f, CleaveRadius = 1f;
    public const float LightFootedSafeFall = 8f, PickpocketCoins = 1.5f, AmbushSeconds = 2f, FleetSprint = 0.7f;
    public const float KindlingRadius = 1f, FocusStamina = 4f, PyromancerDamage = 6f, GlacialSeconds = 2f;
    public const float HerbalistChance = 1f / 3f, SteadyHandCooldown = 2f, LifebloomHeal = 15f, LifelineSpeed = 2f, LifelineHealth = 0.5f;
    public const float LongDrawRange = 10f, QuickNockCooldown = 0.15f, FleetfootSpeed = 1.06f;
    /// <summary>The heat talents: heat builds this much as fast.</summary>
    public const float HeatTalentGain = 0.7f;
    public const float UnbreakableBlock = 0.7f, PhantomCooldown = 4f, StarfallCooldown = 6f, EvergreenCooldown = 6f, ArrowStormCooldown = 3f;

    public static readonly TalentDef[] All =
    {
        new(Bulwark, PlayerClasses.WarriorId, 3, "Bulwark", "A clean parry gives back 10 more stamina."),
        new(Brute, PlayerClasses.WarriorId, 3, "Brute", "Heavy attacks hit 15% harder."),
        new(IronSkin, PlayerClasses.WarriorId, 8, "Iron Skin", "+5 armour, whatever you wear."),
        new(Cleave, PlayerClasses.WarriorId, 8, "Cleave", "Whirlwind reaches 1 m further."),

        new(LightFooted, PlayerClasses.RogueId, 3, "Light-footed", "No fall damage from drops under 8 m."),
        new(Pickpocket, PlayerClasses.RogueId, 3, "Pickpocket", "Bandits you defeat drop 50% more coin."),
        new(Ambush, PlayerClasses.RogueId, 8, "Ambush", "Vanish lasts 2 s longer."),
        new(Fleet, PlayerClasses.RogueId, 8, "Fleet", "Sprinting costs 30% less stamina."),

        new(Kindling, PlayerClasses.WizardId, 3, "Kindling", "Firebolt's burst scorches 1 m further."),
        new(Focus, PlayerClasses.WizardId, 3, "Focus", "Firebolt costs 4 less stamina."),
        new(Pyromancer, PlayerClasses.WizardId, 8, "Pyromancer", "Firebolt's direct hit does 6 more damage."),
        new(Glacial, PlayerClasses.WizardId, 8, "Glacial", "Frost Nova's slow lasts 2 s longer."),

        new(Herbalist, PlayerClasses.HealerId, 3, "Herbalist", "Cooking makes an extra meal 1 time in 3."),
        new(SteadyHand, PlayerClasses.HealerId, 3, "Steady Hand", "Mend's cooldown is 2 s shorter."),
        new(Lifebloom, PlayerClasses.HealerId, 8, "Lifebloom", "Mend heals 15 more."),
        new(Lifeline, PlayerClasses.HealerId, 8, "Lifeline", "You revive a downed friend twice as fast, and they get up with half their health."),

        new(LongDraw, PlayerClasses.ArcherId, 3, "Long Draw", "The long-shot bonus starts at 10 m, not 15."),
        new(QuickNock, PlayerClasses.ArcherId, 3, "Quick Nock", "Shoot's cooldown is 0.15 s shorter."),
        new(Piercing, PlayerClasses.ArcherId, 8, "Piercing", "Your arrows go straight through armour."),
        new(Fleetfoot, PlayerClasses.ArcherId, 8, "Fleetfoot", "6% faster on your feet."),

        new(Emberhide, PlayerClasses.WarriorId, 15, "Emberhide", "The Foothills' heat builds 30% slower."),
        new(Unbreakable, PlayerClasses.WarriorId, 15, "Unbreakable", "Blows you block do 30% less damage."),
        new(Ashwalker, PlayerClasses.RogueId, 15, "Ashwalker", "The Foothills' heat builds 30% slower."),
        new(Phantom, PlayerClasses.RogueId, 15, "Phantom", "Shadowstep's cooldown is 4 s shorter."),
        new(Fireborn, PlayerClasses.WizardId, 15, "Fireborn", "The Foothills' heat builds 30% slower."),
        new(Starfall, PlayerClasses.WizardId, 15, "Starfall", "Meteor's cooldown is 6 s shorter."),
        new(Wellspring, PlayerClasses.HealerId, 15, "Wellspring", "The Foothills' heat builds 30% slower."),
        new(Evergreen, PlayerClasses.HealerId, 15, "Evergreen", "Blossom Ward's cooldown is 6 s shorter."),
        new(CinderRunner, PlayerClasses.ArcherId, 15, "Cinder-runner", "The Foothills' heat builds 30% slower."),
        new(ArrowStorm, PlayerClasses.ArcherId, 15, "Arrow Storm", "Volley's cooldown is 3 s shorter."),
    };

    /// <summary>Has one of the level-15 heat talents.</summary>
    public static bool HeatTalent(PlayerController p) =>
        Has(p, Emberhide) || Has(p, Ashwalker) || Has(p, Fireborn) || Has(p, Wellspring) || Has(p, CinderRunner);

    public static TalentDef Get(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>The two choices a class has at a talent level.</summary>
    public static IEnumerable<TalentDef> Choices(string classId, int level) => All.Where(t => t.ClassId == classId && t.Level == level);

    public static string Key(string classId, int level) => $"{classId}:{level}";

    /// <summary>Does this player have this talent working right now (picked, for their class, and
    /// they're at or past its level)?</summary>
    public static bool Has(PlayerController p, string id)
    {
        if (p == null) return false;
        var def = Get(id);
        return def != null && def.ClassId == p.Class.Id && p.Level >= def.Level
               && p.TalentPicks.TryGetValue(Key(def.ClassId, def.Level), out var pick) && pick == id;
    }

    /// <summary>Talent levels this player has reached for their class with nothing picked yet.</summary>
    public static int Unpicked(PlayerController p) =>
        p == null ? 0 : Levels.Count(l => p.Level >= l && !p.TalentPicks.ContainsKey(Key(p.Class.Id, l)));

    /// <summary>An ability with this player's talents applied (cost, cooldown).</summary>
    public static AbilityDef Tune(PlayerController p, AbilityDef def)
    {
        if (def == null || p == null) return def;
        if (def.Id == PlayerClasses.WizardId + "_1" && Has(p, Focus)) def = def with { Stamina = def.Stamina - FocusStamina };
        if (def.Id == PlayerClasses.HealerId + "_1" && Has(p, SteadyHand)) def = def with { Cooldown = def.Cooldown - SteadyHandCooldown };
        if (def.Id == PlayerClasses.ArcherId + "_1" && Has(p, QuickNock)) def = def with { Cooldown = def.Cooldown - QuickNockCooldown };
        if (def.Id == PlayerClasses.ShadowstepId && Has(p, Phantom)) def = def with { Cooldown = Mathf.Max(1f, def.Cooldown - PhantomCooldown) };
        if (def.Id == PlayerClasses.MeteorId && Has(p, Starfall)) def = def with { Cooldown = Mathf.Max(1f, def.Cooldown - StarfallCooldown) };
        if (def.Id == PlayerClasses.BlossomWardId && Has(p, Evergreen)) def = def with { Cooldown = Mathf.Max(1f, def.Cooldown - EvergreenCooldown) };
        if (def.Id == PlayerClasses.VolleyId && Has(p, ArrowStorm)) def = def with { Cooldown = Mathf.Max(1f, def.Cooldown - ArrowStormCooldown) };
        return def;
    }

    // ---------------------------------------------------------------- saving / sending

    public static Godot.Collections.Dictionary ToDict(Dictionary<string, string> picks)
    {
        var d = new Godot.Collections.Dictionary();
        foreach (var kv in picks) d[kv.Key] = kv.Value;
        return d;
    }

    /// <summary>Picks from a saved or sent dictionary; anything that isn't a real talent at its
    /// slot is dropped.</summary>
    public static Dictionary<string, string> FromDict(Godot.Collections.Dictionary d)
    {
        var picks = new Dictionary<string, string>();
        if (d == null) return picks;
        foreach (var kv in d)
        {
            var def = Get(kv.Value.AsString());
            if (def != null && Key(def.ClassId, def.Level) == kv.Key.AsString()) picks[kv.Key.AsString()] = def.Id;
        }
        return picks;
    }
}
