using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>The level-12 abilities (GDD §6), slot 2: War Cry, Venom, Meteor, Rekindle and Mark.
/// The server does the real thing; every screen gets the effects (C_Meteor, C_EnemyStatus, toasts).
/// Damage bonuses (War Cry, Mark) and Venom's poison ride on every hit a player lands on an enemy,
/// through <see cref="Health.Bonus"/>.</summary>
public partial class Net
{
    public const float WarCryRadius = 9f, WarCrySeconds = 8f, WarCryBonus = 0.2f, WarCryStaggerRadius = 5f, WarCryStagger = 1f;
    public const float VenomSeconds = 10f, VenomDps = 4f, VenomPoisonSeconds = 5f;
    public const int VenomMaxStacks = 3;
    public const float MeteorRange = 25f, MeteorDelay = 1f, MeteorDamage = 60f, MeteorRadius = 4f;
    public const float RekindleRange = 12f, RekindleHealth = 0.5f;
    public const float MarkRange = 40f, MarkSeconds = 10f, MarkBonus = 0.25f;

    /// <summary>Server: a poison tick is landing (it mustn't poison again or get bonuses).</summary>
    public static bool PoisonTicking;

    public const int StatusMark = 1, StatusPoison = 2;

    /// <summary>Server: what a player's hit on an enemy is multiplied by (War Cry, Mark); Venom
    /// poisons on the way. Hooked into Health.Bonus.</summary>
    private float ServerDamageBonus(Health h, Node source)
    {
        if (PoisonTicking || source is not PlayerController p || h.GetParent() is not Enemy e || e.Current == Enemy.State.Dead) return 1f;
        float m = 1f;
        if (Now < p.WarCryUntil) m *= 1f + WarCryBonus;
        if (e.Marked) m *= 1f + MarkBonus;
        if (Now < p.VenomUntil)
        {
            e.Poison(p);
            SendEnemyStatus(e, StatusPoison, VenomPoisonSeconds);
        }
        return m;
    }

    private void SendEnemyStatus(Enemy e, int kind, float seconds)
    {
        foreach (var peer in _joined) RpcId(peer, MethodName.C_EnemyStatus, e.NetId, kind, seconds);
        if (!IsDedicated) C_EnemyStatus(e.NetId, kind, seconds);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_EnemyStatus(int netId, int kind, float seconds)
    {
        if (Wildlife.Instance == null || !Wildlife.Instance.Enemies.TryGetValue(netId, out var e) || !IsInstanceValid(e)) return;
        if (kind == StatusMark) e.SetMarked(Now + seconds);
        else if (kind == StatusPoison)
            Fx.Sparks(GetTree().CurrentScene, e.GlobalPosition + Vector3.Up * 0.8f, new Color(0.45f, 0.9f, 0.3f, 0.85f), 14, 1.2f, 0.5f, 0.7f);
    }

    // ---------------------------------------------------------------- targets (every machine)

    /// <summary>Where a Meteor aimed this way comes down: what you look at within 25 m, or the
    /// ground under the 25 m point.</summary>
    public static Vector3 MeteorPoint(PlayerController p, Vector3 aim)
    {
        var space = p.GetWorld3D().DirectSpaceState;
        var from = p.GlobalPosition + Vector3.Up * 1.6f;
        var exclude = new Godot.Collections.Array<Rid> { p.GetRid() };
        var ray = PhysicsRayQueryParameters3D.Create(from, from + aim * MeteorRange, GameState.LayerWorld | GameState.LayerProps, exclude);
        var hit = space.IntersectRay(ray);
        if (hit.Count > 0) return hit["position"].AsVector3();
        var end = from + aim * MeteorRange;
        var down = PhysicsRayQueryParameters3D.Create(end + Vector3.Up * 30f, end + Vector3.Down * 60f, GameState.LayerWorld, exclude);
        hit = space.IntersectRay(down);
        return hit.Count > 0 ? hit["position"].AsVector3() : end;
    }

    /// <summary>The downed friend Rekindle would raise: the nearest within 12 m (null: none).</summary>
    public static PlayerController RekindleTarget(PlayerController p) =>
        Gs.Players.Values.Where(o => o != p && IsInstanceValid(o) && o.Downed && o.GlobalPosition.DistanceTo(p.GlobalPosition) <= RekindleRange)
            .OrderBy(o => o.GlobalPosition.DistanceTo(p.GlobalPosition)).FirstOrDefault();

    /// <summary>The enemy Mark would tag: the one nearest your aim within 40 m and about 20° (null: none).</summary>
    public static Enemy MarkTarget(PlayerController p, Vector3 aim)
    {
        if (aim.LengthSquared() < 0.0001f) return null;
        aim = aim.Normalized();
        var eye = p.GlobalPosition + Vector3.Up * 1.6f;
        Enemy best = null;
        float bestScore = float.MaxValue;
        foreach (var e in LiveEnemies())
        {
            var to = e.GlobalPosition + Vector3.Up * 0.8f - eye;
            float d = to.Length();
            if (d > MarkRange || d < 0.01f) continue;
            float dot = aim.Dot(to / d);
            if (dot < 0.94f) continue;
            float score = (1f - dot) * 40f + d * 0.05f; // closest to the crosshair first
            if (score < bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    // ---------------------------------------------------------------- server

    private void ServerThirdAbility(PlayerController p, AbilityDef def, Vector3 aim)
    {
        switch (def.Id)
        {
            case PlayerClasses.WarCryId:
                foreach (var o in Gs.Players.Values)
                    if (IsInstanceValid(o) && !o.Health.IsDead && !o.Downed && o.GlobalPosition.DistanceTo(p.GlobalPosition) <= WarCryRadius)
                    {
                        o.WarCryUntil = Now + WarCrySeconds;
                        ToastTo(o.PeerId, o == p ? "War Cry! +20% damage for 8 s." : $"{p.DisplayName}'s War Cry: +20% damage for 8 s.");
                    }
                foreach (var e in LiveEnemies().ToList())
                    if (FlatDistance(e.GlobalPosition, p.GlobalPosition) <= WarCryStaggerRadius) e.Stun(WarCryStagger);
                break;
            case PlayerClasses.VenomId:
                p.VenomUntil = Now + VenomSeconds;
                ToastTo(p.PeerId, "Venom: your hits poison for 10 s.");
                break;
            case PlayerClasses.MeteorId:
            {
                var at = MeteorPoint(p, aim);
                foreach (var peer in _joined) RpcId(peer, MethodName.C_Meteor, at);
                if (!IsDedicated) C_Meteor(at);
                GetTree().CreateTimer(MeteorDelay).Timeout += () =>
                {
                    if (!IsInstanceValid(p)) return;
                    bool hit = false;
                    foreach (var e in LiveEnemies().ToList())
                        if (FlatDistance(e.GlobalPosition, at) <= MeteorRadius && Mathf.Abs(e.GlobalPosition.Y - at.Y) < 4f)
                        {
                            e.Health.TakeDamage(MeteorDamage, p);
                            hit = true;
                        }
                    if (hit) HitMarkerTo(p.PeerId);
                };
                break;
            }
            case PlayerClasses.RekindleId:
            {
                var d = RekindleTarget(p);
                if (d != null) ServerRevive(d, p, RekindleHealth);
                break;
            }
            case PlayerClasses.MarkId:
            {
                var e = MarkTarget(p, aim);
                if (e == null) break;
                e.SetMarked(Now + MarkSeconds);
                foreach (var peer in _joined) RpcId(peer, MethodName.C_EnemyStatus, e.NetId, StatusMark, MarkSeconds);
                ToastTo(p.PeerId, "Marked! Everyone hits it 25% harder.");
                break;
            }
        }
    }

    /// <summary>Every screen: the Meteor's warning circle, then the rock coming down.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Meteor(Vector3 at)
    {
        var root = GetTree().CurrentScene;
        if (root == null) return;
        var fire = new Color(1f, 0.45f, 0.15f, 0.9f);
        Fx.Ring(root, at, new Color(1f, 0.3f, 0.1f, 0.6f), MeteorRadius, MeteorDelay);
        var rock = new MeshInstance3D
        {
            Name = "Meteor",
            Mesh = new SphereMesh { Radius = 0.7f, Height = 1.4f, RadialSegments = 10, Rings = 6 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.35f, 0.15f, 0.08f),
                EmissionEnabled = true, Emission = new Color(1f, 0.4f, 0.1f), EmissionEnergyMultiplier = 3f,
            },
        };
        root.AddChild(rock);
        rock.GlobalPosition = at + new Vector3(5f, 24f, 2f);
        Sfx.At("firebolt", at + Vector3.Up * 12f, 2f, 0.05f, 80f);
        var tw = rock.CreateTween();
        tw.TweenProperty(rock, "global_position", at, MeteorDelay).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        tw.TweenCallback(Callable.From(() =>
        {
            Sfx.At("explode", at + Vector3.Up, 4f, 0.05f, 80f);
            Fx.Sparks(root, at + Vector3.Up * 0.5f, fire, 70, 7f, 0.8f, 0.9f);
            Fx.Ring(root, at, fire, MeteorRadius, 0.5f);
            rock.QueueFree();
        }));
    }
}
