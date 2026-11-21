using System;
using Godot;

namespace KatrinasGame;

/// <summary>Pick a world to play: every saved world (day, your level and class, when it was last
/// played) with Play and Delete, plus a new world with a name of your choosing. Used for single
/// player and for hosting.</summary>
public partial class SaveSlotsMenu : Control
{
    private readonly string _title, _player;
    private readonly Action<string> _onPick;
    private readonly Action _onCancel;
    private VBoxContainer _list;
    private LineEdit _newName;
    private Label _status;

    public SaveSlotsMenu(string title, string player, Action<string> onPick, Action onCancel)
    {
        _title = title;
        _player = player;
        _onPick = onPick;
        _onCancel = onCancel;
        Name = "SaveSlotsMenu";
    }

    public SaveSlotsMenu() : this("Worlds", "Player", null, null) { }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        Theme = UiTheme.Theme;
        AddChild(new ColorRect { Color = new Color(0.06f, 0.035f, 0.02f, 0.85f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var board = new PanelContainer();
        center.AddChild(board);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        col.AddThemeConstantOverride("separation", 12);
        board.AddChild(col);
        col.AddChild(UiTheme.TitlePlate(_title, out _));

        col.AddChild(new Label { Text = "Saved worlds", ThemeTypeVariation = "Heading" });
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(740, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        col.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);

        col.AddChild(UiTheme.Divider(720));
        col.AddChild(new Label { Text = "Start a new world", ThemeTypeVariation = "Heading" });
        var newRow = new HBoxContainer();
        newRow.AddThemeConstantOverride("separation", 10);
        col.AddChild(newRow);
        newRow.AddChild(UiTheme.Label("Name"));
        _newName = new LineEdit { Text = SaveSlots.FreshName(), SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 40 };
        _newName.TextSubmitted += _ => CreateNew();
        newRow.AddChild(_newName);
        var create = new Button { Text = "New world", CustomMinimumSize = new Vector2(160, 40) };
        create.Pressed += CreateNew;
        newRow.AddChild(create);

        _status = UiTheme.Label("", "Dim");
        col.AddChild(_status);

        var buttons = new HBoxContainer();
        col.AddChild(buttons);
        var back = new Button { Text = "Back", CustomMinimumSize = new Vector2(130, 40) };
        back.Pressed += Cancel;
        buttons.AddChild(back);

        Refresh();
        if (Gamepad.UsingPad) Callable.From(() => Hud.FocusFirst(this)).CallDeferred();
    }

    private void Refresh()
    {
        foreach (var c in _list.GetChildren()) c.QueueFree();
        var slots = SaveSlots.List(_player);
        if (slots.Count == 0)
        {
            _list.AddChild(UiTheme.Label("No saved worlds yet. Start a new one below.", "Dim"));
            return;
        }
        foreach (var slot in slots)
        {
            var panel = new PanelContainer { ThemeTypeVariation = "Plate" };
            _list.AddChild(panel);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            panel.AddChild(row);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            text.AddThemeConstantOverride("separation", 0);
            row.AddChild(text);
            var name = UiTheme.Label(slot.World);
            name.AddThemeFontSizeOverride("font_size", 20);
            name.AddThemeColorOverride("font_color", UiTheme.GoldBright);
            text.AddChild(name);
            text.AddChild(UiTheme.Label(slot.Summary(_player), "Small"));

            var play = new Button { Text = "Play", CustomMinimumSize = new Vector2(110, 40) };
            string world = slot.World;
            play.Pressed += () => Pick(world);
            row.AddChild(play);
            var delete = new Button { Text = "Delete", CustomMinimumSize = new Vector2(100, 40) };
            delete.Pressed += () => ConfirmDelete(world);
            row.AddChild(delete);
        }
    }

    private void ConfirmDelete(string world)
    {
        var dialog = new ConfirmationDialog
        {
            Title = "Delete world",
            DialogText = $"Delete \"{world}\"? Its buildings, stockpile and characters will be gone.\n(The file is moved to saves/deleted, so it can be recovered by hand.)",
            OkButtonText = "Delete",
        };
        dialog.Confirmed += () =>
        {
            _status.Text = SaveSlots.Delete(world) ? $"Deleted \"{world}\"." : $"Couldn't delete \"{world}\".";
            Refresh();
        };
        dialog.Canceled += dialog.QueueFree;
        dialog.Confirmed += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void CreateNew()
    {
        string name = SaveSlots.Clean(_newName.Text);
        if (name == "") { _status.Text = "Give the new world a name."; return; }
        if (SaveSlots.Exists(name)) { _status.Text = $"There's already a world called \"{name}\". Pick another name, or play it from the list."; return; }
        Pick(name);
    }

    private void Pick(string world)
    {
        _onPick?.Invoke(world);
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
