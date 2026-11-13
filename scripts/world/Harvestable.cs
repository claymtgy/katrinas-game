using Godot;

namespace KatrinasGame;

/// <summary>A tree / rock / bush. Hitting it deals damage to its Health child; every
/// DamagePerDrop damage yields one item, breaking it yields a bonus. Respawns later.</summary>
public partial class Harvestable : StaticBody3D
{
    [Export] public string DropItemId { get; set; } = "wood";
    [Export] public float DamagePerDrop { get; set; } = 15f;
    [Export] public int BonusOnBreak { get; set; } = 3;
    [Export] public float RespawnSeconds { get; set; } = 90f;
    /// <summary>"fall" topples like a tree, "crumble" shrinks like a rock.</summary>
    [Export] public string BreakStyle { get; set; } = "fall";

    private Health _health;
    private Node3D _visual;
    private float _accumulated;
    private uint _layer;
    private Tween _tween;

    public override void _Ready()
    {
        _health = GetNode<Health>("Health");
        _visual = GetNode<Node3D>("Visual");
        _layer = CollisionLayer;
        _health.Damaged += OnDamaged;
        _health.Died += OnBroken;
    }

    private void OnDamaged(float amount, Node source)
    {
        _accumulated += amount;
        int drops = (int)(_accumulated / DamagePerDrop);
        if (drops > 0)
        {
            _accumulated -= drops * DamagePerDrop;
            GameState.Instance.Inventory.Add(DropItemId, drops);
        }

        // Little shake.
        if (_health.IsDead) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:z", 0.06f, 0.05);
        _tween.TweenProperty(_visual, "rotation:z", -0.04f, 0.06);
        _tween.TweenProperty(_visual, "rotation:z", 0f, 0.08);
    }

    private void OnBroken(Node source)
    {
        GameState.Instance.Inventory.Add(DropItemId, BonusOnBreak);
        CollisionLayer = 0;
        _tween?.Kill();
        _tween = CreateTween();
        if (BreakStyle == "fall")
        {
            // Topple away from whoever chopped it.
            float dir = 1f;
            if (source is Node3D s) dir = ToLocal(s.GlobalPosition).X > 0 ? -1f : 1f;
            _tween.TweenProperty(_visual, "rotation:z", dir * Mathf.Pi / 2f, 0.9).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        }
        _tween.TweenProperty(_visual, "scale", Vector3.One * 0.01f, 0.35);
        _tween.TweenCallback(Callable.From(() => _visual.Visible = false));
        GetTree().CreateTimer(RespawnSeconds).Timeout += Respawn;
    }

    private void Respawn()
    {
        if (!IsInstanceValid(this)) return;
        _health.Reset();
        _accumulated = 0;
        CollisionLayer = _layer;
        _visual.Rotation = Vector3.Zero;
        _visual.Visible = true;
        _visual.Scale = Vector3.One * 0.01f;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "scale", Vector3.One, 1.2).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
