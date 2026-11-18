using Godot;

namespace KatrinasGame;

/// <summary>A key cap for an action that shows the right key or button for the device in use
/// ("E" on the keyboard, "X" on an Xbox pad, "□" on a DualSense). Hidden when the device has none.</summary>
public partial class ActionCap : PanelContainer
{
    private readonly string _action;
    private readonly Label _label;

    public ActionCap() { }

    public ActionCap(string action)
    {
        _action = action;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", UiTheme.Box("slot", 14, 9, 2));
        _label = UiTheme.Label("", "Heading", HorizontalAlignment.Center);
        _label.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_label);
        Refresh();
    }

    public override void _EnterTree() => Gamepad.Changed += Refresh;
    public override void _ExitTree() => Gamepad.Changed -= Refresh;

    private void Refresh()
    {
        if (_label == null) return;
        _label.Text = Gamepad.ButtonName(_action);
        Visible = _label.Text != "";
    }
}
