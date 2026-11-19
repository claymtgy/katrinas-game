using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>The Talents tab of the character sheet (GDD §6): at levels 3 and 8 each class picks
/// one of two passives. The first pick works anywhere; changing it later only at home.</summary>
public partial class Hud
{
    private Button _tabTalents;
    private VBoxContainer _sheetTalents, _talentRows;
    private Label _talentNote;
    private int _talentsVersion = -1;

    /// <summary>The sheet's tabs: 0 = Equipment, 1 = Talents, 2 = Journal.</summary>
    private int _sheetPage;

    public bool TalentsTabOpen => _sheet.Visible && _sheetPage == 1;

    private VBoxContainer BuildTalents()
    {
        _sheetTalents = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false };
        _sheetTalents.AddThemeConstantOverride("separation", 10);
        _talentNote = UiTheme.Label("", "", HorizontalAlignment.Center);
        _talentNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sheetTalents.AddChild(_talentNote);
        _talentRows = new VBoxContainer();
        _talentRows.AddThemeConstantOverride("separation", 14);
        _sheetTalents.AddChild(_talentRows);
        return _sheetTalents;
    }

    private static int TalentsVersion(PlayerController me) =>
        System.HashCode.Combine(me.Level, me.Class.Id, string.Join(",", me.TalentPicks.OrderBy(k => k.Key).Select(k => k.Key + k.Value)));

    private void RefreshTalents()
    {
        var me = GameState.Instance.LocalPlayer;
        if (me == null || _talentRows == null) return;
        _talentsVersion = TalentsVersion(me);
        foreach (var c in _talentRows.GetChildren()) c.QueueFree();

        int open = Talents.Unpicked(me);
        _talentNote.Text = $"{me.Class.Name} · level {me.Level}.  " + (open > 0
            ? $"You have {open} talent{(open > 1 ? "s" : "")} to choose. Pick one of each pair."
            : "Picks are free to change at home.");

        foreach (int level in Talents.Levels)
        {
            var section = new VBoxContainer();
            section.AddThemeConstantOverride("separation", 6);
            bool reached = me.Level >= level;
            section.AddChild(UiTheme.Label(reached ? $"Level {level}" : $"Level {level}  (locked)", "Heading"));
            var pair = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            pair.AddThemeConstantOverride("separation", 16);
            me.TalentPicks.TryGetValue(Talents.Key(me.Class.Id, level), out var picked);
            foreach (var t in Talents.Choices(me.Class.Id, level))
            {
                bool chosen = picked == t.Id;
                var card = new Button
                {
                    Name = $"Talent_{t.Id}",
                    Text = $"{(chosen ? "✔ " : "")}{t.Name}\n{t.Text}",
                    CustomMinimumSize = new Vector2(440, 92),
                    Disabled = !reached,
                    ToggleMode = true,
                    ButtonPressed = chosen,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    FocusMode = Control.FocusModeEnum.All,
                };
                var id = t.Id;
                card.Pressed += () =>
                {
                    card.SetPressedNoSignal(chosen); // the server decides; the tab redraws when it does
                    if (chosen) return;
                    Sfx.Ui("ui_click", -6f);
                    Net.Instance.RequestTalent(id);
                };
                pair.AddChild(card);
            }
            section.AddChild(pair);
            _talentRows.AddChild(section);
        }
    }

    /// <summary>Open the character sheet on the Talents tab.</summary>
    public void OpenTalents()
    {
        OpenCharacter(false);
        ShowSheetPage(1, focus: false);
    }

    /// <summary>Scripted tests: is the downed screen up?</summary>
    public bool DownedScreenShown => _downedShade?.Visible == true;

    /// <summary>Scripted tests: the button for a talent on the open tab.</summary>
    public Button TalentButton(string talentId) => _talentRows?.FindChild($"Talent_{talentId}", true, false) as Button;

    /// <summary>A level with a talent to pick: say where to pick it.</summary>
    private void TalentLevelHint(int level)
    {
        if (Talents.Levels.Contains(level))
            Toast("A new talent to choose: open the character sheet (I), Talents tab.");
    }
}
