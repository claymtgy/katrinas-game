using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>Class abilities over the network (see PlayerAbility, PlayerClasses, Projectile).</summary>
public partial class Net
{
    public const float WhirlwindRadius = 3.2f, WhirlwindDamage = 1.3f, WhirlwindDelay = 0.2f;
    public const float VanishSeconds = 4f;
    public const float MendRadius = 9f, MendHeal = 30f;

    private int _nextProjectileId = 1;

    public void RequestAbility(Vector3 aim) => ToServer(MethodName.S_Ability, aim);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Ability(Vector3 aim)
    {
        var p = SenderPlayer();
        if (p == null || p.Health.IsDead || p.Sleeping) return;
        var ability = p.Ability;
        var cls = p.Class;
        // Allow a little slack for latency, but no faster than the class's cooldown.
        if (Now < ability.ServerReadyAt - 0.15) return;
        ability.ServerReadyAt = Now + cls.AbilityCooldown;
        aim = aim.LengthSquared() > 0.01f ? aim.Normalized() : -p.Body.GlobalBasis.Z;
        var chest = p.GlobalPosition + Vector3.Up * 1.35f;

        switch (cls.Id)
        {
            case PlayerClasses.WarriorId:
                GetTree().CreateTimer(WhirlwindDelay).Timeout += () =>
                {
                    if (IsInstanceValid(p) && !p.Health.IsDead)
                        p.Combat.ServerStrikeArea(p.GlobalPosition + Vector3.Up * 1f, WhirlwindRadius, p.Combat.LightDamage * WhirlwindDamage);
                };
                break;
            case PlayerClasses.RogueId:
                p.VanishUntil = Now + VanishSeconds;
                SetVanished(p, true);
                break;
            case PlayerClasses.WizardId:
                SpawnProjectile(p, Projectile.Firebolt, chest + aim * 0.7f, aim * Projectile.BoltSpeed);
                break;
            case PlayerClasses.HealerId:
                foreach (var other in Gs.Players.Values)
                    if (!other.Health.IsDead && other.GlobalPosition.DistanceTo(p.GlobalPosition) <= MendRadius)
                        other.Health.Heal(MendHeal);
                break;
            case PlayerClasses.ArcherId:
                // A touch of loft so a level shot carries.
                SpawnProjectile(p, Projectile.Arrow, chest + aim * 0.6f, (aim + Vector3.Up * 0.03f).Normalized() * Projectile.ArrowSpeed);
                break;
        }
        foreach (var peer in _joined)
            if (peer != p.PeerId) RpcId(peer, MethodName.C_AbilityFx, p.PeerId, aim);
        if (!p.IsLocal && !IsDedicated) p.Ability.PlayFx(aim);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_AbilityFx(int id, Vector3 aim) => Gs.GetPlayer(id)?.Ability.PlayFx(aim);

    // ---------------------------------------------------------------- vanish

    /// <summary>Server: Vanish is over (timed out, or the rogue struck).</summary>
    public void ServerEndVanish(PlayerController p)
    {
        p.VanishUntil = 0;
        SetVanished(p, false);
    }

    private void SetVanished(PlayerController p, bool on)
    {
        p.SetVanished(on);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Vanish, p.PeerId, on);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Vanish(int id, bool on) => Gs.GetPlayer(id)?.SetVanished(on);

    // ---------------------------------------------------------------- projectiles

    private void SpawnProjectile(PlayerController shooter, string kind, Vector3 origin, Vector3 velocity)
    {
        int id = _nextProjectileId++;
        Projectile.Spawn(GetTree().CurrentScene, id, kind, origin, velocity, shooter, server: true);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Projectile, id, kind, origin, velocity, shooter.PeerId);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Projectile(int id, string kind, Vector3 origin, Vector3 velocity, int shooter)
    {
        if (GetTree().CurrentScene == null) return;
        _clientProjectiles[id] = Projectile.Spawn(GetTree().CurrentScene, id, kind, origin, velocity, Gs.GetPlayer(shooter), server: false);
    }

    private readonly System.Collections.Generic.Dictionary<int, Projectile> _clientProjectiles = new();

    /// <summary>Server: a projectile hit something (or ran out of time) at this point.</summary>
    public void ServerProjectileEnded(int id, Vector3 at)
    {
        foreach (var peer in _joined) RpcId(peer, MethodName.C_ProjectileEnd, id, at);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_ProjectileEnd(int id, Vector3 at)
    {
        if (_clientProjectiles.Remove(id, out var p) && IsInstanceValid(p)) p.End(at);
    }
}
