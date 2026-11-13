using System.Linq;
using System.Text;
using Godot;

namespace KatrinasGame;

/// <summary>All on-screen UI, built in code: bars, inventory, hotbar, prompt, build info,
/// toasts, crosshair/hit marker, crafting menu and help overlay.</summary>
public partial class Hud : CanvasLayer
{
    public static Hud Instance { get; private set; }

    private static readonly Color Gold = new(1f, 0.82f, 0.45f);
    private static readonly Color Blush = new(1f, 0.7f, 0.78f);

    private ProgressBar _health, _stamina;
    private Label _inventory, _hotbar, _prompt, _buildInfo, _clock, _crosshair, _help;
    private VBoxContainer _toasts;
    private PanelContainer _craftPanel;
    private VBoxContainer _craftList;
    private Label _craftTitle;
    private CraftingStation _station;
    private float _hitFlash;
    private bool _inventoryDirty = true;

    public override void _Ready()
    {
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

        // Inventory — top right.
        _inventory = MakeLabel(18, HorizontalAlignment.Right);
        Place(root, _inventory, 1, 0, 1, 0, -360, 20, -24, 400);

        // Clock — top left.
        _clock = MakeLabel(18);
        _clock.Modulate = Gold;
        Place(root, _clock, 0, 0, 0, 0, 24, 20, 400, 50);

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
            "WASD move · Shift sprint · Space jump · Mouse look\n" +
            "V  first / third person\n" +
            "LMB light attack · RMB heavy attack · 1-5 weapon slots\n" +
            "E  interact (workbench, bed, shrine, signs)\n" +
            "B  build mode: wheel cycle · R rotate · LMB place · RMB exit\n" +
            "F5 save · F9 load · Esc free mouse / close menu\n\n" +
            "Goal: chop trees & mine rocks → build a Workbench → craft a Wooden Sword → defeat the training dummy.";
        _help.Modulate = Blush;
        Place(root, _help, 0, 0, 0, 0, 24, 60, 760, 280);
        GetTree().CreateTimer(25).Timeout += () => _help.Visible = false;

        BuildCraftPanel(root);

        var gs = GameState.Instance;
        gs.Inventory.Changed += () => _inventoryDirty = true;
        gs.Inventory.ItemAdded += (id, n) => Toast($"+{n} {ItemDb.NameOf(id)}");
    }

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
        _craftTitle.Text = station.StationName;
        RefreshCrafting();
        _craftPanel.Visible = true;
        GameState.Instance.SetUiOpen(true);
    }

    public void CloseMenus()
    {
        _craftPanel.Visible = false;
        _station = null;
        GameState.Instance.SetUiOpen(false);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("help"))
        {
            _help.Visible = !_help.Visible;
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
        var gs = GameState.Instance;
        var player = gs.Player;
        if (player == null) return;

        _health.MaxValue = player.Health.Max;
        _health.Value = player.Health.Current;
        _stamina.MaxValue = player.MaxStamina;
        _stamina.Value = player.Stamina;

        float hours = gs.TimeOfDay * 24f;
        int h = (int)hours, m = (int)((hours - h) * 60);
        string phase = gs.TimeOfDay is > 0.22f and < 0.78f ? "☀" : "☾";
        _clock.Text = $"Day {gs.Day}  {phase} {h:00}:{m:00}";

        if (_inventoryDirty) RefreshInventory();

        var slots = gs.WeaponSlots();
        var sb = new StringBuilder();
        for (int i = 0; i < slots.Count && i < 5; i++)
        {
            string name = slots[i] == "" ? "Fists" : ItemDb.NameOf(slots[i]);
            sb.Append(slots[i] == gs.Equipped ? $"  [{i + 1}] » {name} «  " : $"  [{i + 1}] {name}  ");
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
    }

    private void RefreshInventory()
    {
        _inventoryDirty = false;
        var items = GameState.Instance.Inventory.Items;
        _inventory.Text = items.Count == 0
            ? "Inventory: empty"
            : "Inventory\n" + string.Join("\n", items.OrderBy(kv => kv.Key).Select(kv => $"{ItemDb.NameOf(kv.Key)}  ×{kv.Value}"));
        if (_craftPanel.Visible) RefreshCrafting();
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
        Place(root, _craftPanel, 0.5f, 0.5f, 0.5f, 0.5f, -260, -180, 260, 180);

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
        if (_station == null) return;
        foreach (var child in _craftList.GetChildren()) child.QueueFree();
        var inv = GameState.Instance.Inventory;
        foreach (var recipe in _station.Recipes)
        {
            var cost = recipe.GetInputs();
            bool canAfford = inv.Has(cost);
            var button = new Button
            {
                Text = $"{recipe.DisplayName}   —   {ItemDb.FormatCost(cost)}",
                Disabled = !canAfford,
                TooltipText = canAfford ? "Click to craft" : "Not enough materials",
            };
            var r = recipe;
            button.Pressed += () =>
            {
                if (CraftingStation.TryCraft(r)) Toast($"Crafted {r.DisplayName}");
            };
            _craftList.AddChild(button);
        }
        if (_craftList.GetChildCount() == 0)
            _craftList.AddChild(MakeLabel(16, HorizontalAlignment.Center, "No recipes for this station yet."));
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
