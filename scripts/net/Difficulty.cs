using Godot;

namespace KatrinasGame;

/// <summary>Co-op difficulty scaling (GDD §16). Small animals come in bigger packs;
/// big monsters get more health and damage instead of more copies.
/// Every enemy spawner must go through these. Server-side only.</summary>
public static class Difficulty
{
    private static ServerConfig Cfg => Net.Instance?.Config ?? new ServerConfig();

    /// <summary>Connected players (at least 1, so an empty dedicated server behaves like solo).</summary>
    public static int Players => Mathf.Max(1, GameState.Instance?.Players.Count ?? 1);

    /// <summary>How many small animals (wolves, boars...) to spawn for a pack designed for one player.</summary>
    public static int PackSize(int soloSize, int players = -1) =>
        Mathf.Max(1, Mathf.RoundToInt(soloSize * (1f + Cfg.SmallCreaturesPerExtraPlayer * (Extra(players)))));

    /// <summary>Health of a big monster (bandit captain, elite, boss) designed for one player.</summary>
    public static float BigMonsterHealth(float soloHealth, int players = -1) =>
        soloHealth * (1f + Cfg.BigMonsterHealthPerExtraPlayer * Extra(players));

    /// <summary>Damage of a big monster designed for one player.</summary>
    public static float BigMonsterDamage(float soloDamage, int players = -1) =>
        soloDamage * (1f + Cfg.BigMonsterDamagePerExtraPlayer * Extra(players));

    private static int Extra(int players) => Mathf.Max(0, (players > 0 ? players : Players) - 1);
}
