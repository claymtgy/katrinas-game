using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>XP and levels over the network (GDD §6, *Levels and abilities*; see Levels).
///
/// The server awards XP and owns each player's total. Every machine gets the new total
/// (C_Xp), because levels show on nameplates; only the player who earned it sees the toast,
/// the bar move and the "Level up" banner.</summary>
public partial class Net
{
    /// <summary>Server: give one player XP. Players below the highest level online earn double
    /// (catch-up). reason shows in their toast ("first wolf"); "" for a quiet award.</summary>
    public void ServerAwardXp(PlayerController p, int amount, string reason = "", int top = 0)
    {
        if (!IsServer || p == null || !IsInstanceValid(p) || amount <= 0) return;
        if (p.Level >= Levels.MaxLevel) return;
        if (top <= 0) top = TopLevel();
        bool catchUp = p.Level < top;
        if (catchUp) amount *= 2;
        int before = p.Level;
        ServerSetXp(p, p.Xp + amount);
        if (p.Level > before) GD.Print($"[Xp] {p.DisplayName} reached level {p.Level}");
        string why = reason == "" ? "" : reason + (catchUp ? " (catch-up ×2)" : "");
        int levelUp = p.Level > before ? p.Level : 0;
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Xp, p.PeerId, p.Xp, amount, why, levelUp);
        if (!IsDedicated) C_Xp(p.PeerId, p.Xp, amount, why, levelUp);
    }

    /// <summary>Server: a world milestone (a sanctuary lit, a raid beaten off, a home upgrade, a
    /// goal): everyone online gets it.</summary>
    public void ServerAwardXpAll(int amount, string reason)
    {
        int top = TopLevel(); // before anyone levels up from this
        foreach (var p in Gs.Players.Values.ToList()) ServerAwardXp(p, amount, reason, top);
    }

    private static int TopLevel() =>
        Gs.Players.Values.Where(IsInstanceValid).Select(o => o.Level).DefaultIfEmpty(1).Max();

    /// <summary>Server: an enemy died. Everyone within 60 m gets full XP. A first kill of a kind
    /// (per character) is worth more; a boss counts as a boss only the first time.</summary>
    public void ServerXpForKill(Enemy e)
    {
        if (!IsServer || e == null) return;
        var at = e.GlobalPosition;
        int top = TopLevel();
        foreach (var p in Gs.Players.Values.ToList())
        {
            if (!IsInstanceValid(p) || p.Health.IsDead || p.GlobalPosition.DistanceTo(at) > Levels.KillShareRadius) continue;
            string kind = e.Kind;
            if (p.XpFirsts.Add($"kill:{kind}"))
            {
                bool boss = Levels.IsBoss(kind);
                ServerAwardXp(p, boss ? Levels.BossXp(kind) : Levels.FirstKill, $"{(boss ? "defeated the" : "first")} {Levels.KindName(kind)}", top);
            }
            else ServerAwardXp(p, Levels.RepeatKill(kind), "", top);
        }
    }

    /// <summary>Server: the first time this character crafts an item.</summary>
    public void ServerXpForCraft(PlayerController p, string itemId)
    {
        if (p != null && p.XpFirsts.Add($"craft:{itemId}"))
            ServerAwardXp(p, Levels.FirstCraft, $"first {ItemDb.NameOf(itemId)}");
    }

    /// <summary>Server: a saved total (joining, F9 load). Tells everyone, but no toast or banner.</summary>
    public void ServerLoadXp(PlayerController p, int total)
    {
        if (!IsServer || p == null) return;
        ServerSetXp(p, total);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Xp, p.PeerId, p.Xp, 0, "", 0);
        if (!IsDedicated && p.IsLocal) Hud.Instance?.OnXp(0, "", 0);
    }

    /// <summary>Server: set a player's total XP, and heal them by any max health the new level
    /// adds (so a level-up, or rejoining at level 7, doesn't leave you short of a full bar).</summary>
    private static void ServerSetXp(PlayerController p, int total)
    {
        float maxBefore = p.Health.Max;
        p.SetXp(total);
        float gained = p.Health.Max - maxBefore;
        if (gained > 0) p.Health.Heal(gained);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void C_Xp(int id, int total, int gained, string reason, int levelUp)
    {
        var p = Gs.GetPlayer(id);
        if (p == null) return;
        // The server already applied it (ServerAwardXp); clients mirror it here.
        if (!IsServer) p.SetXp(total);
        if (p.IsLocal) Hud.Instance?.OnXp(gained, reason, levelUp);
    }
}
