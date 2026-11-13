using Godot;

namespace KatrinasGame;

/// <summary>Owns the sun, moon, sky and WorldEnvironment; advances GameState.TimeOfDay.
/// The sun arcs across the north (cliff) side so the view off the cliff is always backlit.</summary>
public partial class DayNight : Node3D
{
    [Export] public float DayLengthSeconds { get; set; } = 600f;

    // Palette from the visual brief.
    private static readonly Color DayTop = new(0.27f, 0.45f, 0.78f);
    private static readonly Color DayHorizon = new(0.98f, 0.82f, 0.66f);
    private static readonly Color DuskTop = new(0.13f, 0.11f, 0.32f);      // twilight indigo
    private static readonly Color DuskHorizon = new(0.98f, 0.46f, 0.2f);   // magma orange / sunset gold
    private static readonly Color NightTop = new(0.02f, 0.02f, 0.07f);    // obsidian
    private static readonly Color NightHorizon = new(0.1f, 0.08f, 0.22f);
    private static readonly Color SunNoon = new(1f, 0.93f, 0.82f);
    private static readonly Color SunLow = new(1f, 0.55f, 0.35f);

    private const string SkyShader = @"shader_type sky;
global uniform vec3 sun_dir;
global uniform float night;
uniform vec3 top_color : source_color;
uniform vec3 horizon_color : source_color;
uniform vec3 ground_color : source_color;
uniform vec3 glow_color : source_color = vec3(1.0, 0.5, 0.2);
uniform float glow_strength = 1.0;
uniform float energy = 1.0;
float hash(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
void sky() {
    vec3 dir = EYEDIR;
    vec3 sd = normalize(sun_dir);
    float h = dir.y;
    vec3 col = mix(horizon_color, top_color, smoothstep(-0.02, 0.5, h));
    col = mix(col, ground_color, smoothstep(0.0, -0.3, h));
    float sun_dot = max(dot(dir, sd), 0.0);
    // Burning band along the horizon, strongest toward the sun.
    float band = exp(-abs(h - 0.015) * 11.0);
    float toward = pow(max(dot(normalize(vec3(dir.x, 0.0, dir.z)), normalize(vec3(sd.x, 0.0, sd.z))), 0.0), 2.5);
    col += glow_color * band * (0.25 + 1.1 * toward) * glow_strength;
    col += glow_color * (pow(sun_dot, 8.0) * 0.6 + pow(sun_dot, 64.0) * 1.5) * glow_strength;
    // Sun disc.
    col += vec3(1.0, 0.85, 0.6) * smoothstep(0.9993, 0.9997, sun_dot) * 8.0 * step(-0.03, sd.y);
    // Stars: tiny twinkling points, fading in at dusk.
    if (!AT_CUBEMAP_PASS && night > 0.01 && h > 0.0) {
        vec3 cell = floor(dir * 260.0);
        float r = hash(cell);
        float star = step(0.9965, r) * (0.55 + 0.45 * sin(TIME * (1.5 + r * 3.0) + r * 60.0));
        col += vec3(0.9, 0.9, 1.0) * star * night * smoothstep(0.02, 0.35, h) * 2.2;
    }
    COLOR = col * energy;
}";

    private DirectionalLight3D _sun, _moon;
    private ShaderMaterial _sky;
    private Godot.Environment _env;

    public override void _Ready()
    {
        _sky = new ShaderMaterial { Shader = new Shader { Code = SkyShader } };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.6f,
            AmbientLightEnergy = 0.8f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowBloom = 0.12f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            SsaoEnabled = true,
            FogEnabled = true,
            FogDensity = 0.0012f,
            FogSkyAffect = 0.08f,
            FogHeight = -15f,
            FogHeightDensity = 0.06f,
            FogAerialPerspective = 0.3f,
            FogSunScatter = 0.35f,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        _sun = new DirectionalLight3D
        {
            Name = "Sun",
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 120f,
        };
        AddChild(_sun);
        _moon = new DirectionalLight3D
        {
            Name = "Moon",
            LightColor = new Color(0.55f, 0.6f, 1f),
            ShadowEnabled = false,
        };
        AddChild(_moon);

        Apply(GameState.Instance.TimeOfDay);
    }

    public override void _Process(double delta)
    {
        var gs = GameState.Instance;
        float t = gs.TimeOfDay + (float)delta / DayLengthSeconds;
        if (t >= 1f) { t -= 1f; gs.Day++; }
        gs.TimeOfDay = t;
        Apply(t);
    }

    private void Apply(float t)
    {
        // θ = 0 at sunrise (t = 0.25), π at sunset (t = 0.75).
        float theta = (t - 0.25f) * Mathf.Tau;
        float elevation = Mathf.Sin(theta);
        var toSun = new Vector3(Mathf.Cos(theta) * 0.8f, elevation, -0.75f).Normalized();

        _sun.Basis = Basis.LookingAt(-toSun, Vector3.Up);
        _moon.Basis = Basis.LookingAt(new Vector3(toSun.X, -toSun.Y, toSun.Z).Normalized() * -1f, Vector3.Up);

        float day = Mathf.SmoothStep(0f, 0.35f, elevation);
        float night = Mathf.SmoothStep(0.02f, -0.22f, elevation);
        float dusk = Mathf.Clamp(1f - Mathf.Abs(elevation) / 0.3f, 0f, 1f);

        _sun.LightEnergy = Mathf.Clamp(elevation * 4f, 0f, 1f) * 1.3f;
        _sun.LightColor = SunLow.Lerp(SunNoon, day);
        _sun.Visible = elevation > -0.05f;
        _moon.LightEnergy = 0.22f * night;
        _moon.Visible = night > 0.01f;

        var top = DayTop.Lerp(DuskTop, dusk).Lerp(NightTop, night);
        var horizon = DayHorizon.Lerp(DuskHorizon, dusk).Lerp(NightHorizon, night);
        _sky.SetShaderParameter("top_color", top);
        _sky.SetShaderParameter("horizon_color", horizon);
        _sky.SetShaderParameter("ground_color", horizon.Lerp(top, 0.6f).Darkened(0.3f));
        _sky.SetShaderParameter("glow_color", SunLow);
        _sky.SetShaderParameter("glow_strength", Mathf.Clamp(dusk * 1.2f, 0f, 1f) * (1f - night * 0.6f));
        _sky.SetShaderParameter("energy", Mathf.Lerp(1f, 0.55f, night));

        _env.FogLightColor = horizon.Lerp(new Color(1f, 0.75f, 0.8f), 0.3f * dusk);

        // Shared with the stylized shaders (rim light, blossom glow).
        RenderingServer.GlobalShaderParameterSet("sun_dir", toSun);
        RenderingServer.GlobalShaderParameterSet("sun_color", _sun.LightColor * Mathf.Max(_sun.LightEnergy, 0.15f));
        RenderingServer.GlobalShaderParameterSet("night", night);
        _env.AmbientLightEnergy = Mathf.Lerp(0.8f, 0.35f, night);
    }
}
