using System;
using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The options board, opened from the main menu and the in-game Esc menu, with three
/// pages: Audio (master, music, effects and ambience volumes, soundtrack switches, mute in the
/// background), Graphics (display mode, window size, vsync) and Gameplay (mouse and stick
/// sensitivity, invert look, field of view, camera shake, tips). Changes apply at once and are
/// saved to settings.cfg.</summary>
public partial class OptionsMenu : Control
{
    public enum Page { Audio, Graphics, Gameplay }

    private readonly Action _onClose;
    private readonly Page _startPage;
    private readonly Dictionary<Page, Control> _pages = new();
    private readonly Dictionary<Page, Button> _tabs = new();
    private OptionButton _mode, _resolution;
    private Label _graphicsNote;
    private List<Vector2I> _sizes;

    public Page Showing { get; private set; }

    public OptionsMenu(Action onClose, Page start = Page.Audio)
    {
        _onClose = onClose;
        _startPage = start;
        Name = "OptionsMenu";
    }

    public OptionsMenu() : this(null) { }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = UiTheme.Theme;
        AddChild(new ColorRect { Color = new Color(0.06f, 0.035f, 0.02f, 0.75f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var board = new PanelContainer();
        center.AddChild(board);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        col.AddThemeConstantOverride("separation", 12);
        board.AddChild(col);
        col.AddChild(UiTheme.TitlePlate("Options", out _));

        var tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tabs.AddThemeConstantOverride("separation", 8);
        col.AddChild(tabs);
        var group = new ButtonGroup();
        foreach (Page page in Enum.GetValues(typeof(Page)))
        {
            var tab = new Button { Text = page.ToString(), ToggleMode = true, ButtonGroup = group, CustomMinimumSize = new Vector2(150, 38) };
            var p = page;
            tab.Pressed += () => ShowPage(p);
            tabs.AddChild(tab);
            _tabs[page] = tab;
        }
        col.AddChild(UiTheme.Divider(600));

        var stack = new VBoxContainer { CustomMinimumSize = new Vector2(620, 330) };
        col.AddChild(stack);
        _pages[Page.Audio] = BuildAudio();
        _pages[Page.Graphics] = BuildGraphics();
        _pages[Page.Gameplay] = BuildGameplay();
        foreach (var page in _pages.Values) stack.AddChild(page);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        col.AddChild(buttons);
        var reset = new Button { Text = "Defaults", CustomMinimumSize = new Vector2(130, 40), TooltipText = "Put this page back to its defaults" };
        reset.Pressed += ResetPage;
        buttons.AddChild(reset);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(130, 40) };
        back.Pressed += Close;
        buttons.AddChild(back);

        ShowPage(_startPage);
    }

    public void ShowPage(Page page)
    {
        Showing = page;
        foreach (var (p, c) in _pages) c.Visible = p == page;
        _tabs[page].ButtonPressed = true;
        if (Gamepad.UsingPad) Callable.From(() => _tabs[page].GrabFocus()).CallDeferred();
    }

    // ------------------------------------------------------------------ pages

    private Control BuildAudio()
    {
        var page = Page_();
        Slider(page, "Master volume", AudioSettings.Master, v => { AudioSettings.Master = v; AudioChanged(); });
        Slider(page, "Music", AudioSettings.Music, v => { AudioSettings.Music = v; AudioChanged(); });
        Slider(page, "Sound effects", AudioSettings.Effects, v => { AudioSettings.Effects = v; AudioChanged(); }, preview: "ui_click");
        Slider(page, "Ambience", AudioSettings.Ambience, v => { AudioSettings.Ambience = v; AudioChanged(); });
        Check(page, "Soundtrack", "Play music", AudioSettings.MusicOn, on => { AudioSettings.MusicOn = on; AudioChanged(); });
        Check(page, "Battle music", "Switch to battle music when enemies hunt you", AudioSettings.CombatMusic, on => { AudioSettings.CombatMusic = on; AudioChanged(); });
        Check(page, "In background", "Mute the game when its window isn't focused", AudioSettings.MuteUnfocused, on => { AudioSettings.MuteUnfocused = on; AudioChanged(); });
        var now = UiTheme.Label("", "Dim");
        page.AddChild(now);
        // Show what's playing (handy for the credits in assets/audio/music/CREDITS.md).
        var timer = new Timer { WaitTime = 1, Autostart = true };
        timer.Timeout += () => now.Text = Music.Instance is { CurrentTrack: not "" } m
            ? $"Now playing: {Pretty(m.CurrentTrack)} (CC0 music from OpenGameArt)" : "";
        page.AddChild(timer);
        return page;
    }

    private static string Pretty(string file)
    {
        string name = file.GetBaseName();
        int us = name.IndexOf('_');
        if (us >= 0) name = name[(us + 1)..];
        return string.Join(' ', Array.ConvertAll(name.Split('_'), w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    private Control BuildGraphics()
    {
        var page = Page_();
        _mode = new OptionButton();
        foreach (GraphicsSettings.DisplayMode m in Enum.GetValues(typeof(GraphicsSettings.DisplayMode)))
            _mode.AddItem(GraphicsSettings.ModeName(m), (int)m);
        _mode.Selected = (int)GraphicsSettings.Mode;
        _mode.ItemSelected += i => { GraphicsSettings.Mode = (GraphicsSettings.DisplayMode)(int)i; GraphicsChanged(); };
        Row(page, "Display mode", _mode);

        _resolution = new OptionButton();
        _sizes = GraphicsSettings.Resolutions();
        foreach (var s in _sizes) _resolution.AddItem($"{s.X} × {s.Y}");
        _resolution.Selected = Math.Max(0, _sizes.IndexOf(GraphicsSettings.Resolution));
        _resolution.ItemSelected += i => { GraphicsSettings.Resolution = _sizes[(int)i]; GraphicsChanged(); };
        Row(page, "Window size", _resolution);

        Check(page, "VSync", "Limit to the monitor's refresh rate", GraphicsSettings.VSync, on => { GraphicsSettings.VSync = on; GraphicsChanged(); });

        _graphicsNote = UiTheme.Label("", "Dim");
        _graphicsNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _graphicsNote.CustomMinimumSize = new Vector2(600, 0);
        page.AddChild(_graphicsNote);
        RefreshGraphicsNote();
        return page;
    }

    private Control BuildGameplay()
    {
        var page = Page_();
        Slider(page, "Mouse sensitivity", GameplaySettings.MouseSensitivity, v => { GameplaySettings.MouseSensitivity = v; GameplaySettings.Save(); },
            min: 0.2f, max: 3f, format: v => $"{v:0.00}×");
        Slider(page, "Stick sensitivity", GameplaySettings.StickSensitivity, v => { GameplaySettings.StickSensitivity = v; GameplaySettings.Save(); },
            min: 0.2f, max: 3f, format: v => $"{v:0.00}×");
        Check(page, "Invert look", "Push up to look down", GameplaySettings.InvertY, on => { GameplaySettings.InvertY = on; GameplaySettings.Save(); });
        Slider(page, "Field of view", GameplaySettings.Fov, v => { GameplaySettings.Fov = v; GameplaySettings.Save(); },
            min: GameplaySettings.MinFov, max: GameplaySettings.MaxFov, step: 1f, format: v => $"{v:0}°");
        Check(page, "Camera shake", "Shake the camera when you're hit", GameplaySettings.CameraShake, on => { GameplaySettings.CameraShake = on; GameplaySettings.Save(); });
        Check(page, "Tips", "Show one-time tips as new things come up", GameplaySettings.ShowTips, on => { GameplaySettings.ShowTips = on; GameplaySettings.Save(); });
        return page;
    }

    // ------------------------------------------------------------------ apply

    private static void AudioChanged()
    {
        AudioSettings.Apply();
        AudioSettings.Save();
    }

    private void GraphicsChanged()
    {
        GraphicsSettings.Apply();
        GraphicsSettings.Save();
        RefreshGraphicsNote();
    }

    private void RefreshGraphicsNote()
    {
        bool windowed = GraphicsSettings.Mode == GraphicsSettings.DisplayMode.Windowed;
        _resolution.Disabled = !windowed;
        _graphicsNote.Text = windowed
            ? "The menus and HUD scale with the window, so any size keeps the same layout."
            : "Fullscreen uses your monitor's own resolution. Switch to Windowed to pick a window size.";
    }

    private void ResetPage()
    {
        switch (Showing)
        {
            case Page.Audio:
                AudioSettings.Master = 0.8f; AudioSettings.Music = 0.55f; AudioSettings.Effects = 0.9f; AudioSettings.Ambience = 0.8f;
                AudioSettings.MusicOn = true; AudioSettings.CombatMusic = true; AudioSettings.MuteUnfocused = false;
                AudioChanged();
                break;
            case Page.Graphics:
                GraphicsSettings.Mode = GraphicsSettings.DisplayMode.Windowed;
                GraphicsSettings.Resolution = new Vector2I(1600, 900);
                GraphicsSettings.VSync = true;
                GraphicsSettings.Apply();
                GraphicsSettings.Save();
                break;
            case Page.Gameplay:
                GameplaySettings.MouseSensitivity = 1f; GameplaySettings.StickSensitivity = 1f; GameplaySettings.InvertY = false;
                GameplaySettings.Fov = 75f; GameplaySettings.CameraShake = true; GameplaySettings.ShowTips = true;
                GameplaySettings.Save();
                break;
        }
        // Rebuild the page so its controls show the defaults.
        var old = _pages[Showing];
        var parent = old.GetParent();
        int at = old.GetIndex();
        var fresh = Showing switch { Page.Audio => BuildAudio(), Page.Graphics => BuildGraphics(), _ => BuildGameplay() };
        parent.AddChild(fresh);
        parent.MoveChild(fresh, at);
        old.QueueFree();
        _pages[Showing] = fresh;
    }

    // ------------------------------------------------------------------ widgets

    private static VBoxContainer Page_()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 12);
        return page;
    }

    private static void Row(VBoxContainer parent, string label, Control control, Control after = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var l = UiTheme.Label(label);
        l.CustomMinimumSize = new Vector2(190, 0);
        row.AddChild(l);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);
        if (after != null) row.AddChild(after);
        parent.AddChild(row);
    }

    private static void Check(VBoxContainer parent, string label, string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Text = text, ButtonPressed = on };
        box.Toggled += v => changed(v);
        Row(parent, label, box);
    }

    private static void Slider(VBoxContainer parent, string label, float value, Action<float> changed,
        float min = 0f, float max = 1f, float step = 0.01f, Func<float, string> format = null, string preview = null)
    {
        format ??= v => $"{v * 100:0}%";
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 24),
        };
        var readout = UiTheme.Label(format(value));
        readout.CustomMinimumSize = new Vector2(70, 0);
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        slider.ValueChanged += v =>
        {
            readout.Text = format((float)v);
            changed((float)v);
        };
        if (preview != null) slider.DragEnded += _ => Sfx.Ui(preview);
        Row(parent, label, slider, readout);
    }

    public void Close()
    {
        _onClose?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel")) { Close(); GetViewport().SetInputAsHandled(); }
    }
}
