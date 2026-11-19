using Godot;

namespace KatrinasGame;

/// <summary>A tree / rock / bush. Hitting it deals damage to its Health child; every
/// DamagePerDrop damage yields one item, breaking it yields a bonus. Respawns later.
/// Multiplayer: drops go to the shared stockpile and are decided on the server; the shake, fall
/// and regrow animations play on every machine (driven by the mirrored Health events).</summary>
public partial class Harvestable : StaticBody3D
{
    [Export] public string DropItemId { get; set; } = "wood";
    [Export] public float DamagePerDrop { get; set; } = 15f;
    [Export] public int BonusOnBreak { get; set; } = 3;
    [Export] public float RespawnSeconds { get; set; } = 90f;
    /// <summary>"fall" topples like a tree, "crumble" shrinks like a rock.</summary>
    [Export] public string BreakStyle { get; set; } = "fall";
    /// <summary>The tool that works it: "axe" or "pick" ("" = worked out from what it drops).</summary>
    [Export] public string Tool { get; set; } = "";
    /// <summary>Only the right tool gets anything out of it (iron ore needs a pickaxe).</summary>
    [Export] public bool RequiresTool { get; set; } = false;

    /// <summary>"axe" for wood, "pick" for stone and ore, "" for plants (any hand will do).</summary>
    public string ToolNeeded => Tool != "" ? Tool : DropItemId switch { "wood" => "axe", "stone" or "iron_ore" => "pick", _ => "" };

    /// <summary>Most a swing does without the right tool: a sword or a club is no axe.</summary>
    public const float BareHandsCap = 8f;

    /// <summary>Server: what a swing of <paramref name="held"/> does to this node. The right tool
    /// does <see cref="ItemDef.ToolPower"/> drops' worth a swing; anything else is capped at
    /// <see cref="BareHandsCap"/>, and does nothing where the tool is required.</summary>
    public float HarvestDamage(ItemDef held, float swing, bool heavy)
    {
        string need = ToolNeeded;
        if (need == "") return swing;
        if (held != null && held.ToolKind == need) return DamagePerDrop * held.ToolPower * (heavy ? 1.5f : 1f);
        return RequiresTool ? 0f : Mathf.Min(swing, BareHandsCap);
    }

    [ExportGroup("Model")]
    /// <summary>Semicolon-separated model paths; one is picked at random to replace the Visual's placeholder meshes.</summary>
    [Export] public string ModelPaths { get; set; } = "";
    /// <summary>If > 0 the model is scaled so its height matches this (meters); otherwise ModelScale is used.</summary>
    [Export] public float TargetHeight { get; set; } = 0f;
    [Export] public float ModelScale { get; set; } = 1f;

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
        SwapInModel();
        _health.Damaged += OnDamaged;
        _health.Died += OnBroken;
        _health.Revived += Respawn;
    }

    private void SwapInModel()
    {
        var paths = ModelPaths.Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        if (paths.Length == 0) return;
        foreach (var child in _visual.GetChildren())
        {
            _visual.RemoveChild(child);
            child.QueueFree();
        }
        // Pick by name (not randomly) so every player sees the same model.
        var model = Models.Instance(paths[(int)((uint)GD.Hash(Name) % (uint)paths.Length)]);
        _visual.AddChild(model);
        if (TargetHeight > 0) Models.FitHeight(model, TargetHeight);
        else model.Scale = Vector3.One * ModelScale;
    }

    private void OnDamaged(float amount, Node source)
    {
        if (Net.Instance.IsServer)
        {
            _accumulated += amount;
            int drops = (int)(_accumulated / DamagePerDrop);
            if (drops > 0)
            {
                _accumulated -= drops * DamagePerDrop;
                Give(source, drops);
            }
        }

        if (!Net.Instance.IsDedicated)
            Sfx.At(DropItemId switch { "wood" => "chop", "stone" => "mine", _ => "rustle" }, GlobalPosition + Vector3.Up, 0f, 0.1f);

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
        if (Net.Instance.IsServer)
        {
            Give(source, BonusOnBreak);
            GetTree().CreateTimer(RespawnSeconds).Timeout += () =>
            {
                if (IsInstanceValid(this)) _health.Reset(); // fires Revived everywhere
            };
        }
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
    }

    /// <summary>Server: into the gatherer's pack (Supplies), with a pickup message.</summary>
    private void Give(Node source, int count)
    {
        if (count <= 0) return;
        var p = source as PlayerController;
        int got = Supplies.Give(p, DropItemId, count);
        if (p != null && got > 0) Net.Instance.ToastTo(p.PeerId, $"+{got} {ItemDb.NameOf(DropItemId)}");
    }

    private void Respawn()
    {
        if (!IsInstanceValid(this)) return;
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
