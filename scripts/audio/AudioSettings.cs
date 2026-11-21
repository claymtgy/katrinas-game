using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Volume settings and the mixer buses they drive: Master, with Music, Effects (sound
/// effects and the interface) and Ambience (wind, rain, birds, waterfalls, fires) under it. The
/// buses are made in code at startup, so no bus layout file is needed. Kept in the [audio] section
/// of settings.cfg; scripted tests and servers never write it.</summary>
public static class AudioSettings
{
    public const string MusicBus = "Music", EffectsBus = "Effects", AmbienceBus = "Ambience";

    /// <summary>Linear volumes, 0..1.</summary>
    public static float Master = 0.8f, Music = 0.55f, Effects = 0.9f, Ambience = 0.8f;
    /// <summary>Silence the game while its window isn't focused.</summary>
    public static bool MuteUnfocused = false;
    /// <summary>Play the soundtrack at all.</summary>
    public static bool MusicOn = true;
    /// <summary>Switch to battle music when enemies hunt you.</summary>
    public static bool CombatMusic = true;

    public static bool Persist = true;
    private static bool _focused = true;

    public static void Startup()
    {
        EnsureBuses();
        var args = OS.GetCmdlineUserArgs();
        if (args.Any(a => a == "--server" || a.StartsWith("--autotest"))) Persist = false;
        else Load();
        Apply();
    }

    private static void EnsureBuses()
    {
        foreach (var name in new[] { MusicBus, EffectsBus, AmbienceBus })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, name);
            AudioServer.SetBusSend(i, "Master");
        }
    }

    private static void SetBus(string bus, float linear)
    {
        int i = AudioServer.GetBusIndex(bus);
        if (i < 0) return;
        AudioServer.SetBusMute(i, linear <= 0.001f);
        AudioServer.SetBusVolumeDb(i, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
    }

    public static void Apply()
    {
        SetBus("Master", (MuteUnfocused && !_focused) ? 0f : Master);
        SetBus(MusicBus, MusicOn ? Music : 0f);
        SetBus(EffectsBus, Effects);
        SetBus(AmbienceBus, Ambience);
    }

    /// <summary>The window gained or lost focus (GameState forwards the notification).</summary>
    public static void FocusChanged(bool focused)
    {
        _focused = focused;
        if (MuteUnfocused) Apply();
    }

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Appearance.SettingsPath) != Error.Ok) return;
        float F(string key, float def) => Mathf.Clamp((float)cfg.GetValue("audio", key, def).AsDouble(), 0f, 1f);
        Master = F("master", Master);
        Music = F("music", Music);
        Effects = F("effects", Effects);
        Ambience = F("ambience", Ambience);
        MusicOn = cfg.GetValue("audio", "music_on", MusicOn).AsBool();
        CombatMusic = cfg.GetValue("audio", "combat_music", CombatMusic).AsBool();
        MuteUnfocused = cfg.GetValue("audio", "mute_unfocused", MuteUnfocused).AsBool();
    }

    public static void Save()
    {
        if (!Persist) return;
        var cfg = new ConfigFile();
        cfg.Load(Appearance.SettingsPath); // keep the other sections
        cfg.SetValue("audio", "master", Master);
        cfg.SetValue("audio", "music", Music);
        cfg.SetValue("audio", "effects", Effects);
        cfg.SetValue("audio", "ambience", Ambience);
        cfg.SetValue("audio", "music_on", MusicOn);
        cfg.SetValue("audio", "combat_music", CombatMusic);
        cfg.SetValue("audio", "mute_unfocused", MuteUnfocused);
        cfg.Save(Appearance.SettingsPath);
    }
}
