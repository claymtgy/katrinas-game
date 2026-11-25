using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>Class abilities over the network (see PlayerAbility, PlayerClasses, Projectile).</summary>
public partial class Net
{
    public const float WhirlwindRadius = 3.2f, WhirlwindDamage = 1.3f, WhirlwindDelay = 0.2f;
    public const float VanishSeconds = 4f;
    public const float MendRadius = 9f, MendHeal = 30f;
    // Level-5 abilities (GDD §6).
    public const float ChargeDistance = 8f, ChargeSeconds = 0.32f, ChargeWidth = 1.3f, ChargeKnockdown = 1.2f;
    public const float ShadowstepRange = 12f, ShadowstepBehind = 1.4f, CritSeconds = 2f, CritDamage = 2f;
    public const float FrostNovaRadius = 5f, FrostNovaSlowSeconds = 3f, FrostNovaSlow = 0.5f;
    public const float WardRadius = 5f, WardSeconds = 8f;
    public const int VolleyArrows = 5;
    public const float VolleyFanDegrees = 30f;

    private int _nextProjectileId = 1;

    /// <summary>How far this player's Whirlwind reaches (the Cleave talent adds 1 m).</summary>
    public static float WhirlwindReach(PlayerController p) => WhirlwindRadius + (Talents.Has(p, Talents.Cleave) ? Talents.CleaveRadius : 0f);

    public void RequestAbility(Vector3 aim, int slot = 0) => ToServer(MethodName.S_Ability, aim, slot);

    public static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Every machine: enemies that are alive (abilities' targets and effects).</summary>
    public static System.Collections.Generic.IEnumerable<Enemy> LiveEnemies() =>
        System.Linq.Enumerable.Where(Wildlife.Instance?.Enemies.Values ?? (System.Collections.Generic.IEnumerable<Enemy>)System.Array.Empty<Enemy>(),
            e => IsInstanceValid(e) && e.Current != Enemy.State.Dead);

    /// <summary>Every machine: the enemy Shadowstep would go to: the one nearest where you look,
    /// within 12 m and 60° of your aim (null: none).</summary>
    public static Enemy ShadowstepTarget(PlayerController p, Vector3 aim)
    {
        var flatAim = new Vector3(aim.X, 0, aim.Z);
        if (flatAim.LengthSquared() < 0.0001f) return null;
        flatAim = flatAim.Normalized();
        Enemy best = null;
        float bestScore = float.MaxValue;
        foreach (var e in LiveEnemies())
        {
            var to = e.GlobalPosition - p.GlobalPosition;
            to.Y = 0;
            float d = to.Length();
            if (d > ShadowstepRange || d < 0.01f) continue;
            float dot = flatAim.Dot(to / d);
            if (dot < 0.5f) continue;
            float score = d * (2f - dot); // near and in front
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Ability(Vector3 aim, int slot)
    {
        var p = SenderPlayer();
        if (p == null || p.Health.IsDead || p.Sleeping || p.Downed) return;
        var ability = p.AbilitySlot(slot);
        var def = p.AbilityDef(slot);
        if (ability == null || def == null || p.Level < def.UnlockLevel) return;
        // Allow a little slack for latency, but no faster than the ability's cooldown.
        if (Now < ability.ServerReadyAt - 0.15) return;
        ability.ServerReadyAt = Now + def.Cooldown;
        aim = aim.LengthSquared() > 0.01f ? aim.Normalized() : -p.Body.GlobalBasis.Z;
        var chest = p.GlobalPosition + Vector3.Up * 1.35f;

        if (slot == 2) ServerThirdAbility(p, def, aim);
        else if (slot == 1) ServerSecondAbility(p, def, aim, chest);
        else switch (p.Class.Id)
        {
            case PlayerClasses.WarriorId:
                GetTree().CreateTimer(WhirlwindDelay).Timeout += () =>
                {
                    if (IsInstanceValid(p) && !p.Health.IsDead)
                        p.Combat.ServerStrikeArea(p.GlobalPosition + Vector3.Up * 1f, WhirlwindReach(p), p.Combat.LightDamage * WhirlwindDamage);
                };
                break;
            case PlayerClasses.RogueId:
                p.VanishUntil = Now + VanishSeconds + (Talents.Has(p, Talents.Ambush) ? Talents.AmbushSeconds : 0f);
                SetVanished(p, true);
                break;
            case PlayerClasses.WizardId:
                SpawnProjectile(p, Projectile.Firebolt, chest + aim * 0.7f, aim * Projectile.BoltSpeed);
                break;
            case PlayerClasses.HealerId:
                foreach (var other in Gs.Players.Values)
                    if (!other.Health.IsDead && !other.Downed && other.GlobalPosition.DistanceTo(p.GlobalPosition) <= MendRadius)
                        other.Health.Heal(MendHeal * Gear.AbilityPower(p) + (Talents.Has(p, Talents.Lifebloom) ? Talents.LifebloomHeal : 0f));
                break;
            case PlayerClasses.ArcherId:
                // A touch of loft so a level shot carries.
                SpawnProjectile(p, Projectile.Arrow, chest + aim * 0.6f, (aim + Vector3.Up * 0.03f).Normalized() * Projectile.ArrowSpeed * (Gear.BowOf(p)?.ArrowSpeed ?? 1f));
                break;
        }
        foreach (var peer in _joined)
            if (peer != p.PeerId) RpcId(peer, MethodName.C_AbilityFx, p.PeerId, aim, slot);
        if (!p.IsLocal && !IsDedicated) ability.PlayFx(aim);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_AbilityFx(int id, Vector3 aim, int slot) => Gs.GetPlayer(id)?.AbilitySlot(slot)?.PlayFx(aim);

    // ---------------------------------------------------------------- level-5 abilities

    private void ServerSecondAbility(PlayerController p, AbilityDef def, Vector3 aim, Vector3 chest)
    {
        var flat = new Vector3(aim.X, 0, aim.Z);
        flat = flat.LengthSquared() > 0.0001f ? flat.Normalized() : -p.Body.GlobalBasis.Z;
        switch (def.Id)
        {
            case PlayerClasses.ChargeId:
                ServerCharge(p, flat);
                break;
            case PlayerClasses.ShadowstepId:
            {
                var e = ShadowstepTarget(p, aim);
                if (e == null) break;
                // Behind it: enemies face -Z, so behind is +Z.
                var back = e.GlobalBasis.Z;
                back.Y = 0;
                back = back.LengthSquared() > 0.0001f ? back.Normalized() : flat;
                var to = e.GlobalPosition + Vector3.Up * 0.1f + back * ShadowstepBehind;
                p.CritUntil = Now + CritSeconds + 0.4; // (+ a swing's windup)
                ToPeer(p.PeerId, MethodName.C_Blink, to, e.GlobalPosition);
                break;
            }
            case PlayerClasses.FrostNovaId:
                foreach (var e in LiveEnemies())
                    if (FlatDistance(e.GlobalPosition, p.GlobalPosition) <= FrostNovaRadius)
                        e.Slow(FrostNovaSlowSeconds + (Talents.Has(p, Talents.Glacial) ? Talents.GlacialSeconds : 0f), FrostNovaSlow);
                break;
            case PlayerClasses.BlossomWardId:
            {
                // Like a sanctuary: enemies keep out and won't hunt anyone inside (Enemy.CanHunt).
                var zone = (p.GlobalPosition, WardRadius);
                Gs.SafeZones.Add(zone);
                GetTree().CreateTimer(WardSeconds).Timeout += () => Gs.SafeZones.Remove(zone);
                break;
            }
            case PlayerClasses.VolleyId:
                for (int i = 0; i < VolleyArrows; i++)
                {
                    float angle = Mathf.DegToRad(VolleyFanDegrees) * ((float)i / (VolleyArrows - 1) - 0.5f);
                    var dir = aim.Rotated(Vector3.Up, angle);
                    SpawnProjectile(p, Projectile.Volley, chest + dir * 0.6f, (dir + Vector3.Up * 0.03f).Normalized() * Projectile.ArrowSpeed * (Gear.BowOf(p)?.ArrowSpeed ?? 1f));
                }
                break;
        }
    }

    /// <summary>Server: the Warrior's Charge. The rush itself is the player's own movement
    /// (PlayerController.StartDash); here the first enemy in the lane is struck and knocked down
    /// when the rush reaches it.</summary>
    private void ServerCharge(PlayerController p, Vector3 dir)
    {
        var from = p.GlobalPosition;
        Enemy first = null;
        float firstAlong = float.MaxValue;
        foreach (var e in LiveEnemies())
        {
            var rel = e.GlobalPosition - from;
            rel.Y = 0;
            float along = rel.Dot(dir);
            if (along < 0f || along > ChargeDistance + 1f) continue;
            if ((rel - dir * along).Length() > ChargeWidth) continue;
            if (along < firstAlong) { firstAlong = along; first = e; }
        }
        if (first == null) return;
        float delay = Mathf.Clamp(firstAlong / (ChargeDistance / ChargeSeconds), 0.05f, ChargeSeconds);
        var target = first;
        GetTree().CreateTimer(delay).Timeout += () =>
        {
            if (!IsInstanceValid(target) || !IsInstanceValid(p) || target.Current == Enemy.State.Dead) return;
            target.Health.TakeDamage(p.Combat.LightDamage, p);
            if (target.Current != Enemy.State.Dead) target.Stun(ChargeKnockdown);
            HitMarkerTo(p.PeerId);
            foreach (var peer in _joined) RpcId(peer, MethodName.C_ChargeHit, target.GlobalPosition);
            if (!IsDedicated) C_ChargeHit(target.GlobalPosition);
        };
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_ChargeHit(Vector3 at)
    {
        Sfx.At("parry", at + Vector3.Up, 0f, 0.08f);
        Fx.Sparks(GetTree().CurrentScene, at + Vector3.Up * 0.8f, new Color(1f, 0.75f, 0.4f), 30, 4f, 0.3f, 0.5f);
    }

    /// <summary>The Rogue's own machine: Shadowstep moves us behind the enemy, facing it.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Blink(Vector3 to, Vector3 faceAt)
    {
        var p = Gs.LocalPlayer;
        if (p == null) return;
        p.Teleport(to);
        var d = faceAt - to;
        var (_, pitch) = p.Rig.Look;
        p.Rig.SetLook(Mathf.Atan2(-d.X, -d.Z), pitch);
        Sfx.At("vanish", to + Vector3.Up, 0f);
        Fx.Sparks(GetTree().CurrentScene, to + Vector3.Up * 0.9f, new Color(0.35f, 0.3f, 0.5f, 0.8f), 30, 1.6f, 0.5f, 0.6f);
        Hud.Toast("Strike now for a critical hit!");
    }

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

    /// <summary>Server: fly a projectile everywhere. shooter null = a watchtower (never hurts players).</summary>
    public void SpawnProjectile(PlayerController shooter, string kind, Vector3 origin, Vector3 velocity)
    {
        int id = _nextProjectileId++;
        Projectile.Spawn(GetTree().CurrentScene, id, kind, origin, velocity, shooter, server: true);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Projectile, id, kind, origin, velocity, shooter?.PeerId ?? 0);
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
