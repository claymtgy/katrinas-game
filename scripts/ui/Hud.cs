using System.Linq;
using System.Text;
using Godot;

namespace KatrinasGame;

/// <summary>All on-screen UI, built in code: bars, inventory, hotbar, prompt, build info,
/// toasts, crosshair/hit marker, crafting menu, help overlay, and the co-op bits: session status,
/// player list (Tab), sleep vote banner and the Esc game menu.</summary>
public partial class Hud : CanvasLayer
{
    public static Hud Instance { get; private set; }

    private static readonly Color Gold = new(1f, 0.82f, 0.45f);
    private static readonly Color Blush = new(1f, 0.7f, 0.78f);

    private ProgressBar _health, _stamina;
    private Label _inventory, _hotbar, _prompt, _buildInfo, _clock, _crosshair, _help, _session, _sleep, _playerList, _buffs, _ability;
    private int _packVersion = -1;
    private PanelContainer _gameMenu;
    private VBoxContainer _toasts;
    private PanelContainer _craftPanel;
    private VBoxContainer _craftList;
    private Label _craftTitle;
    private CraftingStation _station;
    private Trader _trader;
    private float _hitFlash;
    private bool _inventoryDirty = true;

    public override void _Ready()
    {
        if (Net.Instance.IsDedicated) return; // Main frees us; a server has no screen
        Instance = this;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Bars — bottom left.
        var bars = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(root, bars, 0, 1, 0, 1, 24, -84, 324, -24);
        _health = MakeBar(new Color(0.85f, 0.25f, 0.3f));
        _stamina = MakeBar(new Color(0.95f, 0.75f, 0.3f));
        bars.AddChild(_health);
        bars.AddChild(_stamina);

        // Food buffs — just above the bars.
        _buffs = MakeLabel(15);
        _buffs.Modulate = Gold;
        Place(root, _buffs, 0, 1, 0, 1, 24, -132, 600, -88);
        // Class and ability, right of the bars.
        _ability = MakeLabel(17);
        Place(root, _ability, 0, 1, 0, 1, 340, -86, 900, -40);

        // Inventory — top right.
        _inventory = MakeLabel(17, HorizontalAlignment.Right);
        Place(root, _inventory, 1, 0, 1, 0, -360, 20, -24, 700);

        // Clock — top left.
        _clock = MakeLabel(18);
        _clock.Modulate = Gold;
        Place(root, _clock, 0, 0, 0, 0, 24, 20, 400, 50);

        // Session status — under the clock.
        _session = MakeLabel(15);
        _session.Modulate = Blush;
        Place(root, _session, 0, 0, 0, 0, 24, 46, 600, 70);

        // Sleep vote — top centre.
        _sleep = MakeLabel(20, HorizontalAlignment.Center);
        _sleep.Modulate = Gold;
        Place(root, _sleep, 0.5f, 0, 0.5f, 0, -500, 60, 500, 120);

        // Player list (hold Tab) — centre.
        _playerList = MakeLabel(20, HorizontalAlignment.Center);
        _playerList.Visible = false;
        Place(root, _playerList, 0.5f, 0.5f, 0.5f, 0.5f, -300, -220, 300, -40);

        // Hotbar — bottom centre.
        _hotbar = MakeLabel(18, HorizontalAlignment.Center);
        Place(root, _hotbar, 0.5f, 1, 0.5f, 1, -500, -50, 500, -20);

        // Interact prompt and build info — below centre.
        _prompt = MakeLabel(22, HorizontalAlignment.Center);
        Place(root, _prompt, 0.5f, 0.5f, 0.5f, 0.5f, -400, 40, 400, 72);
        _buildInfo = MakeLabel(18, HorizontalAlignment.Center);
        _buildInfo.Modulate = Gold;
        Place(root, _buildInfo, 0.5f, 1, 0.5f, 1, -600, -140, 600, -60);

        // Crosshair.
        _crosshair = MakeLabel(22, HorizontalAlignment.Center);
        _crosshair.Text = "·";
        _crosshair.VerticalAlignment = VerticalAlignment.Center;
        Place(root, _crosshair, 0.5f, 0.5f, 0.5f, 0.5f, -20, -16, 20, 16);

        // Toasts — left middle.
        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(root, _toasts, 0, 0.5f, 0, 0.5f, 24, -60, 700, 200);

        // Help overlay.
        _help = MakeLabel(17);
        _help.Text =
            "CONTROLS   (F1 to toggle)\n" +
            "WASD move · Shift sprint · Space jump · Ctrl / C dodge roll · R class ability · Mouse look\n" +
            "V  first / third person\n" +
            "LMB light attack · Q / MMB heavy attack · hold RMB block (just in time = parry) · 1-5 weapons\n" +
            "E  interact (workbench, campfire, bed, plots, wells, chest, trader, signs) · F eat\n" +
            "What you gather goes in your pack: empty it into the chest by the shack. Crops need water (well, pool or rain).\n" +
            "B  build mode: wheel cycle · R rotate · LMB place · RMB exit\n" +
            "F5 save · F9 load (host only) · Tab players · Esc menu\n\n" +
            "Goal: chop & mine → Workbench → sword → follow the road south to Ashby → clear the bandit camp\n" +
            "→ farm and trade for nails → upgrade the shack to a cottage (board by the shack).";
        _help.Modulate = Blush;
        Place(root, _help, 0, 0, 0, 0, 24, 80, 1000, 330);
        GetTree().CreateTimer(25).Timeout += () => _help.Visible = false;

        BuildCraftPanel(root);
        BuildGameMenu(root);

        // The stockpile outlives this scene, so unsubscribe in _ExitTree.
        GameState.Instance.Inventory.Changed += OnInventoryChanged;
    }

    public override void _ExitTree()
    {
        GameState.Instance.Inventory.Changed -= OnInventoryChanged;
        if (Instance == this) Instance = null;
    }

    private void OnInventoryChanged() => _inventoryDirty = true;

    // ------------------------------------------------------------------ public API

    public static void Toast(string message)
    {
        GD.Print($"[Toast] {message}");
        if (Instance == null) return;
        var label = Instance.MakeLabel(18);
        label.Text = message;
        Instance._toasts.AddChild(label);
        if (Instance._toasts.GetChildCount() > 6) Instance._toasts.GetChild(0).QueueFree();
        var tween = label.CreateTween();
        tween.TweenInterval(2.2);
        tween.TweenProperty(label, "modulate:a", 0f, 0.6);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    public void FlashHitMarker() => _hitFlash = 0.15f;

    public void OpenCrafting(CraftingStation station)
    {
        _station = station;
        _trader = null;
        _craftTitle.Text = station.StationName;
        RefreshCrafting();
        _craftPanel.Visible = true;
        GameState.Instance.SetUiOpen(true);
    }

    /// <summary>Buy from / sell to a trader (stockpile goods for coins).</summary>
    public void OpenTrade(Trader trader)
    {
        _station = null;
        _trader = trader;
        _craftTitle.Text = trader.TraderName;
        RefreshCrafting();
        _craftPanel.Visible = true;
        GameState.Instance.SetUiOpen(true);
    }

    public void CloseMenus()
    {
        _craftPanel.Visible = false;
        _gameMenu.Visible = false;
        foreach (var c in GetChildren())
            if (c is CharacterCreator cc) cc.QueueFree();
        _station = null;
        _trader = null;
        GameState.Instance.SetUiOpen(false);
    }

    /// <summary>Esc menu. The game keeps running (it's multiplayer, even solo).</summary>
    public void OpenGameMenu()
    {
        _gameMenu.Visible = true;
        GameState.Instance.SetUiOpen(true);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (Instance != this) return;
        if (e.IsActionPressed("help"))
        {
            _help.Visible = !_help.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsAction("player_list"))
        {
            _playerList.Visible = e.IsPressed();
            GetViewport().SetInputAsHandled();
        }
        else if (_craftPanel.Visible && e.IsActionPressed("interact"))
        {
            CloseMenus();
            GetViewport().SetInputAsHandled();
        }
    }

    // ------------------------------------------------------------------ per-frame

    public override void _Process(double delta)
    {
        if (Instance != this) return;
        var gs = GameState.Instance;
        var net = Net.Instance;
        UpdateSession(gs, net);
        var player = gs.LocalPlayer;
        if (player == null) return;

        _health.MaxValue = player.Health.Max;
        _health.Value = player.Health.Current;
        _stamina.MaxValue = player.MaxStamina;
        _stamina.Value = player.Stamina;

        float hours = gs.TimeOfDay * 24f;
        int h = (int)hours, m = (int)((hours - h) * 60);
        string phase = gs.TimeOfDay is > 0.22f and < 0.78f ? "☀" : "☾";
        _clock.Text = $"Day {gs.Day}  {phase} {h:00}:{m:00}";

        if (player.Pack.Version != _packVersion) { _packVersion = player.Pack.Version; _inventoryDirty = true; }
        if (_inventoryDirty) RefreshInventory();
        UpdateBuffs(player);

        var slots = gs.WeaponSlots(player);
        var sb = new StringBuilder();
        for (int i = 0; i < slots.Count && i < 5; i++)
        {
            string name = slots[i] == "" ? "Fists" : ItemDb.NameOf(slots[i]);
            sb.Append(slots[i] == player.Equipped ? $"  [{i + 1}] » {name} «  " : $"  [{i + 1}] {name}  ");
        }
        _hotbar.Text = sb.ToString();

        var target = player.Interactor.Target;
        _prompt.Text = target != null && !gs.UiOpen ? $"[E] {target.InteractPrompt}" : "";

        var build = player.Build;
        if (build.Active && build.Current != null)
        {
            var def = build.Current;
            string status = build.InvalidReason == "" ? "LMB to place" : build.InvalidReason;
            _buildInfo.Text = $"BUILD: {def.DisplayName}  ({ItemDb.FormatCost(def.GetCost())})  —  {status}\n" +
                              "Wheel: change · R: rotate · RMB/B: exit";
        }
        else _buildInfo.Text = "";

        _hitFlash -= (float)delta;
        _crosshair.Text = _hitFlash > 0 ? "✕" : "·";
        _crosshair.Modulate = _hitFlash > 0 ? new Color(1, 0.4f, 0.3f) : Colors.White;
        _crosshair.Visible = !gs.UiOpen;

        // Sleep vote.
        if (player.Sleeping)
            _sleep.Text = net.SleepTotal > 1
                ? $"Sleeping… {net.SleepingCount}/{net.SleepTotal} asleep. The night passes when everyone is in bed.\n[E] or move to get up"
                : "Sleeping…";
        else if (net.SleepingCount > 0)
            _sleep.Text = $"{net.SleepingCount}/{net.SleepTotal} asleep. Go to bed to skip the night.";
        else _sleep.Text = "";
    }

    private void UpdateSession(GameState gs, Net net)
    {
        if (!net.IsMultiplayer)
        {
            _session.Text = "";
            _playerList.Text = "";
            return;
        }
        string role = net.Mode == NetMode.Client ? "Co-op" : "Hosting";
        _session.Text = $"{role} · {gs.Players.Count}/{net.Config.MaxPlayers} players · PVP {(net.Pvp ? "ON" : "off")}";
        if (_playerList.Visible)
        {
            var lines = gs.Players.Values.OrderBy(p => p.PeerId).Select(p =>
                $"{p.DisplayName}{(p.PeerId == 1 ? "  (host)" : "")}{(p.IsLocal ? "  ← you" : "")}" +
                $"   HP {p.Health.Current:0}{(p.Sleeping ? "   asleep" : "")}");
            _playerList.Text = $"{net.Config.Name}\n\n" + string.Join("\n", lines);
        }
    }

    private void RefreshInventory()
    {
        _inventoryDirty = false;
        var sb = new StringBuilder();
        var player = GameState.Instance.LocalPlayer;
        if (player != null)
        {
            var pack = player.Pack;
            sb.Append($"Pack  {pack.Load}/{Supplies.PackCapacity}{(pack.Load >= Supplies.PackCapacity ? "  FULL" : "")}\n");
            foreach (var kv in pack.Items.OrderBy(kv => kv.Key)) sb.Append($"{ItemDb.NameOf(kv.Key)}  ×{kv.Value}\n");
            sb.Append('\n');
        }
        var items = GameState.Instance.Inventory.Items;
        string title = Net.Instance.IsMultiplayer ? "Stockpile (shared)" : "Stockpile";
        if (player != null && !Supplies.AtHome(player)) title += " (at home)";
        sb.Append(items.Count == 0
            ? $"{title}: empty"
            : $"{title}\n" + string.Join("\n", items.OrderBy(kv => kv.Key).Select(kv => $"{ItemDb.NameOf(kv.Key)}  ×{kv.Value}")));
        _inventory.Text = sb.ToString();
        if (_craftPanel.Visible) RefreshCrafting();
    }

    private bool _wasHome;

    private void UpdateAbility(PlayerController player)
    {
        var cls = player.Class;
        float left = player.Ability.CooldownLeft;
        string state = player.Vanished ? "hidden" : left > 0 ? $"{left:0.0}s" : player.Stamina < cls.AbilityStamina ? "tired" : "ready";
        _ability.Text = $"{cls.Name}   [R] {cls.AbilityName}: {state}";
        _ability.Modulate = state == "ready" ? cls.Tint : new Color(0.75f, 0.72f, 0.8f);
    }

    private void UpdateBuffs(PlayerController player)
    {
        UpdateAbility(player);
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

    // ------------------------------------------------------------------ crafting panel

    private void BuildCraftPanel(Control root)
    {
        _craftPanel = new PanelContainer { Visible = false };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.06f, 0.13f, 0.92f),
            BorderColor = Gold,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        _craftPanel.AddThemeStyleboxOverride("panel", style);
        Place(root, _craftPanel, 0.5f, 0.5f, 0.5f, 0.5f, -280, -260, 280, 260);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        _craftPanel.AddChild(vbox);

        _craftTitle = MakeLabel(26, HorizontalAlignment.Center);
        _craftTitle.Modulate = Gold;
        vbox.AddChild(_craftTitle);

        _craftList = new VBoxContainer();
        _craftList.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(_craftList);

        var close = new Button { Text = "Close  [Esc / E]" };
        close.Pressed += CloseMenus;
        vbox.AddChild(close);
    }

    private void RefreshCrafting()
    {
        foreach (var child in _craftList.GetChildren()) child.QueueFree();
        if (_trader != null) { RefreshTrade(); return; }
        if (_station == null) return;
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
            var button = new Button
            {
                Text = $"{recipe.DisplayName}   —   {ItemDb.FormatCost(cost)}",
                Disabled = !canAfford,
                TooltipText = canAfford ? "Click to craft" : "Not enough materials",
            };
            var r = recipe;
            var station = _station;
            button.Pressed += () => { Sfx.Ui("craft", -4f); station.RequestCraft(r); };
            _craftList.AddChild(button);
        }
        if (_craftList.GetChildCount() == 0)
            _craftList.AddChild(MakeLabel(16, HorizontalAlignment.Center, "No recipes for this station yet."));
    }

    private void RefreshTrade()
    {
        // Trading is done from what you carry (your pack), not the stockpile at home.
        var inv = GameState.Instance.LocalPlayer?.Pack ?? new Inventory();
        int coins = inv.Count("coin");
        _craftList.AddChild(MakeLabel(17, HorizontalAlignment.Center, $"Coins in your pack: {coins}"));
        _craftList.AddChild(MakeLabel(16, HorizontalAlignment.Left, "Buy"));
        foreach (var item in ItemDb.Items.Values.Where(i => i.BuyPrice > 0).OrderBy(i => i.BuyPrice))
        {
            var id = item.Id;
            var b = new Button { Text = $"{item.DisplayName}   —   {item.BuyPrice} coins", Disabled = coins < item.BuyPrice };
            b.Pressed += () => { Sfx.Ui("ui_click", -6f); Net.Instance.RequestTrade(_trader, id, buy: true); };
            _craftList.AddChild(b);
        }
        _craftList.AddChild(MakeLabel(16, HorizontalAlignment.Left, "Sell"));
        foreach (var item in ItemDb.Items.Values.Where(i => i.SellPrice > 0).OrderBy(i => i.DisplayName))
        {
            var id = item.Id;
            int have = inv.Count(id);
            var b = new Button { Text = $"{item.DisplayName} (×{have})   —   +{item.SellPrice} coins", Disabled = have == 0 };
            b.Pressed += () => { Sfx.Ui("ui_click", -6f); Net.Instance.RequestTrade(_trader, id, buy: false); };
            _craftList.AddChild(b);
        }
    }

    // ------------------------------------------------------------------ game menu (Esc)

    private void BuildGameMenu(Control root)
    {
        _gameMenu = new PanelContainer { Visible = false };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.06f, 0.13f, 0.92f),
            BorderColor = Gold,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(8);
        _gameMenu.AddThemeStyleboxOverride("panel", style);
        Place(root, _gameMenu, 0.5f, 0.5f, 0.5f, 0.5f, -180, -140, 180, 140);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        _gameMenu.AddChild(vbox);
        var title = MakeLabel(26, HorizontalAlignment.Center, "Menu");
        title.Modulate = Gold;
        vbox.AddChild(title);

        var resume = new Button { Text = "Resume  [Esc]" };
        resume.Pressed += CloseMenus;
        vbox.AddChild(resume);
        var looks = new Button { Text = "Character…" };
        looks.Pressed += () => OpenCreator();
        vbox.AddChild(looks);
        var leave = new Button { Text = "Leave to main menu" };
        leave.Pressed += () => Net.Instance.Leave();
        vbox.AddChild(leave);
        var quit = new Button { Text = "Quit to desktop" };
        quit.Pressed += () =>
        {
            Net.Instance.Leave();
            GetTree().Quit();
        };
        vbox.AddChild(quit);
    }

    /// <summary>Pause menu: change your look mid-game. Done saves it and shows it to everyone.</summary>
    public CharacterCreator OpenCreator()
    {
        bool fromMenu = _gameMenu.Visible;
        _gameMenu.Visible = false;
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

    // ------------------------------------------------------------------ helpers

    private Label MakeLabel(int size, HorizontalAlignment align = HorizontalAlignment.Left, string text = "")
    {
        var label = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.04f, 0.1f));
        return label;
    }

    private static ProgressBar MakeBar(Color fill)
    {
        var bar = new ProgressBar
        {
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(300, 18),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var bg = new StyleBoxFlat { BgColor = new Color(0.06f, 0.05f, 0.12f, 0.75f) };
        bg.SetCornerRadiusAll(4);
        var fg = new StyleBoxFlat { BgColor = fill };
        fg.SetCornerRadiusAll(4);
        bar.AddThemeStyleboxOverride("background", bg);
        bar.AddThemeStyleboxOverride("fill", fg);
        return bar;
    }

    private static void Place(Control parent, Control c, float al, float at, float ar, float ab,
        float offL, float offT, float offR, float offB)
    {
        parent.AddChild(c);
        c.AnchorLeft = al; c.AnchorTop = at; c.AnchorRight = ar; c.AnchorBottom = ab;
        c.OffsetLeft = offL; c.OffsetTop = offT; c.OffsetRight = offR; c.OffsetBottom = offB;
    }
}
