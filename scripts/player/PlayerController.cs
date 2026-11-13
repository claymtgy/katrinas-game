using Godot;

namespace KatrinasGame;

/// <summary>Walk / sprint / jump with stamina, fall damage and respawn at the blossom shrine.</summary>
public partial class PlayerController : CharacterBody3D
{
    [Export] public float WalkSpeed { get; set; } = 4.5f;
    [Export] public float SprintSpeed { get; set; } = 8.0f;
    [Export] public float JumpVelocity { get; set; } = 5.5f;
    [Export] public float GroundAcceleration { get; set; } = 12f;
    [Export] public float AirAcceleration { get; set; } = 3f;

    [ExportGroup("Stamina")]
    [Export] public float MaxStamina { get; set; } = 100f;
    [Export] public float StaminaRegen { get; set; } = 20f;
    [Export] public float SprintCostPerSecond { get; set; } = 14f;
    [Export] public float JumpCost { get; set; } = 8f;

    [ExportGroup("Falling")]
    /// <summary>Landing speeds above this (m/s) hurt.</summary>
    [Export] public float SafeFallSpeed { get; set; } = 12f;
    [Export] public float FallDamagePerSpeed { get; set; } = 7f;
    [Export] public float KillHeight { get; set; } = -40f;

    public float Stamina { get; private set; }
    public Health Health { get; private set; }
    public CameraRig Rig { get; private set; }
    public Node3D Body { get; private set; }
    public BuildSystem Build { get; private set; }
    public PlayerCombat Combat { get; private set; }
    public Interactor Interactor { get; private set; }

    private float _gravity;
    private float _regenDelay;
    private float _airVelocityY;
    private bool _wasOnFloor = true;

    // Children's _Ready runs before ours, so grab references in _EnterTree where they can use them.
    public override void _EnterTree()
    {
        Health = GetNode<Health>("Health");
        Rig = GetNode<CameraRig>("CameraRig");
        Body = GetNode<Node3D>("Body");
        Build = GetNode<BuildSystem>("BuildSystem");
        Combat = GetNode<PlayerCombat>("Combat");
        Interactor = GetNode<Interactor>("Interactor");
    }

    public override void _Ready()
    {
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        Stamina = MaxStamina;

        var gs = GameState.Instance;
        gs.Player = this;
        if (gs.SpawnPoint != Vector3.Zero) GlobalPosition = gs.SpawnPoint;
        else gs.SpawnPoint = GlobalPosition;

        Health.Died += _ => OnDied("You collapsed. The blossom tree restores you.");
        Rig.ViewChanged += firstPerson => Body.GetNode<Node3D>("Mesh").Visible = !firstPerson;
    }

    public bool TrySpendStamina(float amount)
    {
        if (Stamina < amount) return false;
        Stamina -= amount;
        _regenDelay = 0.8f;
        return true;
    }

    public void RestoreStamina() => Stamina = MaxStamina;

    public void Teleport(Vector3 position)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _airVelocityY = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        bool blocked = gs.UiOpen;
        bool onFloor = IsOnFloor();
        var v = Velocity;

        if (!onFloor) v.Y -= _gravity * dt;

        // Camera-relative movement on the ground plane.
        Vector2 input = blocked ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var basis = Rig.GlobalTransform.Basis;
        var forward = new Vector3(-basis.Z.X, 0, -basis.Z.Z).Normalized();
        var right = new Vector3(basis.X.X, 0, basis.X.Z).Normalized();
        var dir = right * input.X - forward * input.Y;
        if (dir.LengthSquared() > 1f) dir = dir.Normalized();

        bool moving = dir.LengthSquared() > 0.01f;
        bool sprinting = !blocked && moving && onFloor && Input.IsActionPressed("sprint") && Stamina > 1f;
        if (sprinting)
        {
            Stamina = Mathf.Max(0, Stamina - SprintCostPerSecond * dt);
            _regenDelay = 0.6f;
        }

        var target = dir * (sprinting ? SprintSpeed : WalkSpeed);
        float accel = Mathf.Min(1f, (onFloor ? GroundAcceleration : AirAcceleration) * dt);
        v.X = Mathf.Lerp(v.X, target.X, accel);
        v.Z = Mathf.Lerp(v.Z, target.Z, accel);

        if (onFloor && !blocked && Input.IsActionJustPressed("jump") && TrySpendStamina(JumpCost))
            v.Y = JumpVelocity;

        if (!onFloor) _airVelocityY = v.Y;
        Velocity = v;
        MoveAndSlide();

        // Fall damage on landing.
        onFloor = IsOnFloor();
        if (onFloor && !_wasOnFloor && -_airVelocityY > SafeFallSpeed)
        {
            float dmg = (-_airVelocityY - SafeFallSpeed) * FallDamagePerSpeed;
            Health.TakeDamage(dmg, null);
            Hud.Toast($"Ouch! Fall damage -{dmg:0}");
        }
        if (onFloor) _airVelocityY = 0;
        _wasOnFloor = onFloor;

        // Stamina regen.
        if (_regenDelay > 0) _regenDelay -= dt;
        else Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegen * dt);

        // Body faces where the camera looks (over-the-shoulder style).
        var rot = Body.Rotation;
        rot.Y = Mathf.LerpAngle(rot.Y, Rig.Rotation.Y, Mathf.Min(1f, 14f * dt));
        Body.Rotation = rot;

        if (GlobalPosition.Y < KillHeight)
            OnDied("You fell into the clouds... the blossom tree carries you home.");
    }

    private void OnDied(string message)
    {
        Teleport(GameState.Instance.SpawnPoint);
        Health.Reset();
        RestoreStamina();
        Hud.Toast(message);
    }
}
