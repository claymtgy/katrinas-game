using Godot;

namespace KatrinasGame;

/// <summary>Straw dummy: shows floating damage numbers, topples when destroyed, rebuilds itself.</summary>
public partial class TrainingDummy : StaticBody3D
{
    [Export] public float RespawnSeconds { get; set; } = 6f;

    private Health _health;
    private Node3D _visual;
    private Label3D _hpLabel;
    private uint _layer;
    private Tween _tween;

    public override void _Ready()
    {
        _health = GetNode<Health>("Health");
        _visual = GetNode<Node3D>("Visual");
        _layer = CollisionLayer;
        _hpLabel = new Label3D
        {
            Position = new Vector3(0, 2.3f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48, PixelSize = 0.006f, OutlineSize = 10,
            Modulate = new Color(1f, 0.85f, 0.6f),
        };
        AddChild(_hpLabel);
        UpdateLabel();
        _health.Damaged += OnDamaged;
        _health.Died += OnDied;
    }

    private void UpdateLabel() => _hpLabel.Text = $"Training Dummy  {_health.Current:0}/{_health.Max:0}";

    private void OnDamaged(float amount, Node source)
    {
        UpdateLabel();
        SpawnDamageNumber(amount);
        if (_health.IsDead) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:x", -0.25f, 0.06);
        _tween.TweenProperty(_visual, "rotation:x", 0.1f, 0.12);
        _tween.TweenProperty(_visual, "rotation:x", 0f, 0.15);
    }

    private void SpawnDamageNumber(float amount)
    {
        var label = new Label3D
        {
            Text = $"-{amount:0}",
            Position = new Vector3((float)GD.RandRange(-0.4, 0.4), 1.8f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 72, PixelSize = 0.006f, OutlineSize = 12,
            Modulate = new Color(1f, 0.45f, 0.25f),
            NoDepthTest = true,
        };
        AddChild(label);
        var t = label.CreateTween().SetParallel();
        t.TweenProperty(label, "position:y", 2.8f, 0.8);
        t.TweenProperty(label, "modulate:a", 0f, 0.8).SetDelay(0.3);
        t.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    private void OnDied(Node source)
    {
        Hud.Toast("Training dummy destroyed! It will be rebuilt shortly.");
        CollisionLayer = 0;
        _hpLabel.Visible = false;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:x", -Mathf.Pi / 2f, 0.5).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        GetTree().CreateTimer(RespawnSeconds).Timeout += () =>
        {
            if (!IsInstanceValid(this)) return;
            _health.Reset();
            CollisionLayer = _layer;
            _hpLabel.Visible = true;
            UpdateLabel();
            _tween?.Kill();
            _tween = CreateTween();
            _tween.TweenProperty(_visual, "rotation:x", 0f, 0.6).SetTrans(Tween.TransitionType.Back);
        };
    }
}
