using Godot;

namespace KatrinasGame;

/// <summary>A health / stamina bar: a glossy fill in a dark trough, under a gold frame, with
/// "84 / 100" on it. A pale "recent loss" sliver trails the fill when it drops.</summary>
public partial class FramedBar : Control
{
    private readonly TextureProgressBar _fill, _trail;
    private readonly Label _text;
    private float _shown = -1, _trailValue = -1;

    public bool ShowNumbers { get; set; } = true;

    public FramedBar(string fill, Vector2 size)
    {
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        const float inset = 5;
        _trail = MakeBar(null, inset);
        _trail.TintProgress = new Color(1f, 0.92f, 0.8f, 0.55f);
        _fill = MakeBar(fill, inset);
        var frame = new NinePatchRect
        {
            Texture = UiTheme.Tex("bar_frame"), PatchMarginLeft = 10, PatchMarginRight = 10, PatchMarginTop = 10, PatchMarginBottom = 10,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(frame);
        frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _text = UiTheme.Label("", "Small", HorizontalAlignment.Center);
        _text.VerticalAlignment = VerticalAlignment.Center;
        _text.AddThemeFontSizeOverride("font_size", Mathf.Clamp((int)(size.Y * 0.52f), 11, 16));
        AddChild(_text);
        _text.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    private TextureProgressBar MakeBar(string fill, float inset)
    {
        var bar = new TextureProgressBar
        {
            NinePatchStretch = true, MouseFilter = MouseFilterEnum.Ignore, Step = 0.01,
            TextureUnder = fill == null ? UiTheme.Tex("bar_back") : null,
            TextureProgress = UiTheme.Tex(fill ?? "bar_stamina"),
        };
        AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bar.OffsetLeft = inset; bar.OffsetTop = inset; bar.OffsetRight = -inset; bar.OffsetBottom = -inset;
        return bar;
    }

    public void Set(float value, float max)
    {
        _fill.MaxValue = _trail.MaxValue = Mathf.Max(max, 1f);
        if (_shown < 0) _shown = _trailValue = value;
        _shown = value;
        _fill.Value = value;
        if (value > _trailValue) _trailValue = value;
        _trail.Value = _trailValue;
        _text.Text = ShowNumbers ? $"{Mathf.CeilToInt(value)} / {Mathf.RoundToInt(max)}" : "";
    }

    public override void _Process(double delta)
    {
        // The pale trail catches up with the fill after a moment.
        if (_trailValue > _shown) _trailValue = Mathf.MoveToward(_trailValue, _shown, (float)delta * (float)_fill.MaxValue * 0.6f);
        _trail.Value = _trailValue;
    }
}
