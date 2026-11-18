using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Server options. A dedicated server reads them from server.cfg (created with defaults
/// the first time); a host gets them from the main menu. See GDD §16.</summary>
public class ServerConfig
{
    public const int DefaultPort = 24570;
    /// <summary>The design and balance target. More is allowed through max_players, untested.</summary>
    public const int DesignedMaxPlayers = 4;
    public const int HardMaxPlayers = 32;

    public string Name = "Ember & Blossom Server";
    public int Port = DefaultPort;
    public int MaxPlayers = DesignedMaxPlayers;
    public string Password = "";
    public string World = "home";
    public bool Pvp = false;
    public float AutosaveMinutes = 5f;
    public bool PauseWhenEmpty = true;
    /// <summary>Night wolf packs and (later) other hostile creatures. Off = peaceful building.</summary>
    public bool Enemies = true;

    // Difficulty scaling per extra player (GDD §16).
    public float SmallCreaturesPerExtraPlayer = 0.5f;
    public float BigMonsterHealthPerExtraPlayer = 0.35f;
    public float BigMonsterDamagePerExtraPlayer = 0.15f;

    public static string DefaultPath => "user://server.cfg";

    /// <summary>Loads the file at path, writing a commented default file first if it doesn't exist.</summary>
    public static ServerConfig LoadOrCreate(string path)
    {
        var cfg = new ServerConfig();
        if (!FileAccess.FileExists(path))
        {
            cfg.Save(path);
            GD.Print($"[Server] Wrote default config to {ProjectSettings.GlobalizePath(path)}");
            return cfg;
        }

        var file = new ConfigFile();
        var err = file.Load(path);
        if (err != Error.Ok)
        {
            GD.PushError($"[Server] Could not read {path} ({err}); using defaults");
            return cfg;
        }
        cfg.Name = file.GetValue("server", "name", cfg.Name).AsString();
        cfg.Port = file.GetValue("server", "port", cfg.Port).AsInt32();
        cfg.MaxPlayers = file.GetValue("server", "max_players", cfg.MaxPlayers).AsInt32();
        cfg.Password = file.GetValue("server", "password", cfg.Password).AsString();
        cfg.World = file.GetValue("server", "world", cfg.World).AsString();
        cfg.Pvp = file.GetValue("server", "pvp", cfg.Pvp).AsBool();
        cfg.AutosaveMinutes = (float)file.GetValue("server", "autosave_minutes", cfg.AutosaveMinutes).AsDouble();
        cfg.PauseWhenEmpty = file.GetValue("server", "pause_when_empty", cfg.PauseWhenEmpty).AsBool();
        cfg.Enemies = file.GetValue("difficulty", "enemies", cfg.Enemies).AsBool();
        cfg.SmallCreaturesPerExtraPlayer = (float)file.GetValue("difficulty", "small_creatures_per_extra_player", cfg.SmallCreaturesPerExtraPlayer).AsDouble();
        cfg.BigMonsterHealthPerExtraPlayer = (float)file.GetValue("difficulty", "big_monster_health_per_extra_player", cfg.BigMonsterHealthPerExtraPlayer).AsDouble();
        cfg.BigMonsterDamagePerExtraPlayer = (float)file.GetValue("difficulty", "big_monster_damage_per_extra_player", cfg.BigMonsterDamagePerExtraPlayer).AsDouble();
        return cfg; // Net clamps when the server starts
    }

    public void Clamp()
    {
        MaxPlayers = Mathf.Clamp(MaxPlayers, 1, HardMaxPlayers);
        Port = Mathf.Clamp(Port, 1024, 65535);
        if (string.IsNullOrWhiteSpace(World)) World = "home";
        // Saves go to user://saves/<world>.json: keep the name file-safe.
        World = new string(World.StripEdges().Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
        if (MaxPlayers > DesignedMaxPlayers)
            GD.PushWarning($"[Server] max_players={MaxPlayers}: the game is designed and balanced for {DesignedMaxPlayers}.");
    }

    public void Save(string path)
    {
        var file = new ConfigFile();
        file.SetValue("server", "name", Name);
        file.SetValue("server", "port", Port);
        file.SetValue("server", "max_players", MaxPlayers);
        file.SetValue("server", "password", Password);
        file.SetValue("server", "world", World);
        file.SetValue("server", "pvp", Pvp);
        file.SetValue("server", "autosave_minutes", AutosaveMinutes);
        file.SetValue("server", "pause_when_empty", PauseWhenEmpty);
        file.SetValue("difficulty", "enemies", Enemies);
        file.SetValue("difficulty", "small_creatures_per_extra_player", SmallCreaturesPerExtraPlayer);
        file.SetValue("difficulty", "big_monster_health_per_extra_player", BigMonsterHealthPerExtraPlayer);
        file.SetValue("difficulty", "big_monster_damage_per_extra_player", BigMonsterDamagePerExtraPlayer);
        file.Save(path);
    }
}
