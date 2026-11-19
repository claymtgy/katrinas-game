using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Window settings: display mode (windowed, borderless fullscreen, exclusive fullscreen),
/// the window size when windowed, and vsync. Kept in the [graphics] section of settings.cfg and
/// applied at startup (not on a dedicated server or in scripted tests). The UI scales with the
/// window (stretch mode canvas_items, 1600×900 base), so any size keeps the same layout.</summary>
public static class GraphicsSettings
{
    public enum DisplayMode { Windowed, Borderless, Fullscreen }

    public static DisplayMode Mode = DisplayMode.Windowed;
    public static Vector2I Resolution = new(1600, 900);
    public static bool VSync = true;

    /// <summary>Tests and servers never touch the player's window or settings file.</summary>
    public static bool Persist = true;

    private static readonly Vector2I[] Common =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 2160),
    };

    public static string ModeName(DisplayMode m) => m switch
    {
        DisplayMode.Borderless => "Borderless fullscreen",
        DisplayMode.Fullscreen => "Fullscreen (exclusive)",
        _ => "Windowed",
    };

    private static bool HasWindow => DisplayServer.GetName() != "headless";

    private static int Screen => DisplayServer.WindowGetCurrentScreen();

    /// <summary>Window sizes that fit on this monitor, plus the monitor's own size.</summary>
    public static List<Vector2I> Resolutions()
    {
        var screen = HasWindow ? DisplayServer.ScreenGetSize(Screen) : new Vector2I(1920, 1080);
        var list = Common.Where(r => r.X <= screen.X && r.Y <= screen.Y).ToList();
        if (!list.Contains(screen)) list.Add(screen);
        if (!list.Contains(Resolution)) list.Add(Resolution);
        return list.OrderBy(r => r.X).ThenBy(r => r.Y).ToList();
    }

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Appearance.SettingsPath) != Error.Ok) return;
        Mode = (DisplayMode)Mathf.Clamp(cfg.GetValue("graphics", "mode", (int)Mode).AsInt32(), 0, 2);
        Resolution = new Vector2I(
            Mathf.Max(640, cfg.GetValue("graphics", "width", Resolution.X).AsInt32()),
            Mathf.Max(360, cfg.GetValue("graphics", "height", Resolution.Y).AsInt32()));
        VSync = cfg.GetValue("graphics", "vsync", VSync).AsBool();
    }

    public static void Save()
    {
        if (!Persist) return;
        var cfg = new ConfigFile();
        cfg.Load(Appearance.SettingsPath); // keep the other sections
        cfg.SetValue("graphics", "mode", (int)Mode);
        cfg.SetValue("graphics", "width", Resolution.X);
        cfg.SetValue("graphics", "height", Resolution.Y);
        cfg.SetValue("graphics", "vsync", VSync);
        cfg.Save(Appearance.SettingsPath);
    }

    /// <summary>Put the window in the chosen mode and size.</summary>
    public static void Apply()
    {
        if (!HasWindow) return;
        DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        switch (Mode)
        {
            case DisplayMode.Borderless:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            case DisplayMode.Fullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
            default:
                if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
                    DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                // Leaving fullscreen settles over a frame: size and centre the window after it.
                Callable.From(SizeWindow).CallDeferred();
                break;
        }
    }

    private static void SizeWindow()
    {
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
        var usable = DisplayServer.ScreenGetUsableRect(Screen);
        var size = new Vector2I(Mathf.Min(Resolution.X, usable.Size.X), Mathf.Min(Resolution.Y, usable.Size.Y));
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(usable.Position + (usable.Size - size) / 2);
    }

    /// <summary>At startup: read settings.cfg and set the window up (players only, not tests).</summary>
    public static void Startup()
    {
        var args = OS.GetCmdlineUserArgs();
        if (args.Any(a => a == "--server" || a.StartsWith("--autotest")) || !HasWindow)
        {
            Persist = false;
            return;
        }
        Load();
        // Only touch the window if the player changed something from the default window.
        if (Mode != DisplayMode.Windowed || Resolution != new Vector2I(1600, 900) || !VSync) Apply();
    }
}
