using Godot;

namespace KatrinasGame;

/// <summary>The level curve and what each XP source is worth (GDD §6, *Levels and abilities*).
///
/// A player's progress is one number, their total XP; the level is worked out from it, so there
/// is nothing to keep in step. The server awards XP (Net.Xp.cs), saves it with the character, and
/// tells every machine (levels show on nameplates).</summary>
public static class Levels
{
    public const int MaxLevel = 20;

    /// <summary>Every level past the first: +2 max health and +1 max stamina.</summary>
    public const float HealthPerLevel = 2f, StaminaPerLevel = 1f;

    /// <summary>Everyone within this range of a kill gets full XP for it (nothing is split).</summary>
    public const float KillShareRadius = 60f;

    // ---------------------------------------------------------------- XP sources

    public const int FirstKill = 50;
    public const int Boss = 200;
    public const int SanctuaryLit = 150;
    public const int RaidBeaten = 100;
    public const int MainGoal = 60, SideGoal = 40, Errand = 30;
    public const int FirstCraft = 20;
    public const int HomeUpgrade = 250;

    /// <summary>XP for killing an enemy again, by Enemy.Kind. A boss counts as its kind only the
    /// first time; after that it's worth a bandit.</summary>
    public static int RepeatKill(string kind) => kind switch
    {
        "wolf" => 4,
        "harpy" => 5,
        "bandit" or "captain" => 6,
        "raider" => 8,
        _ => 4,
    };

    public static bool IsBoss(string kind) => kind == "captain";

    /// <summary>A friendly name for the toast ("+50 XP: first wolf").</summary>
    public static string KindName(string kind) => kind switch
    {
        "captain" => "bandit captain",
        _ => kind,
    };

    // ---------------------------------------------------------------- the curve

    /// <summary>XP needed to go from level n to n+1: about 120 × n^1.4.</summary>
    public static int ToNext(int level) =>
        level >= MaxLevel ? 0 : Mathf.RoundToInt(120f * Mathf.Pow(level, 1.4f));

    /// <summary>Total XP at which a level starts (level 1 starts at 0).</summary>
    public static int StartOf(int level)
    {
        int total = 0;
        for (int n = 1; n < Mathf.Min(level, MaxLevel); n++) total += ToNext(n);
        return total;
    }

    public static int LevelFor(int totalXp)
    {
        int level = 1, need = 0;
        while (level < MaxLevel && totalXp >= need + ToNext(level))
        {
            need += ToNext(level);
            level++;
        }
        return level;
    }

    /// <summary>(XP into this level, XP this level needs); (0, 0) at the cap.</summary>
    public static (int Into, int Need) Progress(int totalXp)
    {
        int level = LevelFor(totalXp);
        if (level >= MaxLevel) return (0, 0);
        return (totalXp - StartOf(level), ToNext(level));
    }
}
