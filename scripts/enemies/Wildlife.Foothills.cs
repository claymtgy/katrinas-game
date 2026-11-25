using System.Collections.Generic;
using System.Linq;
using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>The Cinder Foothills (GDD §11, step 3): ash wolves denned in the burnt hills (they hunt
/// by day here: the haze never lets the sun through), ash boars rooting round the springs, the
/// Warlord's fort with its veteran guard, and the Bandit Warlord himself. Each site fills like the
/// others: emptied, it comes back after a few days, never while anyone is near. Saved with the
/// west sites (SaveWest). The Warlord's death buys the base some peace: no raids for
/// <see cref="WarlordPeaceDays"/> days.</summary>
public partial class Wildlife
{
    /// <summary>Where the Foothills' enemies are kept (x, z): the island's bounding box.</summary>
    public static readonly Rect2 FoothillsBounds = Regions.Move(new Rect2(-92, -78, 184, 156), Regions.Foothills);

    /// <summary>Ash wolf dens: in the northern hills, and out west past the springs.</summary>
    public static readonly Vector3[] AshWolfDens = { HomeCliffs.InFoothills(22, 0, -48), HomeCliffs.InFoothills(-66, 0, 24) };
    public const int SoloAshWolves = 2;
    public const int AshWolfRespawnDays = 1;

    /// <summary>Ash boars root about the basin.</summary>
    public static readonly Vector3[] AshBoarHerds = { HomeCliffs.InFoothills(48, 0, -8), HomeCliffs.InFoothills(-22, 0, 44) };
    public const int SoloAshBoars = 2;
    public const int AshBoarRespawnDays = 1;

    /// <summary>The Warlord's fort (the world builder puts the palisade here) and his tent in it.</summary>
    public static Vector3 FortCenter => HomeCliffs.FortCenter;
    public static Vector3 WarlordTent => HomeCliffs.FortCenter + new Vector3(-4, 0, -6);
    public const int SoloFortGuard = 3;
    public const int FortRespawnDays = 2;
    public const int WarlordRespawnDays = 5;
    /// <summary>No raids on the base for this many days after the Warlord falls.</summary>
    public const int WarlordPeaceDays = 5;

    private const int AshWolfGroupBase = 63000, AshBoarGroupBase = 64000, FortGroupId = 65000, WarlordGroupId = 65001;

    private readonly Camp[] _ashDens = AshWolfDens.Select(c => new Camp { Center = c }).ToArray();
    private readonly Camp[] _ashHerds = AshBoarHerds.Select(c => new Camp { Center = c }).ToArray();
    private readonly Camp _fort = new() { Center = HomeCliffs.FortCenter };
    private readonly Camp _warlordTent = new() { Center = HomeCliffs.FortCenter + new Vector3(-4, 0, -6) };

    /// <summary>Server: the day raids may start again (the Warlord's death keeps them off).</summary>
    public int WarlordPeaceUntil { get; private set; }

    public static int AshWolfGroup(int i) => AshWolfGroupBase + i;
    public static int AshBoarGroup(int i) => AshBoarGroupBase + i;
    public static int FortGroup => FortGroupId;
    public static int WarlordGroup => WarlordGroupId;
    public bool WarlordCleared => _warlordTent.Cleared;
    public bool FortCleared => _fort.Cleared;
    public Bandit Warlord => Enemies.Values.OfType<Bandit>().FirstOrDefault(b => b.Warlord && b.Current != Enemy.State.Dead);

    private static bool IsFoothillsGroup(int group) => group >= AshWolfGroupBase && group <= WarlordGroupId;

    /// <summary>Server, once a second: fill empty dens, herds, the fort and the Warlord's tent.</summary>
    private void TendFoothills()
    {
        var gs = GameState.Instance;
        if (!AutoSpawn || !Net.Instance.Config.Enemies || gs.Players.Count == 0) return;
        for (int i = 0; i < _ashDens.Length; i++)
            if (Due(_ashDens[i], AshWolfGroup(i), 34f)) SpawnAshWolves(i);
        for (int i = 0; i < _ashHerds.Length; i++)
            if (Due(_ashHerds[i], AshBoarGroup(i), 30f)) SpawnAshBoars(i);
        if (Due(_fort, FortGroup, 40f)) SpawnFort();
        if (Due(_warlordTent, WarlordGroup, 44f)) SpawnWarlord();
    }

    public List<Wolf> SpawnAshWolves(int i)
    {
        _ashDens[i].Cleared = false;
        int count = Difficulty.PackSize(SoloAshWolves);
        var list = new List<Wolf>();
        for (int n = 0; n < count; n++)
            list.Add((Wolf)SpawnAround("ash_wolf", AshWolfGroup(i), _ashDens[i].Center, n, count, Wolf.AshHealth, Wolf.AshDamage));
        GD.Print($"[Wildlife] Ash wolf den {i}: {count} ash wolves");
        return list;
    }

    public List<Boar> SpawnAshBoars(int i)
    {
        _ashHerds[i].Cleared = false;
        int count = Difficulty.PackSize(SoloAshBoars);
        var list = new List<Boar>();
        for (int n = 0; n < count; n++)
            list.Add((Boar)SpawnAround("ash_boar", AshBoarGroup(i), _ashHerds[i].Center, n, count, Boar.AshHealth, Boar.AshDamage, 3f));
        return list;
    }

    /// <summary>Server: the fort's guard, posted round the yard (more with more players).</summary>
    public List<Bandit> SpawnFort()
    {
        _fort.Cleared = false;
        int count = Difficulty.PackSize(SoloFortGuard);
        var list = new List<Bandit>();
        for (int n = 0; n < count; n++)
            list.Add((Bandit)SpawnAround("fort_bandit", FortGroup, _fort.Center, n, count, Bandit.FortHealth, Bandit.FortDamage, 6f));
        GD.Print($"[Wildlife] The Warlord's fort: {count} guards");
        return list;
    }

    /// <summary>Server: the Bandit Warlord, before his tent (one of him, scaled for co-op like the other bosses).</summary>
    public Bandit SpawnWarlord()
    {
        _warlordTent.Cleared = false;
        var w = (Bandit)Spawn("warlord", WarlordGroup, _warlordTent.Center + new Vector3(0, 0, 3f), 0f,
            Difficulty.BigMonsterHealth(Bandit.WarlordHealth), Difficulty.BigMonsterDamage(Bandit.WarlordDamage));
        GD.Print($"[Wildlife] The Bandit Warlord ({GameState.Instance.Players.Count} players)");
        return w;
    }

    /// <summary>Server: a fort guard answers the Warlord's bellow (joins his group, so the tent is
    /// clear only once he is down).</summary>
    public Bandit SpawnWarlordGuard(Bandit warlord)
    {
        var off = new Vector3(_rng.RandfRange(-7, 7), 0.3f, _rng.RandfRange(4, 9));
        var g = (Bandit)Spawn("fort_bandit", warlord.Group, _fort.Center + off, _rng.RandfRange(0, Mathf.Tau), Bandit.FortHealth, Bandit.FortDamage);
        if (warlord.Target != null) g?.Hunt(warlord.Target);
        return g;
    }

    private void CheckFoothills(int group)
    {
        var gs = GameState.Instance;
        if (group == WarlordGroup)
        {
            if (Enemies.Values.Any(e => e.Group == group && e is Bandit { Warlord: true } && e.Current != Enemy.State.Dead)) return;
            _warlordTent.Cleared = true;
            _warlordTent.RespawnDay = gs.Day + WarlordRespawnDays;
            WarlordPeaceUntil = gs.Day + WarlordPeaceDays;
            Net.Instance.ToastAll($"The Bandit Warlord is dead. With nobody to send them, the raids stop: no raiders for {WarlordPeaceDays} days.");
            Journal.Fact("warlord_slain");
            return;
        }
        if (Enemies.Values.Any(e => e.Group == group && e.Current != Enemy.State.Dead)) return;
        int den = group - AshWolfGroupBase, herd = group - AshBoarGroupBase;
        if (group == FortGroup)
        {
            _fort.Cleared = true;
            _fort.RespawnDay = gs.Day + FortRespawnDays;
            Net.Instance.ToastAll("The fort's guard is down. Only the Warlord's tent is left.");
            Journal.Fact("fort_cleared");
        }
        else if (den >= 0 && den < _ashDens.Length)
        {
            _ashDens[den].Cleared = true;
            _ashDens[den].RespawnDay = gs.Day + AshWolfRespawnDays;
            Net.Instance.ToastAll("That ash wolf den is empty. For now.");
        }
        else if (herd >= 0 && herd < _ashHerds.Length)
        {
            _ashHerds[herd].Cleared = true;
            _ashHerds[herd].RespawnDay = gs.Day + AshBoarRespawnDays;
        }
    }

    private bool FoothillsAnchor(int group, out Vector3 at)
    {
        int den = group - AshWolfGroupBase, herd = group - AshBoarGroupBase;
        if (den >= 0 && den < _ashDens.Length) { at = _ashDens[den].Center; return true; }
        if (herd >= 0 && herd < _ashHerds.Length) { at = _ashHerds[herd].Center; return true; }
        if (group == FortGroup) { at = _fort.Center; return true; }
        if (group == WarlordGroup) { at = _warlordTent.Center; return true; }
        at = default;
        return false;
    }

    private void SaveFoothills(GDict d)
    {
        d["ash_dens"] = SaveSites(_ashDens);
        d["ash_herds"] = SaveSites(_ashHerds);
        d["fort"] = SaveSites(new[] { _fort });
        d["warlord"] = SaveSites(new[] { _warlordTent });
        d["peace_until"] = WarlordPeaceUntil;
    }

    private void LoadFoothills(GDict d)
    {
        LoadSites(_ashDens, d != null && d.TryGetValue("ash_dens", out var a) ? a.AsGodotArray() : null);
        LoadSites(_ashHerds, d != null && d.TryGetValue("ash_herds", out var h) ? h.AsGodotArray() : null);
        LoadSites(new[] { _fort }, d != null && d.TryGetValue("fort", out var f) ? f.AsGodotArray() : null);
        LoadSites(new[] { _warlordTent }, d != null && d.TryGetValue("warlord", out var w) ? w.AsGodotArray() : null);
        WarlordPeaceUntil = d != null && d.TryGetValue("peace_until", out var p) ? p.AsInt32() : 0;
    }
}
