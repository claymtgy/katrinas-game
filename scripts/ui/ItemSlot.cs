using Godot;

namespace KatrinasGame;

/// <summary>One recessed item slot: the item's icon, a count in the corner, an optional key label
/// and a parchment tooltip describing it. Selected slots glow. Click it to act (Pressed).</summary>
public partial class ItemSlot : Button
{
    public string ItemId { get; private set; } = "";
    public int Count { get; private set; }

    private readonly TextureRect _icon, _glint;
    private readonly Label _count, _key, _placeholder;
    private bool _selected;

    public ItemSlot(float size = 64, string key = "")
    {
        ThemeTypeVariation = "Slot";
        CustomMinimumSize = new Vector2(size, size);
        FocusMode = FocusModeEnum.None;
        _icon = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_icon);
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        float pad = size * 0.1f;
        _icon.OffsetLeft = pad; _icon.OffsetTop = pad; _icon.OffsetRight = -pad; _icon.OffsetBottom = -pad;
        _glint = new TextureRect
        {
            Texture = UiTheme.Tex("icons/_glint"), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore, Visible = false,
        };
        AddChild(_glint);
        _glint.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _placeholder = UiTheme.Label("", "Dim", HorizontalAlignment.Center);
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _placeholder.AddThemeFontSizeOverride("font_size", (int)Mathf.Clamp(size * 0.2f, 11, 15));
        AddChild(_placeholder);
        _placeholder.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _count = UiTheme.Label("", "Small", HorizontalAlignment.Right);
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        _count.AddThemeFontSizeOverride("font_size", (int)Mathf.Clamp(size * 0.27f, 13, 18));
        AddChild(_count);
        _count.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _count.OffsetRight = -size * 0.1f; _count.OffsetBottom = -size * 0.04f;
        _key = UiTheme.Label(key, "Small");
        _key.AddThemeColorOverride("font_color", UiTheme.Gold);
        _key.AddThemeFontSizeOverride("font_size", (int)Mathf.Clamp(size * 0.24f, 12, 16));
        AddChild(_key);
        _key.Position = new Vector2(size * 0.12f, size * 0.02f);
    }

    /// <summary>Show an item ("" = empty; placeholder text such as a slot name shows instead).</summary>
    public void SetItem(string itemId, int count = 1, string placeholder = "")
    {
        ItemId = itemId ?? "";
        Count = count;
        var def = ItemDb.Get(ItemId);
        var tex = ItemId == "" ? null : UiTheme.Icon(ItemId);
        _icon.Texture = tex;
        _icon.Modulate = Colors.White;
        if (ItemId != "" && tex == null)
        {
            // No icon painted for it yet: a plain gem in the item's colour.
            _icon.Texture = UiTheme.Tex("icons/rune_shard");
            _icon.Modulate = def?.IconColor ?? Colors.White;
        }
        _glint.Visible = def != null && def.Enchant != "";
        _count.Text = count > 1 ? count.ToString() : "";
        _placeholder.Text = ItemId == "" ? placeholder : "";
        TooltipText = def?.Describe() ?? placeholder;
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            if (value) AddThemeStyleboxOverride("normal", UiTheme.Box("slot_active", 14, 4, 4));
            else RemoveThemeStyleboxOverride("normal");
        }
    }

    public void SetKey(string key) => _key.Text = key;
}
