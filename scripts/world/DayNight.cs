using Godot;

namespace KatrinasGame;

/// <summary>Owns the sun, moon, sky and WorldEnvironment; advances GameState.TimeOfDay.
/// The sun arcs across the north (cliff) side so the view off the cliff is always backlit.
/// The server's clock is the real one; clients run theirs between the server's time syncs.</summary>
public partial class DayNight : Node3D
{
    [Export] public float DayLengthSeconds { get; set; } = 600f;

    // Palette from the visual brief.
    // Day stays hazy: a soft periwinkle overhead fading to peach, never a hard blue (concept 1).
    private static readonly Color DayTop = new(0.36f, 0.53f, 0.88f);
    private static readonly Color DayHorizon = new(0.99f, 0.85f, 0.74f);
    private static readonly Color DuskTop = new(0.13f, 0.11f, 0.32f);      // twilight indigo
    private static readonly Color DuskHorizon = new(0.98f, 0.46f, 0.2f);   // magma orange / sunset gold
    private static readonly Color NightTop = new(0.04f, 0.06f, 0.17f);    // deep night blue
    private static readonly Color NightHorizon = new(0.14f, 0.18f, 0.36f);
    private static readonly Color SunNoon = new(1f, 0.93f, 0.82f);
    private static readonly Color SunLow = new(1f, 0.55f, 0.35f);
    /// <summary>The air itself by day: distance fades into this warm peach (mixed with the horizon).</summary>
    private static readonly Color Haze = new(0.96f, 0.85f, 0.8f);
    /// <summary>Shadows take this violet, not brown or black (the brief: lavender shadows, peach light).</summary>
    private static readonly Color ShadowTint = new(0.62f, 0.6f, 0.9f);

    /// <summary>Haze thickness on a dry day (rain and the mine thicken it).</summary>
    public const float HazeDensity = 0.0024f;
    private const float BaseExposure = 1.2f;

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
        // r only says *whether* a cell has a star (r > 0.9965, so every star's r is ~1). Speed and
        // phase need their own hashes, or every star twinkles in lockstep.
        float speed = 0.6 + hash(cell + 17.31) * 2.4;
        float phase = hash(cell + 41.7) * 6.2832;
        float tw = 0.5 + 0.5 * sin(TIME * speed + phase);
        float star = step(0.9965, r) * (0.35 + 0.65 * tw * tw) * (0.6 + 0.4 * hash(cell + 3.9));
        col += vec3(0.9, 0.9, 1.0) * star * night * smoothstep(0.02, 0.35, h) * 2.2;
    }
    COLOR = col * energy;
}";

    private static Color Grey(Color c)
    {
        float l = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
        return new Color(l, l * 1.02f, l * 1.1f);
    }

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
            AmbientLightColor = ShadowTint,
            AmbientLightSkyContribution = 0.5f,
            AmbientLightEnergy = 0.8f,
            // AgX rolls highlights off softly and keeps sunsets from going red-brown (ACES did).
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = BaseExposure,
            // A gentle grade: a little less saturation so the palette holds together.
            AdjustmentEnabled = true,
            AdjustmentSaturation = 0.9f,
            AdjustmentContrast = 1.04f,
            GlowEnabled = true,
            GlowIntensity = 0.9f,
            GlowBloom = 0.12f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            SsaoEnabled = true,
            FogEnabled = true,
            // Aerial perspective: far cliffs fade into warm haze, so the world reads in layers.
            FogDensity = HazeDensity,
            FogSkyAffect = 0.12f,
            FogHeight = -15f,
            FogHeightDensity = 0.06f,
            FogAerialPerspective = 0.55f,
            FogSunScatter = 0.3f,
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
        float t = gs.TimeOfDay;
        if (!Net.Instance.ClockPaused)
        {
            t += (float)delta / DayLengthSeconds;
            if (t >= 1f) { t -= 1f; if (Net.Instance.IsServer) gs.Day++; }
            gs.TimeOfDay = t;
        }
        if (!Net.Instance.IsDedicated)
        {
            // Underground (the Hollow Mine): fade the sky's light out over a second or so.
            var cam = GetViewport()?.GetCamera3D();
            float target = cam != null && HomeCliffs.InMine(cam.GlobalPosition) ? 1f : 0f;
            Underground = Mathf.MoveToward(Underground, target, (float)delta * 0.8f);
            Apply(t);
        }
    }

    /// <summary>0 outside, 1 deep in the mine (this screen only): how much of the sky's light is shut out.</summary>
    public static float Underground { get; private set; }

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
        _moon.LightEnergy = 0.55f * night;
        _moon.Visible = night > 0.01f;

        var top = DayTop.Lerp(DuskTop, dusk).Lerp(NightTop, night);
        var horizon = DayHorizon.Lerp(DuskHorizon, dusk).Lerp(NightHorizon, night);

        // Rain: a lower, greyer sky, a dimmer sun and thicker haze.
        float wet = Weather.Wetness;
        if (wet > 0.001f)
        {
            top = top.Lerp(Grey(top) * 0.85f, 0.65f * wet);
            horizon = horizon.Lerp(Grey(horizon) * 0.8f, 0.7f * wet);
            _sun.LightEnergy *= 1f - 0.65f * wet;
        }
        // Underground: no sun, moon or sky light; a close, dark haze instead.
        float under = Underground;
        _sun.LightEnergy *= 1f - under;
        _moon.LightEnergy *= 1f - under;
        _env.FogDensity = Mathf.Lerp(HazeDensity + 0.005f * wet, 0.035f, under);
        RenderingServer.GlobalShaderParameterSet("wetness", wet);
        _sky.SetShaderParameter("top_color", top);
        _sky.SetShaderParameter("horizon_color", horizon);
        _sky.SetShaderParameter("ground_color", horizon.Lerp(top, 0.6f).Darkened(0.3f));
        _sky.SetShaderParameter("glow_color", SunLow);
        _sky.SetShaderParameter("glow_strength", Mathf.Clamp(dusk * 1.2f, 0f, 1f) * (1f - night * 0.6f) * (1f - 0.7f * wet));
        _sky.SetShaderParameter("energy", Mathf.Lerp(1f, 0.85f, night));

        // By day the haze is warm peach; at dusk it warms to rose; at night it goes blue with the sky.
        var haze = horizon.Lerp(Haze, 0.4f * (1f - night)).Lerp(new Color(1f, 0.72f, 0.74f), 0.3f * dusk);
        _env.FogLightColor = haze.Lerp(new Color(0.015f, 0.014f, 0.02f), under);
        _env.FogHeightDensity = Mathf.Lerp(0.06f, 0f, under);
        _env.FogSunScatter = Mathf.Lerp(0.3f, 0f, under);
        // Night shadows go indigo rather than violet.
        _env.AmbientLightColor = ShadowTint.Lerp(new Color(0.35f, 0.42f, 0.85f), night);

        // Shared with the stylized shaders (rim light, blossom glow).
        RenderingServer.GlobalShaderParameterSet("sun_dir", toSun);
        RenderingServer.GlobalShaderParameterSet("sun_color", _sun.LightColor * Mathf.Max(_sun.LightEnergy, 0.15f * (1f - under)));
        RenderingServer.GlobalShaderParameterSet("night", night);
        _env.AmbientLightEnergy = Mathf.Lerp(Mathf.Lerp(0.8f, 0.6f, night), 0.035f, under);
        _env.ReflectedLightSource = under > 0.5f ? Godot.Environment.ReflectionSource.Disabled : Godot.Environment.ReflectionSource.Bg;
        _env.TonemapExposure = Mathf.Lerp(BaseExposure, 1.35f, under);
    }
}
