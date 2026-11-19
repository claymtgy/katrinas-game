using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>Talent picks over the network (GDD §6; see Talents). The server checks and stores a
/// pick, then tells every machine.</summary>
public partial class Net
{
    /// <summary>Local player: pick a talent (its class and level come from the talent).</summary>
    public void RequestTalent(string talentId) => ToServer(MethodName.S_Talent, talentId);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void S_Talent(string talentId)
    {
        var p = SenderPlayer();
        var def = Talents.Get(talentId);
        if (p == null || def == null) return;
        if (def.ClassId != p.Class.Id) return;
        if (p.Level < def.Level) { ToastTo(p.PeerId, $"{def.Name} unlocks at level {def.Level}."); return; }
        string key = Talents.Key(def.ClassId, def.Level);
        bool had = p.TalentPicks.TryGetValue(key, out var old);
        if (had && old == talentId) return;
        // The first pick can be made anywhere; changing your mind is free, but only at home.
        if (had && !Supplies.AtHome(p)) { ToastTo(p.PeerId, "Change talents at home."); return; }
        var picks = new System.Collections.Generic.Dictionary<string, string>(p.TalentPicks) { [key] = talentId };
        ServerSetTalents(p, picks);
        ToastTo(p.PeerId, $"Talent: {def.Name}");
    }

    /// <summary>Server: set a player's picks (a choice, or a saved character) and tell everyone.</summary>
    public void ServerSetTalents(PlayerController p, System.Collections.Generic.Dictionary<string, string> picks)
    {
        if (!IsServer || p == null) return;
        p.TalentPicks = picks;
        var d = Talents.ToDict(picks);
        foreach (var peer in _joined) RpcId(peer, MethodName.C_Talents, p.PeerId, d);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Talents(int id, GDict picks)
    {
        var p = Gs.GetPlayer(id);
        if (p == null) return;
        p.TalentPicks = Talents.FromDict(picks);
    }
}
