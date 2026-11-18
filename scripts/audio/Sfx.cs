using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Fire-and-forget sound effects by name (res://assets/audio/&lt;name&gt;.wav, made by
/// tools/make_audio.py; swap in real recordings with the same names). Positional sounds get a
/// short-lived AudioStreamPlayer3D under the current scene. Silent on a dedicated server, and for
/// the first moments after a world loads (so a join snapshot doesn't play every building's
/// "thunk" at once).</summary>
public static class Sfx
{
    public const string Dir = "res://assets/audio/";

    private static readonly Dictionary<string, AudioStream> Cache = new();
    private static readonly HashSet<string> Warned = new();
    private static ulong _quietUntilMs;

    /// <summary>How many times each sound has played this run, for tests and debugging.</summary>
    public static readonly Dictionary<string, int> Played = new();

    public static bool Enabled => !(Net.Instance?.IsDedicated ?? false);

    /// <summary>Hold one-shots back for a moment (called when a world is built).</summary>
    public static void QuietFor(float seconds) => _quietUntilMs = Time.GetTicksMsec() + (ulong)(seconds * 1000);

    public static AudioStream Get(string name, bool loop = false)
    {
        if (Cache.TryGetValue(name, out var s)) return s;
        string path = Dir + name + ".wav";
        s = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
        if (s == null && Warned.Add(name)) GD.PushWarning($"[Sfx] Missing sound {path} (run tools/make_audio.py, then import)");
        if (loop && s is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
        }
        Cache[name] = s;
        return s;
    }

    private static Node Root => (Engine.GetMainLoop() as SceneTree)?.CurrentScene;

    private static bool Allow(string name)
    {
        if (!Enabled || Time.GetTicksMsec() < _quietUntilMs) return false;
        Played[name] = Played.GetValueOrDefault(name) + 1;
        return true;
    }

    /// <summary>Play a sound in the world. pitchVar randomises pitch ± that fraction.</summary>
    public static void At(string name, Vector3 position, float volumeDb = 0f, float pitchVar = 0.08f, float maxDistance = 45f)
    {
        if (!Allow(name) || Root is not { } root || Get(name) is not { } stream) return;
        var p = new AudioStreamPlayer3D
        {
            Stream = stream,
            VolumeDb = volumeDb,
            UnitSize = 4f,
            MaxDistance = maxDistance,
            PitchScale = 1f + (float)GD.RandRange(-pitchVar, pitchVar),
            AttenuationFilterCutoffHz = 9000f,
            AttenuationFilterDb = -12f,
        };
        root.AddChild(p);
        p.GlobalPosition = position;
        p.Finished += p.QueueFree;
        p.Play();
    }

    /// <summary>Play a non-positional sound (interface, your own pack).</summary>
    public static void Ui(string name, float volumeDb = 0f, float pitchVar = 0.04f)
    {
        if (!Allow(name) || Root is not { } root || Get(name) is not { } stream) return;
        var p = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = volumeDb,
            PitchScale = 1f + (float)GD.RandRange(-pitchVar, pitchVar),
        };
        root.AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }

    /// <summary>A looping positional emitter (waterfall, campfire, lava) parented to a node.</summary>
    public static AudioStreamPlayer3D Loop(Node3D parent, string name, Vector3 localPos, float volumeDb, float maxDistance, float unitSize = 6f)
    {
        if (!Enabled || Get(name, loop: true) is not { } stream) return null;
        var p = new AudioStreamPlayer3D
        {
            Name = "Sound_" + name,
            Stream = stream,
            VolumeDb = volumeDb,
            UnitSize = unitSize,
            MaxDistance = maxDistance,
            Position = localPos,
            AttenuationFilterCutoffHz = 6000f,
        };
        parent.AddChild(p);
        // Start each loop somewhere different so identical emitters don't phase.
        p.Ready += () => p.Play((float)GD.RandRange(0, stream.GetLength() * 0.9));
        return p;
    }
}
