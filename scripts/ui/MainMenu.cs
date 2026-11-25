using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Title screen: Single Player, Host, Join, Quit. Built in code like the HUD.
///
/// Command line (after "--"), for servers and testing:
///   --server [--config=path]             dedicated server (use with --headless), reads server.cfg
///   --host [--port=N] [--max-players=N] [--pvp] [--world=name] [--password=pw]
///   --join=address[:port] [--password=pw]
///   --solo [--world=name]                single player
///   --name=Player                        your player name
///   --autotest / --autotest-mp=host|client   scripted tests (see DevAutotest / NetAutotest)</summary>
public partial class MainMenu : Control
{
    private const string SettingsPath = "user://settings.cfg";
    private static readonly Color Gold = new(1f, 0.82f, 0.45f);
    private static readonly Color Blush = new(1f, 0.7f, 0.78f);

    private LineEdit _name, _address, _password;
    /// <summary>The world last played (preselected nowhere, but kept for the --world default).</summary>
    private string _lastWorld = "home";
    private SpinBox _port, _maxPlayers;
    private CheckBox _pvp;
    private Label _status;
    /// <summary>The command line starts at most one session per launch (not again after leaving).</summary>
    private static bool _argsHandled;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Appearance.LoadLocal();
        if (HandleCommandLine()) return;
        BuildUi();
        LoadSettings();
        Gamepad.Changed += OnDeviceChanged;
        if (Gamepad.UsingPad) Callable.From(OnDeviceChanged).CallDeferred();
        var err = Net.Instance.LastError;
        if (err != "") SetStatus(err, error: true);
    }

    public override void _ExitTree() => Gamepad.Changed -= OnDeviceChanged;

    /// <summary>A controller: put the focus on the first button so the d-pad works.</summary>
    private void OnDeviceChanged()
    {
        if (Gamepad.UsingPad && IsInsideTree() && GetViewport().GuiGetFocusOwner() == null) Hud.FocusFirst(this);
    }

    // ------------------------------------------------------------------ command line

    private static string Arg(string[] args, string key, string fallback = null) =>
        args.FirstOrDefault(a => a.StartsWith(key + "="))?.Substring(key.Length + 1) ?? fallback;

    /// <summary>Returns true if the command line started a session (no menu needed).</summary>
    private bool HandleCommandLine()
    {
        if (_argsHandled) return false;
        _argsHandled = true;
        var args = OS.GetCmdlineUserArgs();
        var net = Net.Instance;
        net.LocalName = Arg(args, "--name", net.LocalName);
        string mpTest = Arg(args, "--autotest-mp");

        if (args.Contains("--server"))
        {
            string path = Arg(args, "--config", ServerConfig.DefaultPath);
            var cfg = ServerConfig.LoadOrCreate(path);
            GD.Print($"[Server] Config: {ProjectSettings.GlobalizePath(path)}");
            if (net.StartDedicated(cfg) != Error.Ok) GetTree().Quit(1);
            return true;
        }
        if (args.Contains("--host") || mpTest == "host")
        {
            var cfg = new ServerConfig
            {
                Name = $"{net.LocalName}'s world",
                Port = int.Parse(Arg(args, "--port", ServerConfig.DefaultPort.ToString())),
                MaxPlayers = int.Parse(Arg(args, "--max-players", ServerConfig.DesignedMaxPlayers.ToString())),
                Pvp = args.Contains("--pvp"),
                World = Arg(args, "--world", mpTest != null ? "autotest_mp" : "home"),
                Password = Arg(args, "--password", ""),
            };
            StartTests(mpTest);
            if (net.StartHost(cfg) != Error.Ok) GetTree().Quit(1);
            return true;
        }
        var join = Arg(args, "--join") ?? (mpTest == "client" ? "127.0.0.1" : null);
        if (join != null)
        {
            var (address, port) = ParseAddress(join, int.Parse(Arg(args, "--port", ServerConfig.DefaultPort.ToString())));
            StartTests(mpTest);
            if (net.Join(address, port, Arg(args, "--password", "")) != Error.Ok) GetTree().Quit(1);
            return true;
        }
        if (args.Contains("--solo") || args.Contains("--autotest"))
        {
            StartTests(null);
            net.StartOffline(Arg(args, "--world", args.Contains("--autotest") ? "autotest" : "home"));
            return true;
        }
        return false;
    }

    private static void StartTests(string mpRole)
    {
        // Tests use their own looks and never write the player's settings.
        Appearance.Persist = false;
        GraphicsSettings.Persist = false;
        Appearance.Local = new Appearance();
        // Scripted tests always start from a fresh world, and spawn wolves themselves.
        Wildlife.AutoSpawn = false;
        AmbientLife.Enabled = false; // no stray birds in the screenshots
        Net.StarterWeapons = false; // tests count the stockpile's weapons exactly
        AudioSettings.Persist = false;
        foreach (var world in new[] { "autotest", "autotest_mp" })
            if (FileAccess.FileExists($"user://saves/{world}.json")) DirAccess.RemoveAbsolute($"user://saves/{world}.json");

        if (mpRole != null) NetAutotest.Start(GameState.Instance, mpRole);
        else DevAutotest.MaybeStart(GameState.Instance);
    }

    private static (string, int) ParseAddress(string text, int defaultPort)
    {
        text = text.StripEdges();
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out int port)) return (text[..colon], port);
        return (text, defaultPort);
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = UiTheme.Theme;
        // Dusk: deep plum at the top warming to an ember glow below.
        var sky = new Gradient();
        sky.SetColor(0, new Color(0.07f, 0.045f, 0.09f));
        sky.SetColor(1, new Color(0.32f, 0.12f, 0.06f));
        sky.AddPoint(0.6f, new Color(0.14f, 0.06f, 0.08f));
        AddChild(new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = sky, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1), Width = 8, Height = 256 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, AnchorRight = 1, AnchorBottom = 1,
        });

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        center.AddChild(outer);
        var title = new Label { Text = "Ember & Blossom", ThemeTypeVariation = "Title", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 64);
        title.AddThemeConstantOverride("outline_size", 10);
        outer.AddChild(title);
        var sub = Label("A cliffside base-building adventure · alone or with up to 3 friends", 17, Blush);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        outer.AddChild(sub);
        var divider = UiTheme.Divider(520);
        outer.AddChild(divider);
        var board = new PanelContainer();
        outer.AddChild(board);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(580, 0) };
        col.AddThemeConstantOverride("separation", 10);
        board.AddChild(col);

        _name = Field(col, "Your name", "Player");
        var looks = new Button { Text = "Customise new character…", CustomMinimumSize = new Vector2(0, 36) };
        looks.Pressed += () => OpenCreator();
        col.AddChild(looks);
        var solo = BigButton("Single Player");
        solo.Pressed += OnSolo;
        col.AddChild(solo);

        col.AddChild(Section("Host a co-op game"));
        var hostRow = new HBoxContainer();
        hostRow.AddThemeConstantOverride("separation", 10);
        col.AddChild(hostRow);
        hostRow.AddChild(Label("Max players", 16));
        _maxPlayers = new SpinBox { MinValue = 2, MaxValue = ServerConfig.DesignedMaxPlayers, Value = ServerConfig.DesignedMaxPlayers };
        hostRow.AddChild(_maxPlayers);
        _pvp = new CheckBox { Text = "PVP" };
        hostRow.AddChild(_pvp);
        hostRow.AddChild(Label("Port", 16));
        _port = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = ServerConfig.DefaultPort };
        hostRow.AddChild(_port);
        var host = BigButton("Host Game…");
        host.Pressed += OnHost;
        col.AddChild(host);

        col.AddChild(Section("Join a friend"));
        _address = Field(col, "Address (ip or ip:port)", "127.0.0.1");
        _password = Field(col, "Password (if any)", "");
        _password.Secret = true;
        var join = BigButton("Join Game…");
        join.Pressed += OnJoin;
        col.AddChild(join);

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var graphics = new Button { Text = "Options…" };
        graphics.Pressed += () => AddChild(new OptionsMenu(() => { if (Gamepad.UsingPad) Hud.FocusFirst(this); }));
        col.AddChild(graphics);
        var quit = new Button { Text = "Quit" };
        quit.Pressed += () => GetTree().Quit();
        col.AddChild(quit);

        _status = Label("", 16, Blush);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        col.AddChild(_status);

        var hint = Label("Dedicated server: run with  --headless -- --server  (settings in server.cfg)", 13, new Color(0.7f, 0.7f, 0.8f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(hint);
    }

    /// <summary>Open the character creator over the menu; Done keeps the look in settings.</summary>
    public CharacterCreator OpenCreator()
    {
        // The default look for new characters; a world keeps its own copy once you've played there.
        var creator = new CharacterCreator(Appearance.Local, look =>
        {
            Appearance.Local = look;
            SaveSettings();
        }, null);
        AddChild(creator);
        return creator;
    }

    // Every way into a world goes: (pick a world) -> pick a class -> go.

    private void OnSolo()
    {
        Apply();
        PickWorld("Single Player", world => PickClass(world, () => Net.Instance.StartOffline(world)));
    }

    private void OnHost()
    {
        Apply();
        PickWorld("Host a Co-op Game", world => PickClass(world, () =>
        {
            var cfg = new ServerConfig
            {
                Name = $"{Net.Instance.LocalName}'s world",
                Port = (int)_port.Value,
                MaxPlayers = (int)_maxPlayers.Value,
                Pvp = _pvp.ButtonPressed,
                World = world,
            };
            if (Net.Instance.StartHost(cfg) != Error.Ok) SetStatus(Net.Instance.LastError, error: true);
        }));
    }

    private void OnJoin()
    {
        Apply();
        var (address, port) = ParseAddress(_address.Text, (int)_port.Value);
        if (address == "") { SetStatus("Enter the host's address.", error: true); return; }
        PickClass(null, () =>
        {
            if (Net.Instance.Join(address, port, _password.Text) != Error.Ok) SetStatus(Net.Instance.LastError, error: true);
            else SetStatus($"Connecting to {address}:{port}…", error: false);
        });
    }

    /// <summary>The save-slot list; picking (or creating) a world calls <paramref name="then"/>.</summary>
    public SaveSlotsMenu PickWorld(string title, System.Action<string> then)
    {
        var menu = new SaveSlotsMenu(title, Net.Instance.LocalName, world =>
        {
            _lastWorld = world;
            SaveSettings();
            then(world);
        }, Refocus);
        AddChild(menu);
        return menu;
    }

    /// <summary>Choose a class before spawning a new character. A world that already has your
    /// character keeps its class and looks (they're saved with the world), so you go straight in.
    /// (Joining a server: the server knows; a returning character's pick is ignored there.)</summary>
    public ClassPicker PickClass(string world, System.Action then)
    {
        string current = world != null ? SaveSlots.ClassIn(world, Net.Instance.LocalName) : "";
        if (PlayerClasses.IsValid(current)) { then(); return null; }
        current = Appearance.Local.Class;
        var picker = new ClassPicker(current, id =>
        {
            var look = Appearance.Local.Clone();
            look.Class = id;
            Appearance.Local = look;
            SaveSettings();
            then();
        }, Refocus, ClassPicker.Context.BeforeSpawn);
        AddChild(picker);
        return picker;
    }

    private void Refocus()
    {
        if (Gamepad.UsingPad && IsInsideTree()) Callable.From(() => Hud.FocusFirst(this)).CallDeferred();
    }

    private void Apply()
    {
        Net.Instance.LocalName = _name.Text.StripEdges() == "" ? "Player" : _name.Text.StripEdges();
        Net.Instance.LastError = "";
        SaveSettings();
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.Modulate = error ? new Color(1f, 0.5f, 0.45f) : Blush;
    }

    private void LoadSettings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SettingsPath) != Error.Ok) return;
        _name.Text = cfg.GetValue("player", "name", _name.Text).AsString();
        _lastWorld = cfg.GetValue("player", "world", _lastWorld).AsString();
        _address.Text = cfg.GetValue("join", "address", _address.Text).AsString();
        _port.Value = cfg.GetValue("host", "port", _port.Value).AsDouble();
        _maxPlayers.Value = cfg.GetValue("host", "max_players", _maxPlayers.Value).AsDouble();
        _pvp.ButtonPressed = cfg.GetValue("host", "pvp", false).AsBool();
    }

    private void SaveSettings()
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath); // keep the other sections (graphics, tips)
        cfg.SetValue("player", "name", _name.Text);
        cfg.SetValue("player", "world", _lastWorld);
        cfg.SetValue("join", "address", _address.Text);
        cfg.SetValue("host", "port", _port.Value);
        cfg.SetValue("host", "max_players", _maxPlayers.Value);
        cfg.SetValue("host", "pvp", _pvp.ButtonPressed);
        if (!Appearance.Persist) return;
        Appearance.Local.SaveTo(cfg);
        cfg.Save(SettingsPath);
    }

    // ------------------------------------------------------------------ widgets

    private static Label Label(string text, int size, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color != null) l.Modulate = color.Value;
        return l;
    }

    private static Control Section(string text)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        col.AddChild(new Label { Text = text, ThemeTypeVariation = "Heading" });
        col.AddChild(UiTheme.Divider(540));
        return col;
    }

    private static LineEdit Field(Container parent, string label, string value)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);
        var l = Label(label, 16);
        l.CustomMinimumSize = new Vector2(190, 0);
        row.AddChild(l);
        var edit = new LineEdit { Text = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(edit);
        return edit;
    }

    private static Button BigButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44) };
        b.AddThemeFontSizeOverride("font_size", 20);
        return b;
    }
}
