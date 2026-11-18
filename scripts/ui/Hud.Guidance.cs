using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>The HUD's guidance and controller parts: the goal tracker under the clock, the
/// goal-complete banner, the gold marker that points to the goal, the Journal tab of the
/// character sheet, one-time tips, the trader's errand row, and menu focus for controllers.</summary>
public partial class Hud
{
    private PanelContainer _tracker, _banner, _tip;
    private Label _trackTitle, _trackHint, _trackProgress, _bannerTitle, _tipText;
    private GoalMarker _marker;
    private string _trackedId;
    private double _hintUntil, _nextGuidanceTick;
    private VBoxContainer _journalLeft, _journalRight;
    private ScrollContainer _journalScroll;

    /// <summary>One-time tips (off in scripted tests unless a test turns them on).</summary>
    public static bool TipsEnabled = !IsAutotest;
    private static bool IsAutotest => OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--autotest"));
    private readonly HashSet<string> _tipsSeen = new();
    private double _tipUntil;
    /// <summary>Tests: is the goal marker on screen; is the journal tab / Esc menu open.</summary>
    public bool MarkerShown => _marker.Visible;
    public bool JournalTabOpen => _sheet.Visible && _journalTab;
    public bool MenuOpen => _gameMenu.Visible;
    public void OpenHelp() => ShowBoard(_help);

    /// <summary>The tip on screen now ("" if none): for tests.</summary>
    public string TipShowing { get; private set; } = "";

    private void BuildGuidance()
    {
        // Tracker: a parchment note under the clock.
        _tracker = new PanelContainer { ThemeTypeVariation = "Parchment", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        Place(_root, _tracker, 0, 0, 0, 0, 18, 88, 440, 220);
        _tracker.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 1);
        _tracker.AddChild(col);
        var head = UiTheme.Label("GOAL", "OnParchment");
        head.AddThemeFontSizeOverride("font_size", 12);
        head.AddThemeColorOverride("font_color", new Color(0.5f, 0.3f, 0.1f));
        col.AddChild(head);
        _trackTitle = UiTheme.Label("", "OnParchment");
        _trackTitle.AddThemeFontOverride("font", UiTheme.TitleFont);
        _trackTitle.AddThemeFontSizeOverride("font_size", 19);
        col.AddChild(_trackTitle);
        _trackProgress = UiTheme.Label("", "OnParchment");
        _trackProgress.AddThemeFontSizeOverride("font_size", 14);
        col.AddChild(_trackProgress);
        _trackHint = UiTheme.Label("", "OnParchment");
        _trackHint.AddThemeFontSizeOverride("font_size", 14);
        _trackHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _trackHint.CustomMinimumSize = new Vector2(380, 0);
        col.AddChild(_trackHint);

        // Banner: "Goal complete" in the top centre.
        var bannerCenter = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_root, bannerCenter, 0.5f, 0, 0.5f, 0, -400, 14, 400, 108);
        _banner = new PanelContainer { ThemeTypeVariation = "Plate", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var bcol = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bcol.AddChild(UiTheme.Label("Goal complete", "Small", HorizontalAlignment.Center));
        _bannerTitle = UiTheme.Label("", "Title", HorizontalAlignment.Center);
        _bannerTitle.AddThemeFontSizeOverride("font_size", 26);
        _bannerTitle.AddThemeColorOverride("font_color", UiTheme.GoldBright);
        bcol.AddChild(_bannerTitle);
        _banner.AddChild(bcol);
        bannerCenter.AddChild(_banner);

        // Tips: bottom right.
        _tip = new PanelContainer { ThemeTypeVariation = "Parchment", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        Place(_root, _tip, 1, 1, 1, 1, -500, -270, -20, -96);
        _tip.GrowVertical = Control.GrowDirection.Begin;
        var tcol = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var th = UiTheme.Label("TIP", "OnParchment");
        th.AddThemeFontSizeOverride("font_size", 12);
        th.AddThemeColorOverride("font_color", new Color(0.5f, 0.3f, 0.1f));
        tcol.AddChild(th);
        _tipText = UiTheme.Label("", "OnParchment");
        _tipText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _tipText.CustomMinimumSize = new Vector2(440, 0);
        tcol.AddChild(_tipText);
        _tip.AddChild(tcol);

        // Marker: drawn over the world, under the boards.
        _marker = new GoalMarker { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.AddChild(_marker);
        _root.MoveChild(_marker, 0);
        _marker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        LoadTipsSeen();
        var journal = Journal.Instance;
        journal.Changed += OnJournalChanged;
        journal.GoalCompleted += OnGoalCompleted;
        OnJournalChanged();
    }

    private void UnhookGuidance()
    {
        Gamepad.Changed -= OnDeviceChanged;
        if (Journal.Instance == null) return;
        Journal.Instance.Changed -= OnJournalChanged;
        Journal.Instance.GoalCompleted -= OnGoalCompleted;
    }

    // ================================================================== tracker, banner, marker

    private void OnJournalChanged()
    {
        var goal = Journal.Instance.Current;
        if (goal?.Id != _trackedId)
        {
            _trackedId = goal?.Id;
            _hintUntil = Time.GetTicksMsec() / 1000.0 + 30;
            if (goal != null && _tracker.Visible) UiTheme.PopIn(_tracker);
        }
        RefreshTracker();
        if (_sheet.Visible && _journalTab) RefreshJournal();
        if (_craftPanel.Visible && _trader != null) RefreshCrafting();
    }

    private void RefreshTracker()
    {
        var goal = Journal.Instance.Current;
        bool inGame = GameState.Instance.LocalPlayer != null;
        if (goal == null)
        {
            _trackTitle.Text = "All goals done: the valley is yours";
            _trackProgress.Text = Gamepad.Format("Side goals and errands are in your journal ({journal}).");
            _trackProgress.Visible = true;
            _trackHint.Visible = false;
        }
        else
        {
            _trackTitle.Text = goal.Title;
            string progress = goal.Progress?.Invoke() ?? "";
            _trackProgress.Text = progress;
            _trackProgress.Visible = progress != "";
            _trackHint.Text = Gamepad.Format(goal.Hint);
            _trackHint.Visible = Time.GetTicksMsec() / 1000.0 < _hintUntil;
        }
        _tracker.Visible = inGame;
        _tracker.ResetSize();
    }

    private void OnGoalCompleted(Journal.Goal goal)
    {
        _bannerTitle.Text = goal.Title;
        _banner.Visible = true;
        UiTheme.PopIn(_banner);
        Sfx.Ui("heal", -4f);
        Gamepad.Rumble(0.3f, 0.1f, 0.15f);
        var tw = _banner.CreateTween();
        tw.TweenInterval(3.2);
        tw.TweenProperty(_banner, "modulate:a", 0f, 0.7);
        tw.TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    /// <summary>Per frame: the marker; twice a second: progress, tips.</summary>
    private void UpdateGuidance(GameState gs, PlayerController player)
    {
        var goal = Journal.Instance.Current;
        var cam = GetViewport().GetCamera3D();
        Vector3? target = null;
        try { target = goal?.Target?.Invoke(); } catch (Exception) { }
        _marker.Show(cam, target, player.GlobalPosition, !gs.UiOpen && !player.Sleeping);

        double now = Time.GetTicksMsec() / 1000.0;
        if (_tip.Visible && now > _tipUntil) HideTip();
        if (now < _nextGuidanceTick) return;
        _nextGuidanceTick = now + 0.5;
        RefreshTracker();
        if (TipsEnabled && !gs.UiOpen && !_tip.Visible) CheckTips(gs, player);
        if (_sheet.Visible && _journalTab) ScrollJournalWithStick();
    }

    // ================================================================== journal tab

    private Control BuildJournal()
    {
        _journalScroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false,
        };
        var cols = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cols.AddThemeConstantOverride("separation", 28);
        _journalScroll.AddChild(cols);
        _journalLeft = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.2f };
        _journalRight = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _journalLeft.AddThemeConstantOverride("separation", 6);
        _journalRight.AddThemeConstantOverride("separation", 4);
        cols.AddChild(_journalLeft);
        cols.AddChild(_journalRight);
        return _journalScroll;
    }

    private void RefreshJournal()
    {
        var j = Journal.Instance;
        foreach (var c in _journalLeft.GetChildren()) c.QueueFree();
        foreach (var c in _journalRight.GetChildren()) c.QueueFree();

        Label Text(VBoxContainer col, string text, string variation = "Small", Color? color = null)
        {
            var l = UiTheme.Label(text, variation);
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (color is { } c) l.AddThemeColorOverride("font_color", c);
            col.AddChild(l);
            return l;
        }

        // Left: now, next, the errand.
        _journalLeft.AddChild(JournalSection("Now"));
        var current = j.Current;
        var now = new PanelContainer { ThemeTypeVariation = "Parchment" };
        var nowCol = new VBoxContainer();
        now.AddChild(nowCol);
        _journalLeft.AddChild(now);
        if (current == null)
        {
            Text(nowCol, "Every main goal is done.", "OnParchment");
            Text(nowCol, "Build, explore, and help Marta. Raiders still come at dusk.", "OnParchment");
        }
        else
        {
            var t = Text(nowCol, current.Title, "OnParchment");
            t.AddThemeFontOverride("font", UiTheme.TitleFont);
            t.AddThemeFontSizeOverride("font_size", 20);
            string progress = current.Progress?.Invoke() ?? "";
            if (progress != "") Text(nowCol, progress, "OnParchment");
            Text(nowCol, Gamepad.Format(current.Hint), "OnParchment");
        }
        var ahead = Journal.Goals.Where(g => !g.Side && !j.Completed.Contains(g.Id) && g != current).Take(3).ToList();
        if (ahead.Count > 0)
        {
            _journalLeft.AddChild(JournalSection("After that"));
            foreach (var g in ahead) Text(_journalLeft, "·  " + g.Title, "Small", UiTheme.Muted);
            int more = Journal.Goals.Count(g => !g.Side && !j.Completed.Contains(g.Id)) - 1 - ahead.Count;
            if (more > 0) Text(_journalLeft, $"…and {more} more", "Dim");
        }
        _journalLeft.AddChild(JournalSection("Errand for Marta (Ashby)"));
        var e = j.CurrentErrand;
        Text(_journalLeft, $"Bring {e.Count} {ItemDb.NameOf(e.Item)}  →  {e.Reward} coins", "Small", UiTheme.GoldBright);
        Text(_journalLeft, $"Errands done: {j.ErrandsDone}", "Dim");

        // Right: done, side goals.
        int done = Journal.Goals.Count(g => !g.Side && j.Completed.Contains(g.Id));
        int total = Journal.Goals.Count(g => !g.Side);
        _journalRight.AddChild(JournalSection($"Done  {done}/{total}"));
        foreach (var g in Journal.Goals.Where(g => !g.Side && j.Completed.Contains(g.Id)))
            Text(_journalRight, "✓  " + g.Title, "Small", UiTheme.Good);
        if (done == 0) Text(_journalRight, "Nothing yet.", "Dim");
        _journalRight.AddChild(JournalSection("Side goals"));
        foreach (var g in Journal.Goals.Where(g => g.Side))
        {
            bool ok = j.Completed.Contains(g.Id);
            string progress = ok ? "" : g.Progress?.Invoke() ?? "";
            Text(_journalRight, (ok ? "✓  " : "◇  ") + g.Title + (progress != "" ? $"  ({progress})" : ""), "Small", ok ? UiTheme.Good : UiTheme.Ink);
            if (!ok) Text(_journalRight, "    " + Gamepad.Format(g.Hint), "Dim");
        }
    }

    private static Control JournalSection(string text)
    {
        var col = new VBoxContainer();
        col.AddChild(UiTheme.Label(text, "Heading"));
        col.AddChild(UiTheme.Divider(360));
        return col;
    }

    private void ShowSheetTab(bool journal, bool focus = true)
    {
        _journalTab = journal;
        _sheetCharacter.Visible = !journal;
        _sheetJournal.Visible = journal;
        _tabCharacter.SetPressedNoSignal(!journal);
        _tabJournal.SetPressedNoSignal(journal);
        BoardTitle(_sheet).Text = journal ? "Journal" : "Character";
        RefreshSheetHint();
        if (journal) RefreshJournal();
        if (focus && Gamepad.UsingPad && _sheet.Visible) Callable.From(() => FocusFirst(_sheet)).CallDeferred();
    }

    private void RefreshSheetHint()
    {
        _sheetHint.Text = Gamepad.Format(_journalTab
            ? (Gamepad.UsingPad ? "{tabs}: character · {back} to close" : "I: character · J or {back} to close")
            : (Gamepad.UsingPad
                ? "{accept} armour in your pack to wear it, or a worn piece to take it off · {tabs}: journal · {back} to close"
                : "Click armour in your pack to wear it · click a worn piece to take it off · J: journal · I or {back} to close"));
    }

    private void ScrollJournalWithStick()
    {
        float v = Input.GetAxis("look_up", "look_down");
        if (Mathf.Abs(v) > 0.2f) _journalScroll.ScrollVertical += (int)(v * 90);
    }

    // ================================================================== errands

    /// <summary>Trade board: Marta's errand at the top (hand over from your pack).</summary>
    private void AddErrandRow(PlayerController me)
    {
        var j = Journal.Instance;
        var e = j.CurrentErrand;
        int have = me?.Pack.Count(e.Item) ?? 0;
        _craftList.AddChild(Section("Errand"));
        var cost = new Dictionary<string, int> { [e.Item] = e.Count };
        var trader = _trader;
        _craftList.AddChild(RecipeRow(e.Item, $"Bring {e.Count} {ItemDb.NameOf(e.Item)}",
            $"Reward: {e.Reward} coins · you carry {have} · errands done: {j.ErrandsDone}", cost, me, "Hand over", have >= e.Count,
            () => { Sfx.Ui("craft", -4f); Net.Instance.RequestErrand(trader); }, useStock: false));
    }

    // ================================================================== tips

    private record Tip(string Id, Func<GameState, PlayerController, bool> When, string Text);

    private static readonly Tip[] Tips =
    {
        new("hurt", (gs, p) => p.Health.Current < p.Health.Max * 0.35f && !p.Health.IsDead,
            "You're badly hurt. Eat ({eat}), rest at a campfire, or pray at a Blossom shrine to heal."),
        new("enemy", (gs, p) => Wildlife.Instance != null && Wildlife.Instance.Enemies.Values.Any(e =>
                GodotObject.IsInstanceValid(e) && e.Current != Enemy.State.Dead && e.GlobalPosition.DistanceTo(p.GlobalPosition) < 18f),
            "Danger! Block ({block}) just as a blow lands to parry it, or roll away ({dodge})."),
        new("pack_full", (gs, p) => p.Pack.Load >= Supplies.PackCapacity,
            "Your pack is full. Empty it into the chest by the shack ({interact})."),
        new("climb_tired", (gs, p) => p.Climbing && p.Stamina < p.MaxStamina * 0.3f,
            "Climbing uses stamina. Rest on a ledge, or leap off ({jump}) before your grip fails."),
        new("dusk", (gs, p) => gs.TimeOfDay is > 0.70f and < 0.79f,
            "Night is coming, and wolves hunt in the dark. Sleep in the bed in the shack ({interact}) to skip the night, or stay by a fire."),
        new("raids", (gs, p) => gs.Day >= Wildlife.RaidFirstDay - 1 && gs.TimeOfDay > 0.55f && !gs.PlacedBuildings.Any(b => b.Id == "watchtower"),
            "Raiders come at dusk from day 3. A watchtower shoots at them, and palisades slow them down."),
        new("rain", (gs, p) => gs.Raining,
            "Rain waters your crops for you, but wet rock is slippery to climb."),
        new("armour", (gs, p) => p.Pack.Items.Keys.Any(id => ItemDb.Get(id)?.IsWearable ?? false),
            "You have armour in your pack. Put it on from the character sheet ({inventory})."),
        new("glider", (gs, p) => p.Pack.Count("glider") > 0,
            "You have a glider: jump, then press {jump} in the air to open it. Updrafts lift you high."),
        new("shard", (gs, p) => p.Pack.Count(Gear.ShardId) > 0,
            "Rune shards enchant armour at a Rune Altar (build one in build mode)."),
    };

    private void CheckTips(GameState gs, PlayerController player)
    {
        foreach (var tip in Tips)
        {
            if (_tipsSeen.Contains(tip.Id)) continue;
            bool show;
            try { show = tip.When(gs, player); } catch (Exception) { show = false; }
            if (!show) continue;
            ShowTip(tip.Id, tip.Text);
            return;
        }
    }

    /// <summary>Show a tip now (marks it seen).</summary>
    public void ShowTip(string id, string text)
    {
        _tipsSeen.Add(id);
        SaveTipsSeen();
        TipShowing = id;
        _tipText.Text = Gamepad.Format(text);
        _tip.Visible = true;
        _tip.ResetSize();
        UiTheme.PopIn(_tip);
        Sfx.Ui("ui_click", -10f);
        _tipUntil = Time.GetTicksMsec() / 1000.0 + 10;
    }

    private void HideTip()
    {
        _tip.Visible = false;
        TipShowing = "";
    }

    /// <summary>Tests: forget which tips were shown (in memory only).</summary>
    public void ResetTips()
    {
        _tipsSeen.Clear();
        HideTip();
    }

    private void LoadTipsSeen()
    {
        if (IsAutotest) return;
        var cfg = new ConfigFile();
        if (cfg.Load(Appearance.SettingsPath) != Error.Ok || !cfg.HasSection("tips")) return;
        foreach (var key in cfg.GetSectionKeys("tips")) _tipsSeen.Add(key);
    }

    private void SaveTipsSeen()
    {
        if (IsAutotest) return; // tests never touch the player's settings
        var cfg = new ConfigFile();
        cfg.Load(Appearance.SettingsPath);
        foreach (var id in _tipsSeen) cfg.SetValue("tips", id, true);
        cfg.Save(Appearance.SettingsPath);
    }

    // ================================================================== controller focus

    private void OnDeviceChanged()
    {
        FillHelp();
        for (int i = 0; i < _hotbar.Count; i++) _hotbar[i].SetKey(Gamepad.UsingPad ? "" : (i + 1).ToString());
        RefreshSheetHint();
        RefreshTracker();
        if (_sheet.Visible && _journalTab) RefreshJournal();
        if (!Gamepad.UsingPad || !GameState.Instance.UiOpen || GetViewport().GuiGetFocusOwner() != null) return;
        var open = new[] { _craftPanel, _gameMenu, _sheet, _help }.FirstOrDefault(b => b.Visible);
        if (open != null) FocusFirst(open);
    }

    private static IEnumerable<Control> Focusable(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is not Control c || !c.IsVisibleInTree() || c.IsQueuedForDeletion()) continue;
            if (c.FocusMode != Control.FocusModeEnum.None && c is not ScrollContainer && !(c is BaseButton { Disabled: true }))
                yield return c;
            foreach (var inner in Focusable(c)) yield return inner;
        }
    }

    /// <summary>Give the first button on a board the focus (controller).</summary>
    public static void FocusFirst(Control board)
    {
        if (!GodotObject.IsInstanceValid(board) || !board.Visible) return;
        Focusable(board).FirstOrDefault()?.GrabFocus();
    }

    private static int FocusIndex(Control board)
    {
        var owner = board.GetViewport()?.GuiGetFocusOwner();
        if (owner == null) return -1;
        return Focusable(board).ToList().IndexOf(owner);
    }

    private static void RestoreFocus(Control board, int index)
    {
        if (!GodotObject.IsInstanceValid(board) || !board.Visible) return;
        var list = Focusable(board).ToList();
        if (list.Count == 0) return;
        list[Mathf.Clamp(index, 0, list.Count - 1)].GrabFocus();
    }
}

/// <summary>A gold diamond over the goal, with its distance. Off screen it sits on the edge as an
/// arrow pointing the way.</summary>
public partial class GoalMarker : Control
{
    private Vector2 _pos;
    private bool _edge;
    private float _angle;
    private string _text = "";

    public void Show(Camera3D cam, Vector3? target, Vector3 from, bool allowed)
    {
        bool show = allowed && cam != null && target is { } t && t.DistanceTo(from) > 8f;
        if (!show) { if (Visible) Visible = false; return; }
        Visible = true;
        var world = target.Value + Vector3.Up * 2.2f;
        float dist = world.DistanceTo(from);
        _text = dist >= 1000 ? $"{dist / 1000f:0.0} km" : $"{dist:0} m";
        var size = GetViewportRect().Size;
        var centre = size / 2f;
        Vector2 p = cam.UnprojectPosition(world);
        bool behind = cam.IsPositionBehind(world);
        if (behind) p = centre - (p - centre) * 1000f; // mirror, and push off screen
        const float inset = 64f;
        var rect = new Rect2(inset, inset, size.X - inset * 2, size.Y - inset * 2);
        _edge = behind || !rect.HasPoint(p);
        if (_edge)
        {
            var dir = (p - centre);
            if (dir.LengthSquared() < 1f) dir = Vector2.Down;
            // Scale the direction until it touches the inset rectangle.
            float sx = dir.X != 0 ? (rect.Size.X / 2f) / Mathf.Abs(dir.X) : float.MaxValue;
            float sy = dir.Y != 0 ? (rect.Size.Y / 2f) / Mathf.Abs(dir.Y) : float.MaxValue;
            p = centre + dir * Mathf.Min(sx, sy);
            _angle = dir.Angle();
        }
        _pos = p;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var gold = UiTheme.GoldBright;
        var dark = UiTheme.Shadow;
        float pulse = 1f + 0.08f * Mathf.Sin(Time.GetTicksMsec() / 260f);
        if (_edge)
        {
            // An arrow on the edge of the screen.
            var fwd = Vector2.FromAngle(_angle);
            var side = new Vector2(-fwd.Y, fwd.X);
            float s = 16f * pulse;
            var tip = _pos + fwd * s;
            var pts = new[] { tip, _pos - fwd * s * 0.6f + side * s * 0.8f, _pos - fwd * s * 0.2f, _pos - fwd * s * 0.6f - side * s * 0.8f };
            DrawColoredPolygon(pts, dark);
            var inner = pts.Select(q => _pos + (q - _pos) * 0.72f).ToArray();
            DrawColoredPolygon(inner, gold);
        }
        else
        {
            float s = 11f * pulse;
            var outer = new[] { _pos + new Vector2(0, -s - 3), _pos + new Vector2(s + 3, 0), _pos + new Vector2(0, s + 3), _pos + new Vector2(-s - 3, 0) };
            var inner = new[] { _pos + new Vector2(0, -s), _pos + new Vector2(s, 0), _pos + new Vector2(0, s), _pos + new Vector2(-s, 0) };
            DrawColoredPolygon(outer, dark);
            DrawColoredPolygon(inner, gold);
            DrawColoredPolygon(inner.Select(q => _pos + (q - _pos) * 0.4f).ToArray(), new Color(1f, 0.98f, 0.85f));
        }
        var font = UiTheme.BodyFont;
        var at = _pos + new Vector2(0, _edge ? 34 : 32);
        var w = font.GetStringSize(_text, HorizontalAlignment.Left, -1, 16).X;
        DrawStringOutline(font, at - new Vector2(w / 2, 0), _text, HorizontalAlignment.Left, -1, 16, 5, dark);
        DrawString(font, at - new Vector2(w / 2, 0), _text, HorizontalAlignment.Left, -1, 16, gold);
    }
}
