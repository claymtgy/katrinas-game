using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Controls and camera settings: mouse and stick sensitivity, inverted look, field of
/// view, camera shake and the one-time tips. Kept in the [gameplay] section of settings.cfg; read
/// live by the camera rig, so changes apply at once. Tests and servers never write it.</summary>
public static class GameplaySettings
{
    /// <summary>Multipliers on the default speeds (1 = default).</summary>
    public static float MouseSensitivity = 1f, StickSensitivity = 1f;
    public static bool InvertY = false;
    /// <summary>Vertical field of view, degrees.</summary>
    public static float Fov = 75f;
    public const float MinFov = 60f, MaxFov = 100f;
    public static bool CameraShake = true;
    public static bool ShowTips = true;

    public static bool Persist = true;

    public static void Startup()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Any(a => a == "--server" || a.StartsWith("--autotest"))) { Persist = false; return; }
        Load();
        Hud.TipsEnabled = ShowTips;
    }

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Appearance.SettingsPath) != Error.Ok) return;
        MouseSensitivity = Mathf.Clamp((float)cfg.GetValue("gameplay", "mouse_sensitivity", MouseSensitivity).AsDouble(), 0.1f, 4f);
        StickSensitivity = Mathf.Clamp((float)cfg.GetValue("gameplay", "stick_sensitivity", StickSensitivity).AsDouble(), 0.1f, 4f);
        InvertY = cfg.GetValue("gameplay", "invert_y", InvertY).AsBool();
        Fov = Mathf.Clamp((float)cfg.GetValue("gameplay", "fov", Fov).AsDouble(), MinFov, MaxFov);
        CameraShake = cfg.GetValue("gameplay", "camera_shake", CameraShake).AsBool();
        ShowTips = cfg.GetValue("gameplay", "show_tips", ShowTips).AsBool();
    }

    public static void Save()
    {
        Hud.TipsEnabled = ShowTips;
        if (!Persist) return;
        var cfg = new ConfigFile();
        cfg.Load(Appearance.SettingsPath); // keep the other sections
        cfg.SetValue("gameplay", "mouse_sensitivity", MouseSensitivity);
        cfg.SetValue("gameplay", "stick_sensitivity", StickSensitivity);
        cfg.SetValue("gameplay", "invert_y", InvertY);
        cfg.SetValue("gameplay", "fov", Fov);
        cfg.SetValue("gameplay", "camera_shake", CameraShake);
        cfg.SetValue("gameplay", "show_tips", ShowTips);
        cfg.Save(Appearance.SettingsPath);
    }
}
