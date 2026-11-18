using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace KatrinasGame;

/// <summary>Controller support (Xbox, PlayStation / DualSense, and anything Godot's SDL mapping
/// knows): the joypad bindings for every action, which device was used last (so prompts show the
/// right buttons: "X", "□" or "E"), right-stick look, click-to-sprint, trigger edges and rumble.
///
/// Layout (Xbox names / PlayStation names):
///   Left stick move (click: sprint) · Right stick look (click: first / third person)
///   A/✕ jump · B/○ dodge · X/□ use · Y/△ class ability
///   RT/R2 attack · RB/R1 heavy attack · LT/L2 block · LB/L1 eat
///   D-pad ◀ ▶ weapons · ▲ build mode · ▼ character sheet & journal
///   Menu/Options: menu · View/Create: help
///   In build mode: RT place, LT exit, RB rotate, D-pad ◀ ▶ change building.
///   In menus: D-pad or left stick move, A/✕ choose, B/○ back, LB/RB (L1/R1) switch tabs.</summary>
public partial class Gamepad : Node
{
    public static Gamepad Instance { get; private set; }

    public enum Style { Keyboard, Xbox, PlayStation }

    /// <summary>The last input came from a controller.</summary>
    public static bool UsingPad { get; private set; }
    /// <summary>Which button names to show.</summary>
    public static Style Current => UsingPad ? _padStyle : Style.Keyboard;
    /// <summary>Prompts must change (keyboard ↔ controller, or another kind of controller).</summary>
    public static event Action Changed;

    /// <summary>Right-stick look speed, radians a second at full tilt.</summary>
    public static float LookSpeedYaw = 3.4f, LookSpeedPitch = 2.3f;
    /// <summary>Clicked the left stick: sprint until you stop moving.</summary>
    public static bool SprintLatched { get; private set; }

    private static Style _padStyle = Style.Xbox;
    private static int _device;
    private static readonly Dictionary<string, bool> AxisDown = new();
    private static readonly Dictionary<string, InputEvent> AxisEdge = new();

    public override void _EnterTree() => Instance = this;

    // ================================================================== bindings

    /// <summary>Add the controller events to the actions (GameState made the keyboard ones).</summary>
    public static void RegisterBindings()
    {
        Axis("move_left", JoyAxis.LeftX, -1); Axis("move_right", JoyAxis.LeftX, 1);
        Axis("move_forward", JoyAxis.LeftY, -1); Axis("move_back", JoyAxis.LeftY, 1);
        Axis("look_left", JoyAxis.RightX, -1); Axis("look_right", JoyAxis.RightX, 1);
        Axis("look_up", JoyAxis.RightY, -1); Axis("look_down", JoyAxis.RightY, 1);
        foreach (var a in new[] { "move_left", "move_right", "move_forward", "move_back", "look_left", "look_right", "look_up", "look_down" })
            InputMap.ActionSetDeadzone(a, 0.18f);

        Button("jump", JoyButton.A);
        Button("dodge", JoyButton.B);
        Button("interact", JoyButton.X);
        Button("ability", JoyButton.Y);
        Axis("attack", JoyAxis.TriggerRight, 1);
        Axis("block", JoyAxis.TriggerLeft, 1);
        InputMap.ActionSetDeadzone("attack", 0.45f);
        InputMap.ActionSetDeadzone("block", 0.45f);
        Button("heavy_attack", JoyButton.RightShoulder);
        Button("rotate_build", JoyButton.RightShoulder);
        Button("eat", JoyButton.LeftShoulder);
        Button("tab_prev", JoyButton.LeftShoulder);
        Button("tab_next", JoyButton.RightShoulder);
        Button("toggle_view", JoyButton.RightStick);
        Button("pad_sprint", JoyButton.LeftStick);
        Button("prev_weapon", JoyButton.DpadLeft);
        Button("next_weapon", JoyButton.DpadRight);
        Button("prev_build", JoyButton.DpadLeft);
        Button("next_build", JoyButton.DpadRight);
        Button("build_mode", JoyButton.DpadUp);
        Button("inventory", JoyButton.DpadDown);
        Button("menu", JoyButton.Start);
        Button("help", JoyButton.Back);
        Button("help", JoyButton.Touchpad); // DualSense: the touchpad click

        // Menus: the left stick moves the focus too (the d-pad, A and B are Godot's defaults).
        Axis("ui_left", JoyAxis.LeftX, -1); Axis("ui_right", JoyAxis.LeftX, 1);
        Axis("ui_up", JoyAxis.LeftY, -1); Axis("ui_down", JoyAxis.LeftY, 1);
        Button("ui_accept", JoyButton.A);
        Button("ui_cancel", JoyButton.B);
    }

    private static void Ensure(string action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
    }

    private static void Button(string action, JoyButton button)
    {
        Ensure(action);
        foreach (var e in InputMap.ActionGetEvents(action))
            if (e is InputEventJoypadButton b && b.ButtonIndex == button) return;
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button, Device = -1 });
    }

    private static void Axis(string action, JoyAxis axis, float sign)
    {
        Ensure(action);
        foreach (var e in InputMap.ActionGetEvents(action))
            if (e is InputEventJoypadMotion m && m.Axis == axis && Mathf.Sign(m.AxisValue) == Mathf.Sign(sign)) return;
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = sign, Device = -1 });
    }

    // ================================================================== edges

    /// <summary>Did this event just press the action? Like InputEvent.IsActionPressed, except that a
    /// trigger or stick held down sends a stream of motion events: only the first counts, so a held
    /// trigger is one swing, not one per frame. Several handlers may ask about the same event.</summary>
    public static bool Pressed(InputEvent e, string action)
    {
        if (e is not InputEventJoypadMotion) return e.IsActionPressed(action);
        if (!e.IsAction(action)) return false;
        bool down = e.IsActionPressed(action);
        AxisDown.TryGetValue(action, out bool was);
        if (down && !was)
        {
            AxisDown[action] = true;
            AxisEdge[action] = e;
            return true;
        }
        if (!down) AxisDown[action] = false;
        return down && AxisEdge.GetValueOrDefault(action) == e;
    }

    // ================================================================== device tracking

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            case InputEventJoypadButton { Pressed: true } jb:
                SetDevice(true, jb.Device);
                break;
            case InputEventJoypadMotion jm when Mathf.Abs(jm.AxisValue) > 0.5f:
                SetDevice(true, jm.Device);
                break;
            case InputEventKey { Pressed: true }:
            case InputEventMouseButton { Pressed: true }:
                SetDevice(false, _device);
                break;
            case InputEventMouseMotion mm when mm.Relative.LengthSquared() > 36f:
                SetDevice(false, _device);
                break;
        }
        if (e.IsActionPressed("pad_sprint")) SprintLatched = !SprintLatched;
    }

    /// <summary>Tests: pretend the last input came from a controller (or not).</summary>
    public static void SetDevice(bool pad, int device, Style? style = null)
    {
        var before = Current;
        bool wasPad = UsingPad;
        UsingPad = pad;
        if (pad)
        {
            _device = device;
            _padStyle = style ?? StyleOf(device);
        }
        if (Current != before || wasPad != UsingPad)
        {
            Changed?.Invoke();
        }
    }

    private static Style StyleOf(int device)
    {
        string name = Input.GetJoyName(device).ToLowerInvariant();
        var info = Input.GetJoyInfo(device);
        bool sony = info.TryGetValue("vendor_id", out var vendor) && vendor.AsInt32() == 0x054C;
        if (sony || name.Contains("ps4") || name.Contains("ps5") || name.Contains("dualsense") || name.Contains("dualshock")
            || name.Contains("sony") || name.Contains("playstation") || name == "wireless controller")
            return Style.PlayStation;
        return Style.Xbox;
    }

    public override void _Process(double delta)
    {
        // Sprint stays on until you let go of the stick.
        if (SprintLatched && Input.GetVector("move_left", "move_right", "move_forward", "move_back").LengthSquared() < 0.04f)
            SprintLatched = false;
    }

    /// <summary>Right stick, with a curve so small tilts aim finely: radians to turn this frame.</summary>
    public static Vector2 LookDelta(float dt)
    {
        var v = Input.GetVector("look_left", "look_right", "look_up", "look_down");
        if (v == Vector2.Zero) return Vector2.Zero;
        float mag = v.Length();
        v = v / mag * (mag * mag);
        return new Vector2(v.X * LookSpeedYaw, v.Y * LookSpeedPitch) * dt;
    }

    /// <summary>Shake the controller (does nothing on keyboard).</summary>
    public static void Rumble(float weak, float strong, float seconds)
    {
        if (!UsingPad) return;
        Input.StartJoyVibration(_device, Mathf.Clamp(weak, 0, 1), Mathf.Clamp(strong, 0, 1), seconds);
    }

    // ================================================================== prompts

    private static readonly Dictionary<string, (string Key, string Xbox, string Ps)> Names = new()
    {
        ["move"] = ("WASD", "L-stick", "L-stick"),
        ["look"] = ("Mouse", "R-stick", "R-stick"),
        ["jump"] = ("Space", "A", "✕"),
        ["sprint"] = ("Shift", "L-stick click", "L3"),
        ["dodge"] = ("Ctrl", "B", "○"),
        ["interact"] = ("E", "X", "□"),
        ["ability"] = ("R", "Y", "△"),
        ["attack"] = ("LMB", "RT", "R2"),
        ["heavy_attack"] = ("Q", "RB", "R1"),
        ["block"] = ("RMB", "LT", "L2"),
        ["eat"] = ("F", "LB", "L1"),
        ["toggle_view"] = ("V", "R-stick click", "R3"),
        ["build_mode"] = ("B", "D-pad ▲", "D-pad ▲"),
        ["inventory"] = ("I", "D-pad ▼", "D-pad ▼"),
        ["journal"] = ("J", "D-pad ▼", "D-pad ▼"),
        ["weapons"] = ("1–5", "D-pad ◀ ▶", "D-pad ◀ ▶"),
        ["next_build"] = ("Wheel", "D-pad ◀ ▶", "D-pad ◀ ▶"),
        ["rotate_build"] = ("R", "RB", "R1"),
        ["place"] = ("LMB", "RT", "R2"),
        ["exit_build"] = ("RMB", "LT", "L2"),
        ["help"] = ("F1", "View", "Create"),
        ["menu"] = ("Esc", "Menu", "Options"),
        ["back"] = ("Esc", "B", "○"),
        ["accept"] = ("Click", "A", "✕"),
        ["tabs"] = ("Tab buttons", "LB / RB", "L1 / R1"),
        ["player_list"] = ("Tab", "", ""),
        ["save"] = ("F5 / F9", "", ""),
    };

    /// <summary>The key or button for an action, for the device in use ("E", "X" or "□").</summary>
    public static string ButtonName(string action)
    {
        if (!Names.TryGetValue(action, out var n)) return action;
        return Current switch { Style.Xbox => n.Xbox, Style.PlayStation => n.Ps, _ => n.Key };
    }

    private static readonly Regex Token = new(@"\{([a-z_]+)\}");

    /// <summary>Replace {action} tokens with the key or button: "Press {interact}" → "Press E".</summary>
    public static string Format(string text) => Token.Replace(text, m => Names.ContainsKey(m.Groups[1].Value) ? ButtonName(m.Groups[1].Value) : m.Value);
}
