using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>The character creator: a live 3D preview (drag to turn) beside the choices: body,
/// skin, hair, outfit and trim colours, cape, helmet, height, plus Randomise. Opened from the
/// main menu and from the in-game pause menu. Built in code like the rest of the UI.</summary>
public partial class CharacterCreator : Control
{
    private static readonly Color Gold = new(1f, 0.82f, 0.45f);

    private readonly Appearance _look;
    private readonly string _startClass;
    /// <summary>Opened from the pause menu (class changes then need you to be at home).</summary>
    public bool InGame { get; set; }
    private readonly Action<Appearance> _onDone;
    private readonly Action _onCancel;

    private SubViewport _viewport;
    private Node3D _pivot, _body;
    private float _dragVel;
    private bool _dragging;
    private VBoxContainer _options;
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>The look being edited (for tests).</summary>
    public Appearance Current => _look;
    /// <summary>The preview body (for tests and screenshots).</summary>
    public Node3D PreviewBody => _body;

    public CharacterCreator(Appearance start, Action<Appearance> onDone, Action onCancel)
    {
        _look = (start ?? new Appearance()).Clone();
        _startClass = _look.Class;
        _onDone = onDone;
        _onCancel = onCancel;
        Name = "CharacterCreator";
        _rng.Randomize();
    }

    public CharacterCreator() : this(null, null, null) { }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = new Color(0.05f, 0.04f, 0.1f, 0.94f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });

        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 28);
        center.AddChild(row);

        // Preview.
        var frame = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(380, 560) };
        frame.GuiInput += OnPreviewInput;
        row.AddChild(frame);
        _viewport = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X };
        frame.AddChild(_viewport);
        BuildStage();

        // Options.
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(500, 0) };
        right.AddThemeConstantOverride("separation", 10);
        row.AddChild(right);
        var title = new Label { Text = "Your character" };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.Modulate = Gold;
        right.AddChild(title);
        _options = new VBoxContainer();
        _options.AddThemeConstantOverride("separation", 10);
        right.AddChild(_options);
        BuildOptions();

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        right.AddChild(buttons);
        var random = new Button { Text = "Randomise", CustomMinimumSize = new Vector2(130, 40) };
        random.Pressed += () => { Copy(Appearance.Random(_rng)); Refresh(); };
        buttons.AddChild(random);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var cancel = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(110, 40) };
        cancel.Pressed += Cancel;
        buttons.AddChild(cancel);
        var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(130, 40) };
        done.AddThemeFontSizeOverride("font_size", 20);
        done.Pressed += Done;
        buttons.AddChild(done);

        var hint = new Label { Text = "Drag the preview to turn it. Your look is kept on this computer and shown to everyone you play with." };
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.Modulate = new Color(0.75f, 0.72f, 0.82f);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.CustomMinimumSize = new Vector2(500, 0);
        right.AddChild(hint);
    }

    public void Done()
    {
        _onDone?.Invoke(_look.Clone());
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

    /// <summary>Change the look being edited (tests; the controls call this too).</summary>
    public void Set(Action<Appearance> change)
    {
        change(_look);
        Refresh();
    }

    private void Copy(Appearance a)
    {
        _look.Body = a.Body; _look.Skin = a.Skin; _look.Hair = a.Hair; _look.Outfit = a.Outfit; _look.Trim = a.Trim;
        _look.Cape = a.Cape; _look.Helmet = a.Helmet; _look.Height = a.Height;
    }

    private void Refresh()
    {
        RebuildBody();
        foreach (var c in _options.GetChildren()) { _options.RemoveChild(c); c.QueueFree(); }
        BuildOptions();
    }

    // ------------------------------------------------------------------ preview stage

    private void BuildStage()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.5f, 0.7f),
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = env });
        var sun = new DirectionalLight3D { LightColor = new Color(1f, 0.86f, 0.7f), LightEnergy = 1.3f, ShadowEnabled = true };
        _viewport.AddChild(sun);
        sun.RotationDegrees = new Vector3(-35, 35, 0);
        var rim = new DirectionalLight3D { LightColor = new Color(1f, 0.55f, 0.6f), LightEnergy = 0.7f };
        _viewport.AddChild(rim);
        rim.RotationDegrees = new Vector3(-10, 200, 0);

        // A little round plinth of cliff rock.
        _viewport.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.85f, Height = 0.2f, RadialSegments = 7 },
            MaterialOverride = StyleMaterials.Toon(new Color(0.2f, 0.18f, 0.24f), 0.4f),
            Position = new Vector3(0, -0.1f, 0),
        });
        var cam = new Camera3D { Fov = 32f, Current = true };
        _viewport.AddChild(cam);
        cam.Position = new Vector3(0, 1.25f, 5.4f);
        cam.LookAt(new Vector3(0, 0.95f, 0), Vector3.Up);

        _pivot = new Node3D { Name = "Pivot" };
        _viewport.AddChild(_pivot);
        _pivot.Rotation = new Vector3(0, Mathf.Pi + 0.5f, 0); // face the camera, a little turned
        RebuildBody();
    }

    private void RebuildBody()
    {
        if (_body != null) { _pivot.RemoveChild(_body); _body.QueueFree(); }
        _body = CharacterModel.BuildBody(_look);
        _pivot.AddChild(_body);
        CharacterModel.FitHeight(_body, 1.8f * _look.Height, 0f);
        if (_body.FindChildren("*", "AnimationPlayer", true, false).FirstOrDefault() is AnimationPlayer anim && anim.HasAnimation("Idle"))
        {
            anim.GetAnimation("Idle").LoopMode = Animation.LoopModeEnum.Linear;
            anim.Play("Idle");
        }
    }

    private void OnPreviewInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            _dragVel = mm.Relative.X * 0.01f;
            _pivot.RotateY(_dragVel);
        }
    }

    public override void _Process(double delta)
    {
        if (_pivot == null || _dragging) return;
        // A slow turntable, plus whatever spin a drag left behind.
        _dragVel = Mathf.Lerp(_dragVel, 0f, Mathf.Min(1f, (float)delta * 3f));
        _pivot.RotateY((float)delta * 0.25f + _dragVel);
    }

    // ------------------------------------------------------------------ options

    private void BuildOptions()
    {
        var classRow = Row("Class");
        foreach (var c in PlayerClasses.All)
        {
            var button = new Button
            {
                Text = c.Name, ToggleMode = true, ButtonPressed = _look.Class == c.Id, CustomMinimumSize = new Vector2(78, 34),
            };
            if (_look.Class == c.Id) button.Modulate = c.Tint.Lightened(0.3f);
            string id = c.Id;
            button.Pressed += () => Set(a => a.Class = id);
            classRow.AddChild(button);
        }
        var cls = PlayerClasses.Get(_look.Class);
        var about = new Label
        {
            Text = cls.Blurb + "\n[R] " + cls.AbilityName + ": " + cls.AbilityText,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0),
        };
        about.AddThemeFontSizeOverride("font_size", 14);
        about.Modulate = cls.Tint.Lightened(0.35f);
        _options.AddChild(about);
        if (_startClass != _look.Class && InGame)
        {
            var note = new Label { Text = "(You can only change class at home.)" };
            note.AddThemeFontSizeOverride("font_size", 13);
            note.Modulate = new Color(0.8f, 0.75f, 0.85f);
            _options.AddChild(note);
        }

        var bodyRow = Row("Body");
        foreach (var b in Appearance.Bodies)
        {
            var button = new Button
            {
                Text = b == Appearance.Knight ? "Knight" : "Wanderer",
                ToggleMode = true, ButtonPressed = _look.Body == b, CustomMinimumSize = new Vector2(120, 34),
            };
            string body = b;
            button.Pressed += () => Set(a => a.Body = body);
            bodyRow.AddChild(button);
        }

        Swatches("Skin", Appearance.SkinTones, _look.Skin, c => _look.Skin = c);
        Swatches("Hair", Appearance.HairColors, _look.Hair, c => _look.Hair = c);
        Swatches(_look.Body == Appearance.Knight ? "Cloth" : "Outfit", Appearance.ClothColors, _look.Outfit, c => _look.Outfit = c);
        Swatches(_look.Body == Appearance.Knight ? "Armour" : "Trim", Appearance.ClothColors, _look.Trim, c => _look.Trim = c);

        var gear = Row("Gear");
        var cape = new CheckBox { Text = "Cape", ButtonPressed = _look.Cape };
        cape.Toggled += on => Set(a => a.Cape = on);
        gear.AddChild(cape);
        if (_look.Body == Appearance.Knight)
        {
            var helmet = new CheckBox { Text = "Helmet", ButtonPressed = _look.Helmet };
            helmet.Toggled += on => Set(a => a.Helmet = on);
            gear.AddChild(helmet);
        }

        var heightRow = Row("Height");
        var slider = new HSlider
        {
            MinValue = Appearance.MinHeight, MaxValue = Appearance.MaxHeight, Step = 0.01, Value = _look.Height,
            CustomMinimumSize = new Vector2(260, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var value = new Label { Text = $"{_look.Height * 100:0}%" };
        slider.ValueChanged += v =>
        {
            _look.Height = (float)v;
            value.Text = $"{v * 100:0}%";
            CharacterModel.FitHeight(_body, 1.8f * _look.Height, 0f);
        };
        heightRow.AddChild(slider);
        heightRow.AddChild(value);
    }

    private HBoxContainer Row(string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _options.AddChild(row);
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) };
        l.AddThemeFontSizeOverride("font_size", 17);
        row.AddChild(l);
        return row;
    }

    private void Swatches(string label, IEnumerable<string> colors, string current, Action<string> set)
    {
        var row = Row(label);
        foreach (var c in colors)
        {
            bool original = c == "";
            var fill = original ? new Color(0.16f, 0.14f, 0.2f) : Color.FromHtml(c);
            var style = new StyleBoxFlat { BgColor = fill };
            style.SetCornerRadiusAll(14);
            bool selected = c == current;
            style.SetBorderWidthAll(selected ? 3 : 1);
            style.BorderColor = selected ? Colors.White : new Color(0, 0, 0, 0.5f);
            var b = new Button
            {
                CustomMinimumSize = new Vector2(30, 30),
                TooltipText = original ? "The model's own colour" : c,
                Text = original ? "–" : "",
            };
            foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
                b.AddThemeStyleboxOverride(state, style);
            string color = c;
            b.Pressed += () => Set(_ => set(color));
            row.AddChild(b);
        }
    }
}
