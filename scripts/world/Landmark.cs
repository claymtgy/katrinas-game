using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>A place worth finding out in the country (a ruined tower, a great tree, a quarry).
/// The first time anyone comes within <see cref="Radius"/> it's discovered for the whole world
/// (the journal fact "found:&lt;node name&gt;", shared and saved): everyone hears about it and
/// gets <see cref="Levels.Landmark"/> XP. Server only; clients learn through the journal.</summary>
public partial class Landmark : Node3D
{
    [Export] public string Title { get; set; } = "Landmark";
    [Export] public float Radius { get; set; } = 16f;

    public static readonly List<Landmark> All = new();

    private double _nextCheck;

    public string Fact => $"found:{Name}";
    public bool Found => Journal.Instance?.Facts.Contains(Fact) ?? false;

    public override void _Ready() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public override void _Process(double delta)
    {
        var net = Net.Instance;
        if (net == null || !net.InSession || !net.IsServer) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextCheck) return;
        _nextCheck = now + 0.5;
        if (Found) return;
        var at = GlobalPosition;
        foreach (var p in GameState.Instance.Players.Values)
        {
            if (!IsInstanceValid(p) || p.Health.IsDead) continue;
            var d = p.GlobalPosition - at;
            if (new Vector2(d.X, d.Z).Length() > Radius || Mathf.Abs(d.Y) > Radius) continue;
            Discover();
            return;
        }
    }

    /// <summary>Server: mark it found and tell everyone.</summary>
    public void Discover()
    {
        if (Found) return;
        Journal.Fact(Fact);
        Net.Instance.BroadcastJournal("");
        Net.Instance.ToastAll($"Discovered: {Title}");
        Net.Instance.ServerAwardXpAll(Levels.Landmark, $"found {Title}");
    }
}
