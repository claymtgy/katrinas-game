using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>All on-screen UI, built in code in the medieval kit (<see cref="UiTheme"/>): framed
/// health and stamina bars, the day plate, pack and stockpile grids, the weapon hotbar, key-cap
/// prompts, parchment toasts, the help board (F1), the crafting / trade / enchanting board, the
/// character sheet (I: armour and pack), and the co-op bits (session line, player list on Tab,
/// sleep banner, the Esc menu).</summary>
public partial class Hud : CanvasLayer
{
    public static Hud Instance { get; private set; }

    private static readonly Color Blush = new(1f, 0.72f, 0.8f);
    private const int GridMax = 18;

    private Control _root;
    private FramedBar _health, _stamina;
    private Label _clock, _session, _buffs, _ability, _prompt, _buildInfo, _crosshair, _sleep, _playerListText;
    private Control _promptRow, _buildPlate, _sleepPlate, _helpHint;
    private PanelContainer _playerList, _help, _gameMenu, _craftPanel, _sheet;
    private VBoxContainer _toasts;
    private readonly List<ItemSlot> _hotbar = new();
    private GridContainer _packGrid, _stockGrid;
    private Label _packTitle, _stockTitle, _packMore, _stockMore;
    private int _packVersion = -1;
    private bool _inventoryDirty = true;
    private float _hitFlash;
    /// <summary>A blue-green wash over the screen while the camera is under water.</summary>
    private ColorRect _underwater;

    // crafting / trade / enchanting board
    private VBoxContainer _craftList;
    private Label _craftTitle, _craftFooter;
    private CraftingStation _station;
    private Trader _trader;
    private BlossomShrine _shrine;
    private string _enchantPick = "";

    // character sheet
    private readonly Dictionary<string, ItemSlot> _wornSlots = new();
    private GridContainer _sheetPack, _sheetArms;
    /// <summary>The paper doll's weapon slot (what you hold; not armour).</summary>
    private const string MainHand = "main_hand";
    /// <summary>The equipment screen's slots: the main hand, then weapons and tools (tests).</summary>
    public IEnumerable<ItemSlot> EquipmentSlots =>
        new[] { _wornSlots[MainHand] }.Concat(_sheetArms.GetChildren().OfType<ItemSlot>().Where(s => s.Visible));
    private VBoxContainer _sheetStats;
    private Label _sheetName;
    private int _sheetVersion = -1;
    private Button _tabCharacter, _tabJournal;   // (and _tabTalents, Hud.Talents)
    private Control _sheetCharacter, _sheetJournal;
    private Label _sheetHint;
    private bool _journalTab;

    public override void _Ready()
    {
        if (Net.Instance.IsDedicated) return; // Main frees us; a server has no screen
        Instance = this;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Theme };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _underwater = new ColorRect { Color = new Color(0.08f, 0.32f, 0.4f, 0.45f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _root.AddChild(_underwater);
        _underwater.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        BuildHurt();

        BuildVitals();
        BuildClock();
        BuildInventory();
        BuildHotbar();
        BuildCentre();
        BuildHelp();
        BuildPlayerList();
        BuildCraftPanel();
        BuildGameMenu();
        BuildSheet();
        BuildGuidance();
        OnDeviceChanged();

        GameState.Instance.Inventory.Changed += OnInventoryChanged;
    }

    public override void _ExitTree()
    {
        GameState.Instance.Inventory.Changed -= OnInventoryChanged;
        UnhookGuidance();
        if (Instance == this) Instance = null;
    }

    private void OnInventoryChanged() => _inventoryDirty = true;

    // ================================================================== layout

    private void BuildVitals()
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        Place(_root, box, 0, 1, 0, 1, 22, -150, 560, -18);
        box.GrowVertical = Control.GrowDirection.Begin;
        _buffs = UiTheme.Label("", "Small");
        _buffs.AddThemeColorOverride("font_color", UiTheme.Gold);
        box.AddChild(_buffs);
        _ability = UiTheme.Label("", "Heading");
        _ability.AddThemeFontSizeOverride("font_size", 18);
        box.AddChild(_ability);
        _health = new FramedBar("bar_health", new Vector2(340, 30)) { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        box.AddChild(_health);
        _stamina = new FramedBar("bar_stamina", new Vector2(290, 22)) { ShowNumbers = false, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        box.AddChild(_stamina);
        BuildXpRow(box);
    }

    private void BuildClock()
    {
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_root, col, 0, 0, 0, 0, 18, 14, 600, 120);
        var plate = new PanelContainer { ThemeTypeVariation = "Plate", MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _clock = UiTheme.Label("", "Heading");
        _clock.AddThemeFontSizeOverride("font_size", 19);
        _clock.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        plate.AddChild(_clock);
        col.AddChild(plate);
        _session = UiTheme.Label("", "Small");
        _session.AddThemeColorOverride("font_color", Blush);
        col.AddChild(_session);
    }

    private void BuildInventory()
    {
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Begin };
        col.AddThemeConstantOverride("separation", 4);
        Place(_root, col, 1, 0, 1, 0, -336, 14, -16, 600);
        (_packTitle, _packGrid, _packMore) = GridBlock(col);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6), MouseFilter = Control.MouseFilterEnum.Ignore });
        (_stockTitle, _stockGrid, _stockMore) = GridBlock(col);
    }

    private static (Label, GridContainer, Label) GridBlock(VBoxContainer parent)
    {
        var title = UiTheme.Label("", "Heading", HorizontalAlignment.Right);
        title.AddThemeFontSizeOverride("font_size", 17);
        parent.AddChild(title);
        var grid = new GridContainer { Columns = 7, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        parent.AddChild(grid);
        var more = UiTheme.Label("", "Dim", HorizontalAlignment.Right);
        parent.AddChild(more);
        return (title, grid, more);
    }

    private void BuildHotbar()
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 4);
        Place(_root, row, 0.5f, 1, 0.5f, 1, -200, -82, 200, -14);
        for (int i = 0; i < 5; i++)
        {
            var slot = new ItemSlot(62, (i + 1).ToString()) { MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None };
            row.AddChild(slot);
            _hotbar.Add(slot);
        }
    }

    private void BuildCentre()
    {
        // Crosshair.
        _crosshair = UiTheme.Label("·", "Heading", HorizontalAlignment.Center);
        _crosshair.VerticalAlignment = VerticalAlignment.Center;
        Place(_root, _crosshair, 0.5f, 0.5f, 0.5f, 0.5f, -20, -16, 20, 16);

        // Interact prompt: [E] key cap + what it does.
        var promptCenter = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_root, promptCenter, 0.5f, 0.5f, 0.5f, 0.5f, -400, 44, 400, 84);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(UiTheme.ActionCap("interact"));
        _prompt = UiTheme.Label("", "Heading");
        _prompt.AddThemeColorOverride("font_color", UiTheme.Ink);
        _prompt.AddThemeFontSizeOverride("font_size", 20);
        row.AddChild(_prompt);
        promptCenter.AddChild(row);
        _promptRow = row;

        // Build mode: a plate above the hotbar.
        var buildCenter = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_root, buildCenter, 0.5f, 1, 0.5f, 1, -600, -170, 600, -92);
        var plate = new PanelContainer { ThemeTypeVariation = "Plate", MouseFilter = Control.MouseFilterEnum.Ignore };
        _buildInfo = UiTheme.Label("", "", HorizontalAlignment.Center);
        plate.AddChild(_buildInfo);
        buildCenter.AddChild(plate);
        _buildPlate = buildCenter;

        // Sleep banner, top centre.
        var sleepCenter = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_root, sleepCenter, 0.5f, 0, 0.5f, 0, -500, 70, 500, 140);
        var sleepPlate = new PanelContainer { ThemeTypeVariation = "Plate", MouseFilter = Control.MouseFilterEnum.Ignore };
        _sleep = UiTheme.Label("", "", HorizontalAlignment.Center);
        sleepPlate.AddChild(_sleep);
        sleepCenter.AddChild(sleepPlate);
        _sleepPlate = sleepCenter;

        // Toasts, left middle.
        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _toasts.AddThemeConstantOverride("separation", 4);
        Place(_root, _toasts, 0, 0.5f, 0, 0.5f, 18, -110, 560, 220);
    }

    private void BuildHelp()
    {
        // A small reminder at first; F1 opens the full board.
        var hintCenter = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_root, hintCenter, 1, 1, 1, 1, -330, -70, -20, -20);
        var hint = new PanelContainer { ThemeTypeVariation = "Plate", MouseFilter = Control.MouseFilterEnum.Ignore };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(UiTheme.ActionCap("help"));
        row.AddChild(UiTheme.Label("Controls & help", "Heading"));
        hint.AddChild(row);
        hintCenter.AddChild(hint);
        _helpHint = hintCenter;
        GetTree().CreateTimer(30).Timeout += () => { if (IsInstanceValid(_helpHint)) _helpHint.Visible = false; };

        _help = Board(900, 620, "Controls & Help", out var body, out _);
        _help.MouseFilter = Control.MouseFilterEnum.Ignore;
        _helpBody = body;
        FillHelp();
        Gamepad.Changed += OnDeviceChanged;
    }

    private VBoxContainer _helpBody;

    /// <summary>The help board's key list, for the keyboard or the controller in use.</summary>
    private void FillHelp()
    {
        foreach (var c in _helpBody.GetChildren()) c.QueueFree();
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 36);
        _helpBody.AddChild(cols);
        (string Action, string What)[] left =
        {
            ("move", "Move · {sprint} sprint · {jump} jump"), ("dodge", "Dodge roll"), ("look", "Look · {toggle_view} first / third person"),
            ("attack", "Light attack"), ("heavy_attack", "Heavy attack"), ("block", "Block (just in time = parry)"), ("ability", "Class ability"), ("ability_2", "Level-5 ability"), ("ability_3", "Level-12 ability"),
            ("weapons", "Weapons"), ("interact", "Use: stations, beds, chests, plots, traders, signs"), ("eat", "Eat"),
        };
        (string Action, string What)[] right =
        {
            ("inventory", Gamepad.UsingPad ? "Character sheet & journal ({tabs} switch)" : "Character: armour and pack"),
            ("journal", "Journal: goals and errands"),
            ("build_mode", "Build mode ({next_build} change, {rotate_build} rotate)"), ("jump", "In the air: open the glider (look down to dive)"),
            ("move", "Into pale rock: climb ({jump} leaps off)"), ("player_list", "Players"), ("save", "Save / load (host)"), ("menu", "Menu"),
        };
        cols.AddChild(KeyList(left));
        cols.AddChild(KeyList(right.Where(r => !(Gamepad.UsingPad && r.Action == "journal")).ToArray()));
        _helpBody.AddChild(UiTheme.Divider(500));
        var goals = UiTheme.Label(Gamepad.Format(
            "Your journal ({journal}) says what to do next; the goal you're on shows under the clock, and a gold marker points the way.\n" +
            "What you gather rides in your pack: empty it into the chest by the shack. Crops need water. From day 3, raiders come at dusk.\n" +
            "Make armour at the workbench from pelts, feathers and nails; enchant it at a Rune Altar with rune shards."), "Small");
        goals.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        goals.CustomMinimumSize = new Vector2(800, 0);
        _helpBody.AddChild(goals);
    }

    private static Control KeyList((string Action, string What)[] rows)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var (action, what) in rows)
        {
            if (Gamepad.ButtonName(action) == "") continue; // nothing on this device (Tab, F5)
            var c = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, CustomMinimumSize = new Vector2(120, 0) };
            c.AddChild(UiTheme.ActionCap(action));
            grid.AddChild(c);
            grid.AddChild(UiTheme.Label(Gamepad.Format(what), "Small"));
        }
        return grid;
    }

    private void BuildPlayerList()
    {
        _playerList = Board(520, 340, "Players", out var body, out _);
        _playerList.MouseFilter = Control.MouseFilterEnum.Ignore;
        _playerListText = UiTheme.Label("", "", HorizontalAlignment.Center);
        body.AddChild(_playerListText);
    }

    /// <summary>A framed oak board centred on screen: title plate, body, and a footer row.</summary>
    private PanelContainer Board(float w, float h, string title, out VBoxContainer body, out HBoxContainer footer)
    {
        var panel = new PanelContainer { Visible = false };
        Place(_root, panel, 0.5f, 0.5f, 0.5f, 0.5f, -w / 2, -h / 2, w / 2, h / 2);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 8);
        panel.AddChild(col);
        col.AddChild(UiTheme.TitlePlate(title, out var titleLabel));
        titleLabel.Name = "Title";
        body = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        col.AddChild(body);
        footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        col.AddChild(footer);
        return panel;
    }

    private static Label BoardTitle(PanelContainer board) => board.FindChild("Title", true, false) as Label;

    // ================================================================== public API

    public static void Toast(string message)
    {
        GD.Print($"[Toast] {message}");
        if (Instance == null) return;
        var note = new PanelContainer { ThemeTypeVariation = "Parchment", MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        var label = UiTheme.Label(message, "OnParchment");
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(Mathf.Min(480, 40 + message.Length * 8.2f), 0);
        note.AddChild(label);
        Instance._toasts.AddChild(note);
        // At most five at once: the oldest go first.
        var live = Instance._toasts.GetChildren().Where(c => !c.IsQueuedForDeletion()).ToList();
        for (int i = 0; i < live.Count - 5; i++) live[i].QueueFree();
        note.Modulate = new Color(1, 1, 1, 0);
        var tween = note.CreateTween();
        tween.TweenProperty(note, "modulate:a", 1f, 0.18);
        tween.TweenInterval(3.0);
        tween.TweenProperty(note, "modulate:a", 0f, 0.6);
        tween.TweenCallback(Callable.From(note.QueueFree));
    }

    public void FlashHitMarker()
    {
        _hitFlash = 0.15f;
        Gamepad.Rumble(0.5f, 0f, 0.08f);
    }

    public void OpenCrafting(CraftingStation station)
    {
        _station = station;
        _trader = null;
        _shrine = null;
        _enchantPick = "";
        BoardTitle(_craftPanel).Text = station.StationName;
        ShowBoard(_craftPanel);
        RefreshCrafting();
    }

    /// <summary>Buy from / sell to a trader (goods and coins in your pack).</summary>
    public void OpenTrade(Trader trader)
    {
        _station = null;
        _trader = trader;
        _shrine = null;
        BoardTitle(_craftPanel).Text = trader.TraderName;
        ShowBoard(_craftPanel);
        RefreshCrafting();
    }

    /// <summary>Fast travel from a sanctuary to the other attuned ones (GDD §10).</summary>
    public void OpenTravel(BlossomShrine shrine)
    {
        _station = null;
        _trader = null;
        _shrine = shrine;
        BoardTitle(_craftPanel).Text = shrine.ShrineName;
        ShowBoard(_craftPanel);
        RefreshCrafting();
    }

    /// <summary>The character sheet: worn armour, stats and the pack (I), or its Journal tab (J).</summary>
    public void OpenCharacter() => OpenCharacter(false);

    public void OpenCharacter(bool journal)
    {
        _sheetVersion = -1;
        ShowSheetTab(journal, focus: false);
        ShowBoard(_sheet);
        RefreshSheet();
    }

    public bool CharacterOpen => _sheet.Visible;
    /// <summary>The sanctuary travel list is open.</summary>
    public bool TravelOpen => _shrine != null && _craftPanel.Visible;

    public void CloseMenus()
    {
        _craftPanel.Visible = false;
        _gameMenu.Visible = false;
        _sheet.Visible = false;
        _help.Visible = false;
        foreach (var c in GetChildren())
            if (c is CharacterCreator or GraphicsMenu) c.QueueFree();
        _station = null;
        _trader = null;
        _shrine = null;
        GameState.Instance.SetUiOpen(false);
    }

    /// <summary>Esc menu. The game keeps running (it's multiplayer, even solo).</summary>
    public void OpenGameMenu() => ShowBoard(_gameMenu);

    private void ShowBoard(PanelContainer board)
    {
        foreach (var other in new[] { _craftPanel, _gameMenu, _sheet, _help })
            if (other != board) other.Visible = false;
        board.Visible = true;
        UiTheme.PopIn(board);
        GameState.Instance.SetUiOpen(true);
        if (Gamepad.UsingPad) Callable.From(() => FocusFirst(board)).CallDeferred();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Instance != this) return;
        if (e.IsActionPressed("help"))
        {
            _helpHint.Visible = false;
            if (_help.Visible) CloseMenus();
            else if (!GameState.Instance.UiOpen) ShowBoard(_help);
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("inventory"))
        {
            // (On a controller that's the d-pad, which also moves the focus: B closes instead.)
            if (_sheet.Visible) { if (e is not InputEventJoypadButton) CloseMenus(); }
            else if (!GameState.Instance.UiOpen) OpenCharacter();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("journal"))
        {
            if (_sheet.Visible && _journalTab) CloseMenus();
            else if (_sheet.Visible) ShowSheetTab(true);
            else if (!GameState.Instance.UiOpen) OpenCharacter(journal: true);
            GetViewport().SetInputAsHandled();
        }
        else if (_sheet.Visible && (e.IsActionPressed("tab_prev") || e.IsActionPressed("tab_next")))
        {
            ShowSheetPage(_sheetPage + (e.IsActionPressed("tab_next") ? 1 : -1));
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsAction("player_list"))
        {
            _playerList.Visible = e.IsPressed() && !GameState.Instance.UiOpen;
            GetViewport().SetInputAsHandled();
        }
        else if (_craftPanel.Visible && e.IsActionPressed("interact"))
        {
            CloseMenus();
            GetViewport().SetInputAsHandled();
        }
    }

    // ================================================================== downed (GDD §16)

    private ColorRect _downedShade;
    private Label _downedText;

    /// <summary>Downed: the screen greys and darkens, with the time left and how to give up.</summary>
    private void UpdateDowned(PlayerController player)
    {
        if (_downedShade == null)
        {
            _downedShade = new ColorRect
            {
                Name = "DownedShade", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false,
                Material = new ShaderMaterial { Shader = new Shader { Code = @"shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
void fragment() {
    vec3 c = texture(screen, SCREEN_UV).rgb;
    float g = dot(c, vec3(0.3, 0.59, 0.11));
    vec2 d = SCREEN_UV - 0.5;
    float vig = smoothstep(0.25, 0.75, length(d * vec2(1.4, 1.0)));
    COLOR = vec4(mix(vec3(g) * 0.8, vec3(0.08, 0.0, 0.0), vig * 0.75), 1.0);
}" } },
            };
            _downedShade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_downedShade);
            MoveChild(_downedShade, 0);
            _downedText = UiTheme.Label("", "Heading", HorizontalAlignment.Center);
            _downedText.AddThemeFontSizeOverride("font_size", 30);
            _downedText.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.85f));
            _downedText.AddThemeConstantOverride("outline_size", 8);
            _downedText.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
            _downedText.SetAnchorsPreset(Control.LayoutPreset.Center);
            _downedText.GrowHorizontal = Control.GrowDirection.Both;
            _downedText.GrowVertical = Control.GrowDirection.Both;
            _downedShade.AddChild(_downedText);
        }
        _downedShade.Visible = player.Downed;
        if (!player.Downed) return;
        float left = Mathf.Max(0f, (float)(player.DownedUntil - Net.Now));
        string help = player.ReviveProgress > 0.01f ? $"Being revived… {player.ReviveProgress * 100f:0}%" : "Wait for a friend to revive you";
        string giveUp = player.GiveUpHeld > 0 ? $"Giving up… {player.GiveUpHeld / PlayerController.GiveUpSeconds * 100f:0}%"
                                              : $"Hold {Gamepad.ButtonName("jump")} to give up";
        _downedText.Text = $"You're down\n{left:0}\n{help}\n{giveUp}";
    }

    // ================================================================== per-frame

    public override void _Process(double delta)
    {
        if (Instance != this) return;
        var gs = GameState.Instance;
        var net = Net.Instance;
        PadScroll((float)delta);
        UpdateSession(gs, net);
        var player = gs.LocalPlayer;
        UpdateHurt((float)delta, player);
        if (player == null) return;

        _health.Set(player.Health.Current, player.Health.Max);
        _stamina.Set(player.Stamina, player.MaxStamina);
        UpdateXp(player);

        float hours = gs.TimeOfDay * 24f;
        int h = (int)hours, m = (int)((hours - h) * 60);
        string phase = gs.TimeOfDay is > 0.22f and < 0.78f ? "☀" : "☾";
        _clock.Text = $"Day {gs.Day}   {phase}  {h:00}:{m:00}";

        if (player.Pack.Version != _packVersion) { _packVersion = player.Pack.Version; _inventoryDirty = true; }
        if (_inventoryDirty) RefreshInventory();
        UpdateBuffs(player);
        UpdateHotbar(gs, player);
        if (_sheet.Visible && _sheetVersion != SheetVersion(player)) RefreshSheet();
        if (TalentsTabOpen && _talentsVersion != TalentsVersion(player)) RefreshTalents();
        UpdateGuidance(gs, player);

        var target = player.Interactor.Target;
        var rescue = player.Interactor.ReviveTarget;
        _prompt.Text = gs.UiOpen ? ""
            : rescue != null ? $"Hold {Gamepad.ButtonName("interact")} to revive {rescue.DisplayName}"
                               + (rescue.ReviveProgress > 0.01f ? $"  ({rescue.ReviveProgress * 100f:0}%)" : "")
            : target != null ? target.InteractPrompt : "";
        UpdateDowned(player);
        _promptRow.Visible = _prompt.Text != "";

        var build = player.Build;
        if (build.Active && build.Current != null)
        {
            var def = build.Current;
            string status = build.InvalidReason == "" ? Gamepad.Format("{place} to place") : build.InvalidReason;
            _buildInfo.Text = $"{def.DisplayName}  ·  {ItemDb.FormatCost(def.GetCost())}\n{status}   ·   " +
                              Gamepad.Format("{next_build}: change · {rotate_build}: rotate · {exit_build}: exit");
            _buildInfo.AddThemeColorOverride("font_color", build.InvalidReason == "" ? UiTheme.Ink : UiTheme.Bad);
        }
        else _buildInfo.Text = "";
        _buildPlate.Visible = _buildInfo.Text != "";

        var eye = player.Rig.Camera.GlobalPosition;
        float surface = gs.WaterSurfaceAt(eye);
        _underwater.Visible = !float.IsNaN(surface) && eye.Y < surface;

        _hitFlash -= (float)delta;
        _crosshair.Text = _hitFlash > 0 ? "✕" : "·";
        _crosshair.Modulate = _hitFlash > 0 ? new Color(1, 0.4f, 0.3f) : Colors.White;
        _crosshair.Visible = !gs.UiOpen;

        if (player.Sleeping)
            _sleep.Text = net.SleepTotal > 1
                ? $"Sleeping… {net.SleepingCount}/{net.SleepTotal} asleep. The night passes when everyone is in bed.\n" + Gamepad.Format("{interact} or move to get up")
                : "Sleeping…";
        else if (net.SleepingCount > 0)
            _sleep.Text = $"{net.SleepingCount}/{net.SleepTotal} asleep. Go to bed to skip the night.";
        else _sleep.Text = "";
        _sleepPlate.Visible = _sleep.Text != "";
    }

    private void UpdateHotbar(GameState gs, PlayerController player)
    {
        var slots = gs.WeaponSlots(player);
        for (int i = 0; i < _hotbar.Count; i++)
        {
            var s = _hotbar[i];
            if (i < slots.Count)
            {
                string id = slots[i];
                string key = id == "" ? "fists" : id;
                if (s.ItemId != key || !s.Visible)
                {
                    s.SetItem(key);
                    if (id == "") s.TooltipText = "Fists";
                    s.Visible = true;
                }
                s.Selected = id == player.Equipped;
            }
            else s.Visible = i == 0;
        }
    }

    private void UpdateSession(GameState gs, Net net)
    {
        if (!net.IsMultiplayer)
        {
            _session.Text = "";
            _playerList.Visible = false;
            return;
        }
        string role = net.Mode == NetMode.Client ? "Co-op" : "Hosting";
        _session.Text = $"{role} · {gs.Players.Count}/{net.Config.MaxPlayers} players · PVP {(net.Pvp ? "ON" : "off")}";
        if (_playerList.Visible)
        {
            var lines = gs.Players.Values.OrderBy(p => p.PeerId).Select(p =>
                $"{p.DisplayName}{(p.PeerId == 1 ? "  (host)" : "")}{(p.IsLocal ? "  ← you" : "")}" +
                $"   ♥ {p.Health.Current:0}{(p.Sleeping ? "   asleep" : "")}");
            BoardTitle(_playerList).Text = net.Config.Name;
            _playerListText.Text = string.Join("\n", lines);
        }
    }

    private void RefreshInventory()
    {
        _inventoryDirty = false;
        var player = GameState.Instance.LocalPlayer;
        if (player != null)
        {
            var pack = player.Pack;
            bool full = pack.Load >= Supplies.PackCapacity;
            _packTitle.Text = $"Pack  {pack.Load}/{Supplies.PackCapacity}{(full ? "  · FULL" : "")}";
            _packTitle.AddThemeColorOverride("font_color", full ? UiTheme.Bad : UiTheme.Gold);
            FillGrid(_packGrid, _packMore, pack.Items, 44);
        }
        var stock = GameState.Instance.Inventory.Items;
        string title = Net.Instance.IsMultiplayer ? "Stockpile (shared)" : "Stockpile";
        bool home = player == null || Supplies.AtHome(player);
        if (!home) title += " · at home";
        _stockTitle.Text = stock.Count == 0 ? $"{title}: empty" : title;
        _stockTitle.AddThemeColorOverride("font_color", home ? UiTheme.Gold : UiTheme.Muted);
        FillGrid(_stockGrid, _stockMore, stock, 44);
        _stockGrid.Modulate = home ? Colors.White : new Color(1, 1, 1, 0.55f);
        if (_craftPanel.Visible) RefreshCrafting();
    }

    /// <summary>Show stacks as slots (reusing the slot nodes), sorted by name.</summary>
    private static void FillGrid(GridContainer grid, Label more, IReadOnlyDictionary<string, int> items, float size, System.Action<ItemSlot> onPress = null)
    {
        var list = items.OrderBy(kv => ItemDb.NameOf(kv.Key)).ToList();
        int shown = Mathf.Min(list.Count, GridMax);
        while (grid.GetChildCount() < shown)
        {
            var slot = new ItemSlot(size)
            {
                MouseFilter = onPress == null ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Stop,
                FocusMode = onPress == null ? Control.FocusModeEnum.None : Control.FocusModeEnum.All,
            };
            if (onPress != null) slot.Pressed += () => onPress(slot);
            grid.AddChild(slot);
        }
        for (int i = 0; i < grid.GetChildCount(); i++)
        {
            var slot = (ItemSlot)grid.GetChild(i);
            slot.Visible = i < shown;
            if (i < shown) slot.SetItem(list[i].Key, list[i].Value);
        }
        more.Text = list.Count > shown ? $"+{list.Count - shown} more kinds" : "";
        more.Visible = more.Text != "";
    }

    private bool _wasHome;

    private void UpdateBuffs(PlayerController player)
    {
        var cls = player.Class;
        string AbilityState(PlayerAbility a)
        {
            var def = a.Def;
            if (def == null) return "";
            string state = !a.Unlocked ? $"level {def.UnlockLevel}"
                : a.Slot == 0 && player.Vanished ? "hidden"
                : a.CooldownLeft > 0 ? $"{a.CooldownLeft:0.0}s"
                : player.Stamina < def.Stamina ? "tired" : "ready";
            return $"{Gamepad.ButtonName(a.Action)}  {def.Name}: {state}";
        }
        string first = AbilityState(player.Ability), second = AbilityState(player.Ability2), third = AbilityState(player.Ability3);
        _ability.Text = $"{cls.Name}   ·   {first}" + (second != "" ? $"   ·   {second}" : "") + (third != "" ? $"   ·   {third}" : "");
        _ability.AddThemeColorOverride("font_color", first.EndsWith("ready") || second.EndsWith("ready") || third.EndsWith("ready") ? cls.Tint.Lerp(UiTheme.GoldBright, 0.35f) : UiTheme.Muted);

        bool home = Supplies.AtHome(player);
        if (home != _wasHome) { _wasHome = home; _inventoryDirty = true; }
        if (player.Buffs.Count == 0) { _buffs.Text = ""; return; }
        _buffs.Text = "Well fed:  " + string.Join("   ·   ", player.Buffs.Select(kv =>
        {
            string what = kv.Key == PlayerController.BuffVigor ? $"+{kv.Value.Amount:0} max health" : $"stamina regen +{kv.Value.Amount * 100f:0}%";
            int s = Mathf.CeilToInt(kv.Value.Left);
            return $"{what} ({s / 60}:{s % 60:00})";
        }));
    }

    private float _scrollCarry;

    /// <summary>Controller: the right stick scrolls the open board's list (trade, crafting, pack, journal).</summary>
    private void PadScroll(float dt)
    {
        if (!GameState.Instance.UiOpen) { _scrollCarry = 0; return; }
        float y = Input.GetAxis("look_up", "look_down");
        if (Mathf.Abs(y) < 0.05f) { _scrollCarry = 0; return; }
        var board = new[] { _craftPanel, _sheet, _help }.FirstOrDefault(b => b.Visible);
        var scroll = board == null ? null : FirstScroll(board);
        if (scroll == null) return;
        _scrollCarry += y * 1100f * dt;
        int step = (int)_scrollCarry;
        _scrollCarry -= step;
        scroll.ScrollVertical += step;
    }

    /// <summary>Godot's wheel step is an eighth of the view (about 58 px here): long lists like the
    /// trade board took dozens of notches. Scroll about one row per notch instead.</summary>
    private static void FastWheel(ScrollContainer scroll)
    {
        scroll.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true } mb) return;
            int dir = mb.ButtonIndex switch { MouseButton.WheelDown => 1, MouseButton.WheelUp => -1, _ => 0 };
            if (dir == 0) return;
            scroll.ScrollVertical += Mathf.RoundToInt(dir * 110 * (mb.Factor > 0 ? mb.Factor : 1f));
            scroll.AcceptEvent();
        };
    }

    private static ScrollContainer FirstScroll(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is not Control c || !c.IsVisibleInTree()) continue;
            if (c is ScrollContainer s) return s;
            if (FirstScroll(c) is { } inner) return inner;
        }
        return null;
    }

    // ================================================================== crafting / trade / enchanting

    private void BuildCraftPanel()
    {
        _craftPanel = Board(680, 620, "Workbench", out var body, out var footer);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        body.AddChild(scroll);
        FastWheel(scroll);
        _craftList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _craftList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_craftList);
        _craftFooter = UiTheme.Label("", "Dim");
        _craftFooter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_craftFooter);
        var close = new Button { Text = "Close" };
        close.Pressed += CloseMenus;
        footer.AddChild(close);
    }

    private void RefreshCrafting()
    {
        // Rebuilt rows lose the focus: put it back on the same button (controller).
        int focus = Gamepad.UsingPad ? FocusIndex(_craftPanel) : -1;
        if (focus >= 0) Callable.From(() => RestoreFocus(_craftPanel, focus)).CallDeferred();
        foreach (var child in _craftList.GetChildren()) child.QueueFree();
        _craftFooter.Text = Gamepad.Format("{back} to close");
        if (_trader != null) { RefreshTrade(); return; }
        if (_shrine != null) { RefreshTravel(); return; }
        if (_station == null) return;
        if (_station.StationId == Gear.AltarStation) { RefreshEnchanting(); return; }
        var me = GameState.Instance.LocalPlayer;
        if (_station is Campfire fire)
        {
            var rest = new Button { Text = "Rest by the fire  (heal, refill stamina)" };
            rest.Pressed += () => { fire.Rest(); CloseMenus(); };
            _craftList.AddChild(rest);
        }
        foreach (var recipe in _station.Recipes)
        {
            var cost = recipe.GetInputs();
            bool canAfford = Supplies.Has(me, cost);
            var r = recipe;
            var station = _station;
            var out_ = ItemDb.Get(recipe.OutputItem);
            string note = out_ is { IsWearable: true } ? $"{Gear.SlotName(out_.Slot)} · {out_.Armor:0} armour" + (out_.PerkText != "" ? $" · {out_.PerkText}" : "")
                : out_ is { IsWeapon: true } ? $"Weapon · {out_.Damage:0} damage" : "";
            _craftList.AddChild(RecipeRow(recipe.OutputItem, recipe.DisplayName, note, cost, me, "Craft", canAfford,
                () => { Sfx.Ui("craft", -4f); station.RequestCraft(r); }));
        }
        if (_craftList.GetChildCount() == 0)
            _craftList.AddChild(UiTheme.Label("No recipes for this station yet.", "Dim", HorizontalAlignment.Center));
        _craftFooter.Text = "Materials come from your pack, then the stockpile (at home).";
    }

    /// <summary>A row: the item's slot, its name and a note, the cost as icons (red when short), a button.</summary>
    private static Control RecipeRow(string itemId, string name, string note, IReadOnlyDictionary<string, int> cost, PlayerController me,
        string action, bool enabled, System.Action onPress, bool useStock = true)
    {
        var row = new PanelContainer { ThemeTypeVariation = "Inset" };
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        row.AddChild(h);
        var slot = new ItemSlot(56) { FocusMode = Control.FocusModeEnum.None };
        slot.SetItem(itemId);
        h.AddChild(slot);
        var mid = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        mid.AddThemeConstantOverride("separation", 2);
        h.AddChild(mid);
        var title = UiTheme.Label(name, "Heading");
        title.AddThemeFontSizeOverride("font_size", 19);
        mid.AddChild(title);
        if (note != "")
        {
            var n = UiTheme.Label(note, "Dim");
            n.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            mid.AddChild(n);
        }
        if (cost != null && cost.Count > 0)
        {
            var costs = new HBoxContainer();
            costs.AddThemeConstantOverride("separation", 10);
            foreach (var kv in cost)
            {
                int have = me == null ? 0 : useStock ? Supplies.Count(me, kv.Key) : me.Pack.Count(kv.Key);
                var c = new HBoxContainer();
                c.AddThemeConstantOverride("separation", 2);
                c.AddChild(new TextureRect
                {
                    Texture = UiTheme.Icon(kv.Key), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(24, 24),
                    TooltipText = ItemDb.NameOf(kv.Key),
                });
                var amount = UiTheme.Label($"{kv.Value}", "Small");
                amount.TooltipText = $"{ItemDb.NameOf(kv.Key)}: you have {have}";
                amount.MouseFilter = Control.MouseFilterEnum.Pass;
                amount.AddThemeColorOverride("font_color", have >= kv.Value ? UiTheme.Ink : UiTheme.Bad);
                c.AddChild(amount);
                costs.AddChild(c);
            }
            mid.AddChild(costs);
        }
        var button = new Button { Text = action, Disabled = !enabled, CustomMinimumSize = new Vector2(110, 44), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        button.Pressed += onPress;
        h.AddChild(button);
        return row;
    }

    private void RefreshTrade()
    {
        // Trading uses what you carry (your pack), not the stockpile at home.
        var me = GameState.Instance.LocalPlayer;
        var inv = me?.Pack ?? new Inventory();
        int coins = inv.Count("coin");
        _craftFooter.Text = $"Coins in your pack: {coins}";
        AddErrandRow(me);
        _craftList.AddChild(Section("Buy"));
        foreach (var item in ItemDb.Items.Values.Where(i => i.BuyPrice > 0).OrderBy(i => i.BuyPrice))
        {
            var id = item.Id;
            var price = new Dictionary<string, int> { ["coin"] = item.BuyPrice };
            _craftList.AddChild(RecipeRow(id, item.DisplayName, ShortNote(item), price, me, "Buy", coins >= item.BuyPrice,
                () => { Sfx.Ui("ui_click", -6f); Net.Instance.RequestTrade(_trader, id, buy: true); }, useStock: false));
        }
        _craftList.AddChild(Section("Sell"));
        foreach (var item in ItemDb.Items.Values.Where(i => i.SellPrice > 0 && inv.Count(i.Id) > 0).OrderBy(i => i.DisplayName))
        {
            var id = item.Id;
            int have = inv.Count(id);
            _craftList.AddChild(RecipeRow(id, $"{item.DisplayName}  ×{have}", $"+{item.SellPrice} coins each", null, me, "Sell", true,
                () => { Sfx.Ui("ui_click", -6f); Net.Instance.RequestTrade(_trader, id, buy: false); }));
        }
    }

    private void RefreshTravel()
    {
        var me = GameState.Instance.LocalPlayer;
        var here = _shrine;
        _craftList.AddChild(Section("Travel to another sanctuary"));
        var note = UiTheme.Label("The blossoms remember every tree you have prayed at. Travelling heals you and sets your respawn point there. Not while you're fighting.", "Dim");
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.CustomMinimumSize = new Vector2(600, 0);
        _craftList.AddChild(note);
        bool shaken = me != null && Net.Now - me.LastHurtAt < BlossomShrine.CombatLockout;
        foreach (var dest in here.Destinations)
        {
            var to = dest;
            float metres = here.GlobalPosition.DistanceTo(to.GlobalPosition);
            var go = new Button { Text = $"{to.ShrineName}   ·   {metres:0} m away", Disabled = shaken };
            go.Pressed += () =>
            {
                Sfx.Ui("ui_click", -6f);
                Net.Instance.RequestTravel(here, to);
                CloseMenus();
            };
            _craftList.AddChild(go);
        }
        var stay = new Button { Text = "Stay here" };
        stay.Pressed += CloseMenus;
        _craftList.AddChild(stay);
        if (shaken) _craftFooter.Text = "Too shaken to travel. Get clear of the fight first.";
    }

    private static string ShortNote(ItemDef d) =>
        d.IsWearable ? (d.PerkText != "" ? d.PerkText : $"{d.Armor:0} armour") : d.IsFood ? (d.HasBuff ? d.BuffText : $"Heals {d.FoodHeal:0}") : "";

    private static Control Section(string text)
    {
        var col = new VBoxContainer();
        col.AddChild(UiTheme.Label(text, "Heading"));
        col.AddChild(UiTheme.Divider(600));
        return col;
    }

    private void RefreshEnchanting()
    {
        var me = GameState.Instance.LocalPlayer;
        if (me == null) return;
        // Your pieces: worn first, then the pack.
        var pieces = me.Worn.Values.Concat(me.Pack.Items.Keys.Where(id => ItemDb.Get(id)?.IsWearable ?? false)).Distinct().ToList();
        if (!pieces.Contains(_enchantPick)) _enchantPick = pieces.FirstOrDefault() ?? "";
        _craftList.AddChild(Section("Choose a piece of armour"));
        if (pieces.Count == 0)
            _craftList.AddChild(UiTheme.Label("You have no armour to enchant. Craft some at the workbench.", "Dim"));
        var grid = new GridContainer { Columns = 8 };
        grid.AddThemeConstantOverride("h_separation", 4);
        _craftList.AddChild(grid);
        foreach (var id in pieces)
        {
            var slot = new ItemSlot(60);
            slot.SetItem(id);
            slot.Selected = id == _enchantPick;
            if (me.Worn.ContainsValue(id)) slot.SetKey("worn");
            var pick = id;
            slot.Pressed += () => { _enchantPick = pick; Sfx.Ui("ui_click", -8f); RefreshCrafting(); };
            grid.AddChild(slot);
        }
        var picked = ItemDb.Get(_enchantPick);
        if (picked != null)
        {
            var name = UiTheme.Label(picked.DisplayName, "Heading");
            _craftList.AddChild(name);
        }
        _craftList.AddChild(Section("Enchantments"));
        var cost = new Dictionary<string, int> { [Gear.ShardId] = Gear.ShardCost, ["coin"] = Gear.CoinCost };
        bool afford = Supplies.Has(me, cost);
        var altar = _station;
        foreach (var e in Gear.Enchantments)
        {
            var ench = e;
            bool same = picked?.Enchant == e.Id;
            string note = e.Text + (same ? "   (it has this one)" : picked != null && picked.Enchant != "" ? "   (replaces its enchantment)" : "");
            var row = RecipeRow(picked != null ? Gear.Enchanted(_enchantPick, e.Id) : Gear.ShardId, $"{e.Name}", note, cost, me, "Enchant", afford && picked != null && !same,
                () => { Sfx.Ui("craft", -4f); Net.Instance.RequestEnchant(_enchantPick, ench.Id, altar); });
            _craftList.AddChild(row);
        }
        _craftFooter.Text = $"Each enchantment: {Gear.ShardCost} rune shards + {Gear.CoinCost} coins. Shards drop from raiders, the captain and harpies.";
    }

    // ================================================================== character sheet

    private void BuildSheet()
    {
        _sheet = Board(1080, 700, "Character & Equipment", out var body, out var footer);
        // Tabs: Character | Journal (I / J, or LB / RB on a controller).
        var tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tabs.AddThemeConstantOverride("separation", 10);
        body.AddChild(tabs);
        _tabCharacter = new Button { Text = "Equipment", ToggleMode = true, CustomMinimumSize = new Vector2(170, 40) };
        _tabTalents = new Button { Text = "Talents", ToggleMode = true, CustomMinimumSize = new Vector2(170, 40) };
        _tabJournal = new Button { Text = "Journal", ToggleMode = true, CustomMinimumSize = new Vector2(170, 40) };
        var group = new ButtonGroup();
        _tabCharacter.ButtonGroup = group;
        _tabTalents.ButtonGroup = group;
        _tabJournal.ButtonGroup = group;
        _tabCharacter.Pressed += () => ShowSheetPage(0, focus: false);
        _tabTalents.Pressed += () => ShowSheetPage(1, focus: false);
        _tabJournal.Pressed += () => ShowSheetPage(2, focus: false);
        tabs.AddChild(_tabCharacter);
        tabs.AddChild(_tabTalents);
        tabs.AddChild(_tabJournal);

        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 24);
        body.AddChild(row);
        _sheetCharacter = row;
        body.AddChild(BuildTalents());
        _sheetJournal = BuildJournal();
        body.AddChild(_sheetJournal);

        // Paper doll: 3 × 3 with the slots placed like a body.
        var doll = new VBoxContainer();
        _sheetName = UiTheme.Label("", "Heading", HorizontalAlignment.Center);
        doll.AddChild(_sheetName);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        doll.AddChild(grid);
        string[] layout = { "", Gear.Head, MainHand, Gear.Cloak, Gear.Body, Gear.Charm, Gear.Hands, "", Gear.Feet };
        foreach (var slotId in layout)
        {
            if (slotId == "") { grid.AddChild(new Control { CustomMinimumSize = new Vector2(84, 84) }); continue; }
            var slot = new ItemSlot(84);
            var sid = slotId;
            slot.Pressed += () =>
            {
                var me = GameState.Instance.LocalPlayer;
                if (sid == MainHand) { if (me != null && me.Equipped != "") { Sfx.Ui("ui_click", -6f); Net.Instance.RequestEquip(""); } }
                else if (me?.Worn.ContainsKey(sid) ?? false) { Sfx.Ui("ui_click", -6f); Net.Instance.RequestTakeOff(sid); }
            };
            grid.AddChild(slot);
            _wornSlots[slotId] = slot;
        }
        // Weapons and tools you can take in hand (they're kept in the stockpile).
        doll.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        doll.AddChild(UiTheme.Label("Weapons & tools", "Heading", HorizontalAlignment.Center));
        _sheetArms = new GridContainer { Columns = 4 };
        _sheetArms.AddThemeConstantOverride("h_separation", 4);
        _sheetArms.AddThemeConstantOverride("v_separation", 4);
        doll.AddChild(_sheetArms);
        row.AddChild(doll);

        // Stats.
        var statsPanel = new PanelContainer { ThemeTypeVariation = "Parchment", CustomMinimumSize = new Vector2(280, 0) };
        _sheetStats = new VBoxContainer();
        _sheetStats.AddThemeConstantOverride("separation", 4);
        statsPanel.AddChild(_sheetStats);
        row.AddChild(statsPanel);

        // Pack.
        var packCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        packCol.AddChild(UiTheme.Label("Pack", "Heading"));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _sheetPack = new GridContainer { Columns = 5 };
        _sheetPack.AddThemeConstantOverride("h_separation", 4);
        _sheetPack.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(_sheetPack);
        FastWheel(scroll);
        packCol.AddChild(scroll);
        row.AddChild(packCol);

        _sheetHint = UiTheme.Label("", "Dim");
        _sheetHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_sheetHint);
        var close = new Button { Text = "Close" };
        close.Pressed += CloseMenus;
        footer.AddChild(close);
    }

    /// <summary>Changes when anything the sheet shows does: pack, armour, held weapon, stockpile.</summary>
    private static int SheetVersion(PlayerController me) =>
        System.HashCode.Combine(me.Pack.Version, me.WornVersion, me.Equipped, GameState.Instance.Inventory.Version);

    private void RefreshSheet()
    {
        var me = GameState.Instance.LocalPlayer;
        if (me == null) return;
        _sheetVersion = SheetVersion(me);
        _sheetName.Text = $"{me.DisplayName} · {me.Class.Name}";
        foreach (var (slotId, slot) in _wornSlots)
        {
            if (slotId == MainHand)
            {
                slot.SetItem(me.Equipped, 1, "Main hand");
                if (me.Equipped != "") slot.TooltipText += "\n(click to put it away)";
                continue;
            }
            slot.SetItem(me.Worn.TryGetValue(slotId, out var id) ? id : "", 1, Gear.SlotName(slotId));
            if (id != null) slot.TooltipText += "\n(click to take off)";
        }

        // Weapons & tools: fists, then everything in the stockpile, weakest first; click to hold.
        var arms = GameState.Instance.WeaponSlots(me);
        while (_sheetArms.GetChildCount() < arms.Count)
        {
            var slot = new ItemSlot(60);
            slot.Pressed += () => { Sfx.Ui("ui_click", -6f); Net.Instance.RequestEquip(slot.ItemId == "fists" ? "" : slot.ItemId); };
            _sheetArms.AddChild(slot);
        }
        for (int i = 0; i < _sheetArms.GetChildCount(); i++)
        {
            var slot = (ItemSlot)_sheetArms.GetChild(i);
            slot.Visible = i < arms.Count;
            if (i >= arms.Count) continue;
            slot.SetItem(arms[i] == "" ? "fists" : arms[i], 1);
            if (arms[i] == "") slot.TooltipText = "Fists";
            slot.Selected = arms[i] == me.Equipped;
            slot.TooltipText += arms[i] == me.Equipped ? "\n(in hand)" : "\n(click to hold)";
        }

        foreach (var c in _sheetStats.GetChildren()) c.QueueFree();
        void Stat(string text, bool head = false)
        {
            var l = UiTheme.Label(text, "OnParchment");
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (head) { l.AddThemeFontOverride("font", UiTheme.TitleFont); l.AddThemeFontSizeOverride("font_size", 19); }
            _sheetStats.AddChild(l);
        }
        Stat("Vitals", true);
        Stat($"Health {me.Health.Current:0} / {me.Health.Max:0}");
        Stat($"Stamina {me.MaxStamina:0}");
        var (into, need) = Levels.Progress(me.Xp);
        Stat(need == 0 ? $"Level {me.Level} (the cap)" : $"Level {me.Level}: {into} / {need} XP to the next");
        Stat("Armour", true);
        foreach (var line in Gear.Summary(me)) Stat(line);
        Stat("Class", true);
        Stat($"{me.Class.Name}: {me.Class.AbilityName} on {Gamepad.ButtonName("ability")}");
        if (me.Class.Second is { } second)
            Stat(me.Level >= second.UnlockLevel
                ? $"{second.Name} on {Gamepad.ButtonName("ability_2")}"
                : $"{second.Name} unlocks at level {second.UnlockLevel}");
        if (me.Class.Third is { } third)
            Stat(me.Level >= third.UnlockLevel
                ? $"{third.Name} on {Gamepad.ButtonName("ability_3")}"
                : $"{third.Name} unlocks at level {third.UnlockLevel}");

        // Pack: everything you carry; armour is clickable to wear.
        var items = me.Pack.Items.OrderBy(kv => (ItemDb.Get(kv.Key)?.IsWearable ?? false) ? 0 : 1).ThenBy(kv => ItemDb.NameOf(kv.Key)).ToList();
        while (_sheetPack.GetChildCount() < items.Count)
        {
            var slot = new ItemSlot(68);
            slot.Pressed += () =>
            {
                if (ItemDb.Get(slot.ItemId)?.IsWearable ?? false) { Sfx.Ui("ui_click", -6f); Net.Instance.RequestWear(slot.ItemId); }
            };
            _sheetPack.AddChild(slot);
        }
        for (int i = 0; i < _sheetPack.GetChildCount(); i++)
        {
            var slot = (ItemSlot)_sheetPack.GetChild(i);
            slot.Visible = i < items.Count;
            if (i < items.Count)
            {
                slot.SetItem(items[i].Key, items[i].Value);
                if (ItemDb.Get(items[i].Key)?.IsWearable ?? false) slot.TooltipText += "\n(click to wear)";
            }
        }
    }

    // ================================================================== game menu (Esc)

    private void BuildGameMenu()
    {
        _gameMenu = Board(400, 500, "Menu", out var body, out _);
        var resume = new Button { Text = "Resume" };
        resume.Pressed += CloseMenus;
        body.AddChild(resume);
        var sheet = new Button { Text = "Character & equipment" };
        sheet.Pressed += OpenCharacter;
        body.AddChild(sheet);
        var looks = new Button { Text = "Appearance & class…" };
        looks.Pressed += () => OpenCreator();
        body.AddChild(looks);
        var journal = new Button { Text = "Journal" };
        journal.Pressed += () => OpenCharacter(journal: true);
        body.AddChild(journal);
        var help = new Button { Text = "Controls & help" };
        help.Pressed += () => ShowBoard(_help);
        body.AddChild(help);
        var graphics = new Button { Text = "Graphics…" };
        graphics.Pressed += () => OpenGraphics();
        body.AddChild(graphics);
        body.AddChild(UiTheme.Divider(300));
        var leave = new Button { Text = "Leave to main menu" };
        leave.Pressed += () => Net.Instance.Leave();
        body.AddChild(leave);
        var quit = new Button { Text = "Quit to desktop" };
        quit.Pressed += () =>
        {
            Net.Instance.Leave();
            GetTree().Quit();
        };
        body.AddChild(quit);
    }

    /// <summary>Pause menu: change your look mid-game. Done saves it and shows it to everyone.</summary>
    public CharacterCreator OpenCreator()
    {
        bool fromMenu = _gameMenu.Visible;
        _gameMenu.Visible = false;
        GameState.Instance.SetUiOpen(true);
        void Back() { if (fromMenu && GameState.Instance.UiOpen) _gameMenu.Visible = true; }
        var creator = new CharacterCreator(Appearance.Local, look =>
        {
            Appearance.Local = look;
            Appearance.SaveLocal();
            Net.Instance.RequestLook(look);
            Back();
        }, Back) { InGame = true };
        AddChild(creator);
        return creator;
    }

    /// <summary>Pause menu: window mode, size and vsync. Back returns to the menu.</summary>
    public GraphicsMenu OpenGraphics()
    {
        bool fromMenu = _gameMenu.Visible;
        _gameMenu.Visible = false;
        GameState.Instance.SetUiOpen(true);
        var menu = new GraphicsMenu(() =>
        {
            if (!fromMenu || !GameState.Instance.UiOpen) return;
            _gameMenu.Visible = true;
            if (Gamepad.UsingPad) Callable.From(() => FocusFirst(_gameMenu)).CallDeferred();
        });
        AddChild(menu);
        return menu;
    }

    // ================================================================== helpers

    private static void Place(Control parent, Control c, float al, float at, float ar, float ab,
        float offL, float offT, float offR, float offB)
    {
        parent.AddChild(c);
        c.AnchorLeft = al; c.AnchorTop = at; c.AnchorRight = ar; c.AnchorBottom = ab;
        c.OffsetLeft = offL; c.OffsetTop = offT; c.OffsetRight = offR; c.OffsetBottom = offB;
    }
}
