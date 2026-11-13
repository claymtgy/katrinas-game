using Godot;

namespace KatrinasGame;

/// <summary>Raycasts from the camera centre each frame to find an IInteractable; E uses it.</summary>
public partial class Interactor : Node
{
    /// <summary>Max distance from the player's chest to the thing being used.</summary>
    [Export] public float Reach { get; set; } = 3.2f;

    public IInteractable Target { get; private set; }

    private PlayerController _player;

    public override void _Ready() => _player = GetParent<PlayerController>();

    public override void _PhysicsProcess(double delta)
    {
        Target = null;
        if (GameState.Instance.UiOpen || _player.Build.Active) return;

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

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("interact") && Target != null && !GameState.Instance.UiOpen)
        {
            Target.Interact(_player);
            GetViewport().SetInputAsHandled();
        }
    }
}
