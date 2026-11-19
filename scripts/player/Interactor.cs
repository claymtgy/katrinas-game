using Godot;

namespace KatrinasGame;

/// <summary>Raycasts from the camera centre each frame to find an IInteractable; E uses it.</summary>
public partial class Interactor : Node
{
    /// <summary>Max distance from the player's chest to the thing being used.</summary>
    [Export] public float Reach { get; set; } = 3.2f;

    public IInteractable Target { get; private set; }
    /// <summary>A downed friend within reach: holding Interact revives them (GDD §16).</summary>
    public PlayerController ReviveTarget { get; private set; }
    /// <summary>Scripted tests: hold Interact without the key.</summary>
    public bool HoldRevive { get; set; }
    private PlayerController _reviving;

    private PlayerController _player;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        if (_player.IsLocal) return;
        SetPhysicsProcess(false);
        SetProcessUnhandledInput(false);
    }

    public override void _PhysicsProcess(double delta)
    {
        Target = null;
        UpdateRevive();
        if (ReviveTarget != null || _player.Downed) return;
        if (GameState.Instance.UiOpen || _player.Build.Active || _player.Sleeping) return;

        var cam = _player.Rig.Camera;
        var from = cam.GlobalPosition;
        var to = from - cam.GlobalBasis.Z * (Reach + _player.Rig.CurrentDistance + 0.5f);
        var query = PhysicsRayQueryParameters3D.Create(from, to, GameState.LayerWorld | GameState.LayerProps,
            new Godot.Collections.Array<Rid> { _player.GetRid() });
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return;

        var chest = _player.GlobalPosition + Vector3.Up * 1.2f;
        if (chest.DistanceTo(hit["position"].AsVector3()) > Reach) return;

        // The interactable may be the collider itself or its parent (e.g. a mesh child body).
        var node = hit["collider"].AsGodotObject() as Node;
        Target = node as IInteractable ?? node?.GetParent() as IInteractable;
    }

    private void UpdateRevive()
    {
        var gs = GameState.Instance;
        ReviveTarget = null;
        if (!_player.Downed && !_player.Health.IsDead && !_player.Sleeping)
        {
            float best = Net.ReviveReach;
            foreach (var o in gs.Players.Values)
            {
                if (o == _player || !IsInstanceValid(o) || !o.Downed) continue;
                float d = Net.FlatDistance(o.GlobalPosition, _player.GlobalPosition);
                if (d <= best) { best = d; ReviveTarget = o; }
            }
        }
        bool hold = ReviveTarget != null && !gs.UiOpen && (HoldRevive || Input.IsActionPressed("interact"));
        var want = hold ? ReviveTarget : null;
        if (want == _reviving) return;
        if (_reviving != null && IsInstanceValid(_reviving)) Net.Instance.RequestRevive(_reviving, false);
        if (want != null) Net.Instance.RequestRevive(want, true);
        _reviving = want;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("interact") && ReviveTarget != null)
        {
            GetViewport().SetInputAsHandled(); // the hold is read every frame (UpdateRevive)
            return;
        }
        if (e.IsActionPressed("interact") && _player.Sleeping)
        {
            Net.Instance.RequestWake();
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("interact") && Target != null && !GameState.Instance.UiOpen)
        {
            Target.Interact(_player);
            GetViewport().SetInputAsHandled();
        }
    }
}
