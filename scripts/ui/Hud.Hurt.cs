using Godot;

namespace KatrinasGame;

/// <summary>Getting hit, made obvious: a red flash round the edges of the screen (stronger for a
/// bigger hit, and gold when a block took most of it), and a slow red pulse there while you're
/// badly hurt. The camera shake and the sparks on the player are in PlayerController.</summary>
public partial class Hud
{
    private ColorRect _hurt;
    private ShaderMaterial _hurtMat;
    private float _hurtFlash, _hurtTime;
    private Color _hurtColor = HurtRed;

    private static readonly Color HurtRed = new(0.85f, 0.05f, 0.05f);
    private static readonly Color BlockGold = new(1f, 0.75f, 0.25f);

    /// <summary>Below this share of max health the edges pulse red.</summary>
    public const float LowHealthShare = 0.3f;

    private const string HurtShader = @"shader_type canvas_item;
uniform vec4 color : source_color = vec4(0.85, 0.05, 0.05, 1.0);
uniform float strength = 0.0;
void fragment() {
    vec2 d = (UV - 0.5) * vec2(1.6, 1.25);
    float edge = smoothstep(0.38, 0.95, length(d));
    COLOR = vec4(color.rgb, edge * strength);
}";

    private void BuildHurt()
    {
        _hurtMat = new ShaderMaterial { Shader = new Shader { Code = HurtShader } };
        _hurt = new ColorRect { Material = _hurtMat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _root.AddChild(_hurt);
        _hurt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    /// <summary>The local player took a hit: <paramref name="share"/> is the damage as a share of
    /// max health; <paramref name="blocked"/> when a block soaked up most of it.</summary>
    public void OnHurt(float share, bool blocked)
    {
        float s = blocked ? 0.35f : Mathf.Clamp(0.45f + share * 2.5f, 0.45f, 0.95f);
        if (s >= _hurtFlash) _hurtColor = blocked ? BlockGold : HurtRed;
        _hurtFlash = Mathf.Max(_hurtFlash, s);
    }

    private void UpdateHurt(float dt, PlayerController me)
    {
        if (_hurt == null) return;
        _hurtTime += dt;
        _hurtFlash = Mathf.MoveToward(_hurtFlash, 0f, dt * 1.8f);
        float low = 0f;
        if (me != null && !me.Health.IsDead && me.Health.Max > 0)
        {
            float share = me.Health.Current / me.Health.Max;
            if (share < LowHealthShare)
                low = (0.18f + 0.14f * Mathf.Sin(_hurtTime * 4.5f)) * (1f - share / LowHealthShare * 0.6f);
        }
        float strength = Mathf.Max(_hurtFlash, low);
        _hurt.Visible = strength > 0.01f;
        if (!_hurt.Visible) return;
        _hurtMat.SetShaderParameter("color", _hurtFlash > low ? _hurtColor : HurtRed);
        _hurtMat.SetShaderParameter("strength", strength);
    }
}
