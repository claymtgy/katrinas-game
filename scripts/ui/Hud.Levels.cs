using Godot;

namespace KatrinasGame;

/// <summary>Levels on the HUD (GDD §6): the XP bar under the stamina bar with the level beside
/// it, "+50 XP" notes, and the "Level up" banner.</summary>
public partial class Hud
{
    private static readonly Color XpPink = new(1f, 0.55f, 0.82f);

    private FramedBar _xp;
    private Label _level;
    private PanelContainer _levelBanner;
    private Label _levelBannerTitle, _levelBannerGains;

    /// <summary>Under the stamina bar: "Lv 3" and a thin blossom-pink bar.</summary>
    private void BuildXpRow(VBoxContainer vitals)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        _xp = new FramedBar("bar_stamina", new Vector2(290, 18), XpPink) { Suffix = " XP", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        row.AddChild(_xp);
        _level = UiTheme.Label("", "Heading");
        _level.AddThemeFontSizeOverride("font_size", 16);
        _level.AddThemeColorOverride("font_color", XpPink);
        _level.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_level);
        vitals.AddChild(row);

        // Banner: "Level up" under where the goal banner shows, so both can show at once.
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        Place(_root, center, 0.5f, 0, 0.5f, 0, -400, 118, 400, 236);
        _levelBanner = new PanelContainer { ThemeTypeVariation = "Plate", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddChild(UiTheme.Label("Level up", "Small", HorizontalAlignment.Center));
        _levelBannerTitle = UiTheme.Label("", "Title", HorizontalAlignment.Center);
        _levelBannerTitle.AddThemeFontSizeOverride("font_size", 30);
        _levelBannerTitle.AddThemeColorOverride("font_color", XpPink);
        col.AddChild(_levelBannerTitle);
        _levelBannerGains = UiTheme.Label("", "Small", HorizontalAlignment.Center);
        col.AddChild(_levelBannerGains);
        _levelBanner.AddChild(col);
        center.AddChild(_levelBanner);
    }

    /// <summary>For the tests.</summary>
    public bool LevelBannerShown => _levelBanner.Visible;
    public string XpBarLevelText => _level.Text;

    private void UpdateXp(PlayerController player)
    {
        var (into, need) = Levels.Progress(player.Xp);
        _xp.Caption = need == 0 ? "Max level" : "";
        if (need == 0) _xp.Set(1, 1);
        else _xp.Set(into, need);
        _level.Text = $"Lv {player.Level}";
    }

    /// <summary>The local player earned XP (Net.C_Xp). levelUp = the new level, or 0.</summary>
    public void OnXp(int gained, string reason, int levelUp)
    {
        if (gained > 0 && reason != "") Toast($"+{gained} XP: {reason}");
        if (levelUp > 0) ShowLevelUp(levelUp);
    }

    private void ShowLevelUp(int level)
    {
        _levelBannerTitle.Text = $"Level {level}";
        string gains = $"+{Levels.HealthPerLevel:0} max health  ·  +{Levels.StaminaPerLevel:0} max stamina";
        _levelBannerGains.Text = level >= Levels.MaxLevel ? gains + "  ·  the level cap!" : gains;
        _levelBanner.Modulate = Colors.White;
        _levelBanner.Visible = true;
        UiTheme.PopIn(_levelBanner);
        Sfx.Ui("heal", 0f);
        Gamepad.Rumble(0.4f, 0.2f, 0.25f);
        var tw = _levelBanner.CreateTween();
        tw.TweenInterval(3.6);
        tw.TweenProperty(_levelBanner, "modulate:a", 0f, 0.7);
        tw.TweenCallback(Callable.From(() => _levelBanner.Visible = false));
    }
}
