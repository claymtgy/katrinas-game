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
uniform vec3 inner_color : source_color = vec3(1.0, 0.78, 0.88);
uniform vec3 outer_color : source_color = vec3(0.86, 0.24, 0.52);
uniform float glow = 1.1;
void fragment() {
    float f = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    vec3 c = mix(outer_color, inner_color, 0.45 + 0.4 * f);
    ALBEDO = c * 0.55;
    BACKLIGHT = vec3(1.0, 0.55, 0.72);
    ROUGHNESS = 1.0;
    EMISSION = c * glow * (0.55 + 0.25 * f) * (1.0 + night * 0.8);
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
            return copy;
        }
        return source;
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
