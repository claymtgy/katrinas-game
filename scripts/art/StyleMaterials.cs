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

    /// <summary>Dirt paths and trodden ground, drawn on a flat plane laid on the terrain: a ragged
    /// edge where the grass creeps in, patchy dirt in two tones, worn wheel ruts with a grassy
    /// crown between them (roads), scattered pebbles, toon light. Rain darkens it and puddles the
    /// ruts. shape 0 is a strip along the plane's length; shape 1 is a rounded patch (a yard).</summary>
    public const string PathShader = @"shader_type spatial;
render_mode diffuse_toon, specular_toon;
global uniform float wetness;
uniform vec3 dirt_a : source_color = vec3(0.45, 0.35, 0.25);
uniform vec3 dirt_b : source_color = vec3(0.55, 0.44, 0.31);
uniform vec3 grass : source_color = vec3(0.3, 0.41, 0.26);
uniform vec2 size = vec2(4.0, 20.0);   // metres across, along
uniform int shape = 0;
uniform float ruts = 1.0;
uniform float pebbles = 1.0;
varying vec3 wp;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
void vertex() {
    wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
    vec2 m = (UV - 0.5) * size;            // metres from the centre: x across, y along
    vec2 w = wp.xz;
    float wob = (noise(w * 0.3) - 0.5) * 0.7 + (noise(w * 1.3 + 5.0) - 0.5) * 0.3;
    float grain = noise(w * 2.2 + 2.0) - 0.5;
    float inside;                          // metres in from the edge (negative outside)
    if (shape == 0) {
        float half_w = size.x * 0.5 - 0.45;
        inside = min(half_w - abs(m.x), size.y * 0.5 - abs(m.y)) + wob * 0.8;
    } else {
        vec2 q = abs(m) / (size * 0.5);
        float d = pow(pow(q.x, 3.0) + pow(q.y, 3.0), 1.0 / 3.0);
        inside = (1.0 - d) * min(size.x, size.y) * 0.5 - 0.3 + wob * 1.2;
    }
    if (inside + grain * 0.25 < 0.0) discard;

    // Patchy dirt, flecked like the terrain's brushwork.
    float n1 = noise(w * 0.45) * 0.65 + noise(w * 1.7 + 9.0) * 0.35;
    vec3 col = mix(dirt_a, dirt_b, smoothstep(0.35, 0.7, n1));
    col *= 0.93 + 0.12 * smoothstep(0.5, 0.58, noise(w * vec2(2.2, 0.8) + 4.0));
    // Grass creeping in at the edge, in tufts.
    float edge = 1.0 - smoothstep(0.0, 0.55, inside + (noise(w * 2.4) - 0.5) * 0.5);
    col = mix(col, grass, edge * 0.85);

    // Two wheel ruts, a little darker and wandering, with a grassy crown between them.
    float wet = 0.0;
    if (shape == 0 && ruts > 0.5) {
        float x = m.x + (noise(vec2(m.y * 0.12, 1.0)) - 0.5) * 0.35;
        float r = 1.0 - smoothstep(0.12, 0.3, abs(abs(x) - size.x * 0.2));
        col = mix(col, dirt_a * 0.72, r * 0.6);
        float crown = 1.0 - smoothstep(0.15, 0.45, abs(x));
        col = mix(col, grass * 0.95, crown * smoothstep(0.45, 0.75, noise(w * 1.1 + 3.0)) * 0.55);
        wet = r * smoothstep(0.4, 0.6, noise(w * 0.6 + 11.0));
    }

    // Pebbles: a few small stones per square metre, light on top with a dark underside.
    if (pebbles > 0.5) {
        vec2 cell = floor(w * 1.4);
        vec2 f = fract(w * 1.4);
        float h = hash(cell);
        if (h > 0.72) {
            vec2 c = vec2(hash(cell + 1.7), hash(cell + 4.3)) * 0.5 + 0.25;
            float rad = 0.07 + 0.09 * hash(cell + 8.1);
            float d = length((f - c) * vec2(1.0, 1.3));
            float stone = 1.0 - smoothstep(rad * 0.75, rad, d);
            float shade = 1.0 - smoothstep(rad * 0.5, rad, length(f - c - vec2(0.0, rad * 0.35)));
            vec3 sc = mix(dirt_a * 0.8, vec3(0.5, 0.47, 0.43), 0.4 + 0.4 * hash(cell + 2.2));
            col = mix(col, mix(sc * 0.65, sc, shade), stone * (1.0 - edge) * 0.85);
        }
    }

    // Rain: darker dirt, and the ruts hold water.
    col *= 1.0 - wetness * 0.3;
    col = mix(col, vec3(0.3, 0.33, 0.36), wet * wetness * 0.6);
    ALBEDO = col;
    ROUGHNESS = mix(1.0, 0.15, wet * wetness);
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
// Customisation (Appearance): each slot is a bit mask of atlas swatches (column + row * 8 in the
// KayKit 8 x 4 swatch grid) and the colour to paint them, keeping each swatch's gradient.
uniform int slot_mask_0 = 0;
uniform int slot_mask_1 = 0;
uniform int slot_mask_2 = 0;
uniform int slot_mask_3 = 0;
uniform vec3 slot_color_0 : source_color = vec3(1.0);
uniform vec3 slot_color_1 : source_color = vec3(1.0);
uniform vec3 slot_color_2 : source_color = vec3(1.0);
uniform vec3 slot_color_3 : source_color = vec3(1.0);
vec3 swap_swatch(vec3 c, vec2 uv) {
    vec2 cellf = clamp(floor(uv * vec2(8.0, 4.0)), vec2(0.0), vec2(7.0, 3.0));
    int bit = 1 << (int(cellf.x) + int(cellf.y) * 8);
    vec3 target;
    if ((slot_mask_0 & bit) != 0) target = slot_color_0;
    else if ((slot_mask_1 & bit) != 0) target = slot_color_1;
    else if ((slot_mask_2 & bit) != 0) target = slot_color_2;
    else if ((slot_mask_3 & bit) != 0) target = slot_color_3;
    else return c;
    // Shade the new colour by how light this texel is relative to the swatch's middle.
    vec3 mid = textureLod(albedo_tex, (cellf + vec2(0.3, 0.5)) / vec2(8.0, 4.0), 0.0).rgb;
    float l = dot(c, vec3(0.299, 0.587, 0.114));
    float lm = max(dot(mid, vec3(0.299, 0.587, 0.114)), 0.02);
    return target * clamp(l / lm, 0.25, 1.6);
}
void fragment() {
    vec4 c = use_tex ? texture(albedo_tex, UV) : vec4(1.0);
    if (use_tex) c.rgb = swap_swatch(c.rgb, UV);
    c *= tint;
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

    /// <summary>A path or trodden-ground material (see <see cref="PathShader"/>) for a plane
    /// <paramref name="size"/> metres (across, along).</summary>
    public static ShaderMaterial Path(Vector2 size, Color dirtA, Color dirtB, bool patch = false, bool ruts = true, bool pebbles = true)
    {
        var m = new ShaderMaterial { Shader = _path ??= new Shader { Code = PathShader } };
        m.SetShaderParameter("size", size);
        m.SetShaderParameter("dirt_a", dirtA);
        m.SetShaderParameter("dirt_b", dirtB);
        m.SetShaderParameter("shape", patch ? 1 : 0);
        m.SetShaderParameter("ruts", ruts && !patch ? 1f : 0f);
        m.SetShaderParameter("pebbles", pebbles ? 1f : 0f);
        return m;
    }

    private static Shader _terrain, _foliage, _water, _path;
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
    /// keeping each surface's texture and colour, and painting it with a player's look if given.</summary>
    public static void ApplyCharacter(Node root, Appearance look = null)
    {
        var made = new Dictionary<Material, ShaderMaterial>();
        ApplyCharacter(root, look, made);
    }

    private static void ApplyCharacter(Node root, Appearance look, Dictionary<Material, ShaderMaterial> made)
    {
        if (root is MeshInstance3D mi && mi.Mesh != null)
        {
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                var src = mi.GetActiveMaterial(i);
                if (src == null) continue;
                // Unpainted models share one material per source; a painted one gets its own.
                var cache = look == null ? CharacterCache : made;
                if (!cache.TryGetValue(src, out var m))
                {
                    m = new ShaderMaterial { Shader = _character ??= new Shader { Code = CharacterShader } };
                    if (src is BaseMaterial3D std)
                    {
                        m.SetShaderParameter("use_tex", std.AlbedoTexture != null);
                        if (std.AlbedoTexture != null) m.SetShaderParameter("albedo_tex", std.AlbedoTexture);
                        m.SetShaderParameter("tint", std.AlbedoColor);
                    }
                    m.NextPass = Outline;
                    if (look != null) Paint(m, look);
                    cache[src] = m;
                }
                mi.SetSurfaceOverrideMaterial(i, m);
            }
        }
        foreach (var child in root.GetChildren()) ApplyCharacter(child, look, made);
    }

    private static void Paint(ShaderMaterial m, Appearance look)
    {
        var (skin, hair, outfit, trim) = Appearance.Slots(look.Body);
        void Slot(int n, int mask, string color)
        {
            bool on = color != "" && Color.HtmlIsValid(color);
            m.SetShaderParameter($"slot_mask_{n}", on ? mask : 0);
            if (on) m.SetShaderParameter($"slot_color_{n}", Color.FromHtml(color));
        }
        Slot(0, skin, look.Skin);
        Slot(1, hair, look.Hair);
        Slot(2, outfit, look.Outfit);
        Slot(3, trim, look.Trim);
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
