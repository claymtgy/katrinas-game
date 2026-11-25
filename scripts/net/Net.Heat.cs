using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The Cinder Foothills' heat (GDD §11, step 3), on the server. Out there heat builds
/// slowly everywhere, fast by a steam vent and on glowing ground; a cool spring sheds it quickly,
/// the Ashen Blossom more gently, and it drains away anywhere else. Full heat burns. An
/// Ember-glass Charm (Gear.PerkHeatward) and the level-15 heat talents slow the build-up.
/// Each player's heat is sent to them a few times a second (the HUD's heat bar).</summary>
public partial class Net
{
    public const float HeatMax = 100f;
    /// <summary>Per second: anywhere in the Foothills, by a vent, on hot ground.</summary>
    public const float HeatAmbient = 0.4f, HeatVent = 8f, HeatHotGround = 3f;
    /// <summary>Per second: in a spring, by the blossom, anywhere else.</summary>
    public const float HeatSpring = 30f, HeatBlossom = 15f, HeatAway = 6f;
    /// <summary>Damage per second at full heat.</summary>
    public const float HeatBurn = 4f;

    private float _heatSend;
    private readonly Dictionary<int, float> _heatSent = new();
    private readonly Dictionary<int, float> _heatBurnBank = new();

    /// <summary>How fast heat changes here (per second, before charms and talents slow a gain).</summary>
    public static float HeatRate(Vector3 pos, bool raining)
    {
        if (!HomeCliffs.InFoothillsArea(pos)) return -HeatAway;
        var f = new Vector2(pos.X, pos.Z);
        bool Near(Vector3 c, float r) => f.DistanceTo(new Vector2(c.X, c.Z)) < r && Mathf.Abs(pos.Y - Terrain.HeightAt(c.X, c.Z)) < 3f;
        foreach (var s in HomeCliffs.CoolSprings) if (Near(s, HomeCliffs.SpringRadius + 0.6f)) return -HeatSpring;
        if (Near(HomeCliffs.AshenBlossomPos, 8f)) return -HeatBlossom;
        float rate = raining ? 0f : HeatAmbient;
        foreach (var v in HomeCliffs.SteamVents) if (Near(v, HomeCliffs.VentRadius)) rate += HeatVent;
        foreach (var (at, r) in HomeCliffs.HotGround) if (Near(at, r)) rate += HeatHotGround;
        return rate;
    }

    private void TickHeat(float dt)
    {
        _heatSend -= dt;
        bool send = _heatSend <= 0f;
        if (send) _heatSend = 0.25f;
        foreach (var p in Gs.Players.Values)
        {
            if (p.Health.IsDead) p.Heat = 0f;
            else
            {
                float rate = HeatRate(p.GlobalPosition, Gs.Raining);
                if (p.Swimming || p.Sleeping) rate = Mathf.Min(rate, -HeatSpring);
                if (rate > 0f) rate *= Gear.HeatGainMult(p) * (Talents.HeatTalent(p) ? Talents.HeatTalentGain : 1f);
                p.Heat = Mathf.Clamp(p.Heat + rate * dt, 0f, HeatMax);
                if (p.Heat >= HeatMax && !p.Downed && !Gs.InSafeZone(p.GlobalPosition))
                {
                    _heatBurnBank.TryGetValue(p.PeerId, out float bank);
                    bank += HeatBurn * dt;
                    if (bank >= 2f)
                    {
                        if (bank >= 2f && !_heatSent.ContainsKey(-p.PeerId))
                        {
                            _heatSent[-p.PeerId] = 1f; // warned this time round
                            ToastTo(p.PeerId, "The heat is burning you! Find a spring (the pale blue pools) or the Ashen Blossom.");
                        }
                        p.Health.TakeDamage(bank, null);
                        bank = 0f;
                    }
                    _heatBurnBank[p.PeerId] = bank;
                }
                else if (p.Heat < HeatMax * 0.6f) _heatSent.Remove(-p.PeerId);
            }
            if (!send) continue;
            _heatSent.TryGetValue(p.PeerId, out float last);
            if (Mathf.Abs(last - p.Heat) < 0.05f) continue;
            _heatSent[p.PeerId] = p.Heat;
            ToPeer(p.PeerId, MethodName.C_Heat, p.Heat);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority)]
    private void C_Heat(float heat)
    {
        var me = Gs.LocalPlayer;
        if (me != null && !IsServer) me.Heat = heat;
    }
}
