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
    private static readonly Color DuskTop = new(0.2f, 0.17f, 0.42f);      // twilight indigo
    private static readonly Color DuskHorizon = new(1.0f, 0.5f, 0.32f);   // magma orange / sunset gold
    private static readonly Color NightTop = new(0.02f, 0.02f, 0.07f);    // obsidian
    private static readonly Color NightHorizon = new(0.1f, 0.08f, 0.22f);
    private static readonly Color SunNoon = new(1f, 0.93f, 0.82f);
    private static readonly Color SunLow = new(1f, 0.55f, 0.35f);

    private DirectionalLight3D _sun, _moon;
    private ProceduralSkyMaterial _sky;
    private Godot.Environment _env;

    public override void _Ready()
    {
        _sky = new ProceduralSkyMaterial
        {
            SunAngleMax = 20f,
            SunCurve = 0.1f,
            SkyCurve = 0.12f,
            GroundCurve = 0.05f,
        };
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
            FogSkyAffect = 0.25f,
            FogHeight = -15f,
            FogHeightDensity = 0.06f,
            FogAerialPerspective = 0.3f,
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
        float night = Mathf.SmoothStep(0f, -0.25f, elevation);
        float dusk = Mathf.Clamp(1f - Mathf.Abs(elevation) / 0.3f, 0f, 1f);

        _sun.LightEnergy = Mathf.Clamp(elevation * 4f, 0f, 1f) * 1.3f;
        _sun.LightColor = SunLow.Lerp(SunNoon, day);
        _sun.Visible = elevation > -0.05f;
        _moon.LightEnergy = 0.22f * night;
        _moon.Visible = night > 0.01f;

        var top = DayTop.Lerp(DuskTop, dusk).Lerp(NightTop, night);
        var horizon = DayHorizon.Lerp(DuskHorizon, dusk).Lerp(NightHorizon, night);
        _sky.SkyTopColor = top;
        _sky.SkyHorizonColor = horizon;
        _sky.GroundHorizonColor = horizon;
        _sky.GroundBottomColor = top.Darkened(0.4f);
        _sky.SkyEnergyMultiplier = Mathf.Lerp(1f, 0.35f, night);

        _env.FogLightColor = horizon.Lerp(new Color(1f, 0.75f, 0.8f), 0.3f * dusk);
        _env.AmbientLightEnergy = Mathf.Lerp(0.8f, 0.35f, night);
    }
}
