using Godot;

namespace KatrinasGame;

/// <summary>Walk / sprint / jump with stamina, fall damage and respawn at the blossom shrine.
///
/// Multiplayer: every player in the world is one of these, named by peer id. The local one reads
/// input and owns its position (sent to the server 30×/s). Remote ones (other players, and every
/// player on a dedicated server) just glide toward the latest state they were sent.
/// Health, equipped weapon and sleeping are server-owned and mirrored here.</summary>
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

    [ExportGroup("Dodge")]
    [Export] public float DodgeSpeed { get; set; } = 10.5f;
    [Export] public float DodgeDuration { get; set; } = 0.38f;
    [Export] public float DodgeCost { get; set; } = 22f;
    [Export] public float DodgeCooldown { get; set; } = 0.55f;

    [ExportGroup("Falling")]
    /// <summary>Landing speeds above this (m/s) hurt.</summary>
    [Export] public float SafeFallSpeed { get; set; } = 12f;
    [Export] public float FallDamagePerSpeed { get; set; } = 7f;
    [Export] public float KillHeight { get; set; } = -40f;

    /// <summary>Network peer that controls this player (1 = host / single player). Set before adding to the tree.</summary>
    public int PeerId { get; set; } = 1;
    public string DisplayName { get; set; } = "Player";
    /// <summary>True for the player this machine controls.</summary>
    public bool IsLocal { get; private set; }

    /// <summary>Weapon in hand (item id, "" = fists). Server-owned; taken out of the shared stockpile.</summary>
    public string Equipped { get; set; } = "";
    /// <summary>In bed, waiting for everyone else to sleep. Server-owned.</summary>
    public bool Sleeping { get; set; }
    /// <summary>Personal respawn point (last shrine prayed at). Server-owned.</summary>
    public Vector3 SpawnPoint { get; set; }
    /// <summary>Guard raised (server's view; the local player sees Combat.GuardUp at once).</summary>
    public bool Blocking { get; set; }
    /// <summary>Server: when the guard went up (Net.Now), for the parry window.</summary>
    public double BlockStartedAt { get; set; }
    /// <summary>Guard shown on this machine (instant for the local player).</summary>
    public bool GuardShown => IsLocal ? Combat.GuardUp : Blocking;

    /// <summary>What this player carries (GDD §16). Server-owned; the owner gets a mirror.
    /// Emptied into the stockpile at a storage chest. See Supplies.</summary>
    public Inventory Pack { get; } = new();

    /// <summary>How this player looks (set before spawning, then via <see cref="SetLook"/>).</summary>
    public Appearance Look { get; set; } = new();

    /// <summary>Every machine: show a new look (the server decides; see Net.RequestLook).</summary>
    public void SetLook(Appearance look)
    {
        Look = look.Clone();
        Body?.GetNodeOrNull<CharacterModel>("Mesh")?.SetLook(Look);
        if (Health != null && BaseMaxHealth > 0) UpdateMaxHealth();
        // The server may have kept our old class (changed away from home): remember what stuck.
        if (IsLocal && Appearance.Local.Class != Look.Class)
        {
            Appearance.Local.Class = Look.Class;
            Appearance.SaveLocal();
        }
        if (Vanished) SetVanished(true); // (re-apply the fade to the new body)
    }
    /// <summary>Server: when this player's dodge i-frames end (Net.Now).</summary>
    public double DodgeUntil { get; set; }
    /// <summary>Local player: mid-roll.</summary>
    public bool Dodging => _dodgeTime > 0;

    /// <summary>Food buffs: kind -> (amount, seconds left). Mirrored on every machine.</summary>
    public readonly System.Collections.Generic.Dictionary<string, (float Amount, float Left)> Buffs = new();
    public const string BuffStamina = "stamina";
    public const string BuffVigor = "vigor";
    public float BaseMaxHealth { get; private set; } = 100f;

    public float Stamina { get; private set; }
    public Health Health { get; private set; }
    public CameraRig Rig { get; private set; }
    public Node3D Body { get; private set; }
    public BuildSystem Build { get; private set; }
    public PlayerCombat Combat { get; private set; }
    /// <summary>The class ability (R).</summary>
    public PlayerAbility Ability { get; private set; }
    public Interactor Interactor { get; private set; }

    /// <summary>On the ground (remote players use the last state they sent).</summary>
    public bool Grounded => IsLocal ? IsOnFloor() : _remoteFloor;

    private float _gravity;
    private float _regenDelay;
    private float _airVelocityY;
    private bool _wasOnFloor = true;
    private bool _fellOut;
    private bool _wakeRequested;
    private float _dodgeTime, _dodgeCooldown;
    private Vector3 _dodgeDir;

    // Remote state.
    private bool _hasRemoteState;
    private Vector3 _targetPos;
    private float _targetYaw, _targetPitch;
    private bool _remoteFloor = true;

    // Children's _Ready runs before ours, so grab references (and decide local vs remote) here.
    public override void _EnterTree()
    {
        Health = GetNode<Health>("Health");
        Rig = GetNode<CameraRig>("CameraRig");
        Body = GetNode<Node3D>("Body");
        Build = GetNode<BuildSystem>("BuildSystem");
        Combat = GetNode<PlayerCombat>("Combat");
        Ability = new PlayerAbility { Name = "Ability" };
        AddChild(Ability);
        Interactor = GetNode<Interactor>("Interactor");
        IsLocal = PeerId == Multiplayer.GetUniqueId() && !Net.Instance.IsDedicated;
        SetMultiplayerAuthority(PeerId);
    }

    public override void _Ready()
    {
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        Stamina = MaxStamina;
        BaseMaxHealth = Health.Max;
        Health.InitMax(BaseMaxHealth + Class.BonusHealth); // class health, full on spawn

        var gs = GameState.Instance;
        gs.Players[PeerId] = this;
        if (IsLocal) gs.LocalPlayer = this;
        if (SpawnPoint == Vector3.Zero) SpawnPoint = gs.WorldSpawn;
        _targetPos = GlobalPosition;

        Health.Died += source =>
        {
            if (Net.Instance.IsServer) Net.Instance.ServerPlayerDied(this, source);
        };
        Health.Damaged += (amount, _) =>
        {
            LastHurtAt = Net.Now;
            if (amount > 0 && !Net.Instance.IsDedicated) Sfx.At("hit_flesh", GlobalPosition + Vector3.Up * 1.1f, IsLocal ? 2f : 0f, 0.1f);
        };
        if (IsLocal)
        {
            // A soft pop when something lands in your pack.
            int lastLoad = Pack.Load;
            Pack.Changed += () =>
            {
                if (Pack.Load > lastLoad) Sfx.Ui("pickup", -8f, 0.08f);
                lastLoad = Pack.Load;
            };
        }

        if (IsLocal)
            Rig.ViewChanged += firstPerson => Body.GetNode<Node3D>("Mesh").Visible = !firstPerson;
        else
            AddNameplate();
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs.GetPlayer(PeerId) == this) gs.Players.Remove(PeerId);
        if (gs.LocalPlayer == this) gs.LocalPlayer = null;
    }

    private void AddNameplate()
    {
        AddChild(new Label3D
        {
            Name = "Nameplate",
            Text = DisplayName,
            Position = new Vector3(0, 2.15f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40, PixelSize = 0.006f, OutlineSize = 10,
            Modulate = new Color(1f, 0.85f, 0.6f),
            NoDepthTest = false,
        });
    }

    public bool TrySpendStamina(float amount)
    {
        if (Stamina < amount) return false;
        Stamina -= amount;
        _regenDelay = 0.8f;
        return true;
    }

    public void RestoreStamina() => Stamina = MaxStamina;

    // ---------------------------------------------------------------- food buffs

    public float BuffAmount(string kind) => Buffs.TryGetValue(kind, out var b) ? b.Amount : 0f;

    /// <summary>Every machine: start (or refresh) a timed buff. The server decides; see Net.ServerBuff.</summary>
    public void ApplyBuff(string kind, float amount, float seconds)
    {
        if (seconds <= 0) Buffs.Remove(kind);
        else
        {
            Buffs[kind] = (amount, seconds);
            Sfx.At("eat", GlobalPosition + Vector3.Up * 1.5f, IsLocal ? 0f : -4f);
        }
        UpdateMaxHealth();
    }

    public void ClearBuffs()
    {
        Buffs.Clear();
        UpdateMaxHealth();
    }

    private void UpdateMaxHealth() => Health.SetMax(BaseMaxHealth + Class.BonusHealth + BuffAmount(BuffVigor));

    // ---------------------------------------------------------------- class

    /// <summary>This player's class (from the look; see PlayerClasses).</summary>
    public ClassDef Class => PlayerClasses.Get(Look.Class);

    /// <summary>Server clock (Net.Now): Rogue's Vanish runs until then.</summary>
    public double VanishUntil { get; set; }
    /// <summary>Every machine: drawn faded, enemies ignore us (the server decides; Net.C_Vanish).</summary>
    public bool Vanished { get; private set; }
    /// <summary>Server clock: when we were last hurt (the Healer's regeneration waits 5 s).</summary>
    public double LastHurtAt { get; set; }
    private float _regenBank;

    public void SetVanished(bool on)
    {
        Vanished = on;
        var mesh = Body?.GetNodeOrNull<CharacterModel>("Mesh");
        mesh?.SetFade(on ? (IsLocal ? 0.55f : 0.85f) : 0f);
        if (GetNodeOrNull<Label3D>("Nameplate") is { } plate) plate.Visible = !on;
    }

    /// <summary>Server: the Healer's regeneration out of combat (healed in whole points, so the
    /// network isn't told every frame).</summary>
    private void ServerRegen(float dt)
    {
        float rate = Class.HealthRegen;
        if (rate <= 0 || Health.IsDead || Health.Current >= Health.Max || Net.Now - LastHurtAt < 5.0) { _regenBank = 0; return; }
        _regenBank += rate * dt;
        if (_regenBank >= 2f)
        {
            Health.Heal(_regenBank);
            _regenBank = 0;
        }
    }

    private void TickBuffs(float dt)
    {
        if (Buffs.Count == 0) return;
        bool expired = false;
        foreach (var kind in System.Linq.Enumerable.ToList(Buffs.Keys))
        {
            var b = Buffs[kind];
            b.Left -= dt;
            if (b.Left <= 0) { Buffs.Remove(kind); expired = true; }
            else Buffs[kind] = b;
        }
        if (expired)
        {
            UpdateMaxHealth();
            if (IsLocal) Hud.Toast("A meal's strength wears off.");
        }
    }

    // ---------------------------------------------------------------- dodge

    /// <summary>Local player: roll in the move direction (backwards if standing still). Short
    /// invulnerability, costs stamina. Returns false if it couldn't.</summary>
    public bool TryDodge(Vector2 input)
    {
        var gs = GameState.Instance;
        if (gs.UiOpen || Sleeping || Health.IsDead || !IsOnFloor() || _dodgeTime > 0 || _dodgeCooldown > 0) return false;
        if (!TrySpendStamina(DodgeCost * Class.DodgeCost)) { Hud.Toast("Too tired to roll!"); return false; }
        var basis = Rig.GlobalTransform.Basis;
        var forward = new Vector3(-basis.Z.X, 0, -basis.Z.Z).Normalized();
        var right = new Vector3(basis.X.X, 0, basis.X.Z).Normalized();
        if (input.LengthSquared() < 0.1f) input = new Vector2(0, 1); // back
        _dodgeDir = (right * input.X - forward * input.Y).Normalized();
        _dodgeTime = DodgeDuration;
        _dodgeCooldown = DodgeDuration + DodgeCooldown;
        if (Combat.GuardUp) Combat.DropGuard();
        PlayDodge(_dodgeDir);
        Net.Instance.RequestDodge(_dodgeDir);
        return true;
    }

    /// <summary>Everyone: the roll animation (third person) or a camera dip (first person).</summary>
    public void PlayDodge(Vector3 worldDir)
    {
        var (yaw, _) = Rig.Look;
        var fwd = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        float f = worldDir.Dot(fwd), r = worldDir.Dot(right);
        string anim = Mathf.Abs(f) >= Mathf.Abs(r) ? (f >= 0 ? "Dodge_Forward" : "Dodge_Backward") : (r >= 0 ? "Dodge_Right" : "Dodge_Left");
        Body.GetNodeOrNull<CharacterModel>("Mesh")?.PlayOneShot(anim, 1.5f);
        Sfx.At("dodge", GlobalPosition + Vector3.Up * 0.8f, -2f);
        if (IsLocal && Rig.FirstPerson)
        {
            var cam = Rig.Camera;
            var t = cam.CreateTween();
            float roll = r >= 0 ? -0.18f : 0.18f;
            t.TweenProperty(cam, "rotation:z", Mathf.Abs(r) > 0.3f ? roll : 0f, DodgeDuration * 0.4);
            t.Parallel().TweenProperty(cam, "position:y", cam.Position.Y - 0.35f, DodgeDuration * 0.4);
            t.TweenProperty(cam, "rotation:z", 0f, DodgeDuration * 0.6);
            t.Parallel().TweenProperty(cam, "position:y", cam.Position.Y, DodgeDuration * 0.6);
        }
    }

    /// <summary>Lose stamina even below zero cost checks (blocked blows).</summary>
    public void DrainStamina(float amount)
    {
        Stamina = Mathf.Clamp(Stamina - amount, 0f, MaxStamina);
        _regenDelay = 0.8f;
    }

    /// <summary>Is a point in front of where this player looks (within ~70°)? Used for blocking.</summary>
    public bool Facing(Vector3 point)
    {
        var (yaw, _) = Rig.Look;
        var forward = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        var to = new Vector3(point.X - GlobalPosition.X, 0, point.Z - GlobalPosition.Z);
        return to.LengthSquared() < 0.04f || forward.Dot(to.Normalized()) > 0.34f;
    }

    /// <summary>Local player: move instantly (respawn, load, scripted tests).</summary>
    public void Teleport(Vector3 position)
    {
        GlobalPosition = position;
        Velocity = Vector3.Zero;
        _airVelocityY = 0;
        _fellOut = false;
        _wasOnFloor = true;
    }

    /// <summary>Remote player: the latest state from its owner.</summary>
    public void ApplyRemoteState(Vector3 pos, float yaw, float pitch, Vector3 velocity, bool onFloor)
    {
        if (IsLocal) return;
        _targetPos = pos;
        _targetYaw = yaw;
        _targetPitch = pitch;
        Velocity = velocity;
        _remoteFloor = onFloor;
        if (!_hasRemoteState) { GlobalPosition = pos; Rig.SetLook(yaw, pitch); }
        _hasRemoteState = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        TickBuffs(dt);
        if (Net.Instance.IsServer)
        {
            ServerRegen(dt);
            if (Vanished && Net.Now >= VanishUntil) Net.Instance.ServerEndVanish(this);
        }
        if (!IsLocal) { FollowRemote(dt); return; }

        var gs = GameState.Instance;
        bool blocked = gs.UiOpen || Sleeping;
        bool onFloor = IsOnFloor();
        var v = Velocity;
        if (!Sleeping) _wakeRequested = false;

        if (!onFloor) v.Y -= _gravity * dt;

        // Camera-relative movement on the ground plane.
        Vector2 input = gs.UiOpen ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        if (Sleeping)
        {
            // Moving or jumping gets you out of bed.
            if ((input.LengthSquared() > 0.1f || Input.IsActionJustPressed("jump")) && !_wakeRequested)
            {
                _wakeRequested = true;
                Net.Instance.RequestWake();
            }
            input = Vector2.Zero;
        }
        var basis = Rig.GlobalTransform.Basis;
        var forward = new Vector3(-basis.Z.X, 0, -basis.Z.Z).Normalized();
        var right = new Vector3(basis.X.X, 0, basis.X.Z).Normalized();
        var dir = right * input.X - forward * input.Y;
        if (dir.LengthSquared() > 1f) dir = dir.Normalized();

        if (!blocked && Input.IsActionJustPressed("dodge")) TryDodge(input);
        if (_dodgeCooldown > 0) _dodgeCooldown -= dt;

        bool moving = dir.LengthSquared() > 0.01f;
        bool guarding = Combat.GuardUp;
        bool sprinting = !blocked && !guarding && moving && onFloor && Input.IsActionPressed("sprint") && Stamina > 1f;
        if (sprinting)
        {
            Stamina = Mathf.Max(0, Stamina - SprintCostPerSecond * dt);
            _regenDelay = 0.6f;
        }

        var target = dir * (sprinting ? SprintSpeed : WalkSpeed * (guarding ? 0.5f : 1f)) * Class.MoveSpeed;
        float accel = Mathf.Min(1f, (onFloor ? GroundAcceleration : AirAcceleration) * dt);
        if (_dodgeTime > 0)
        {
            // The roll: a burst that eases off toward the end.
            _dodgeTime -= dt;
            float k = Mathf.Clamp(_dodgeTime / DodgeDuration, 0f, 1f);
            target = _dodgeDir * DodgeSpeed * (0.45f + 0.55f * k);
            accel = 1f;
        }
        v.X = Mathf.Lerp(v.X, target.X, accel);
        v.Z = Mathf.Lerp(v.Z, target.Z, accel);

        if (onFloor && !blocked && _dodgeTime <= 0 && Input.IsActionJustPressed("jump") && TrySpendStamina(JumpCost))
            v.Y = JumpVelocity;

        if (!onFloor) _airVelocityY = v.Y;
        Velocity = v;
        MoveAndSlide();

        // Fall damage on landing (the server applies it).
        onFloor = IsOnFloor();
        if (onFloor && !_wasOnFloor && -_airVelocityY > SafeFallSpeed)
        {
            float dmg = (-_airVelocityY - SafeFallSpeed) * FallDamagePerSpeed;
            Net.Instance.ReportFallDamage(dmg);
            Hud.Toast($"Ouch! Fall damage -{dmg:0}");
        }
        if (onFloor) _airVelocityY = 0;
        _wasOnFloor = onFloor;

        // Stamina regen.
        if (_regenDelay > 0) _regenDelay -= dt;
        else Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegen * Class.StaminaRegen * (1f + BuffAmount(BuffStamina)) * (guarding ? 0.4f : 1f) * dt);

        FaceCamera(Rig.Rotation.Y, dt);

        if (GlobalPosition.Y < KillHeight && !_fellOut)
        {
            _fellOut = true; // until the server teleports us home
            Net.Instance.ReportFellOut();
        }

        Net.Instance.SendLocalState(this, dt);
    }

    private void FollowRemote(float dt)
    {
        if (!_hasRemoteState) return;
        if (GlobalPosition.DistanceTo(_targetPos) > 6f) GlobalPosition = _targetPos; // teleported
        else GlobalPosition = GlobalPosition.Lerp(_targetPos, Mathf.Min(1f, 15f * dt));
        var (yaw, pitch) = Rig.Look;
        Rig.SetLook(Mathf.LerpAngle(yaw, _targetYaw, Mathf.Min(1f, 15f * dt)), Mathf.Lerp(pitch, _targetPitch, Mathf.Min(1f, 15f * dt)));
        FaceCamera(Rig.Rotation.Y, dt);
    }

    /// <summary>Body faces where the camera looks (over-the-shoulder style).</summary>
    private void FaceCamera(float yaw, float dt)
    {
        var rot = Body.Rotation;
        rot.Y = Mathf.LerpAngle(rot.Y, yaw, Mathf.Min(1f, 14f * dt));
        Body.Rotation = rot;
    }
}
