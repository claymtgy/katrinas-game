using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Swaps imported model materials for the game's stylized look, by material name.
/// Blender assets use names like "Rock", "Bark", "Blossom"; anything unknown (e.g. KayKit's
/// texture atlas) is kept but switched to toon shading with a warm rim light.</summary>
public static class StyleMaterials
{
    private static readonly Dictionary<string, Material> Cache = new();

    // ------------------------------------------------------------------ shaders

    /// <summary>Faceted rock / terrain: dark sides, lighter tops, toon light bands, warm sun rim.</summary>
    public const string RockShader = @"shader_type spatial;
render_mode diffuse_toon, specular_disabled;
global uniform vec3 sun_dir;
global uniform vec4 sun_color;
uniform vec3 base_color : source_color = vec3(0.1, 0.09, 0.13);
uniform vec3 top_color : source_color = vec3(0.21, 0.2, 0.27);
uniform vec3 rim_color : source_color = vec3(1.0, 0.55, 0.35);
uniform float top_threshold = 0.7;
uniform float rim_strength = 0.6;
varying vec3 world_normal;
void vertex() { world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz); }
void fragment() {
    float up = smoothstep(top_threshold - 0.12, top_threshold + 0.08, world_normal.y);
    ALBEDO = mix(base_color, top_color, up);
    ROUGHNESS = 1.0;
    float facing = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float toward_sun = clamp(dot(world_normal, normalize(sun_dir)) * 0.5 + 0.5, 0.0, 1.0);
    // Warm rim is a golden-hour effect: fade it out when the sun is high.
    float low_sun = 1.0 - smoothstep(0.15, 0.6, normalize(sun_dir).y);
    EMISSION = rim_color * sun_color.rgb * pow(facing, 3.0) * toward_sun * rim_strength * (0.15 + 0.85 * low_sun);
}";

    /// <summary>Glowing blossom canopy: bright inner core, deeper pink edges, backlit.</summary>
    public const string BlossomShader = @"shader_type spatial;
render_mode diffuse_lambert_wrap, specular_disabled;
global uniform vec3 sun_dir;
global uniform float night;
uniform vec3 inner_color : source_color = vec3(1.0, 0.7, 0.84);
uniform vec3 outer_color : source_color = vec3(0.86, 0.24, 0.52);
uniform float glow = 1.1;
void fragment() {
    float f = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    vec3 c = mix(outer_color, inner_color, 0.45 + 0.4 * f);
    ALBEDO = c * 0.55;
    BACKLIGHT = vec3(1.0, 0.55, 0.72);
    ROUGHNESS = 1.0;
    EMISSION = c * glow * (0.55 + 0.25 * f) * (1.0 + night * 0.25);
}";

    /// <summary>Painterly ground: grass in broad patches of three tones with brush flecks on the
    /// flat tops, dark rock on the sides, toon light and the warm golden-hour rim. Darkens in rain.</summary>
    public const string TerrainShader = @"shader_type spatial;
render_mode diffuse_toon, specular_disabled;
global uniform vec3 sun_dir;
global uniform vec4 sun_color;
global uniform float wetness;
uniform vec3 base_color : source_color = vec3(0.11, 0.1, 0.14);
uniform vec3 grass_a : source_color = vec3(0.27, 0.39, 0.27);
uniform vec3 grass_b : source_color = vec3(0.36, 0.46, 0.26);
uniform vec3 grass_c : source_color = vec3(0.47, 0.44, 0.28);
uniform vec3 rim_color : source_color = vec3(1.0, 0.55, 0.35);
uniform float rim_strength = 0.25;
varying vec3 wn;
varying vec3 wp;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
void vertex() {
    wn = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
    float up = smoothstep(0.58, 0.8, wn.y);
    vec2 p = wp.xz;
    float n1 = noise(p * 0.04) * 0.7 + noise(p * 0.11 + 3.1) * 0.3;
    float n2 = noise(p * 0.19 + 13.0);
    float n3 = noise(p * vec2(1.6, 0.55) + 7.0);   // stretched brush strokes
    vec3 g = mix(grass_a, grass_b, smoothstep(0.42, 0.58, n1));
    g = mix(g, grass_c, smoothstep(0.66, 0.74, n2) * 0.75);
    g *= 0.94 + 0.1 * smoothstep(0.55, 0.6, n3);
    vec3 col = mix(base_color, g, up);
    col *= 1.0 - wetness * 0.25 * up;
    ALBEDO = col;
    ROUGHNESS = 1.0;
    float facing = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float toward_sun = clamp(dot(wn, normalize(sun_dir)) * 0.5 + 0.5, 0.0, 1.0);
    float low_sun = 1.0 - smoothstep(0.15, 0.6, normalize(sun_dir).y);
    EMISSION = rim_color * sun_color.rgb * pow(facing, 3.0) * toward_sun * rim_strength * (0.15 + 0.85 * low_sun) * (1.0 - up * 0.6);
}";

    /// <summary>Textured foliage (grass, leaves, flowers): alpha-cut, double-sided, toon-lit, with a
    /// gentle wind sway that grows with height and gusts a little harder in the rain.</summary>
    public const string FoliageShader = @"shader_type spatial;
render_mode diffuse_toon, specular_disabled, cull_disabled;
global uniform float wetness;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0);
uniform float alpha_cut = 0.5;
uniform float sway = 0.05;
uniform float max_height = 3.0;
void vertex() {
    vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float h = clamp(VERTEX.y, 0.0, max_height);
    float t = TIME * 1.7 + wp.x * 0.35 + wp.z * 0.27;
    float gust = 0.65 + 0.35 * sin(TIME * 0.4 + wp.x * 0.04 + wp.z * 0.03);
    VERTEX.xz += vec2(sin(t), cos(t * 0.8 + 1.3) * 0.6) * sway * h * gust * (1.0 + wetness * 0.8);
}
void fragment() {
    vec4 c = texture(albedo_tex, UV) * tint;
    ALBEDO = c.rgb * (1.0 - wetness * 0.2);
    ALPHA = c.a;
    ALPHA_SCISSOR_THRESHOLD = alpha_cut;
    ROUGHNESS = 1.0;
    BACKLIGHT = c.rgb * 0.4;
    RIM = 0.35;
    RIM_TINT = 0.6;
}";

    /// <summary>Stylized water: deep teal with drifting ripple bands, sparkles toward the sun and a
    /// sky-coloured sheen at grazing angles. Rain stipples it.</summary>
    public const string WaterShader = @"shader_type spatial;
render_mode diffuse_toon, specular_toon, cull_disabled, depth_draw_always;
global uniform vec3 sun_dir;
global uniform vec4 sun_color;
global uniform float night;
global uniform float wetness;
uniform vec3 deep : source_color = vec3(0.07, 0.24, 0.32);
uniform vec3 shallow : source_color = vec3(0.24, 0.52, 0.58);
uniform vec3 sheen : source_color = vec3(0.85, 0.75, 0.9);
uniform vec2 flow = vec2(0.0, 0.25);
varying vec3 wp;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
void vertex() { wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec2 p = wp.xz + flow * TIME;
    float n = noise(p * 0.6) * 0.6 + noise(p * 1.7 - TIME * 0.3) * 0.4;
    float bands = smoothstep(0.62, 0.68, fract(n * 3.0));
    vec3 col = mix(deep, shallow, n);
    col += vec3(0.9, 0.95, 1.0) * bands * 0.18;
    // Rain drops: little bright rings popping in a grid.
    vec2 cell = floor(wp.xz * 2.0);
    float ph = fract(TIME * 1.3 + hash(cell) * 7.0);
    float ring = smoothstep(0.08, 0.0, abs(length(fract(wp.xz * 2.0) - 0.5) - ph * 0.45)) * (1.0 - ph);
    col += vec3(0.8) * ring * wetness * step(0.6, hash(cell + 3.0));
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    col = mix(col, sheen * mix(sun_color.rgb, vec3(0.35, 0.4, 0.7), night), fres * 0.6);
    ALBEDO = col * (1.0 - night * 0.5);
    ALPHA = 0.88;
    METALLIC = 0.1;
    ROUGHNESS = 0.15;
    SPECULAR = 0.6;
}";

    /// <summary>Characters (players, NPCs, bandits, wolves): a hard two-band toon ramp with a
    /// small highlight band, shadows tinted dusk-violet rather than black, and a warm sun rim on
    /// the edge facing the light. Pairs with <see cref="OutlineShader"/> as the next pass.</summary>
    public const string CharacterShader = @"shader_type spatial;
render_mode specular_disabled;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool use_tex = true;
uniform vec4 tint : source_color = vec4(1.0);
uniform vec3 shadow_tint : source_color = vec3(0.46, 0.36, 0.66);
uniform float shadow_lift = 0.22;
uniform float rim_strength = 0.55;
void fragment() {
    vec4 c = use_tex ? texture(albedo_tex, UV) * tint : tint;
    ALBEDO = c.rgb;
    ROUGHNESS = 1.0;
}
void light() {
    float ndl = dot(NORMAL, LIGHT);
    float lit = smoothstep(-0.02, 0.06, ndl) * ATTENUATION;
    float hi = smoothstep(0.62, 0.68, ndl) * ATTENUATION;
    vec3 lc = LIGHT_COLOR / PI;
    DIFFUSE_LIGHT += lc * (lit + hi * 0.14);
    // Coloured shadow: the side away from the sun still gets a little violet light.
    if (LIGHT_IS_DIRECTIONAL) DIFFUSE_LIGHT += lc * shadow_tint * shadow_lift * (1.0 - lit);
    float edge = smoothstep(0.58, 0.66, 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0));
    float back = smoothstep(-0.1, 0.4, dot(LIGHT, -VIEW) * 0.5 + ndl * 0.5);
    SPECULAR_LIGHT += lc * edge * back * rim_strength * ATTENUATION * 0.5;
}";

    /// <summary>Inverted-hull ink outline: the mesh pushed out along its normals, front faces
    /// culled, unshaded. Width is in world metres so scaled models get the same line.</summary>
    public const string OutlineShader = @"shader_type spatial;
render_mode unshaded, cull_front, shadows_disabled, depth_draw_opaque;
global uniform float night;
uniform vec4 color : source_color = vec4(0.12, 0.07, 0.13, 1.0);
uniform float width = 0.014;
void vertex() {
    vec3 n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec4 wp = MODEL_MATRIX * vec4(VERTEX, 1.0);
    float d = length((VIEW_MATRIX * wp).xyz);
    wp.xyz += n * width * clamp(d * 0.12, 1.0, 3.0);
    POSITION = PROJECTION_MATRIX * VIEW_MATRIX * wp;
}
void fragment() {
    ALBEDO = color.rgb * (1.0 - night * 0.4);
}";

    // ------------------------------------------------------------------ public API

    /// <summary>Restyle every mesh under root.</summary>
    public static void Apply(Node root)
    {
        if (root is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var source = mi.Mesh.SurfaceGetMaterial(i);
                mi.SetSurfaceOverrideMaterial(i, Get(source?.ResourceName ?? "", source));
            }
        }
        foreach (var child in root.GetChildren()) Apply(child);
    }

    public static Material Get(string name, Material source = null)
    {
        // Blender may suffix duplicates (".001").
        string key = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var m = Create(key, source);
        if (m != null) Cache[key] = m;
        return m;
    }

    public static ShaderMaterial Rock(Color baseColor, Color topColor, float topThreshold = 0.7f, float rim = 0.6f)
    {
        var m = new ShaderMaterial { Shader = new Shader { Code = RockShader } };
        m.SetShaderParameter("base_color", baseColor);
        m.SetShaderParameter("top_color", topColor);
        m.SetShaderParameter("top_threshold", topThreshold);
        m.SetShaderParameter("rim_strength", rim);
        return m;
    }

    public static ShaderMaterial Terrain(Color baseColor, Color grassA, Color grassB, Color grassC)
    {
        var m = new ShaderMaterial { Shader = TerrainShaderRes };
        m.SetShaderParameter("base_color", baseColor);
        m.SetShaderParameter("grass_a", grassA);
        m.SetShaderParameter("grass_b", grassB);
        m.SetShaderParameter("grass_c", grassC);
        return m;
    }

    private static Shader _terrain, _foliage, _water;
    private static Shader TerrainShaderRes => _terrain ??= new Shader { Code = TerrainShader };

    public static ShaderMaterial Water() => new() { Shader = _water ??= new Shader { Code = WaterShader } };

    private static bool IsFoliage(string key) =>
        key.StartsWith("Grass") || key.StartsWith("Leaves") || key.StartsWith("Leaf") || key == "Flowers";

    private static Material Create(string key, Material source)
    {
        switch (key)
        {
            case "Rock": return Rock(new Color(0.09f, 0.08f, 0.115f), new Color(0.2f, 0.19f, 0.26f));
            case "Stone": return Rock(new Color(0.24f, 0.24f, 0.3f), new Color(0.36f, 0.36f, 0.42f), 0.6f, 0.3f);
            case "Bark": return Rock(new Color(0.11f, 0.065f, 0.075f), new Color(0.18f, 0.11f, 0.11f), 0.8f, 0.9f);
            case "Blossom": return new ShaderMaterial { Shader = new Shader { Code = BlossomShader } };
            case "WindowGlow":
                return new StandardMaterial3D
                {
                    AlbedoColor = new Color(1f, 0.7f, 0.4f),
                    EmissionEnabled = true,
                    Emission = new Color(1f, 0.6f, 0.3f),
                    EmissionEnergyMultiplier = 3f,
                };
            case "PineNeedles": return Toon(new Color(0.12f, 0.27f, 0.26f), 0.35f);
            case "Wood": return Toon(new Color(0.42f, 0.27f, 0.17f));
            case "WoodDark": return Toon(new Color(0.24f, 0.15f, 0.1f));
            case "Roof": return Toon(new Color(0.22f, 0.13f, 0.12f));
        }
        // Unknown (e.g. textured third-party models): keep the look, make it toon-lit.
        if (source is StandardMaterial3D std)
        {
            var copy = (StandardMaterial3D)std.Duplicate();
            copy.DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon;
            copy.SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled;
            copy.Roughness = 1f;
            copy.RimEnabled = true;
            copy.Rim = 0.35f;
            copy.RimTint = 0.6f;
            // Third-party packs are brighter/more saturated than our palette: pull them toward it.
            copy.AlbedoColor = std.AlbedoColor * new Color(0.8f, 0.8f, 0.88f);
            // Quaternius twisted-tree leaves are fire-engine red: push them to a dusky plum so the
            // sacred cherry stays the only strong pink/red in the scene.
            if (key == "Leaves_TwistedTree") copy.AlbedoColor = new Color(0.5f, 0.36f, 0.6f);
            if (IsFoliage(key) && std.AlbedoTexture != null)
            {
                var f = new ShaderMaterial { Shader = _foliage ??= new Shader { Code = FoliageShader } };
                f.SetShaderParameter("albedo_tex", std.AlbedoTexture);
                f.SetShaderParameter("tint", copy.AlbedoColor);
                f.SetShaderParameter("alpha_cut", std.Transparency == BaseMaterial3D.TransparencyEnum.Disabled ? 0.02f : Mathf.Max(0.3f, std.AlphaScissorThreshold));
                // Big trees sway less per metre than grass.
                bool tree = key.Contains("Tree") || key.Contains("Pine");
                f.SetShaderParameter("sway", tree ? 0.025f : 0.06f);
                f.SetShaderParameter("max_height", tree ? 4f : 1.5f);
                return f;
            }
            return copy;
        }
        return source;
    }

    private static Shader _character, _outline;
    private static ShaderMaterial _outlineMat;
    private static readonly Dictionary<Material, ShaderMaterial> CharacterCache = new();

    /// <summary>The shared ink-outline pass.</summary>
    public static ShaderMaterial Outline => _outlineMat ??= new ShaderMaterial { Shader = _outline ??= new Shader { Code = OutlineShader } };

    /// <summary>A flat-coloured character material (toon ramp + outline), e.g. wolf fur.
    /// Tint it at runtime through the "tint" shader parameter.</summary>
    public static ShaderMaterial CharacterFlat(Color color, bool outline = true)
    {
        var m = new ShaderMaterial { Shader = _character ??= new Shader { Code = CharacterShader } };
        m.SetShaderParameter("use_tex", false);
        m.SetShaderParameter("tint", color);
        if (outline) m.NextPass = Outline;
        return m;
    }

    /// <summary>Restyle a character model (KayKit glTF) with the toon ramp and outline,
    /// keeping each surface's texture and colour.</summary>
    public static void ApplyCharacter(Node root)
    {
        if (root is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var src = mi.GetActiveMaterial(i);
                if (src == null) continue;
                if (!CharacterCache.TryGetValue(src, out var m))
                {
                    m = new ShaderMaterial { Shader = _character ??= new Shader { Code = CharacterShader } };
                    if (src is BaseMaterial3D std)
                    {
                        m.SetShaderParameter("use_tex", std.AlbedoTexture != null);
                        if (std.AlbedoTexture != null) m.SetShaderParameter("albedo_tex", std.AlbedoTexture);
                        m.SetShaderParameter("tint", std.AlbedoColor);
                    }
                    m.NextPass = Outline;
                    CharacterCache[src] = m;
                }
                mi.SetSurfaceOverrideMaterial(i, m);
            }
        }
        foreach (var child in root.GetChildren()) ApplyCharacter(child);
    }

    public static StandardMaterial3D Toon(Color color, float rim = 0.25f) => new()
    {
        AlbedoColor = color,
        DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        Roughness = 1f,
        RimEnabled = rim > 0,
        Rim = rim,
        RimTint = 0.7f,
    };
}
