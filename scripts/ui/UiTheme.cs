using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The game's one UI look: oak panels in ornate gold frames, walnut plank buttons, recessed
/// item slots, parchment tooltips and a red title plate (art: tools/make_ui.py → assets/ui).
/// Build once, then give <see cref="Theme"/> to each root Control (HUD, main menu, creator); its
/// children inherit it. Type variations: "Title", "Heading", "Small", "Dim" (labels),
/// "Plate", "Parchment", "Inset" (panels), "Slot" (buttons).
///
/// Fonts: Palatino Linotype (installed with Windows) through SystemFont, falling back to any
/// serif. Drop a title.ttf / body.ttf into assets/ui/fonts to bundle your own instead.</summary>
public static class UiTheme
{
    public static readonly Color Ink = new(0.97f, 0.91f, 0.77f);      // parchment-coloured text
    public static readonly Color Gold = new(1f, 0.8f, 0.42f);
    public static readonly Color GoldBright = new(1f, 0.92f, 0.62f);
    public static readonly Color Muted = new(0.72f, 0.66f, 0.56f);
    public static readonly Color Brown = new(0.27f, 0.15f, 0.07f);    // ink on parchment
    public static readonly Color Bad = new(1f, 0.5f, 0.42f);
    public static readonly Color Good = new(0.62f, 0.95f, 0.55f);
    public static readonly Color Shadow = new(0.07f, 0.04f, 0.02f);

    public const int PanelMargin = 52;

    private static Theme _theme;
    public static Theme Theme => _theme ??= Build();

    private static Font _title, _body;
    public static Font TitleFont => _title ??= LoadFont("title", true);
    public static Font BodyFont => _body ??= LoadFont("body", false);

    private static Font LoadFont(string file, bool bold)
    {
        foreach (var ext in new[] { "ttf", "otf" })
        {
            string path = $"res://assets/ui/fonts/{file}.{ext}";
            if (ResourceLoader.Exists(path) && GD.Load<FontFile>(path) is { } f) return f;
        }
        return new SystemFont
        {
            FontNames = new[] { "Palatino Linotype", "Book Antiqua", "Palatino", "Constantia", "Georgia", "serif" },
            FontWeight = bold ? 700 : 400,
            Antialiasing = TextServer.FontAntialiasing.Gray,
        };
    }

    private static readonly Dictionary<string, Texture2D> Textures = new();

    public static Texture2D Tex(string name)
    {
        if (Textures.TryGetValue(name, out var t)) return t;
        string path = $"res://assets/ui/{name}.png";
        t = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        Textures[name] = t;
        return t;
    }

    /// <summary>An item's icon (enchanted copies use their base item's), or null.</summary>
    public static Texture2D Icon(string itemId)
    {
        var def = ItemDb.Get(itemId);
        string name = def == null ? Gear.Split(itemId).Base : def.Icon != "" ? def.Icon : def.BaseId != "" ? def.BaseId : def.Id;
        return Tex($"icons/{name}");
    }

    /// <summary>9-slice style box from a kit texture.</summary>
    public static StyleBox Box(string name, int margin, float padX, float padY, bool tile = false)
    {
        var tex = Tex(name);
        if (tex == null)
        {
            var flat = new StyleBoxFlat { BgColor = new Color(0.2f, 0.12f, 0.07f, 0.95f), BorderColor = Gold };
            flat.SetBorderWidthAll(2);
            flat.SetContentMarginAll(padX);
            return flat;
        }
        var box = new StyleBoxTexture { Texture = tex };
        box.SetTextureMarginAll(margin);
        box.ContentMarginLeft = box.ContentMarginRight = padX;
        box.ContentMarginTop = box.ContentMarginBottom = padY;
        if (tile)
        {
            box.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.TileFit;
            box.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.TileFit;
        }
        return box;
    }

    private static Theme Build()
    {
        var t = new Theme { DefaultFont = BodyFont, DefaultFontSize = 18 };

        // Labels: parchment ink with a soft dark outline (readable over the world).
        t.SetColor("font_color", "Label", Ink);
        t.SetColor("font_outline_color", "Label", Shadow);
        t.SetConstant("outline_size", "Label", 5);
        t.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.45f));
        t.SetConstant("shadow_offset_x", "Label", 1);
        t.SetConstant("shadow_offset_y", "Label", 2);
        Variation(t, "Title", "Label", TitleFont, 34, GoldBright, 7);
        Variation(t, "Heading", "Label", TitleFont, 22, Gold, 5);
        Variation(t, "Small", "Label", BodyFont, 15, Ink, 4);
        Variation(t, "Dim", "Label", BodyFont, 15, Muted, 4);
        t.SetTypeVariation("OnParchment", "Label");
        t.SetColor("font_color", "OnParchment", Brown);
        t.SetConstant("outline_size", "OnParchment", 0);
        t.SetColor("font_shadow_color", "OnParchment", new Color(0, 0, 0, 0));
        t.SetFontSize("font_size", "OnParchment", 16);

        // Panels: the framed oak board.
        var panel = Box("panel", PanelMargin, 34, 30, tile: true);
        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetStylebox("panel", "Panel", panel);
        PanelVariation(t, "Plate", Box("plate", 16, 22, 8));
        PanelVariation(t, "Parchment", Box("parchment", 18, 16, 12));
        PanelVariation(t, "Inset", Box("slot", 14, 12, 10));

        // Buttons: walnut planks with a gold rim.
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            t.SetStylebox("normal", type, Box("button", 14, 18, 8));
            t.SetStylebox("hover", type, Box("button_hover", 14, 18, 8));
            t.SetStylebox("pressed", type, Box("button_pressed", 14, 18, 9));
            t.SetStylebox("hover_pressed", type, Box("button_pressed", 14, 18, 9));
            t.SetStylebox("disabled", type, Box("button_disabled", 14, 18, 8));
            // Focus (controller): lit like hover. Godot hides focus that came from a mouse click.
            t.SetStylebox("focus", type, FocusRing());
            t.SetColor("font_color", type, Ink);
            t.SetColor("font_hover_color", type, GoldBright);
            t.SetColor("font_pressed_color", type, Gold);
            t.SetColor("font_hover_pressed_color", type, Gold);
            t.SetColor("font_focus_color", type, GoldBright);
            t.SetColor("font_disabled_color", type, new Color(0.62f, 0.57f, 0.5f));
            t.SetColor("font_outline_color", type, Shadow);
            t.SetConstant("outline_size", type, 4);
            t.SetFont("font", type, TitleFont);
            t.SetFontSize("font_size", type, 18);
        }
        // Slot buttons (item slots): the recessed square.
        t.SetTypeVariation("Slot", "Button");
        t.SetStylebox("normal", "Slot", Box("slot", 14, 4, 4));
        t.SetStylebox("hover", "Slot", Box("slot_hover", 14, 4, 4));
        t.SetStylebox("pressed", "Slot", Box("slot_active", 14, 4, 4));
        t.SetStylebox("hover_pressed", "Slot", Box("slot_active", 14, 4, 4));
        t.SetStylebox("disabled", "Slot", Box("slot", 14, 4, 4));
        t.SetStylebox("focus", "Slot", FocusRing());

        // Check boxes and toggles: plain text, gold when on.
        foreach (var type in new[] { "CheckBox", "CheckButton" })
        {
            t.SetColor("font_color", type, Ink);
            t.SetColor("font_hover_color", type, GoldBright);
            t.SetColor("font_pressed_color", type, Gold);
            t.SetColor("font_hover_pressed_color", type, GoldBright);
            t.SetColor("font_outline_color", type, Shadow);
            t.SetConstant("outline_size", type, 4);
            t.SetStylebox("focus", type, new StyleBoxEmpty());
        }

        // Text fields: the recessed slot, gold caret.
        var field = Box("slot", 14, 12, 7);
        t.SetStylebox("normal", "LineEdit", field);
        t.SetStylebox("focus", "LineEdit", Box("slot_hover", 14, 12, 7));
        t.SetStylebox("read_only", "LineEdit", field);
        t.SetColor("font_color", "LineEdit", Ink);
        t.SetColor("caret_color", "LineEdit", Gold);
        t.SetColor("selection_color", "LineEdit", new Color(0.8f, 0.6f, 0.2f, 0.4f));
        t.SetColor("font_placeholder_color", "LineEdit", Muted);

        // Tooltips: parchment with brown ink.
        t.SetStylebox("panel", "TooltipPanel", Box("parchment", 18, 14, 10));
        t.SetColor("font_color", "TooltipLabel", Brown);
        t.SetFont("font", "TooltipLabel", BodyFont);
        t.SetFontSize("font_size", "TooltipLabel", 16);
        t.SetConstant("outline_size", "TooltipLabel", 0);

        // Pop-ups (option lists): the inset, gold hover.
        t.SetStylebox("panel", "PopupMenu", Box("slot", 14, 8, 8));
        var hover = new StyleBoxFlat { BgColor = new Color(0.85f, 0.6f, 0.2f, 0.35f) };
        hover.SetCornerRadiusAll(3);
        t.SetStylebox("hover", "PopupMenu", hover);
        t.SetColor("font_color", "PopupMenu", Ink);
        t.SetColor("font_hover_color", "PopupMenu", GoldBright);

        // Scroll bars: a dark groove and a gold grabber.
        var groove = new StyleBoxFlat { BgColor = new Color(0.08f, 0.05f, 0.03f, 0.7f) };
        groove.SetCornerRadiusAll(4);
        groove.SetContentMarginAll(2);
        var grab = new StyleBoxFlat { BgColor = new Color(0.78f, 0.56f, 0.22f) };
        grab.SetCornerRadiusAll(4);
        var grabHi = new StyleBoxFlat { BgColor = GoldBright };
        grabHi.SetCornerRadiusAll(4);
        foreach (var type in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("scroll", type, groove);
            t.SetStylebox("grabber", type, grab);
            t.SetStylebox("grabber_highlight", type, grabHi);
            t.SetStylebox("grabber_pressed", type, grabHi);
        }
        t.SetConstant("separation", "VBoxContainer", 8);
        t.SetConstant("separation", "HBoxContainer", 8);
        return t;
    }

    private static void Variation(Theme t, string name, string baseType, Font font, int size, Color color, int outline)
    {
        t.SetTypeVariation(name, baseType);
        t.SetFont("font", name, font);
        t.SetFontSize("font_size", name, size);
        t.SetColor("font_color", name, color);
        t.SetConstant("outline_size", name, outline);
    }

    private static void PanelVariation(Theme t, string name, StyleBox box)
    {
        t.SetTypeVariation(name, "PanelContainer");
        t.SetStylebox("panel", name, box);
    }

    // ------------------------------------------------------------------ small builders

    public static Label Label(string text = "", string variation = "", HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        if (variation != "") l.ThemeTypeVariation = variation;
        return l;
    }

    /// <summary>A title on a red plate, centred.</summary>
    public static Control TitlePlate(string text, out Label label)
    {
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var plate = new PanelContainer { ThemeTypeVariation = "Plate", MouseFilter = Control.MouseFilterEnum.Ignore };
        label = Label(text, "Heading", HorizontalAlignment.Center);
        label.AddThemeColorOverride("font_color", GoldBright);
        plate.AddChild(label);
        center.AddChild(plate);
        return center;
    }

    /// <summary>The gold flourish rule.</summary>
    public static Control Divider(float width = 320)
    {
        var tex = Tex("divider");
        if (tex == null) return new HSeparator();
        return new TextureRect
        {
            Texture = tex, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, CustomMinimumSize = new Vector2(width, 16),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    /// <summary>A key cap ("E") for prompts.</summary>
    public static Control KeyCap(string key)
    {
        var cap = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", Box("slot", 14, 9, 2));
        var l = Label(key, "Heading", HorizontalAlignment.Center);
        l.AddThemeFontSizeOverride("font_size", 18);
        cap.AddChild(l);
        return cap;
    }

    /// <summary>The controller focus: a bright gold ring drawn over the button.</summary>
    private static StyleBoxFlat FocusRing()
    {
        var ring = new StyleBoxFlat
        {
            DrawCenter = false, BorderColor = new Color(1f, 0.86f, 0.4f),
            ShadowColor = new Color(1f, 0.75f, 0.25f, 0.45f), ShadowSize = 6,
        };
        ring.SetBorderWidthAll(3);
        ring.SetCornerRadiusAll(6);
        ring.SetExpandMarginAll(2);
        return ring;
    }

    /// <summary>A key cap for an action that shows the right key or button for the device in use
    /// ("E" on the keyboard, "X" on an Xbox pad, "□" on a DualSense).</summary>
    public static Control ActionCap(string action) => new ActionCap(action);

    /// <summary>Fade and scale a panel in when it opens.</summary>
    public static void PopIn(Control c)
    {
        c.PivotOffset = c.Size / 2f;
        c.Modulate = new Color(1, 1, 1, 0);
        c.Scale = new Vector2(0.96f, 0.96f);
        var tw = c.CreateTween().SetParallel();
        tw.TweenProperty(c, "modulate:a", 1f, 0.14);
        tw.TweenProperty(c, "scale", Vector2.One, 0.16).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
