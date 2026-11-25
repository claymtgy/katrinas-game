using Godot;

namespace KatrinasGame;

/// <summary>The world's background bed, mixed per machine from what that player can hear:
/// wind (stronger high up and in rain), rain (follows Weather.Wetness), birds by day, crickets
/// at night. Positional loops (waterfalls, campfires, the volcano) are separate
/// <see cref="Sfx.Loop"/> emitters placed with the things that make the noise.</summary>
public partial class Ambience : Node
{
    private AudioStreamPlayer _wind, _rain, _birds, _crickets;

    /// <summary>Current linear volumes (0..1), for tests and debugging.</summary>
    public float Wind { get; private set; }
    public float Rain { get; private set; }
    public float Birds { get; private set; }
    public float Crickets { get; private set; }

    public static Ambience Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        Sfx.QuietFor(2.5f);
        if (!Sfx.Enabled) return;
        _wind = Bed("amb_wind");
        _rain = Bed("amb_rain");
        _birds = Bed("amb_birds");
        _crickets = Bed("amb_crickets");
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private AudioStreamPlayer Bed(string name)
    {
        var stream = Sfx.Get(name, loop: true);
        if (stream == null) return null;
        var p = new AudioStreamPlayer { Name = name, Stream = stream, VolumeDb = -80f, Bus = AudioSettings.AmbienceBus };
        AddChild(p);
        p.Play((float)GD.RandRange(0, stream.GetLength() * 0.9));
        return p;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        float t = gs.TimeOfDay;
        float wet = Weather.Wetness;
        // 0 at night, 1 in daylight, fading over the hour around dawn and dusk.
        float day = Mathf.Clamp((t - 0.22f) / 0.05f, 0f, 1f) * Mathf.Clamp((0.78f - t) / 0.05f, 0f, 1f);
        float height = gs.LocalPlayer != null ? Mathf.Clamp(gs.LocalPlayer.GlobalPosition.Y / 30f, 0f, 1f) : 0f;

        Wind = Ease(Wind, 0.32f + 0.25f * height + 0.3f * wet, dt);
        Rain = Ease(Rain, wet, dt);
        Birds = Ease(Birds, day * (1f - wet) * (gs.LocalPlayer != null && HomeCliffs.InFoothillsArea(gs.LocalPlayer.GlobalPosition) ? 0f : 0.22f), dt);
        Crickets = Ease(Crickets, (1f - day) * (1f - wet * 0.8f) * 0.45f, dt);

        Set(_wind, Wind);
        Set(_rain, Rain * 0.8f);
        Set(_birds, Birds);
        Set(_crickets, Crickets);
    }

    private static float Ease(float from, float to, float dt) => Mathf.Lerp(from, to, Mathf.Min(1f, dt * 0.8f));

    private static void Set(AudioStreamPlayer p, float linear)
    {
        if (p != null) p.VolumeDb = Mathf.LinearToDb(Mathf.Max(linear, 0.0001f));
    }
}
