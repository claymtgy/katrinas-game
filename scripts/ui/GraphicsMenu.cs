using System;
using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The graphics settings board (display mode, window size, vsync), opened from the main
/// menu and the in-game Esc menu. Changes apply at once and are saved to settings.cfg.</summary>
public partial class GraphicsMenu : Control
{
    private readonly Action _onClose;
    private OptionButton _mode, _resolution;
    private CheckBox _vsync;
    private Label _note;
    private List<Vector2I> _sizes;

    public GraphicsMenu(Action onClose)
    {
        _onClose = onClose;
        Name = "GraphicsMenu";
    }

    public GraphicsMenu() : this(null) { }

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
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
        col.AddThemeConstantOverride("separation", 12);
        board.AddChild(col);
        col.AddChild(UiTheme.TitlePlate("Graphics", out _));

        _mode = new OptionButton();
        foreach (GraphicsSettings.DisplayMode m in Enum.GetValues(typeof(GraphicsSettings.DisplayMode)))
            _mode.AddItem(GraphicsSettings.ModeName(m), (int)m);
        _mode.Selected = (int)GraphicsSettings.Mode;
        _mode.ItemSelected += i => { GraphicsSettings.Mode = (GraphicsSettings.DisplayMode)(int)i; Changed(); };
        Row(col, "Display mode", _mode);

        _resolution = new OptionButton();
        _sizes = GraphicsSettings.Resolutions();
        foreach (var s in _sizes) _resolution.AddItem($"{s.X} × {s.Y}");
        _resolution.Selected = Math.Max(0, _sizes.IndexOf(GraphicsSettings.Resolution));
        _resolution.ItemSelected += i => { GraphicsSettings.Resolution = _sizes[(int)i]; Changed(); };
        Row(col, "Window size", _resolution);

        _vsync = new CheckBox { Text = "Limit to the monitor's refresh rate", ButtonPressed = GraphicsSettings.VSync };
        _vsync.Toggled += on => { GraphicsSettings.VSync = on; Changed(); };
        Row(col, "VSync", _vsync);

        _note = UiTheme.Label("", "Dim");
        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.CustomMinimumSize = new Vector2(540, 0);
        col.AddChild(_note);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        col.AddChild(buttons);
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(130, 40) };
        back.Pressed += Close;
        buttons.AddChild(back);

        RefreshNote();
        if (Gamepad.UsingPad) Callable.From(() => _mode.GrabFocus()).CallDeferred();
    }

    private static void Row(VBoxContainer parent, string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var l = UiTheme.Label(label);
        l.CustomMinimumSize = new Vector2(170, 0);
        row.AddChild(l);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(control);
        parent.AddChild(row);
    }

    private void Changed()
    {
        GraphicsSettings.Apply();
        GraphicsSettings.Save();
        RefreshNote();
    }

    private void RefreshNote()
    {
        bool windowed = GraphicsSettings.Mode == GraphicsSettings.DisplayMode.Windowed;
        _resolution.Disabled = !windowed;
        _note.Text = windowed
            ? "The menus and HUD scale with the window, so any size keeps the same layout."
            : "Fullscreen uses your monitor's own resolution. Switch to Windowed to pick a window size.";
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
