using Godot;

namespace KatrinasGame;

/// <summary>B toggles build mode. A translucent ghost follows the crosshair on the ground,
/// snaps to a grid, turns red when placement is invalid. Wheel cycles, R rotates, LMB places.</summary>
public partial class BuildSystem : Node
{
    [Export] public float GridSize { get; set; } = 0.5f;
    [Export] public float RotationStepDegrees { get; set; } = 45f;
    [Export] public float MaxPlaceDistance { get; set; } = 9f;

    public bool Active { get; private set; }
    public BuildingDef Current => ItemDb.Buildings.Count > 0 ? ItemDb.Buildings[_index] : null;
    /// <summary>Why the current spot is invalid ("" when valid). Shown by the HUD.</summary>
    public string InvalidReason { get; private set; } = "";

    private PlayerController _player;
    private int _index;
    private float _rotationY;
    private Node3D _ghost;
    private bool _hasSpot;
    private Vector3 _spot;
    private readonly StandardMaterial3D _ghostMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(0.4f, 1f, 0.5f, 0.45f),
    };

    public override void _Ready() => _player = GetParent<PlayerController>();

    public override void _UnhandledInput(InputEvent e)
    {
        var gs = GameState.Instance;
        if (gs.UiOpen) return;

        if (e.IsActionPressed("build_mode"))
        {
            SetActive(!Active);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (!Active) return;

        if (e.IsActionPressed("next_build")) Cycle(1);
        else if (e.IsActionPressed("prev_build")) Cycle(-1);
        else if (e.IsActionPressed("rotate_build")) _rotationY += Mathf.DegToRad(RotationStepDegrees);
        else if (e.IsActionPressed("attack")) TryPlace();
        else if (e.IsActionPressed("heavy_attack")) SetActive(false);
        else return;
        GetViewport().SetInputAsHandled();
    }

    public void SetActive(bool active)
    {
        if (active && ItemDb.Buildings.Count == 0) { Hud.Toast("No buildings defined in data/buildings"); return; }
        Active = active;
        if (active) CreateGhost();
        else FreeGhost();
    }

    private void Cycle(int dir)
    {
        int n = ItemDb.Buildings.Count;
        _index = ((_index + dir) % n + n) % n;
        CreateGhost();
    }

    private void CreateGhost()
    {
        FreeGhost();
        var def = Current;
        if (def?.Scene == null) return;
        _ghost = def.Scene.Instantiate<Node3D>();
        _ghost.ProcessMode = ProcessModeEnum.Disabled;
        _ghost.Visible = false;
        GetTree().CurrentScene.AddChild(_ghost);
        MakeGhostly(_ghost);
    }

    private void MakeGhostly(Node node)
    {
        switch (node)
        {
            case CollisionObject3D co: co.CollisionLayer = 0; co.CollisionMask = 0; break;
            case GeometryInstance3D gi:
                gi.MaterialOverride = _ghostMaterial;
                gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                break;
            case Light3D light: light.Visible = false; break;
        }
        foreach (var child in node.GetChildren()) MakeGhostly(child);
    }

    private void FreeGhost()
    {
        if (_ghost != null && IsInstanceValid(_ghost)) _ghost.QueueFree();
        _ghost = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Active || _ghost == null) return;

        var cam = _player.Rig.Camera;
        var from = cam.GlobalPosition;
        var to = from - cam.GlobalBasis.Z * (MaxPlaceDistance + _player.Rig.CurrentDistance);
        var space = _player.GetWorld3D().DirectSpaceState;
        var ray = PhysicsRayQueryParameters3D.Create(from, to, GameState.LayerWorld,
            new Godot.Collections.Array<Rid> { _player.GetRid() });
        var hit = space.IntersectRay(ray);

        _hasSpot = hit.Count > 0;
        _ghost.Visible = _hasSpot;
        if (!_hasSpot) { InvalidReason = "Aim at the ground"; return; }

        var p = hit["position"].AsVector3();
        var normal = hit["normal"].AsVector3();
        _spot = new Vector3(Mathf.Snapped(p.X, GridSize), p.Y, Mathf.Snapped(p.Z, GridSize));
        _ghost.GlobalPosition = _spot;
        _ghost.Rotation = new Vector3(0, _rotationY, 0);

        InvalidReason = Validate(space, normal);
        _ghostMaterial.AlbedoColor = InvalidReason == ""
            ? new Color(0.4f, 1f, 0.5f, 0.45f)
            : new Color(1f, 0.3f, 0.3f, 0.45f);
    }

    private string Validate(PhysicsDirectSpaceState3D space, Vector3 normal)
    {
        var gs = GameState.Instance;
        var def = Current;
        if (normal.Y < 0.85f) return "Too steep";

        var flatOffset = new Vector2(_spot.X - gs.ClaimCenter.X, _spot.Z - gs.ClaimCenter.Z);
        if (flatOffset.Length() > gs.ClaimRadius) return "Outside your land claim";

        // Overlap test against props, other buildings and the player.
        var size = def.Footprint;
        var shapeQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = size - new Vector3(0.05f, 0.1f, 0.05f) },
            Transform = new Transform3D(new Basis(Vector3.Up, _rotationY), _spot + Vector3.Up * (size.Y * 0.5f + 0.05f)),
            CollisionMask = GameState.LayerProps | GameState.LayerPlayer,
        };
        if (space.IntersectShape(shapeQuery, 1).Count > 0) return "Something is in the way";

        if (!gs.Inventory.Has(def.GetCost())) return "Not enough materials";
        return "";
    }

    public void TryPlace()
    {
        if (!_hasSpot || InvalidReason != "")
        {
            Hud.Toast(InvalidReason == "" ? "Can't build here" : InvalidReason);
            return;
        }
        var gs = GameState.Instance;
        var def = Current;
        if (!gs.Inventory.TryConsume(def.GetCost())) return;
        gs.PlaceBuilding(def, _spot, _rotationY);
        Hud.Toast($"Built {def.DisplayName}");
    }
}
