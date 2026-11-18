using Godot;

namespace KatrinasGame;

/// <summary>The class ability on R (or mouse button 4): Whirlwind, Vanish, Firebolt, Mend or
/// Shoot (see PlayerClasses). Same pattern as a sword swing: the player's own machine checks
/// the cooldown and stamina, plays the animation and sound at once and asks the server
/// (Net.RequestAbility); the server re-checks the cooldown and does the real thing; other
/// screens get the animation through Net.C_AbilityFx.</summary>
public partial class PlayerAbility : Node
{
    private PlayerController _player;
    private double _readyAt;

    /// <summary>Server clock: when this player may use the ability again (anti-spam).</summary>
    public double ServerReadyAt { get; set; }

    /// <summary>Local player: seconds until the ability is ready (HUD).</summary>
    public float CooldownLeft => Mathf.Max(0f, (float)(_readyAt - Time.GetTicksMsec() / 1000.0));

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        if (!_player.IsLocal) SetProcessUnhandledInput(false);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ability") && !(_player.Build?.Active ?? false))
        {
            TryUse();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Local player: use the class ability, aimed where the camera looks.</summary>
    public bool TryUse()
    {
        var cls = _player.Class;
        if (_player.Health.IsDead || _player.Sleeping || GameState.Instance.UiOpen || _player.Dodging || _player.Climbing) return false;
        if (CooldownLeft > 0) return false;
        if (!_player.TrySpendStamina(cls.AbilityStamina))
        {
            Hud.Toast($"Too tired for {cls.AbilityName}!");
            return false;
        }
        _readyAt = Time.GetTicksMsec() / 1000.0 + cls.AbilityCooldown;
        if (_player.Combat.GuardUp) _player.Combat.DropGuard();
        var aim = -_player.Rig.Camera.GlobalBasis.Z;
        PlayFx(aim);
        Net.Instance.RequestAbility(aim);
        return true;
    }

    /// <summary>Scripted tests: ready again now.</summary>
    public void ResetCooldown() => _readyAt = 0;

    /// <summary>Everyone: the ability's animation, sound and effect on the caster.</summary>
    public void PlayFx(Vector3 aim)
    {
        if (Net.Instance.IsDedicated) return;
        var model = _player.Body.GetNodeOrNull<CharacterModel>("Mesh");
        var at = _player.GlobalPosition;
        var root = GetTree().CurrentScene;
        switch (_player.Class.Id)
        {
            case PlayerClasses.WarriorId:
                model?.PlayOneShot("2H_Melee_Attack_Spin", 1.6f);
                Sfx.At("whirlwind", at + Vector3.Up, 2f);
                Fx.Ring(root, at, new Color(1f, 0.6f, 0.35f, 0.9f), Net.WhirlwindRadius, 0.35f);
                break;
            case PlayerClasses.RogueId:
                Sfx.At("vanish", at + Vector3.Up, 0f);
                Fx.Sparks(root, at + Vector3.Up * 0.9f, new Color(0.5f, 0.45f, 0.65f, 0.7f), 40, 1.6f, 0.6f, 1.0f);
                break;
            case PlayerClasses.WizardId:
                model?.PlayOneShot("Spellcast_Shoot", 1.8f);
                Sfx.At("firebolt", at + Vector3.Up * 1.3f, 0f);
                Fx.Sparks(root, at + Vector3.Up * 1.3f + aim * 0.8f, new Color(1f, 0.55f, 0.2f), 14, 2f, 0.15f, 0.4f);
                break;
            case PlayerClasses.HealerId:
                model?.PlayOneShot("Spellcast_Raise", 1.4f);
                Sfx.At("heal", at + Vector3.Up, 2f, 0.03f);
                Fx.Ring(root, at, new Color(0.5f, 1f, 0.6f, 0.8f), Net.MendRadius, 0.8f);
                foreach (var p in GameState.Instance.Players.Values)
                    if (p.GlobalPosition.DistanceTo(at) <= Net.MendRadius && !p.Health.IsDead)
                        Fx.Sparks(root, p.GlobalPosition + Vector3.Up * 0.3f, new Color(0.55f, 1f, 0.6f), 26, 2.2f, 0.45f, 1.1f);
                break;
            case PlayerClasses.ArcherId:
                model?.PlayOneShot("1H_Ranged_Shoot", 1.8f);
                Sfx.At("bow", at + Vector3.Up * 1.3f, 0f);
                break;
        }
    }
}
