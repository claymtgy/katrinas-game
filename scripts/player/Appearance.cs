using System;
using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>A player's character: their class (<see cref="PlayerClasses"/>) and how they look:
/// body (Rogue or Knight), skin, hair, outfit and trim colours, cape and helmet, and height. Belongs to the player, not the world: it's kept in
/// user://settings.cfg, sent to the server when joining (and when changed from the pause menu),
/// and mirrored to everyone. Colours are HTML hex, or "" to keep the model's own colour.
///
/// The KayKit models colour themselves from an 8 × 4 grid of gradient swatches in one texture;
/// the character shader swaps the swatches each slot uses (see <see cref="Slots"/>) for the
/// chosen colour, keeping the swatch's light-to-dark gradient.</summary>
public sealed class Appearance
{
    public const string Rogue = "rogue", Knight = "knight";
    public const float MinHeight = 0.9f, MaxHeight = 1.1f;

    /// <summary>Class id (PlayerClasses). Changing it is only allowed at home (the server checks).</summary>
    public string Class = PlayerClasses.WarriorId;
    public string Body = Rogue;
    public string Skin = "", Hair = "", Outfit = "", Trim = "";
    public bool Cape = true, Helmet;
    public float Height = 1f;

    /// <summary>False in scripted tests, so they never overwrite the player's real settings.</summary>
    public static bool Persist { get; set; } = true;

    /// <summary>This machine's own look (loaded from settings at startup).</summary>
    public static Appearance Local { get; set; } = new();

    // Presets offered by the character creator ("" = the model's own colour).
    public static readonly string[] SkinTones = { "", "#f2c4a0", "#e0a57c", "#c68a5e", "#a06a44", "#7a4e33", "#563624", "#b8d0c0" };
    public static readonly string[] HairColors = { "", "#1e1a1c", "#4a2c1e", "#8a4a26", "#c46a2c", "#e0b45a", "#e8e0d0", "#8c8c96", "#c04a6a", "#4a6ac0" };
    public static readonly string[] ClothColors = { "", "#2f7a5a", "#2c5c8c", "#5a3c8c", "#8c2c3a", "#c4642c", "#d8b048", "#e8e2d4", "#3a3a44", "#6a4a2e", "#e890a8" };

    public static readonly string[] Bodies = { Rogue, Knight };

    public string ModelPath => Body == Knight ? Models.Knight : Models.Hero;

    /// <summary>Which atlas swatches (index = column + row × 8) each slot recolours, per body.</summary>
    public static (int Skin, int Hair, int Outfit, int Trim) Slots(string body) => body == Knight
        ? (Bit(0, 0), Bit(1, 0), Bit(0, 1) | Bit(2, 2), Bit(3, 0))
        : (Bit(0, 0), Bit(1, 0), Bit(0, 1) | Bit(1, 1), Bit(3, 2) | Bit(5, 2));

    private static int Bit(int col, int row) => 1 << (col + row * 8);

    /// <summary>Model parts never shown on a player (weapons come from the game, not the model).</summary>
    public string[] HiddenParts()
    {
        var hidden = Body == Knight
            ? new[] { "1H_Sword_Offhand", "Badge_Shield", "Rectangle_Shield", "Round_Shield", "Spike_Shield", "1H_Sword", "2H_Sword" }
            : new[] { "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable" };
        string cape = Body == Knight ? "Knight_Cape" : "Rogue_Cape";
        if (!Cape) hidden = [.. hidden, cape];
        if (Body == Knight && !Helmet) hidden = [.. hidden, "Knight_Helmet"];
        return hidden;
    }

    public Appearance Clone() => (Appearance)MemberwiseClone();

    public GDict ToDict() => new()
    {
        ["class"] = Class, ["body"] = Body, ["skin"] = Skin, ["hair"] = Hair, ["outfit"] = Outfit, ["trim"] = Trim,
        ["cape"] = Cape, ["helmet"] = Helmet, ["height"] = Height,
    };

    /// <summary>Read (and sanitise: this may come from another machine) a look.</summary>
    public static Appearance FromDict(GDict d)
    {
        var a = new Appearance();
        if (d == null) return a;
        string S(string key) => d.TryGetValue(key, out var v) && v.VariantType == Variant.Type.String ? v.AsString() : "";
        a.Class = PlayerClasses.IsValid(S("class")) ? S("class") : PlayerClasses.WarriorId;
        a.Body = Array.IndexOf(Bodies, S("body")) >= 0 ? S("body") : Rogue;
        a.Skin = CleanColor(S("skin"));
        a.Hair = CleanColor(S("hair"));
        a.Outfit = CleanColor(S("outfit"));
        a.Trim = CleanColor(S("trim"));
        a.Cape = !d.TryGetValue("cape", out var cape) || cape.AsBool();
        a.Helmet = d.TryGetValue("helmet", out var helmet) && helmet.AsBool();
        a.Height = d.TryGetValue("height", out var h) ? Mathf.Clamp((float)h.AsDouble(), MinHeight, MaxHeight) : 1f;
        if (float.IsNaN(a.Height)) a.Height = 1f;
        return a;
    }

    private static string CleanColor(string s) => s.Length is > 0 and <= 9 && Color.HtmlIsValid(s) ? s : "";

    /// <summary>A random look from the presets (the creator's Randomise button).</summary>
    public static Appearance Random(RandomNumberGenerator rng) => new()
    {
        Body = Bodies[rng.RandiRange(0, Bodies.Length - 1)],
        Skin = SkinTones[rng.RandiRange(1, SkinTones.Length - 2)], // (not the green "fae" tone)
        Hair = HairColors[rng.RandiRange(1, HairColors.Length - 1)],
        Outfit = ClothColors[rng.RandiRange(1, ClothColors.Length - 1)],
        Trim = ClothColors[rng.RandiRange(1, ClothColors.Length - 1)],
        Cape = rng.Randf() < 0.7f,
        Helmet = rng.Randf() < 0.4f,
        Height = Mathf.Snapped(rng.RandfRange(MinHeight, MaxHeight), 0.01f),
    };

    // ------------------------------------------------------------------ settings (user://settings.cfg)

    public const string SettingsPath = "user://settings.cfg";

    public void SaveTo(ConfigFile cfg)
    {
        foreach (var kv in ToDict()) cfg.SetValue("look", kv.Key.AsString(), kv.Value);
    }

    public static Appearance LoadFrom(ConfigFile cfg)
    {
        if (!cfg.HasSection("look")) return new Appearance();
        var d = new GDict();
        foreach (var key in cfg.GetSectionKeys("look")) d[key] = cfg.GetValue("look", key);
        return FromDict(d);
    }

    /// <summary>Load this machine's look from settings into <see cref="Local"/>.</summary>
    public static void LoadLocal()
    {
        var cfg = new ConfigFile();
        Local = cfg.Load(SettingsPath) == Error.Ok ? LoadFrom(cfg) : new Appearance();
    }

    /// <summary>Store <see cref="Local"/> in settings, keeping everything else in the file.</summary>
    public static void SaveLocal()
    {
        if (!Persist) return;
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        Local.SaveTo(cfg);
        cfg.Save(SettingsPath);
    }
}
