using Godot;

namespace KatrinasGame;

/// <summary>One class ability slot (see PlayerClasses): slot 0 on R (or mouse button 4, pad Y):
/// Whirlwind, Vanish, Firebolt, Mend or Shoot. Slot 1, from level 5, on T (or mouse button 5,
/// pad hold LB and press Y): Charge, Shadowstep, Frost Nova, Blossom Ward or Volley.
///
/// Same pattern as a sword swing: the player's own machine checks the level, cooldown and
/// stamina, plays the animation and sound at once and asks the server (Net.RequestAbility); the
/// server re-checks and does the real thing; other screens get the animation through
/// Net.C_AbilityFx.</summary>
public partial class PlayerAbility : Node
{
    private PlayerController _player;
    private double _readyAt;

    /// <summary>0 = R, 1 = T (level 5), 2 = G (level 12).</summary>
    public int Slot { get; init; }

    /// <summary>Server clock: when this player may use the ability again (anti-spam).</summary>
    public double ServerReadyAt { get; set; }

    /// <summary>Local player: seconds until the ability is ready (HUD).</summary>
    public float CooldownLeft => Mathf.Max(0f, (float)(_readyAt - Time.GetTicksMsec() / 1000.0));

    /// <summary>This slot's ability for the player's class (null: none).</summary>
    public AbilityDef Def => _player?.AbilityDef(Slot);

    /// <summary>The player is high enough level for it.</summary>
    public bool Unlocked => Def is { } d && _player.Level >= d.UnlockLevel;

    /// <summary>The input action for this slot (key-cap prompts).</summary>
    public string Action => Slot switch { 0 => "ability", 1 => "ability_2", _ => "ability_3" };

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        if (!_player.IsLocal) SetProcessUnhandledInput(false);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_player.Build?.Active ?? false) return;
        // Pad: Y alone is the first ability; hold LB and press Y for the second, X for the third.
        bool pad = e is InputEventJoypadButton && Gamepad.ModifierHeld;
        bool chordY = pad && e.IsActionPressed("ability");
        bool chordX = pad && e.IsActionPressed("interact");
        bool pressed = Slot switch
        {
            0 => e.IsActionPressed("ability") && !chordY,
            1 => e.IsActionPressed("ability_2") || chordY,
            _ => e.IsActionPressed("ability_3") || chordX,
        };
        if (!pressed) return;
        if (chordY || chordX) Gamepad.ChordUsed = true;
        TryUse();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Which way to send an ability so it goes where the crosshair is. Shots leave the
    /// chest (Net.S_Ability), not the camera; in third person the camera sits off to one side,
    /// so aiming along the camera's own direction would fly parallel to the crosshair and pass
    /// beside the target. Instead: find what the crosshair is on (or a point far ahead) and aim
    /// from the chest at that.</summary>
    private Vector3 CrosshairAim()
    {
        var cam = _player.Rig.Camera;
        var fwd = -cam.GlobalBasis.Z;
        if (_player.Rig.FirstPerson) return fwd;
        var from = cam.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(from, from + fwd * 60f, GameState.LayerWorld | GameState.LayerProps,
            new Godot.Collections.Array<Rid> { _player.GetRid() });
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
        var point = hit.Count > 0 ? hit["position"].AsVector3() : from + fwd * 60f;
        var d = point - (_player.GlobalPosition + Vector3.Up * 1.35f);
        // Something right up against the camera (or behind the chest): keep the camera's direction.
        return d.Length() > 1.5f && d.Normalized().Dot(fwd) > 0.5f ? d.Normalized() : fwd;
    }

    /// <summary>Local player: use this ability, aimed where the camera looks.</summary>
    public bool TryUse()
    {
        var def = Def;
        if (def == null) return false;
        if (_player.Health.IsDead || _player.Downed || _player.Sleeping || GameState.Instance.UiOpen || _player.Dodging || _player.Climbing || _player.Swimming || _player.Grappling) return false;
        if (_player.Level < def.UnlockLevel)
        {
            Hud.Toast($"{def.Name} unlocks at level {def.UnlockLevel}.");
            return false;
        }
        if (CooldownLeft > 0) return false;
        var aim = CrosshairAim();
        // Abilities that need something to work with: don't spend stamina for nothing.
        if (def.Id == PlayerClasses.ShadowstepId && Net.ShadowstepTarget(_player, aim) == null)
        {
            Hud.Toast("No enemy within 12 m to step behind.");
            return false;
        }
        if (def.Id == PlayerClasses.ChargeId && (!_player.IsOnFloor() || _player.Gliding)) return false;
        if (def.Id == PlayerClasses.RekindleId && Net.RekindleTarget(_player) == null)
        {
            Hud.Toast("No one is down within 12 m.");
            return false;
        }
        if (def.Id == PlayerClasses.MarkId && Net.MarkTarget(_player, aim) == null)
        {
            Hud.Toast("No enemy in your sights within 40 m.");
            return false;
        }
        if (!_player.TrySpendStamina(def.Stamina))
        {
            Hud.Toast($"Too tired for {def.Name}!");
            return false;
        }
        _readyAt = Time.GetTicksMsec() / 1000.0 + def.Cooldown;
        if (_player.Combat.GuardUp) _player.Combat.DropGuard();
        if (def.Id == PlayerClasses.ChargeId) _player.StartDash(Flat(aim), Net.ChargeDistance, Net.ChargeSeconds);
        PlayFx(aim);
        Net.Instance.RequestAbility(aim, Slot);
        return true;
    }

    /// <summary>Scripted tests: ready again now.</summary>
    public void ResetCooldown()
    {
        _readyAt = 0;
        ServerReadyAt = 0;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.Y = 0;
        return v.LengthSquared() > 0.0001f ? v.Normalized() : Vector3.Forward;
    }

    /// <summary>Everyone: the ability's animation, sound and effect on the caster.</summary>
    public void PlayFx(Vector3 aim)
    {
        if (Net.Instance.IsDedicated || Def == null) return;
        var model = _player.Body.GetNodeOrNull<CharacterModel>("Mesh");
        var at = _player.GlobalPosition;
        var root = GetTree().CurrentScene;
        switch (Def.Id)
        {
            // ---- slot 0
            case PlayerClasses.WarriorId + "_1":
                model?.PlayOneShot("2H_Melee_Attack_Spin", 1.6f);
                Sfx.At("whirlwind", at + Vector3.Up, 2f);
                Fx.Ring(root, at, new Color(1f, 0.6f, 0.35f, 0.9f), Net.WhirlwindReach(_player), 0.35f);
                break;
            case PlayerClasses.RogueId + "_1":
                Sfx.At("vanish", at + Vector3.Up, 0f);
                Fx.Sparks(root, at + Vector3.Up * 0.9f, new Color(0.5f, 0.45f, 0.65f, 0.7f), 40, 1.6f, 0.6f, 1.0f);
                break;
            case PlayerClasses.WizardId + "_1":
                model?.PlayOneShot("Spellcast_Shoot", 1.8f);
                Sfx.At("firebolt", at + Vector3.Up * 1.3f, 0f);
                Fx.Sparks(root, at + Vector3.Up * 1.3f + aim * 0.8f, new Color(1f, 0.55f, 0.2f), 14, 2f, 0.15f, 0.4f);
                break;
            case PlayerClasses.HealerId + "_1":
                model?.PlayOneShot("Spellcast_Raise", 1.4f);
                Sfx.At("heal", at + Vector3.Up, 2f, 0.03f);
                Fx.Ring(root, at, new Color(0.5f, 1f, 0.6f, 0.8f), Net.MendRadius, 0.8f);
                foreach (var p in GameState.Instance.Players.Values)
                    if (p.GlobalPosition.DistanceTo(at) <= Net.MendRadius && !p.Health.IsDead)
                        Fx.Sparks(root, p.GlobalPosition + Vector3.Up * 0.3f, new Color(0.55f, 1f, 0.6f), 26, 2.2f, 0.45f, 1.1f);
                break;
            case PlayerClasses.ArcherId + "_1":
                model?.PlayOneShot("1H_Ranged_Shoot", 1.8f);
                _player.Combat.PlayBowShot();
                Sfx.At("bow", at + Vector3.Up * 1.3f, 0f);
                break;

            // ---- slot 1 (level 5)
            case PlayerClasses.ChargeId:
                model?.PlayOneShot("Dodge_Forward", 1.2f);
                Sfx.At("dodge", at + Vector3.Up, 2f);
                Sfx.At("swing_heavy", at + Vector3.Up, 0f);
                Fx.Sparks(root, at + Vector3.Up * 0.4f, new Color(1f, 0.7f, 0.4f, 0.8f), 24, 3f, 0.4f, 0.5f);
                break;
            case PlayerClasses.ShadowstepId:
                // The vanishing puff where you were; the server moves you (Net.C_Blink).
                Sfx.At("vanish", at + Vector3.Up, 2f);
                Fx.Sparks(root, at + Vector3.Up * 0.9f, new Color(0.35f, 0.3f, 0.5f, 0.8f), 36, 1.8f, 0.5f, 0.7f);
                break;
            case PlayerClasses.FrostNovaId:
            {
                model?.PlayOneShot("Spellcast_Raise", 1.8f);
                Sfx.At("vanish", at + Vector3.Up, 3f, 0.05f);
                var ice = new Color(0.6f, 0.85f, 1f, 0.9f);
                Fx.Ring(root, at, ice, Net.FrostNovaRadius, 0.45f);
                Fx.Sparks(root, at + Vector3.Up * 0.6f, ice, 50, 5f, 0.5f, 0.6f);
                foreach (var e in Net.LiveEnemies())
                    if (Net.FlatDistance(e.GlobalPosition, at) <= Net.FrostNovaRadius)
                        Fx.Sparks(root, e.GlobalPosition + Vector3.Up * 0.6f, ice, 24, 0.8f, 0.6f, Net.FrostNovaSlowSeconds);
                break;
            }
            case PlayerClasses.BlossomWardId:
                model?.PlayOneShot("Spellcast_Raise", 1.2f);
                Sfx.At("heal", at + Vector3.Up, 3f, 0.05f);
                Fx.Ward(root, at, Net.WardRadius, Net.WardSeconds);
                break;
            case PlayerClasses.VolleyId:
                model?.PlayOneShot("1H_Ranged_Shoot", 1.6f);
                _player.Combat.PlayBowShot();
                Sfx.At("bow", at + Vector3.Up * 1.3f, 2f);
                Sfx.At("bow", at + Vector3.Up * 1.3f, -2f, 0.15f);
                break;

            // ---- slot 2 (level 12)
            case PlayerClasses.WarCryId:
                model?.PlayOneShot("Spellcast_Raise", 1.6f);
                Sfx.At("raid_horn", at + Vector3.Up * 1.5f, -4f, 0.04f);
                Fx.Ring(root, at, new Color(1f, 0.5f, 0.25f, 0.85f), Net.WarCryRadius, 0.6f);
                Fx.Sparks(root, at + Vector3.Up * 1.2f, new Color(1f, 0.6f, 0.3f), 40, 4f, 0.4f, 0.6f);
                break;
            case PlayerClasses.VenomId:
                Sfx.At("vanish", at + Vector3.Up, 0f, 0.1f);
                Fx.Sparks(root, at + Vector3.Up * 1f, new Color(0.45f, 0.9f, 0.3f, 0.85f), 36, 1.6f, 0.5f, 0.9f);
                break;
            case PlayerClasses.MeteorId:
                model?.PlayOneShot("Spellcast_Shoot", 1.4f);
                Fx.Sparks(root, at + Vector3.Up * 1.6f, new Color(1f, 0.5f, 0.2f), 20, 2.5f, 0.3f, 0.5f);
                break;
            case PlayerClasses.RekindleId:
            {
                model?.PlayOneShot("Spellcast_Raise", 1.3f);
                Sfx.At("heal", at + Vector3.Up, 3f, 0.05f);
                var target = Net.RekindleTarget(_player);
                Fx.Sparks(root, (target?.GlobalPosition ?? at) + Vector3.Up * 0.4f, new Color(1f, 0.75f, 0.85f), 50, 2.5f, 0.6f, 1.2f);
                break;
            }
            case PlayerClasses.MarkId:
            {
                model?.PlayOneShot("1H_Ranged_Shoot", 1.6f);
                Sfx.At("arrow_hit", at + Vector3.Up * 1.3f, 0f);
                var e = Net.MarkTarget(_player, aim);
                if (e != null) Fx.Sparks(root, e.GlobalPosition + Vector3.Up * 1.4f, new Color(1f, 0.3f, 0.25f), 24, 1.5f, 0.3f, 0.6f);
                break;
            }
        }
    }
}
