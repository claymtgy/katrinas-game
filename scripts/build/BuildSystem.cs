using Godot;

namespace KatrinasGame;

/// <summary>B toggles build mode. A translucent ghost follows the crosshair on the ground,
/// snaps to a grid, turns red when placement is invalid. Wheel cycles, R rotates, LMB places.
/// The ghost is local; placing asks the server, which re-checks the spot and pays from the
/// shared stockpile (Net.RequestBuild).</summary>
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

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        if (_player.IsLocal) return;
        SetPhysicsProcess(false);
        SetProcessUnhandledInput(false);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        var gs = GameState.Instance;
        if (gs.UiOpen || _player.Sleeping || _player.Downed) return;

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
        else if (Gamepad.Pressed(e, "attack")) TryPlace();
        else if (Gamepad.Pressed(e, "block")) SetActive(false);
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

    /// <summary>Pick a building by id (scripted tests, future build menu).</summary>
    public void Select(string buildingId)
    {
        int i = ItemDb.Buildings.FindIndex(b => b.Id == buildingId);
        if (i < 0) return;
        _index = i;
        if (Active) CreateGhost();
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

        InvalidReason = normal.Y < 0.85f ? "Too steep" : CheckPlacement(space, Current, _spot, _rotationY, _player);
        _ghostMaterial.AlbedoColor = InvalidReason == ""
            ? new Color(0.4f, 1f, 0.5f, 0.45f)
            : new Color(1f, 0.3f, 0.3f, 0.45f);
    }

    /// <summary>Why a building can't go here ("" = it can). Used by the local ghost and again by
    /// the server before it builds.</summary>
    public static string CheckPlacement(PhysicsDirectSpaceState3D space, BuildingDef def, Vector3 spot, float rotationY, PlayerController payer = null)
    {
        var gs = GameState.Instance;

        var ground = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
            spot + Vector3.Up * 1.5f, spot + Vector3.Down * 1.5f, GameState.LayerWorld));
        if (ground.Count == 0) return "Aim at the ground";
        if (ground["normal"].AsVector3().Y < 0.85f) return "Too steep";

        var flatOffset = new Vector2(spot.X - gs.ClaimCenter.X, spot.Z - gs.ClaimCenter.Z);
        if (flatOffset.Length() > gs.ClaimRadius) return "Outside your land claim";
        if (gs.HomeTier < 2 && HomeUpgradeBoard.InCottage(spot, 0.6f)) return "Space kept for the cottage";

        // Overlap test against props, other buildings and players.
        var size = def.Footprint;
        var shapeQuery = new PhysicsShapeQueryParameters3D
        {
            Shape = new BoxShape3D { Size = size - new Vector3(0.05f, 0.1f, 0.05f) },
            Transform = new Transform3D(new Basis(Vector3.Up, rotationY), spot + Vector3.Up * (size.Y * 0.5f + 0.05f)),
            CollisionMask = GameState.LayerProps | GameState.LayerPlayer,
        };
        if (space.IntersectShape(shapeQuery, 1).Count > 0) return "Something is in the way";

        // The server re-checks the cost when it pays (Supplies.TryConsume).
        if (payer != null && !Supplies.Has(payer, def.GetCost())) return "Not enough materials";
        return "";
    }

    public void TryPlace()
    {
        if (!_hasSpot || InvalidReason != "")
        {
            Hud.Toast(InvalidReason == "" ? "Can't build here" : InvalidReason);
            return;
        }
        Net.Instance.RequestBuild(Current.Id, _spot, _rotationY);
    }
}
