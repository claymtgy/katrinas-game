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
    /// <summary>Standing on a road, track or yard last physics frame (moves 1.2× faster).</summary>
    public bool OnPath { get; private set; }
    [Export] public float JumpVelocity { get; set; } = 5.5f;
    [Export] public float GroundAcceleration { get; set; } = 12f;
    [Export] public float AirAcceleration { get; set; } = 3f;

    [ExportGroup("Stamina")]
    /// <summary>Stamina at level 1; each level adds Levels.StaminaPerLevel (see MaxStamina).</summary>
    [Export] public float BaseStamina { get; set; } = 100f;
    [Export] public float StaminaRegen { get; set; } = 20f;
    [Export] public float SprintCostPerSecond { get; set; } = 14f;
    [Export] public float JumpCost { get; set; } = 8f;

    [ExportGroup("Dodge")]
    [Export] public float DodgeSpeed { get; set; } = 10.5f;
    [Export] public float DodgeDuration { get; set; } = 0.38f;
    [Export] public float DodgeCost { get; set; } = 22f;
    [Export] public float DodgeCooldown { get; set; } = 0.55f;

    [ExportGroup("Climb & glide")]
    [Export] public float ClimbSpeed { get; set; } = 2.4f;
    /// <summary>Stamina per second while climbing / just hanging on.</summary>
    [Export] public float ClimbCost { get; set; } = 7f;
    [Export] public float HangCost { get; set; } = 2.5f;
    /// <summary>Wet rock (rain) drains grip this much faster (GDD §9).</summary>
    [Export] public float WetGripCost { get; set; } = 1.6f;
    [Export] public float GlideSpeed { get; set; } = 9f;
    [Export] public float GlideFall { get; set; } = 2.2f;
    /// <summary>Sink speed looking straight down (a dive); looking down a little sinks part way.</summary>
    [Export] public float GlideDiveFall { get; set; } = 14f;
    /// <summary>Stamina per second under the glider (nothing while an updraft lifts you).</summary>
    [Export] public float GlideCost { get; set; } = 5f;
    [Export] public float UpdraftLift { get; set; } = 8f;

    [ExportGroup("Swim")]
    [Export] public float SwimSpeed { get; set; } = 3.0f;
    [Export] public float SwimSprintSpeed { get; set; } = 4.8f;
    /// <summary>Stamina per second for a fast stroke (sprint).</summary>
    [Export] public float SwimSprintCost { get; set; } = 12f;
    /// <summary>Stamina per second while your head is under (holding your breath).</summary>
    [Export] public float BreathCost { get; set; } = 9f;
    /// <summary>Damage per second once you're out of breath.</summary>
    [Export] public float DrownDamage { get; set; } = 12f;
    /// <summary>How far below the surface your feet hang when you float (head and shoulders out).</summary>
    [Export] public float FloatDepth { get; set; } = 1.35f;

    [ExportGroup("Falling")]
    /// <summary>Drops up to this many metres don't hurt.</summary>
    [Export] public float SafeFallHeight { get; set; } = 4.5f;
    /// <summary>Damage per metre fallen beyond <see cref="SafeFallHeight"/> (before gear).</summary>
    [Export] public float FallDamagePerMetre { get; set; } = 6f;
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

    /// <summary>Worn gear: slot -> item id (see Gear). The server decides; every machine mirrors it.</summary>
    public readonly System.Collections.Generic.Dictionary<string, string> Worn = new();
    /// <summary>Bumped whenever <see cref="Worn"/> changes (cheap UI refresh checks).</summary>
    public int WornVersion { get; private set; }

    /// <summary>Every machine: put on this set of gear (looks, max health).</summary>
    public void SetWorn(System.Collections.Generic.IReadOnlyDictionary<string, string> worn)
    {
        Worn.Clear();
        foreach (var kv in worn) Worn[kv.Key] = kv.Value;
        WornVersion++;
        Body?.GetNodeOrNull<CharacterModel>("Mesh")?.SetLook(Gear.Dress(Look, Worn));
        if (Health != null && BaseMaxHealth > 0) UpdateMaxHealth();
        if (Vanished) SetVanished(true);
    }

    /// <summary>Every machine: show a new look (the server decides; see Net.RequestLook).</summary>
    public void SetLook(Appearance look)
    {
        Look = look.Clone();
        Body?.GetNodeOrNull<CharacterModel>("Mesh")?.SetLook(Gear.Dress(Look, Worn));
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
    public float MaxStamina => BaseStamina + (Level - 1) * Levels.StaminaPerLevel;
    /// <summary>Max health from levels (+2 for each past the first).</summary>
    public float LevelHealth => (Level - 1) * Levels.HealthPerLevel;

    // ---------------------------------------------------------------- XP and level (GDD §6)

    /// <summary>Total XP. Server-owned; every machine mirrors it (Net.C_Xp), since the level shows
    /// on the nameplate.</summary>
    public int Xp { get; private set; }
    public int Level { get; private set; } = 1;
    /// <summary>Server: the firsts this character has had XP for ("kill:wolf", "craft:glider").</summary>
    public readonly System.Collections.Generic.HashSet<string> XpFirsts = new();

    /// <summary>Every machine: set the total XP (the server decides). Returns true on a level up.</summary>
    public bool SetXp(int total)
    {
        Xp = Mathf.Max(0, total);
        int old = Level;
        Level = Levels.LevelFor(Xp);
        if (Level == old) return false;
        if (Health != null && BaseMaxHealth > 0) UpdateMaxHealth();
        Stamina = Mathf.Min(Stamina, MaxStamina);
        UpdateNameplate();
        return Level > old;
    }

    public float Stamina { get; private set; }
    public Health Health { get; private set; }
    public CameraRig Rig { get; private set; }
    public Node3D Body { get; private set; }
    public BuildSystem Build { get; private set; }
    public PlayerCombat Combat { get; private set; }
    /// <summary>The class ability (R).</summary>
    public PlayerAbility Ability { get; private set; }
    /// <summary>The level-5 ability (T).</summary>
    public PlayerAbility Ability2 { get; private set; }

    /// <summary>A slot's ability node (0 = R, 1 = T).</summary>
    public PlayerAbility AbilitySlot(int slot) => slot == 1 ? Ability2 : Ability;

    /// <summary>A slot's ability as this player has it: the class's, with talents applied.</summary>
    public AbilityDef AbilityDef(int slot) => Talents.Tune(this, Class.Ability(slot));

    // ---------------------------------------------------------------- talents (GDD §6)

    /// <summary>Every machine: talent picks, "class:level" → talent id (Talents). Server-owned.</summary>
    public System.Collections.Generic.Dictionary<string, string> TalentPicks { get; set; } = new();

    // ---------------------------------------------------------------- downed (GDD §16)

    /// <summary>Every machine: knocked down at 0 health, waiting for a friend (Net.Downed).</summary>
    public bool Downed { get; private set; }
    /// <summary>Server clock: when a downed player is defeated if nobody helps.</summary>
    public double DownedUntil { get; set; }
    /// <summary>0..1: how far along a friend's revive is (shown on the marker).</summary>
    public float ReviveProgress { get; set; }
    public const float CrawlSpeed = 1f, GiveUpSeconds = 1.2f;
    /// <summary>Server: what downed them (credited if they're defeated after all).</summary>
    public Node DownedBy { get; set; }
    /// <summary>Server: set while a downed player is being defeated, so the blow lands.</summary>
    public bool ForceDefeat { get; set; }
    /// <summary>Local: how long Jump has been held while downed (giving up at GiveUpSeconds).</summary>
    public float GiveUpHeld { get; private set; }
    private bool _gaveUp;

    /// <summary>Every machine: go down or get up (Net tells everyone).</summary>
    public void SetDowned(bool downed, double until)
    {
        Downed = downed;
        DownedUntil = until;
        if (!downed) ReviveProgress = 0f;
        if (downed)
        {
            if (Mode != MoveMode.Normal) SetMode(MoveMode.Normal);
            Combat?.DropGuard();
        }
        Body?.GetNodeOrNull<CharacterModel>("Mesh")?.SetDowned(downed);
        UpdateDownedMarker();
    }

    private Label3D _downedMarker;

    /// <summary>For everyone else: a marker over a downed player that shows through walls.</summary>
    private void UpdateDownedMarker()
    {
        if (IsLocal) return;
        if (Downed && _downedMarker == null)
        {
            _downedMarker = new Label3D
            {
                Name = "DownedMarker", Position = new Vector3(0, 1.6f, 0),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = true,
                FontSize = 30, PixelSize = 0.0018f, OutlineSize = 10, Modulate = new Color(1f, 0.45f, 0.35f),
                RenderPriority = 10,
            };
            AddChild(_downedMarker);
        }
        if (_downedMarker != null) _downedMarker.Visible = Downed;
    }

    private void TickDownedMarker()
    {
        if (_downedMarker == null || !Downed) return;
        float left = Mathf.Max(0f, (float)(DownedUntil - Net.Now));
        _downedMarker.Text = $"✚ {DisplayName} is down · {left:0}s" + (ReviveProgress > 0.01f ? $"\nreviving {ReviveProgress * 100f:0}%" : "");
    }

    /// <summary>Server clock (Net.Now): the Rogue's Shadowstep makes the next strike before this a
    /// critical hit.</summary>
    public double CritUntil { get; set; }
    public Interactor Interactor { get; private set; }

    // ---------------------------------------------------------------- climbing / gliding

    public enum MoveMode { Normal = 0, Climb = 1, Glide = 2, Swim = 3 }
    /// <summary>Walking, on a rock face, under the glider or swimming (remote players: what they last sent).</summary>
    public MoveMode Mode { get; private set; }
    public bool Climbing => Mode == MoveMode.Climb;
    public bool Gliding => Mode == MoveMode.Glide;
    public bool Swimming => Mode == MoveMode.Swim;
    /// <summary>Local player: swimming with your head under the water (holding your breath).</summary>
    public bool Underwater { get; private set; }

    /// <summary>Into the water when it's this deep at your feet; out when it's shallower than the second.</summary>
    private const float SwimEnterDepth = 1.2f, SwimExitDepth = 0.9f, HeadDepth = 1.6f;
    private float _drownBank;
    private bool _drownWarned;
    /// <summary>A glider in your pack lets you open it in the air (Space).</summary>
    public bool HasGlider => Pack.Count("glider") > 0;

    private Vector3 _climbNormal, _mantleDir;
    private float _regrabIn, _mantleTime, _airTime, _climbStep;
    private bool _gliderHinted;
    private Node3D _gliderVisual;

    private void SetMode(MoveMode mode)
    {
        if (mode == Mode) return;
        var old = Mode;
        Mode = mode;
        if (mode == MoveMode.Swim && !Net.Instance.IsDedicated)
            Sfx.At("water", GlobalPosition + Vector3.Up, IsLocal ? 0f : -4f, 0.1f); // the splash
        if (mode != MoveMode.Swim) Underwater = false;
        if (mode == MoveMode.Glide || old == MoveMode.Glide)
        {
            ShowGlider(mode == MoveMode.Glide);
            if (!Net.Instance.IsDedicated)
                Sfx.At(mode == MoveMode.Glide ? "glider_open" : "dodge", GlobalPosition + Vector3.Up * 2f, IsLocal ? -2f : -6f, 0.08f);
        }
    }

    /// <summary>The cloth wing over your head while gliding.</summary>
    private void ShowGlider(bool on)
    {
        if (Net.Instance.IsDedicated) return;
        if (_gliderVisual == null && on)
        {
            _gliderVisual = new Node3D { Name = "Glider", Position = new Vector3(0, 2.25f, 0.1f) };
            var cloth = StyleMaterials.Toon(new Color(0.82f, 0.32f, 0.28f), 0.3f);
            var trim = StyleMaterials.Toon(new Color(0.95f, 0.88f, 0.72f), 0.3f);
            cloth.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            foreach (float side in new[] { -1f, 1f })
            {
                _gliderVisual.AddChild(new MeshInstance3D
                {
                    Mesh = new PrismMesh { Size = new Vector3(1.6f, 0.04f, 1.3f), LeftToRight = side > 0 ? 0f : 1f },
                    MaterialOverride = cloth, Position = new Vector3(side * 0.82f, 0.08f, 0), Rotation = new Vector3(0, 0, side * 0.12f),
                });
                _gliderVisual.AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(1.5f, 0.05f, 0.08f) }, MaterialOverride = trim,
                    Position = new Vector3(side * 0.8f, 0.12f, -0.62f), Rotation = new Vector3(0, 0, side * 0.12f),
                });
            }
            _gliderVisual.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.05f, 1.4f) }, MaterialOverride = trim });
            foreach (float side in new[] { -0.25f, 0.25f })
                _gliderVisual.AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.015f, Height = 0.75f, RadialSegments = 4 },
                    MaterialOverride = trim, Position = new Vector3(side, -0.37f, 0),
                });
            Body.AddChild(_gliderVisual);
        }
        if (_gliderVisual != null) _gliderVisual.Visible = on;
    }

    /// <summary>A climbable face straight ahead of the chest (walking into pale rock).</summary>
    private bool TryGrab(Vector3 forward)
    {
        if (forward.LengthSquared() < 0.01f) return false;
        var hit = Probe(GlobalPosition + Vector3.Up * 1.1f, forward, 0.9f);
        if (hit == null) return false;
        _climbNormal = hit.Value;
        return true;
    }

    /// <summary>Ray against climbable rock; returns its (flattened) normal.</summary>
    private Vector3? Probe(Vector3 from, Vector3 dir, float length)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * length, GameState.LayerWorld, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not Node n || !n.IsInGroup(GameState.ClimbGroup)) return null;
        var normal = hit["normal"].AsVector3();
        if (Mathf.Abs(normal.Y) > 0.4f) return null;
        return new Vector3(normal.X, 0, normal.Z).Normalized();
    }

    private void LetGo(float regrab = 0.4f)
    {
        SetMode(MoveMode.Normal);
        _regrabIn = regrab;
    }

    /// <summary>Local player on a rock face: W/S up and down, A/D sideways, Space to leap off.
    /// Drains stamina (faster on wet rock); at zero your grip fails. Over the top you mantle.</summary>
    private Vector3 ClimbStep(float dt, Vector2 input, bool blocked)
    {
        var n = _climbNormal;
        var right = (-n).Cross(Vector3.Up);
        if (blocked) input = Vector2.Zero;
        float up = -input.Y, lateral = input.X;
        bool moving = Mathf.Abs(up) > 0.1f || Mathf.Abs(lateral) > 0.1f;
        var (gripCost, dryGrip) = Gear.Climb(this); // grip boots
        float wet = GameState.Instance.Raining && !dryGrip ? WetGripCost : 1f;
        Stamina = Mathf.Max(0f, Stamina - (moving ? ClimbCost : HangCost) * wet * gripCost * dt);
        _regenDelay = 0.6f;
        _airVelocityY = 0;
        if (Stamina <= 0f)
        {
            LetGo(1.2f);
            Hud.Toast(GameState.Instance.Raining ? "The wet rock slips from your grip!" : "Your grip fails!");
            return n * 1.5f;
        }
        if (!blocked && Input.IsActionJustPressed("jump") && TrySpendStamina(JumpCost))
        {
            LetGo(0.6f);
            return n * 4.5f + Vector3.Up * 4f;
        }
        if (IsOnFloor() && up < -0.5f) { LetGo(0.6f); return Vector3.Zero; }

        var chest = Probe(GlobalPosition + Vector3.Up * 1.1f, -n, 1.2f);
        if (chest != null) _climbNormal = n = chest.Value;
        else
        {
            var feet = Probe(GlobalPosition + Vector3.Up * 0.25f, -n, 1.2f);
            if (feet == null) { LetGo(); return Vector3.Zero; }      // slid off the side
            if (up > 0.1f)
            {
                // Over the top: haul yourself up and onto the ledge.
                LetGo(0.8f);
                _mantleTime = 0.45f;
                _mantleDir = -n;
                return Vector3.Up * 5.4f - n * 2.5f;
            }
            up = Mathf.Min(up, 0f);
        }
        if (moving)
        {
            _climbStep -= dt;
            if (_climbStep <= 0) { _climbStep = 0.45f; Sfx.At("step", GlobalPosition + Vector3.Up, -10f, 0.15f); }
        }
        right = (-n).Cross(Vector3.Up);
        return (Vector3.Up * up + right * lateral) * ClimbSpeed - n;
    }

    /// <summary>How far into a dive the camera is (0 level, 1 looking well down).</summary>
    public float GlideDive => Mathf.Clamp((-Rig.Look.Pitch - 0.3f) / 0.7f, 0f, 1f);

    /// <summary>Local player under the glider: a slow sink, steered with the camera; look down to
    /// dive (sink fast, pick up speed). It costs stamina; out of stamina it folds. An updraft
    /// (waterfall mist) lifts you to its top for free.</summary>
    private Vector3 GlideStep(Vector3 v, float dt, Vector2 input, Vector3 forward, Vector3 right)
    {
        float top = GameState.Instance.UpdraftAt(GlobalPosition);
        bool lifting = top > 0 && GlobalPosition.Y < top;
        if (!lifting)
        {
            Stamina = Mathf.Max(0f, Stamina - GlideCost * dt);
            if (Stamina <= 0f)
            {
                SetMode(MoveMode.Normal);
                Hud.Toast("Too tired to hold the glider!");
                return v;
            }
        }
        float dive = lifting ? 0f : GlideDive;
        // In the mist column you hang and turn rather than race through it.
        float speed = GlideSpeed * (input.Y < -0.3f ? 1.2f : input.Y > 0.3f ? 0.6f : 1f) * (lifting ? 0.35f : 1f) * (1f + 0.4f * dive);
        var heading = (forward + right * input.X * 0.6f).Normalized();
        var h = new Vector3(v.X, 0, v.Z).Lerp(heading * speed, Mathf.Min(1f, 1.8f * dt));
        float targetY = lifting ? UpdraftLift : -Mathf.Lerp(GlideFall, GlideDiveFall, dive);
        float vy = Mathf.Lerp(v.Y, targetY, Mathf.Min(1f, (lifting ? 1.5f : 3f) * dt));
        return new Vector3(h.X, vy, h.Z);
    }

    /// <summary>Local player in a pool: stroke where you're heading, look down (moving forward) to
    /// dive and up to rise, hold Jump to come up; otherwise you bob back to float at the surface.
    /// A fast stroke (sprint) and holding your breath under water use stamina; out of breath you
    /// drown. Swim forward into an edge to haul yourself out. Water breaks a fall (GDD §8).</summary>
    private Vector3 SwimStep(Vector3 v, float dt, Vector2 input, Vector3 dir, Vector3 forward, float surface, bool blocked, bool sprintHeld)
    {
        float feetDepth = surface - GlobalPosition.Y;
        Underwater = feetDepth > HeadDepth;
        bool moving = dir.LengthSquared() > 0.01f;
        bool sprint = !blocked && moving && sprintHeld && Stamina > 1f;
        if (sprint)
        {
            Stamina = Mathf.Max(0, Stamina - SwimSprintCost * dt);
            _regenDelay = 0.6f;
        }
        if (Underwater)
        {
            Stamina = Mathf.Max(0, Stamina - BreathCost * dt);
            _regenDelay = 0.6f;
            if (Stamina <= 0)
            {
                _drownBank += DrownDamage * dt;
                if (_drownBank >= 4f)
                {
                    Net.Instance.ReportFallDamage(_drownBank);
                    _drownBank = 0;
                    if (!_drownWarned) Hud.Toast("Out of breath! Swim up for air.");
                    _drownWarned = true;
                }
            }
        }
        else { _drownBank = 0; _drownWarned = false; }

        // Haul yourself out over an edge in front of you.
        if (!blocked && !Underwater && input.Y < -0.5f && TryClimbOut(forward, surface))
            return Vector3.Up * 6.5f + forward * 2f;

        float speed = (sprint ? SwimSprintSpeed : SwimSpeed) * Gear.MoveSpeedMult(this);
        var target = dir * speed;
        float pitch = Rig.Look.Pitch;
        float vy;
        float floatGap = surface - FloatDepth - GlobalPosition.Y; // > 0: below floating height
        if (!blocked && Input.IsActionPressed("jump") && floatGap > 0.05f) vy = SwimSpeed;
        else if (moving && input.Y < -0.3f && Mathf.Abs(pitch) > 0.3f) vy = Mathf.Sin(pitch) * speed;
        else vy = Mathf.Clamp(floatGap * 2.5f, -2f, 2f);  // bob back to the surface
        if (vy > 0 && floatGap < 0.3f) vy = Mathf.Min(vy, floatGap * 2.5f); // don't leap out of the water
        float k = Mathf.Min(1f, 4f * dt);
        // A dive from a height slows hard in the water.
        float ky = Mathf.Min(1f, (v.Y < -3f ? 7f : 3f) * dt);
        return new Vector3(Mathf.Lerp(v.X, target.X, k), Mathf.Lerp(v.Y, vy, ky), Mathf.Lerp(v.Z, target.Z, k));
    }

    /// <summary>Swimming into a pool's edge: is there a ledge just ahead, low enough to climb onto?
    /// If so, leave the water with a mantle.</summary>
    private bool TryClimbOut(Vector3 forward, float surface)
    {
        var from = new Vector3(GlobalPosition.X, surface + 1.5f, GlobalPosition.Z) + forward * 0.8f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 2.5f, GameState.LayerWorld, new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < 0.7f) return false;
        float top = hit["position"].AsVector3().Y;
        if (top < surface - 0.3f || top - GlobalPosition.Y > 2.1f) return false;
        SetMode(MoveMode.Normal);
        _mantleTime = 0.45f;
        _mantleDir = forward;
        _airVelocityY = 0;
        return true;
    }

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
    // The Warrior's Charge: a straight rush that a wall or an enemy stops.
    private float _dashTime, _dashSpeed;
    private Vector3 _dashDir;

    /// <summary>Local player: rush along dir (flat) for distance metres over seconds (Charge).</summary>
    public void StartDash(Vector3 dir, float distance, float seconds)
    {
        _dashDir = dir;
        _dashTime = seconds;
        _dashSpeed = distance / seconds;
        _dodgeTime = 0;
    }

    /// <summary>Local player: mid-charge.</summary>
    public bool Dashing => _dashTime > 0;

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
        Ability = new PlayerAbility { Name = "Ability", Slot = 0 };
        AddChild(Ability);
        Ability2 = new PlayerAbility { Name = "Ability2", Slot = 1 };
        AddChild(Ability2);
        Interactor = GetNode<Interactor>("Interactor");
        IsLocal = PeerId == Multiplayer.GetUniqueId() && !Net.Instance.IsDedicated;
        SetMultiplayerAuthority(PeerId);
    }

    public override void _Ready()
    {
        _gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        Stamina = MaxStamina;
        BaseMaxHealth = Health.Max;
        Health.InitMax(BaseMaxHealth + LevelHealth + Class.BonusHealth); // class and level health, full on spawn

        var gs = GameState.Instance;
        gs.Players[PeerId] = this;
        if (IsLocal) gs.LocalPlayer = this;
        if (SpawnPoint == Vector3.Zero) SpawnPoint = gs.WorldSpawn;
        _targetPos = GlobalPosition;

        Net.Instance.SetUpDowning(this);
        Health.Died += source =>
        {
            if (Net.Instance.IsServer) Net.Instance.ServerPlayerDied(this, source);
        };
        Health.Damaged += (amount, _) =>
        {
            LastHurtAt = Net.Now;
            if (amount > 0 && !Net.Instance.IsDedicated) Sfx.At("hit_flesh", GlobalPosition + Vector3.Up * 1.1f, IsLocal ? 2f : 0f, 0.1f);
            if (amount > 0 && IsLocal) Gamepad.Rumble(0.4f, Mathf.Clamp(amount / 30f, 0.2f, 0.9f), 0.22f);
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
            Text = NameplateText,
            Position = new Vector3(0, 2.15f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 40, PixelSize = 0.006f, OutlineSize = 10,
            Modulate = new Color(1f, 0.85f, 0.6f),
            NoDepthTest = false,
        });
    }

    private string NameplateText => $"{DisplayName} · {Level}";

    private void UpdateNameplate()
    {
        if (GetNodeOrNull<Label3D>("Nameplate") is { } plate) plate.Text = NameplateText;
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

    private void UpdateMaxHealth() => Health.SetMax(BaseMaxHealth + LevelHealth + Class.BonusHealth + BuffAmount(BuffVigor) + Gear.MaxHealthBonus(this));

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
        if (gs.UiOpen || Sleeping || Health.IsDead || !IsOnFloor() || _dodgeTime > 0 || _dodgeCooldown > 0 || Mode != MoveMode.Normal) return false;
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
        _dashTime = 0;
        _airVelocityY = 0;
        _fellOut = false;
        _wasOnFloor = true;
        _mantleTime = 0;
        if (IsLocal) SetMode(MoveMode.Normal);
    }

    /// <summary>How far a landing at <paramref name="speed"/> m/s (downward) fell: v² / 2g. A jump
    /// that lands where it started counts only its own small drop.</summary>
    public float FallHeight(float speed) => speed <= 0f ? 0f : speed * speed / (2f * _gravity);

    /// <summary>Damage for landing at <paramref name="speed"/> m/s: <see cref="FallDamagePerMetre"/>
    /// for each metre beyond <see cref="SafeFallHeight"/>, less what gear takes off.</summary>
    public float FallDamage(float speed) =>
        Mathf.Max(0f, FallHeight(speed) - SafeFall) * FallDamagePerMetre * Gear.FallDamageMult(this);

    /// <summary>The drop you can take without harm (the Rogue's Light-footed talent raises it).</summary>
    public float SafeFall => Talents.Has(this, Talents.LightFooted) ? Mathf.Max(SafeFallHeight, Talents.LightFootedSafeFall) : SafeFallHeight;

    /// <summary>Scripted tests: open the glider now (as if Space was pressed in the air).</summary>
    public bool TryGlide()
    {
        if (IsOnFloor() || Mode != MoveMode.Normal || !HasGlider || Stamina < 5f) return false;
        SetMode(MoveMode.Glide);
        return true;
    }

    /// <summary>Remote player: the latest state from its owner.</summary>
    public void ApplyRemoteState(Vector3 pos, float yaw, float pitch, Vector3 velocity, bool onFloor, int mode = 0)
    {
        if (IsLocal) return;
        SetMode((MoveMode)Mathf.Clamp(mode, 0, 3));
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
        TickDownedMarker();
        if (Net.Instance.IsServer)
        {
            ServerRegen(dt);
            if (Vanished && Net.Now >= VanishUntil) Net.Instance.ServerEndVanish(this);
        }
        if (!IsLocal) { FollowRemote(dt); return; }

        var gs = GameState.Instance;
        bool blocked = gs.UiOpen || Sleeping || gs.InputQuiet;
        bool onFloor = IsOnFloor();
        var v = Velocity;
        if (!Sleeping) _wakeRequested = false;
        if (_regrabIn > 0) _regrabIn -= dt;
        _airTime = onFloor ? 0f : _airTime + dt;
        if ((Health.IsDead || Sleeping) && Mode != MoveMode.Normal) SetMode(MoveMode.Normal);

        if (!onFloor && Mode == MoveMode.Normal) v.Y -= _gravity * dt;

        // Camera-relative movement on the ground plane.
        Vector2 input = gs.UiOpen ? Vector2.Zero : Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        bool sprintHeld = Input.IsActionPressed("sprint") || Gamepad.SprintLatched;
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

        if (Downed)
        {
            // Downed: crawl, nothing else. Hold Jump to give up and go home now.
            v.X = Mathf.Lerp(v.X, dir.X * CrawlSpeed, Mathf.Min(1f, GroundAcceleration * dt));
            v.Z = Mathf.Lerp(v.Z, dir.Z * CrawlSpeed, Mathf.Min(1f, GroundAcceleration * dt));
            if (onFloor) v.Y = 0f;
            Velocity = v;
            MoveAndSlide();
            GiveUpHeld = !blocked && Input.IsActionPressed("jump") ? GiveUpHeld + dt : 0f;
            if (GiveUpHeld >= GiveUpSeconds && !_gaveUp) { _gaveUp = true; Net.Instance.RequestGiveUp(); }
            _airVelocityY = 0;
            _wasOnFloor = onFloor;
            FinishLocalStep(dt, false);
            return;
        }
        GiveUpHeld = 0f;
        _gaveUp = false;

        // Pools: deep water at your feet and you swim (a glider folds; a fall is broken).
        float surface = gs.WaterSurfaceAt(GlobalPosition);
        float depth = float.IsNaN(surface) ? 0f : surface - GlobalPosition.Y;
        if (Mode == MoveMode.Swim && (depth < SwimExitDepth || Health.IsDead || Sleeping)) SetMode(MoveMode.Normal);
        else if ((Mode is MoveMode.Normal or MoveMode.Glide) && depth > SwimEnterDepth && _mantleTime <= 0 && !Health.IsDead)
        {
            SetMode(MoveMode.Swim);
            if (Combat.GuardUp) Combat.DropGuard();
            _dodgeTime = 0;
            _dashTime = 0;
        }
        if (Mode == MoveMode.Swim)
        {
            Velocity = SwimStep(v, dt, input, dir, forward, surface, blocked, sprintHeld);
            MoveAndSlide();
            _airVelocityY = 0;
            _wasOnFloor = true;
            FinishLocalStep(dt, false);
            return;
        }

        if (Mode == MoveMode.Climb)
        {
            Velocity = ClimbStep(dt, input, blocked);
            MoveAndSlide();
            FinishLocalStep(dt, false);
            return;
        }

        if (!blocked && Input.IsActionJustPressed("dodge")) TryDodge(input);
        if (_dodgeCooldown > 0) _dodgeCooldown -= dt;

        bool moving = dir.LengthSquared() > 0.01f;
        bool guarding = Combat.GuardUp;
        bool sprinting = !blocked && !guarding && moving && onFloor && sprintHeld && Stamina > 1f;
        if (sprinting)
        {
            Stamina = Mathf.Max(0, Stamina - SprintCostPerSecond * (Talents.Has(this, Talents.Fleet) ? Talents.FleetSprint : 1f) * dt);
            _regenDelay = 0.6f;
        }

        var target = dir * (sprinting ? SprintSpeed : WalkSpeed * (guarding ? 0.5f : 1f)) * Class.MoveSpeed * Gear.MoveSpeedMult(this)
                     * (Talents.Has(this, Talents.Fleetfoot) ? Talents.FleetfootSpeed : 1f);
        // Roads and tracks are quicker going (GDD §8).
        OnPath = onFloor && HomeCliffs.OnPath(GlobalPosition);
        if (OnPath) target *= HomeCliffs.PathSpeedMult;
        float accel = Mathf.Min(1f, (onFloor ? GroundAcceleration : AirAcceleration) * dt);
        // Wading in the river: slower, and the current carries you (GDD §9).
        var flow = onFloor ? gs.CurrentAt(GlobalPosition) : Vector3.Zero;
        if (flow != Vector3.Zero) target = target * 0.7f + flow;
        // Hauling yourself over a ledge you just climbed.
        if (_mantleTime > 0)
        {
            _mantleTime -= dt;
            target = _mantleDir * 2.5f;
            accel = 1f;
        }
        if (_dodgeTime > 0)
        {
            // The roll: a burst that eases off toward the end.
            _dodgeTime -= dt;
            float k = Mathf.Clamp(_dodgeTime / DodgeDuration, 0f, 1f);
            target = _dodgeDir * DodgeSpeed * (0.45f + 0.55f * k);
            accel = 1f;
        }
        if (_dashTime > 0)
        {
            _dashTime -= dt;
            target = _dashDir * _dashSpeed;
            accel = 1f;
            // Ran into something head-on (a rock, a wall, the enemy): the charge ends there.
            for (int i = 0; i < GetSlideCollisionCount(); i++)
                if (GetSlideCollision(i).GetNormal().Dot(_dashDir) < -0.6f) { _dashTime = 0; break; }
        }
        if (Mode == MoveMode.Glide) accel = 0f; // the glider steers itself (GlideStep)
        v.X = Mathf.Lerp(v.X, target.X, accel);
        v.Z = Mathf.Lerp(v.Z, target.Z, accel);

        if (onFloor && !blocked && _dodgeTime <= 0 && Input.IsActionJustPressed("jump") && TrySpendStamina(JumpCost))
            v.Y = JumpVelocity;
        else if (!onFloor && !blocked && Input.IsActionJustPressed("jump"))
        {
            // Space in the air opens (or folds) the glider.
            if (Mode == MoveMode.Glide) SetMode(MoveMode.Normal);
            else if (_airTime > 0.2f && _dodgeTime <= 0)
            {
                if (HasGlider && Stamina >= 5f) SetMode(MoveMode.Glide);
                else if (HasGlider) Hud.Toast("Too tired to open the glider.");
                else if (v.Y < -6f && !_gliderHinted)
                {
                    _gliderHinted = true;
                    Hud.Toast("A glider would catch you. Craft one at the workbench (harpy feathers).");
                }
            }
        }
        if (Mode == MoveMode.Glide)
        {
            if (onFloor) SetMode(MoveMode.Normal);
            else v = GlideStep(v, dt, input, forward, right);
        }
        else if (!blocked && input.Y < -0.5f && _regrabIn <= 0 && _dodgeTime <= 0 && _mantleTime <= 0 && !guarding && Stamina > 5f && TryGrab(forward))
        {
            SetMode(MoveMode.Climb);
            v = Vector3.Zero;
            _airVelocityY = 0;
        }

        if (!onFloor) _airVelocityY = v.Y;
        Velocity = v;
        MoveAndSlide();

        // Fall damage on landing (the server applies it).
        onFloor = IsOnFloor();
        if (onFloor && !_wasOnFloor && Mode != MoveMode.Glide)
        {
            float dmg = FallDamage(-_airVelocityY);
            if (dmg >= 1f)
            {
                Net.Instance.ReportFallDamage(dmg);
                Hud.Toast($"Ouch! Fall damage -{dmg:0}");
                Sfx.At("hit_flesh", GlobalPosition + Vector3.Up, -4f, 0.1f);
                Gamepad.Rumble(Mathf.Clamp(dmg / 60f, 0.3f, 1f), 0.6f, 0.25f);
            }
        }
        if (onFloor) _airVelocityY = 0;
        _wasOnFloor = onFloor;
        FinishLocalStep(dt, guarding);
    }

    /// <summary>Local player, end of every physics step: stamina regen, facing, falling out, sending.</summary>
    private void FinishLocalStep(float dt, bool guarding)
    {
        if (_regenDelay > 0) _regenDelay -= dt;
        else if (Mode is not (MoveMode.Climb or MoveMode.Glide))
            Stamina = Mathf.Min(MaxStamina, Stamina + StaminaRegen * Class.StaminaRegen * Gear.StaminaRegenMult(this) * (1f + BuffAmount(BuffStamina)) * (guarding ? 0.4f : 1f) * dt);

        if (Mode == MoveMode.Climb)
        {
            FaceCamera(Mathf.Atan2(_climbNormal.X, _climbNormal.Z), dt);
            _wasOnFloor = IsOnFloor();
        }
        else FaceCamera(Rig.Rotation.Y, dt);

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
