using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>The soundtrack (CC0 tracks in res://assets/audio/music, see CREDITS.md there). Picks a
/// mood from where the local player is and what's happening: the title screen, day or night on
/// the road, Ashby, the Hollow Mine, or a fight. Crossfades between moods; within a calm mood the
/// tracks take turns with a stretch of quiet between them, so the world's own sounds get room.
/// A track's mood is its file name's prefix (day_*, night_*, village_*, cave_*, combat_*, menu_*).
/// Lives under GameState, so it keeps playing across scene changes. Silent on a dedicated server.</summary>
public partial class Music : Node
{
    public const string Dir = "res://assets/audio/music/";
    public enum Mood { None, Menu, Day, Night, Village, Cave, Combat }

    private static readonly Dictionary<Mood, string> Prefix = new()
    {
        [Mood.Menu] = "menu_", [Mood.Day] = "day_", [Mood.Night] = "night_",
        [Mood.Village] = "village_", [Mood.Cave] = "cave_", [Mood.Combat] = "combat_",
    };
    /// <summary>Moods whose tracks loop back to back (no quiet gap).</summary>
    private static bool Loops(Mood m) => m is Mood.Menu or Mood.Combat or Mood.Village or Mood.Cave;

    private const float FadeSeconds = 2.5f;
    private const float CombatHold = 7f;   // keep fighting music this long after the last threat
    private const float CombatRange = 22f;

    public static Music Instance { get; private set; }
    /// <summary>The mood playing now (tests and debugging).</summary>
    public Mood Current { get; private set; } = Mood.None;
    public string CurrentTrack { get; private set; } = "";

    private readonly Dictionary<Mood, List<string>> _tracks = new();
    private readonly Dictionary<Mood, int> _next = new();
    private AudioStreamPlayer _a, _b;   // _a is the live one; _b fades out
    private float _gap;                 // seconds of quiet left before the next calm track
    private float _combatLeft;
    private float _checkTimer;
    private bool _enabled;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _enabled = !OS.GetCmdlineUserArgs().Contains("--server") && DisplayServer.GetName() != "headless";
        if (!_enabled) return;
        _a = Player("MusicA");
        _b = Player("MusicB");
        FindTracks();
    }

    private AudioStreamPlayer Player(string name)
    {
        var p = new AudioStreamPlayer { Name = name, Bus = AudioSettings.MusicBus, VolumeDb = -80f };
        p.Finished += () => OnFinished(p);
        AddChild(p);
        return p;
    }

    private void FindTracks()
    {
        using var dir = DirAccess.Open(Dir);
        if (dir == null) return;
        var files = new HashSet<string>();
        foreach (var f in dir.GetFiles())
        {
            // An exported game lists the imported resources as name.mp3.import / .remap.
            string name = f.Replace(".import", "").Replace(".remap", "");
            if (name.EndsWith(".mp3") || name.EndsWith(".ogg") || name.EndsWith(".wav")) files.Add(name);
        }
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        foreach (var (mood, prefix) in Prefix)
        {
            _tracks[mood] = files.Where(f => f.StartsWith(prefix)).OrderBy(f => f).ToList();
            _next[mood] = _tracks[mood].Count > 0 ? rng.RandiRange(0, _tracks[mood].Count - 1) : 0;
        }
    }

    // ------------------------------------------------------------------ choosing the mood

    private Mood Wanted(float dt)
    {
        var net = Net.Instance;
        var gs = GameState.Instance;
        if (net == null || !net.InSession) { _combatLeft = 0; return Mood.Menu; }
        var me = gs.LocalPlayer;
        if (me == null || !IsInstanceValid(me)) return Current == Mood.Menu ? Mood.None : Current;

        if (AudioSettings.CombatMusic && Threatened(me)) _combatLeft = CombatHold;
        else _combatLeft = Mathf.Max(0, _combatLeft - dt);
        if (_combatLeft > 0) return Mood.Combat;

        var pos = me.GlobalPosition;
        if (HomeCliffs.InMine(pos)) return Mood.Cave;
        if (new Vector2(pos.X - HomeCliffs.AshbyCenter.X, pos.Z - HomeCliffs.AshbyCenter.Z).Length() < 60f) return Mood.Village;
        float t = gs.TimeOfDay;
        return t > 0.22f && t < 0.78f ? Mood.Day : Mood.Night;
    }

    /// <summary>Something hostile is after this player: on the server we know who enemies hunt;
    /// on a client, an angry-looking enemy close by is enough. Raids count from further off.</summary>
    private static bool Threatened(PlayerController me)
    {
        var wild = Wildlife.Instance;
        if (wild == null) return false;
        if (wild.RaidActive && Supplies.AtHome(me)) return true;
        bool server = Net.Instance.IsServer;
        foreach (var e in wild.Enemies.Values)
        {
            if (!IsInstanceValid(e) || e.Current == Enemy.State.Dead || e is Goat) continue;
            float d = e.GlobalPosition.DistanceTo(me.GlobalPosition);
            if (d > CombatRange) continue;
            if (server)
            {
                if (e.Target == me && e.Current is Enemy.State.Chase or Enemy.State.Windup or Enemy.State.Recover or Enemy.State.Stagger) return true;
            }
            else if (d < 14f && e is not Boar) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ playing

    public override void _Process(double delta)
    {
        if (!_enabled) return;
        float dt = (float)delta;
        _checkTimer -= dt;
        if (_checkTimer <= 0)
        {
            _checkTimer = 0.5f;
            var want = Wanted(0.5f);
            if (want != Current) Switch(want);
        }
        if (_gap > 0 && !_a.Playing)
        {
            _gap -= dt;
            if (_gap <= 0) PlayNext(Current, fadeIn: true);
        }
    }

    private void Switch(Mood mood)
    {
        // Calm moods that share a feel (day <-> night) don't cut a track short: let it finish.
        bool calmSwap = Current is Mood.Day or Mood.Night && mood is Mood.Day or Mood.Night && _a.Playing;
        Current = mood;
        if (calmSwap) return;
        FadeOutLive();
        _gap = 0;
        if (mood == Mood.None) return;
        // Coming back from a fight into a calm mood: a short breath of quiet first.
        if (!Loops(mood) && _b.Playing) _gap = 4f;
        else PlayNext(mood, fadeIn: true);
    }

    private void FadeOutLive()
    {
        if (!_a.Playing) return;
        (_a, _b) = (_b, _a);
        var t = CreateTween();
        t.TweenProperty(_b, "volume_db", -60f, FadeSeconds);
        t.TweenCallback(Callable.From(_b.Stop));
    }

    private void PlayNext(Mood mood, bool fadeIn)
    {
        if (!_tracks.TryGetValue(mood, out var list) || list.Count == 0) { CurrentTrack = ""; return; }
        int i = _next[mood] % list.Count;
        _next[mood] = i + 1;
        string file = list[i];
        var stream = ResourceLoader.Exists(Dir + file) ? GD.Load<AudioStream>(Dir + file) : null;
        if (stream == null) return;
        if (_a.Playing) FadeOutLive();
        CurrentTrack = file;
        _a.Stream = stream;
        _a.VolumeDb = fadeIn ? -40f : 0f;
        _a.Play();
        if (fadeIn) CreateTween().TweenProperty(_a, "volume_db", 0f, FadeSeconds).SetTrans(Tween.TransitionType.Sine);
    }

    private void OnFinished(AudioStreamPlayer p)
    {
        if (p != _a || Current == Mood.None) return;
        if (Loops(Current)) PlayNext(Current, fadeIn: false);
        else _gap = (float)GD.RandRange(25.0, 70.0); // a stretch of quiet, then the next track
    }
}
