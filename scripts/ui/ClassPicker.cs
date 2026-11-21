using System;
using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Choose your class: five cards (name, what it's good at, its abilities and the weapon
/// it starts with), then a confirm button. Shown before you enter any world (single player, hosting
/// or joining a server), and from the Esc menu to change class mid-game.</summary>
public partial class ClassPicker : Control
{
    public enum Context { BeforeSpawn, InGame }

    private readonly Action<string> _onChosen;
    private readonly Action _onCancel;
    private readonly Context _context;
    private readonly string _startClass;
    private readonly Dictionary<string, PanelContainer> _cards = new();
    private Button _confirm;
    private Label _details;

    /// <summary>The class picked so far (tests).</summary>
    public string Selected { get; private set; }

    public ClassPicker(string current, Action<string> onChosen, Action onCancel, Context context)
    {
        _startClass = PlayerClasses.IsValid(current) ? current : PlayerClasses.WarriorId;
        Selected = _startClass;
        _onChosen = onChosen;
        _onCancel = onCancel;
        _context = context;
        Name = "ClassPicker";
    }

    public ClassPicker() : this(null, null, null, Context.BeforeSpawn) { }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = UiTheme.Theme;
        AddChild(new ColorRect { Color = new Color(0.06f, 0.035f, 0.02f, 0.88f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var board = new PanelContainer();
        center.AddChild(board);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 12);
        board.AddChild(col);
        col.AddChild(UiTheme.TitlePlate(_context == Context.InGame ? "Change Class" : "Choose Your Class", out _));
        var sub = UiTheme.Label(_context == Context.InGame
            ? "Your level and gear stay with you. You'll take up the new class's weapon (the old one goes to the stockpile)."
            : "Your class sets your abilities and the weapon you start with. You can change it later from the menu (Esc).",
            "Dim", HorizontalAlignment.Center);
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sub.CustomMinimumSize = new Vector2(1000, 0);
        col.AddChild(sub);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        col.AddChild(row);
        foreach (var c in PlayerClasses.All) row.AddChild(Card(c));

        _details = UiTheme.Label("", "Small");
        _details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _details.CustomMinimumSize = new Vector2(1000, 96);
        col.AddChild(_details);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        col.AddChild(buttons);
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(130, 44) };
        back.Pressed += Cancel;
        buttons.AddChild(back);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _confirm = new Button { CustomMinimumSize = new Vector2(260, 44) };
        _confirm.AddThemeFontSizeOverride("font_size", 20);
        _confirm.Pressed += Confirm;
        buttons.AddChild(_confirm);

        Select(Selected);
        if (Gamepad.UsingPad) Callable.From(() => PickButton(_cards[Selected]).GrabFocus()).CallDeferred();
    }

    private Control Card(ClassDef c)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(192, 0) };
        _cards[c.Id] = card;
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        card.AddChild(v);

        var name = UiTheme.Label(c.Name, "Heading", HorizontalAlignment.Center);
        name.AddThemeColorOverride("font_color", c.Tint.Lightened(0.35f));
        v.AddChild(name);

        string weapon = PlayerClasses.StarterWeapon(c.Id);
        var weaponRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        weaponRow.AddThemeConstantOverride("separation", 6);
        weaponRow.AddChild(new TextureRect
        {
            Texture = UiTheme.Icon(weapon), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(48, 48),
            TooltipText = ItemDb.Get(weapon)?.Describe() ?? "",
        });
        var wl = UiTheme.Label(ItemDb.NameOf(weapon), "Small");
        wl.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        weaponRow.AddChild(wl);
        v.AddChild(weaponRow);

        var blurb = UiTheme.Label(c.Blurb, "Small");
        blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        blurb.CustomMinimumSize = new Vector2(176, 0);
        blurb.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(blurb);

        var pick = new Button { Name = "Pick", Text = c.Name, ToggleMode = true, CustomMinimumSize = new Vector2(0, 36) };
        string id = c.Id;
        pick.Pressed += () => Select(id);
        pick.FocusEntered += () => { if (Gamepad.UsingPad) Select(id); };
        v.AddChild(pick);
        // Clicking anywhere on the card picks it too.
        card.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Select(id); };
        return card;
    }

    private static Button PickButton(PanelContainer card) => card.GetChild(0).GetNode<Button>("Pick");

    public void Select(string id)
    {
        if (!PlayerClasses.IsValid(id)) return;
        Selected = id;
        var cls = PlayerClasses.Get(id);
        foreach (var (cid, card) in _cards)
        {
            bool on = cid == id;
            card.Modulate = on ? Colors.White : new Color(0.72f, 0.7f, 0.7f);
            PickButton(card).SetPressedNoSignal(on);
        }
        string weapon = PlayerClasses.StarterWeapon(id);
        string weaponText = ItemDb.Get(weapon)?.Describe().Replace("\n", " · ") ?? weapon;
        _details.Text = $"{cls.Name}: {cls.Blurb}\n" +
                        $"[R] {cls.AbilityName}: {cls.AbilityText}\n" +
                        (cls.Second is { } s ? $"[T, level {s.UnlockLevel}] {s.Name}: {s.Text}\n" : "") +
                        (cls.Third is { } t ? $"[G, level {t.UnlockLevel}] {t.Name}: {t.Text}\n" : "") +
                        $"Starts with: {weaponText}";
        _details.Modulate = cls.Tint.Lightened(0.55f);
        _confirm.Text = _context == Context.InGame
            ? (id == _startClass ? $"Stay a {cls.Name}" : $"Become a {cls.Name}")
            : $"Play as {cls.Name}";
    }

    public void Confirm()
    {
        _onChosen?.Invoke(Selected);
        QueueFree();
    }

    public void Cancel()
    {
        _onCancel?.Invoke();
        QueueFree();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel")) { Cancel(); GetViewport().SetInputAsHandled(); }
    }
}
