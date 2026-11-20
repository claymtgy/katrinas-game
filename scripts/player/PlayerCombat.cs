using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Light (LMB) and heavy (Q / middle mouse) swings, the guard (hold RMB), weapon slots
/// (1-5), and the held-weapon visual.
/// Guard: hits from in front are mostly blocked (each costs stamina; run out and the guard breaks).
/// Raise it just before a blow lands (Net.ParryWindow) to parry: no damage, attacker staggered.
/// The weapon attaches to the camera in first person and to the body in third person.
/// Multiplayer: the swinging player's machine handles cooldown, stamina and animation and asks the
/// server to swing (Net.RequestAttack); the server runs ServerAttack, finds what was hit and applies
/// damage. Other players see the swing through PlayRemoteAttack.</summary>
public partial class PlayerCombat : Node
{
    [Export] public float FistDamage { get; set; } = 6f;
    [Export] public float FistReach { get; set; } = 1.5f;
    [Export] public float LightStamina { get; set; } = 10f;
    [Export] public float HeavyStamina { get; set; } = 25f;
    [Export] public float HeavyMultiplier { get; set; } = 2.2f;

    private static readonly Vector3 FirstPersonHandPos = new(0.34f, -0.36f, -0.62f);
    // Weapon visuals point their blade along +Y (KayKit convention), so first person tilts it forward.
    private static readonly Vector3 FirstPersonRest = new(Mathf.DegToRad(-60), Mathf.DegToRad(-8), 0);

    private PlayerController _player;
    private CharacterModel _model;
    private Node3D _hand;
    private Node3D _weapon;
    private const float FirstPersonSwordScale = 0.44f;
    /// <summary>The look the first-person fist was coloured for (rebuilt when it changes).</summary>
    private Appearance _shownLook;
    private string _shownWeapon = null;
    private string _shownClass = null;
    private BowView _bow;
    private Tween _bowTween;

    // First person: the archer's bow, held up on the left, canted, arrow towards the crosshair.
    private static readonly Vector3 BowPos = new(-0.3f, -0.13f, -0.66f);
    private static readonly Vector3 BowRot = new(Mathf.DegToRad(2), Mathf.DegToRad(-9), Mathf.DegToRad(-9));
    private const float BowScaleFirstPerson = 0.5f;
    private const float BowLength = 1.0f; // metres, held by a full-height character in third person
    private float _cooldown;
    private Tween _swingTween;
    private float _guardLock;

    /// <summary>Local player: guard is up (sent to the server with Net.RequestBlock).</summary>
    public bool GuardUp { get; private set; }
    /// <summary>Scripted tests: hold the guard without the mouse.</summary>
    public bool HoldGuard { get; set; }
    /// <summary>Local player: parries landed this session (tests, HUD).</summary>
    public int Parries { get; private set; }

    private static readonly Vector3 GuardPos = new(0.22f, -0.42f, -0.72f);
    private static readonly Vector3 GuardRot = new(Mathf.DegToRad(30), Mathf.DegToRad(-25), Mathf.DegToRad(62));

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _model = _player.Body.GetNodeOrNull<CharacterModel>("Mesh");
        _hand = new Node3D { Name = "Hand" };
        _player.Body.AddChild(_hand);
        _player.Rig.ViewChanged += OnViewChanged;
        if (_model != null) _model.Rebuilt += () => OnViewChanged(FirstPerson);
        if (!_player.IsLocal) SetProcessUnhandledInput(false);
    }

    private bool FirstPerson => _player.Rig.FirstPerson;

    private void OnViewChanged(bool firstPerson)
    {
        Node3D parent = firstPerson ? _player.Rig.Camera : (Node3D)_model?.RightHandSlot ?? _player.Body;
        if (_hand.GetParent() != parent) _hand.Reparent(parent, false);
        _swingTween?.Kill();
        if (firstPerson)
        {
            _hand.Position = FirstPersonHandPos;
            _hand.Rotation = FirstPersonRest;
            _hand.Scale = Vector3.One * (_model?.ModelScale ?? 1f);
        }
        else
        {
            _hand.Transform = Transform3D.Identity; // bone slot already carries the model's scale
        }
        _shownWeapon = null; // rebuild so fists show/hide correctly
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_cooldown > 0) _cooldown -= dt;
        if (_guardLock > 0) _guardLock -= dt;
        if (_shownWeapon != _player.Equipped || _shownClass != _player.Class.Id || _shownLook != _player.Look) RebuildWeapon(_player.Equipped);
        if (_player.IsLocal) UpdateGuard();
    }

    // ================================================================== guard

    private void UpdateGuard()
    {
        var gs = GameState.Instance;
        bool input = !gs.UiOpen && Input.MouseMode == Input.MouseModeEnum.Captured && !_player.Build.Active
                     && Input.IsActionPressed("block");
        bool want = (input || HoldGuard) && !_player.Dodging && !_player.Downed && !_player.Sleeping && !_player.Health.IsDead && _guardLock <= 0 && _player.Stamina > 0.5f;
        if (want != GuardUp) SetGuard(want);
    }

    /// <summary>Local player: lower the guard now (a dodge roll).</summary>
    public void DropGuard()
    {
        if (GuardUp) SetGuard(false);
    }

    private void SetGuard(bool up)
    {
        GuardUp = up;
        Net.Instance.RequestBlock(up);
        if (!FirstPerson) return;
        _swingTween?.Kill();
        _swingTween = CreateTween().SetParallel();
        _swingTween.TweenProperty(_hand, "position", up ? GuardPos : FirstPersonHandPos, 0.1);
        _swingTween.TweenProperty(_hand, "rotation", up ? GuardRot : FirstPersonRest, 0.1);
    }

    /// <summary>Local player: the server says our guard took a blow.</summary>
    public void OnGuardHit(bool parried, float staminaCost)
    {
        if (parried)
        {
            Parries++;
            _player.DrainStamina(-20f - (Talents.Has(_player, Talents.Bulwark) ? Talents.BulwarkStamina : 0f)); // a clean parry gives breath back
            Hud.Instance?.FlashHitMarker();
            Hud.Toast("Parried!");
            Gamepad.Rumble(0.8f, 0.3f, 0.12f);
            return;
        }
        _player.DrainStamina(staminaCost);
        if (_player.Stamina <= 0.5f)
        {
            _guardLock = 1.2f;
            SetGuard(false);
            Hud.Toast("Guard broken!");
        }
    }

    /// <summary>Everyone: a guard took a hit (recoil in first person, flinch anim in third).</summary>
    public void PlayGuardImpact(bool parried)
    {
        Sfx.At(parried ? "parry" : "block", _player.GlobalPosition + Vector3.Up * 1.2f, parried ? 2f : 0f);
        if (_player.IsLocal && FirstPerson && GuardUp)
        {
            _swingTween?.Kill();
            _swingTween = CreateTween();
            _swingTween.TweenProperty(_hand, "position", GuardPos + new Vector3(0, 0.05f, 0.12f), 0.05);
            _swingTween.TweenProperty(_hand, "position", GuardPos, 0.18);
        }
        else if (_model != null && _model.HasAnimation("Block_Hit")) _model.PlayOneShot(parried ? "Block_Attack" : "Block_Hit", 1.4f);
    }

    /// <summary>Local player: our swing was parried; no attacking for a moment.</summary>
    public void Stagger(float seconds)
    {
        _cooldown = Mathf.Max(_cooldown, seconds);
        Hud.Toast("Your attack was parried!");
    }

    /// <summary>The archer's bow on this machine (tests); null for other classes.</summary>
    public BowView Bow => _bow;

    private bool IsArcher => _player.Class.Id == PlayerClasses.ArcherId;

    /// <summary>The archer carries a bow in the left hand: in front of the camera in first person,
    /// in the model's left hand in third.</summary>
    private void RebuildBow()
    {
        _bowTween?.Kill();
        // The bow may already be gone: it rides in the model's hand, and a rebuilt model frees it.
        if (IsInstanceValid(_bow)) _bow.QueueFree();
        _bow = null;
        if (!IsArcher || Net.Instance.IsDedicated) return;
        Node3D parent = FirstPerson ? _player.Rig.Camera : _model?.LeftHandSlot;
        if (parent == null) return;
        _bow = new BowView { Name = "Bow", RestNocked = FirstPerson };
        parent.AddChild(_bow);
        if (FirstPerson)
        {
            _bow.Position = BowPos;
            _bow.Rotation = BowRot;
            _bow.Scale = Vector3.One * BowScaleFirstPerson;
        }
        else
        {
            // The stave runs through the fist like a sword's grip. The bone slot carries the rig's
            // own scale, so size the bow against the slot's real size in the world.
            float slot = Mathf.Max(parent.GlobalBasis.Scale.X, 0.0001f);
            // The slot's +Y points out of the fist (forward, for a sword); stand the stave up across it,
            // with the string towards the archer.
            // Bow +Y -> slot +X (up in the fist), string (+Z) -> slot -Y (back towards the archer).
            _bow.Basis = new Basis(new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, -1, 0));
            _bow.Scale = Vector3.One * (BowLength * (_model.TargetHeight * _model.Look.Height / 1.8f) / BowView.Height / slot);
        }
    }

    /// <summary>Everyone: the archer looses an arrow (draw, snap, re-nock; a small kick in first person).</summary>
    public void PlayBowShot()
    {
        if (_bow == null || !IsInstanceValid(_bow)) return;
        _bow.Loose(_player.Class.AbilityCooldown * 0.7f);
        if (!FirstPerson || !_player.IsLocal) return;
        _bowTween?.Kill();
        _bowTween = CreateTween();
        _bowTween.TweenProperty(_bow, "position", BowPos + new Vector3(0.01f, 0.01f, 0.05f), 0.06).SetDelay(0.07);
        _bowTween.TweenProperty(_bow, "position", BowPos, 0.22).SetTrans(Tween.TransitionType.Sine);
    }

    private void RebuildWeapon(string itemId)
    {
        _shownWeapon = itemId;
        _shownClass = _player.Class.Id;
        _shownLook = _player.Look;
        if (IsInstanceValid(_weapon)) _weapon.QueueFree();
        _weapon = null;
        RebuildBow();
        var def = ItemDb.Get(itemId);

        if (def == null)
        {
            if (!FirstPerson || IsArcher) return; // the model already has hands; the archer's are on the bow
            var (skin, sleeve) = HeldModels.Colors(_player.Look);
            _weapon = HeldModels.Fist(skin, sleeve, holding: false);
            _weapon.Scale = Vector3.One * 1.2f;   // a bare fist reads small at the screen's edge
        }
        else if (def.ToolKind == "axe") _weapon = HeldModels.Axe(def.IconColor);
        else if (def.ToolKind == "pick") _weapon = HeldModels.Pickaxe(def.IconColor);
        else if (itemId.Contains("club")) _weapon = HeldModels.Club(def.IconColor);
        else if (itemId.Contains("sword"))
        {
            var sword = Models.Instance(Models.Sword, style: false);
            foreach (var n in sword.FindChildren("*", "MeshInstance3D", true, false))
                ((MeshInstance3D)n).MaterialOverride = StyleMaterials.Toon(def.IconColor, 0.3f);
            if (FirstPerson)
            {
                // The KayKit sword is sized for its chunky characters (1.8 m, a 6 cm grip). In your
                // own fist it's shrunk to a one-handed length with the grip's middle on the origin.
                sword.Scale = Vector3.One * FirstPersonSwordScale;
                sword.Position = new Vector3(0, 0.03f * FirstPersonSwordScale, 0);
            }
            _weapon = new Node3D { Name = "Sword" };
            _weapon.AddChild(sword);
        }
        else
        {
            var size = def.WeaponSize; // (width, thickness, length) -> blade along +Y
            _weapon = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size.X, size.Z, size.Y) },
                MaterialOverride = StyleMaterials.Toon(def.IconColor, 0.3f),
                Position = new Vector3(0, size.Z * 0.4f, 0),
            };
        }
        _hand.AddChild(_weapon);
        // First person: your own fist round the grip of whatever you hold.
        if (def != null && FirstPerson)
        {
            var (skin, sleeve) = HeldModels.Colors(_player.Look);
            _weapon.AddChild(HeldModels.Fist(skin, sleeve, holding: true));
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        var gs = GameState.Instance;
        if (gs.UiOpen || Input.MouseMode != Input.MouseModeEnum.Captured || _player.Sleeping) return;

        for (int i = 1; i <= 5; i++)
        {
            if (!e.IsActionPressed($"slot_{i}")) continue;
            var slots = gs.WeaponSlots(_player);
            if (i - 1 < slots.Count) Net.Instance.RequestEquip(slots[i - 1]);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e.IsActionPressed("eat"))
        {
            Net.Instance.RequestEat();
            GetViewport().SetInputAsHandled();
            return;
        }
        // Pad: LB pressed and let go on its own is "eat"; with Y between, it was an ability chord.
        if (e.IsActionPressed("pad_modifier"))
        {
            Gamepad.ChordUsed = false;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e.IsActionReleased("pad_modifier"))
        {
            if (!Gamepad.ChordUsed) Net.Instance.RequestEat();
            Gamepad.ChordUsed = true;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_player.Build.Active) return;
        if (e.IsActionPressed("next_weapon") || e.IsActionPressed("prev_weapon"))
        {
            // Controller: step through the hotbar.
            var slots = gs.WeaponSlots(_player);
            int at = Mathf.Max(0, slots.IndexOf(_player.Equipped));
            int step = e.IsActionPressed("next_weapon") ? 1 : -1;
            if (slots.Count > 1) Net.Instance.RequestEquip(slots[(at + step + slots.Count) % slots.Count]);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (GuardUp) return;
        if (Gamepad.Pressed(e, "attack")) TryAttack(heavy: false);
        else if (e.IsActionPressed("heavy_attack")) TryAttack(heavy: true);
    }

    /// <summary>Local player: swing now (animation, stamina) and ask the server to resolve the hit.</summary>
    public void TryAttack(bool heavy)
    {
        // On a rock face you can still swing a pickaxe (iron seams on the spires), nothing else.
        bool climbSwing = _player.Climbing && ItemDb.Get(_player.Equipped)?.ToolKind == "pick";
        if (_cooldown > 0 || GuardUp || _player.Dodging || _player.Downed) return;
        if (_player.Mode != PlayerController.MoveMode.Normal && !climbSwing)
        {
            if (_player.Climbing) Hud.Toast("Only a pickaxe can be swung while you climb.");
            return;
        }
        if (!_player.TrySpendStamina(heavy ? HeavyStamina : LightStamina))
        {
            Hud.Toast("Too tired to swing!");
            return;
        }

        var stats = Stats(heavy);
        _cooldown = stats.Cooldown;
        PlaySwingSound(heavy, stats.Windup);
        if (FirstPerson) PlaySwing(stats.Windup, heavy);
        else _model?.PlayAttack(heavy, ItemDb.Get(_player.Equipped) != null);
        Net.Instance.RequestAttack(heavy, -_player.Rig.Camera.GlobalBasis.Z);
    }

    /// <summary>Someone else's swing, seen on this machine.</summary>
    public void PlayRemoteAttack(bool heavy)
    {
        _model?.PlayAttack(heavy, ItemDb.Get(_player.Equipped) != null);
        PlaySwingSound(heavy, Stats(heavy).Windup);
    }

    private void PlaySwingSound(bool heavy, float windup)
    {
        // The whoosh lands with the strike, not the wind-up.
        GetTree().CreateTimer(windup * 0.7f).Timeout += () =>
        {
            if (IsInstanceValid(_player)) Sfx.At(heavy ? "swing_heavy" : "swing_light", _player.GlobalPosition + Vector3.Up * 1.2f, heavy ? 0f : -3f);
        };
    }

    private (float Damage, float Reach, float Windup, float Cooldown) Stats(bool heavy)
    {
        var def = ItemDb.Get(_player.Equipped);
        var cls = _player.Class;
        float damage = (def?.Damage ?? FistDamage) * (heavy ? HeavyMultiplier * cls.HeavyDamage * (Talents.Has(_player, Talents.Brute) ? Talents.BruteHeavy : 1f) : cls.LightDamage) * Gear.MeleeDamageMult(_player);
        float reach = def?.Reach ?? FistReach;
        float cooldown = (def?.AttackCooldown ?? 0.4f) * (heavy ? 1.8f : 1f);
        return (damage, reach, heavy ? 0.38f : 0.2f, cooldown);
    }

    /// <summary>Server: resolve this player's swing after its windup.</summary>
    public void ServerAttack(bool heavy, Vector3 aim)
    {
        var stats = Stats(heavy);
        if (aim.LengthSquared() < 0.01f) aim = -_player.Body.GlobalBasis.Z;
        GetTree().CreateTimer(stats.Windup).Timeout += () => DoHit(stats.Damage, stats.Reach, aim, heavy);
    }

    private void PlaySwing(float windup, bool heavy)
    {
        _swingTween?.Kill();
        var raised = new Vector3(Mathf.DegToRad(heavy ? 25 : 10), Mathf.DegToRad(-25), Mathf.DegToRad(-20));
        var struck = new Vector3(Mathf.DegToRad(-125), Mathf.DegToRad(25), Mathf.DegToRad(15));
        _swingTween = CreateTween();
        _swingTween.TweenProperty(_hand, "rotation", raised, windup * 0.8).SetEase(Tween.EaseType.Out);
        _swingTween.TweenProperty(_hand, "rotation", struck, 0.1).SetTrans(Tween.TransitionType.Expo);
        _swingTween.TweenProperty(_hand, "rotation", FirstPersonRest, 0.28).SetTrans(Tween.TransitionType.Sine);
    }

    private void DoHit(float damage, float reach, Vector3 aim, bool heavy = false)
    {
        if (!IsInstanceValid(_player) || !_player.IsInsideTree()) return;
        // Rogue: the first blow out of Vanish lands double, and ends it.
        if (_player.Vanished)
        {
            damage *= 2f;
            Net.Instance.ServerEndVanish(_player);
        }
        // Rogue: the strike right after a Shadowstep is a critical hit.
        bool crit = Net.Now < _player.CritUntil;
        if (crit)
        {
            damage *= Net.CritDamage;
            _player.CritUntil = 0;
        }
        // Sphere in front of the chest, along the aim (works in both views).
        var center = _player.GlobalPosition + Vector3.Up * 1.2f + aim * (reach * 0.65f);
        int hits = ServerStrikeArea(center, 0.45f + reach * 0.3f, damage, heavy);
        if (crit && hits > 0) Net.Instance.ToastTo(_player.PeerId, "Critical hit!");
    }

    /// <summary>Server: the light-attack damage with the current weapon and class (Whirlwind).</summary>
    public float LightDamage => Stats(false).Damage;

    /// <summary>Server: damage everything with health in a sphere (enemies, dummies, resource
    /// nodes, and other players when PVP is on). Returns how many things were hit.</summary>
    public int ServerStrikeArea(Vector3 center, float radius, float damage, bool heavy = false)
    {
        bool pvp = Net.Instance.Pvp;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, center),
            CollisionMask = GameState.LayerProps | (pvp ? GameState.LayerPlayer : 0),
            Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() },
        };

        var hitThisSwing = new HashSet<ulong>();
        int hits = 0;
        foreach (var hit in _player.GetWorld3D().DirectSpaceState.IntersectShape(query, 16))
        {
            var collider = hit["collider"].AsGodotObject();
            if (collider == null || !hitThisSwing.Add(collider.GetInstanceId())) continue;
            if (collider is PlayerController && !pvp) continue;
            var health = Health.FindOn(collider);
            if (health == null || health.IsDead) continue;
            hits++;
            if (collider is PlayerController target)
            {
                if (Net.Instance.ServerDamagePlayer(target, damage, _player) == Net.HitResult.Parried)
                    Net.Instance.StaggerPlayer(_player, 1.0f);
            }
            else if (collider is Harvestable node)
            {
                // Trees want an axe, rock and ore a pickaxe (iron ore will take nothing else).
                float dealt = node.HarvestDamage(ItemDb.Get(_player.Equipped), damage, heavy);
                if (dealt <= 0f)
                {
                    hits--;
                    Net.Instance.ToastTo(_player.PeerId, $"You need a {(node.ToolNeeded == "axe" ? "axe" : "pickaxe")} for {ItemDb.NameOf(node.DropItemId).ToLowerInvariant()}.");
                    continue;
                }
                health.TakeDamage(dealt, _player);
            }
            else
            {
                health.TakeDamage(damage, _player);
                // Leeching gear: living foes only (not trees, rocks or the dummy).
                if (collider is Enemy && Gear.LeechShare(_player) > 0) _player.Health.Heal(damage * Gear.LeechShare(_player));
            }
        }
        if (hits > 0) Net.Instance.HitMarkerTo(_player.PeerId);
        return hits;
    }
}
