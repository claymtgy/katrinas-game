using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Passing rain showers. The server rolls when they come and go (GameState.Raining,
/// replicated by Net); while it rains every farm plot stays watered. Every machine with a
/// screen draws the rain around its camera and lets DayNight grey the sky (Wetness).</summary>
public partial class Weather : Node3D
{
    /// <summary>Real seconds between showers (random in this range).</summary>
    [Export] public Vector2 DryInterval { get; set; } = new(240f, 540f);
    /// <summary>Real seconds a shower lasts.</summary>
    [Export] public Vector2 ShowerLength { get; set; } = new(50f, 110f);

    public static Weather Instance { get; private set; }
    /// <summary>0 = clear, 1 = full rain (smoothed; drives the sky and fog).</summary>
    public static float Wetness { get; private set; }
    /// <summary>Scripted tests: no random showers.</summary>
    public static bool AutoRain { get; set; } = true;

    private float _timer;
    private float _waterTimer;
    private GpuParticles3D _rain;
    private readonly RandomNumberGenerator _rng = new();

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        Wetness = 0f;
    }

    public override void _Ready()
    {
        _rng.Randomize();
        _timer = _rng.RandfRange(DryInterval.X, DryInterval.Y);
        if (!Net.Instance.IsDedicated) BuildRain();
    }

    /// <summary>Server: start or stop a shower now.</summary>
    public void SetRain(bool on)
    {
        if (!Net.Instance.IsServer) return;
        _timer = on ? _rng.RandfRange(ShowerLength.X, ShowerLength.Y) : _rng.RandfRange(DryInterval.X, DryInterval.Y);
        Net.Instance.ServerSetRain(on);
        if (on) WaterPlots();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        var net = Net.Instance;

        if (net.IsServer && net.InSession && !net.ClockPaused)
        {
            if (AutoRain)
            {
                _timer -= dt;
                if (_timer <= 0) SetRain(!gs.Raining);
            }
            if (gs.Raining)
            {
                _waterTimer -= dt;
                if (_waterTimer <= 0) { _waterTimer = 2f; WaterPlots(); }
            }
        }

        Wetness = Mathf.MoveToward(Wetness, gs.Raining ? 1f : 0f, dt / 6f);
        if (_rain != null)
        {
            _rain.Emitting = Wetness > 0.05f;
            _rain.AmountRatio = Mathf.Clamp(Wetness, 0.05f, 1f);
            var cam = GetViewport()?.GetCamera3D();
            if (cam != null) _rain.GlobalPosition = cam.GlobalPosition + Vector3.Up * 9f;
        }
    }

    private static void WaterPlots()
    {
        foreach (var plot in GameState.Instance.PlacedBuildings.Select(b => b.Node).OfType<FarmPlot>())
            if (IsInstanceValid(plot)) plot.ServerWater();
    }

    private void BuildRain()
    {
        // Thin bright streaks falling around the camera.
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(0.85f, 0.9f, 1f, 0.45f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _rain = new GpuParticles3D
        {
            Name = "Rain",
            Amount = 1800,
            Lifetime = 1.1f,
            Emitting = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(22, 2, 22),
                Direction = new Vector3(0.12f, -1, 0.05f),
                Spread = 3f,
                InitialVelocityMin = 17f,
                InitialVelocityMax = 21f,
                Gravity = Vector3.Zero,
                Color = new Color(0.8f, 0.86f, 1f, 0.55f),
            },
            DrawPass1 = new QuadMesh { Size = new Vector2(0.025f, 0.7f), Material = mat },
            VisibilityAabb = new Aabb(new Vector3(-30, -30, -30), new Vector3(60, 45, 60)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            LocalCoords = false,
        };
        AddChild(_rain);
    }
}
