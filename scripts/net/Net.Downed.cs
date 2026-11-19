using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Downed and revived (GDD §16). A killing blow downs a player instead of defeating
/// them when another player who isn't down is within 60 m. Downed, they crawl and wait: a friend
/// holds Interact beside them for 3 s and they get up with 30% health; after 30 s, or if nobody
/// is left to help, or if they hold Jump to give up, they're defeated as before. Enemies leave a
/// downed player alone and no hit can hurt them further. The server decides all of it.</summary>
public partial class Net
{
    public const float DownedSeconds = 30f, DownedHelpRange = 60f, ReviveReach = 2.6f, ReviveSeconds = 3f, ReviveHealth = 0.3f;

    /// <summary>Server: a revive in progress, by the downed player's id.</summary>
    private readonly Dictionary<int, (int Helper, float Progress, double Since)> _revives = new();
    private float _reviveSendTimer;

    /// <summary>Local player: start or stop holding Interact over a downed friend.</summary>
    public void RequestRevive(PlayerController downed, bool holding) => ToServer(MethodName.S_Revive, downed.PeerId, holding);

    /// <summary>Local player (downed): give up and go home now.</summary>
    public void RequestGiveUp() => ToServer(MethodName.S_GiveUp);

    /// <summary>Server: set up a player's Health so killing blows can down them.</summary>
    public void SetUpDowning(PlayerController p)
    {
        p.Health.Absorb = _ => IsServer && p.Downed && !p.ForceDefeat;
        p.Health.BeforeLethal = source => IsServer && !p.ForceDefeat && ServerTryDown(p, source);
    }

    /// <summary>Is someone able to help this player: alive, not down, within 60 m?</summary>
    public static bool HasHelper(PlayerController p) =>
        Gs.Players.Values.Any(o => o != p && IsInstanceValid(o) && !o.Health.IsDead && !o.Downed && !o.Sleeping
                                   && o.GlobalPosition.DistanceTo(p.GlobalPosition) <= DownedHelpRange);

    /// <summary>Server: a killing blow on <paramref name="p"/>. True: they went down (or were already).</summary>
    private bool ServerTryDown(PlayerController p, Node source)
    {
        if (p.Downed) return true;
        if (!HasHelper(p)) return false;
        p.DownedBy = source;
        ServerSetDowned(p, true);
        ToastTo(p.PeerId, "You're down! A friend can revive you.");
        foreach (var o in Gs.Players.Values)
            if (o != p && o.GlobalPosition.DistanceTo(p.GlobalPosition) <= DownedHelpRange)
                ToastTo(o.PeerId, $"{p.DisplayName} is down! Hold {Gamepad.ButtonName("interact")} beside them to revive.");
        return true;
    }

    private void ServerSetDowned(PlayerController p, bool downed)
    {
        double until = downed ? Now + DownedSeconds : 0;
        if (!downed) _revives.Remove(p.PeerId);
        if (downed) p.Blocking = false;
        p.SetDowned(downed, until);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Downed, p.PeerId, downed, (float)DownedSeconds);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Downed(int id, bool downed, float seconds)
    {
        var p = Gs.GetPlayer(id);
        if (p == null) return;
        // Clients count down on their own clock.
        p.SetDowned(downed, downed ? Now + seconds : 0);
    }

    /// <summary>Server: the downed player is defeated now (timer, give up, nobody left).</summary>
    private void ServerDefeat(PlayerController p, string why = "")
    {
        if (!p.Downed) return;
        var source = p.DownedBy;
        ServerSetDowned(p, false);
        if (why != "") ToastTo(p.PeerId, why);
        p.ForceDefeat = true;
        p.Health.TakeDamage(p.Health.Current + 1000f, IsInstanceValid(source) ? source : null, true);
        p.ForceDefeat = false;
    }

    /// <summary>Server: up again, with 30% health (half with the Healer's Lifeline talent).</summary>
    private void ServerRevive(PlayerController p, PlayerController helper, float healthFraction)
    {
        if (!p.Downed) return;
        ServerSetDowned(p, false);
        float target = p.Health.Max * healthFraction;
        if (target > p.Health.Current) p.Health.Heal(target - p.Health.Current);
        ToastTo(p.PeerId, helper != null ? $"{helper.DisplayName} got you back on your feet." : "You're back on your feet.");
        if (helper != null) ToastTo(helper.PeerId, $"You revived {p.DisplayName}.");
        Journal.Fact("revived");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Revive(int downedId, bool holding)
    {
        var helper = SenderPlayer();
        var p = Gs.GetPlayer(downedId);
        if (helper == null || p == null || p == helper) return;
        if (!holding)
        {
            if (_revives.TryGetValue(downedId, out var r) && r.Helper == helper.PeerId) _revives.Remove(downedId);
            return;
        }
        if (!p.Downed || helper.Downed || helper.Health.IsDead) return;
        if (FlatDistance(p.GlobalPosition, helper.GlobalPosition) > ReviveReach + 0.6f) return;
        if (_revives.TryGetValue(downedId, out var cur) && cur.Helper == helper.PeerId) return; // already holding
        _revives[downedId] = (helper.PeerId, 0f, Now);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_GiveUp()
    {
        var p = SenderPlayer();
        if (p != null && p.Downed) ServerDefeat(p);
    }

    /// <summary>Server, every frame: timers, revives, and nobody-left-to-help.</summary>
    private void TickDowned(float dt)
    {
        bool send = (_reviveSendTimer -= dt) <= 0;
        if (send) _reviveSendTimer = 0.15f;
        foreach (var p in Gs.Players.Values.ToList())
        {
            if (!IsInstanceValid(p) || !p.Downed) continue;
            if (Now >= p.DownedUntil) { ServerDefeat(p, "Nobody reached you in time."); continue; }
            if (!HasHelper(p)) { ServerDefeat(p, "Nobody is left to help."); continue; }

            float progress = 0f;
            if (_revives.TryGetValue(p.PeerId, out var r))
            {
                var helper = Gs.GetPlayer(r.Helper);
                bool ok = helper != null && IsInstanceValid(helper) && !helper.Downed && !helper.Health.IsDead
                          && FlatDistance(helper.GlobalPosition, p.GlobalPosition) <= ReviveReach + 0.6f
                          && helper.LastHurtAt < r.Since;   // a hit breaks the hold
                if (!ok) _revives.Remove(p.PeerId);
                else
                {
                    bool lifeline = Talents.Has(helper, Talents.Lifeline);
                    progress = r.Progress + dt * (lifeline ? Talents.LifelineSpeed : 1f);
                    if (progress >= ReviveSeconds)
                    {
                        ServerRevive(p, helper, lifeline ? Talents.LifelineHealth : ReviveHealth);
                        continue;
                    }
                    _revives[p.PeerId] = (r.Helper, progress, r.Since);
                }
            }
            if (send && !Mathf.IsEqualApprox(progress / ReviveSeconds, p.ReviveProgress))
            {
                p.ReviveProgress = progress / ReviveSeconds;
                foreach (var peer in _joined) RpcId(peer, MethodName.C_ReviveProgress, p.PeerId, p.ReviveProgress);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void C_ReviveProgress(int id, float progress)
    {
        var p = Gs.GetPlayer(id);
        if (p != null) p.ReviveProgress = progress;
    }
}
