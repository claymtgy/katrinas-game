using System;
using Godot;

namespace KatrinasGame;

/// <summary>Mouse-look rig with a first/third person toggle (V).
/// Structure: CameraRig (yaw) / Pivot (pitch) / SpringArm3D / Camera3D.</summary>
public partial class CameraRig : Node3D
{
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;
    [Export] public float ThirdPersonDistance { get; set; } = 4.2f;
    [Export] public Vector3 ShoulderOffset { get; set; } = new(0.85f, 0.35f, 0f);
    [Export] public bool StartInFirstPerson { get; set; } = false;

    public bool FirstPerson { get; private set; }
    public Camera3D Camera { get; private set; }
    public event Action<bool> ViewChanged;

    private Node3D _pivot;
    private SpringArm3D _arm;
    private Tween _tween;

    public override void _Ready()
    {
        _pivot = GetNode<Node3D>("Pivot");
        _arm = _pivot.GetNode<SpringArm3D>("SpringArm3D");
        Camera = _arm.GetNode<Camera3D>("Camera3D");
        if (GetParent() is CollisionObject3D owner) _arm.AddExcludedObject(owner.GetRid());

        Input.MouseMode = Input.MouseModeEnum.Captured;
        // Defer so listeners (player body, combat) have connected first.
        CallDeferred(MethodName.SetView, StartInFirstPerson, true);
    }

    /// <summary>Actual distance from the pivot to the camera (shorter when the arm hits a wall).</summary>
    public float CurrentDistance => FirstPerson ? 0f : _arm.GetHitLength();

    public override void _UnhandledInput(InputEvent e)
    {
        var gs = GameState.Instance;

        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured && !gs.UiOpen)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            var r = _pivot.Rotation;
            r.X = Mathf.Clamp(r.X - motion.Relative.Y * MouseSensitivity, -1.45f, 1.3f);
            _pivot.Rotation = r;
        }
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured && !gs.UiOpen)
        {
            // Clicking back into the game recaptures the mouse (and doesn't swing).
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("toggle_view") && !gs.UiOpen)
        {
            SetView(!FirstPerson, false);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Point the camera (radians). Used by scripted tests and cutscenes.</summary>
    public void SetLook(float yaw, float pitch)
    {
        Rotation = new Vector3(0, yaw, 0);
        _pivot.Rotation = new Vector3(pitch, 0, 0);
    }

    public void SetView(bool firstPerson, bool instant)
    {
        FirstPerson = firstPerson;
        float length = firstPerson ? 0f : ThirdPersonDistance;
        var offset = firstPerson ? Vector3.Zero : ShoulderOffset;

        _tween?.Kill();
        if (instant)
        {
            _arm.SpringLength = length;
            _arm.Position = offset;
        }
        else
        {
            _tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            _tween.TweenProperty(_arm, "spring_length", length, 0.25);
            _tween.TweenProperty(_arm, "position", offset, 0.25);
        }
        ViewChanged?.Invoke(firstPerson);
    }
}
