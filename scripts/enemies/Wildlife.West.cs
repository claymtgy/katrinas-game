using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>The Forest Road and the Hollow Mine (GDD §11, step 1): boar herds in the forest
/// clearings and cave crawler nests in the mine, the last of them the Crawler Mother's lair.
/// Each is kept filled like the bandit camp: emptied, it fills again after a few days, but not
/// while anyone is close by.</summary>
public partial class Wildlife
{
    /// <summary>Boar clearings in the forest (the world builder keeps trees out of them).</summary>
    public static readonly Vector3[] HerdSites = { HomeCliffs.InWest(-96, 0, 186), HomeCliffs.InWest(-126, 0, 216) };
    public const int SoloHerdSize = 2;
    public const int HerdRespawnDays = 1;

    /// <summary>Crawler nests: the upper gallery, the deep gallery, and the Mother's lair.</summary>
    public static readonly Vector3[] NestSites = { HomeCliffs.InWest(-184, 0, 201), HomeCliffs.InWest(-228, 0, 244), HomeCliffs.InWest(-224, 0, 192) };
    /// <summary>Crawlers per nest for one player (the lair: the Mother and her guard).</summary>
    public static readonly int[] SoloNestSize = { 2, 3, 2 };
    public const int LairNest = 2;
    public const int NestRespawnDays = 2;

    private const int HerdGroupBase = 40000, NestGroupBase = 50000;

    private readonly Camp[] _herds = HerdSites.Select(c => new Camp { Center = c }).ToArray();
    private readonly Camp[] _nests = NestSites.Select(c => new Camp { Center = c }).ToArray();

    public IEnumerable<Boar> Boars => Enemies.Values.OfType<Boar>();
    public IEnumerable<Crawler> Crawlers => Enemies.Values.OfType<Crawler>();

    public static int HerdGroup(int i) => HerdGroupBase + i;
    public static int NestGroup(int i) => NestGroupBase + i;
    public bool NestCleared(int i) => _nests[i].Cleared;

    private static bool IsWestGroup(int group) => group >= HerdGroupBase && group < NestGroupBase + 1000;

    /// <summary>Server, once a second: fill empty herds and nests.</summary>
    private void TendWest()
    {
        var gs = GameState.Instance;
        if (!AutoSpawn || !Net.Instance.Config.Enemies || gs.Players.Count == 0) return;
        for (int i = 0; i < _herds.Length; i++)
            if (Due(_herds[i], HerdGroup(i), 30f)) SpawnHerd(i);
        for (int i = 0; i < _nests.Length; i++)
            if (Due(_nests[i], NestGroup(i), 24f)) SpawnNest(i);
    }

    private bool Due(Camp site, int group, float clearRadius)
    {
        var gs = GameState.Instance;
        if (Enemies.Values.Any(e => e.Group == group)) return false;
        if (site.Cleared && gs.Day < site.RespawnDay) return false;
        return !gs.Players.Values.Any(p => p.GlobalPosition.DistanceTo(site.Center) < clearRadius);
    }

    public List<Boar> SpawnHerd(int i)
    {
        _herds[i].Cleared = false;
        int count = Difficulty.PackSize(SoloHerdSize);
        var list = new List<Boar>();
        for (int n = 0; n < count; n++)
            list.Add((Boar)SpawnAround("boar", HerdGroup(i), _herds[i].Center, n, count, Boar.MaxHealth, Boar.GoreDamage, 3f));
        return list;
    }

    /// <summary>Server: fill a nest. The lair gets the Mother (scaled like the bandit captain) and her guard.</summary>
    public List<Crawler> SpawnNest(int i)
    {
        var nest = _nests[i];
        nest.Cleared = false;
        int count = Difficulty.PackSize(SoloNestSize[i]);
        var list = new List<Crawler>();
        for (int n = 0; n < count; n++)
            list.Add((Crawler)SpawnAround("crawler", NestGroup(i), nest.Center, n, count, Crawler.MaxHealth, Crawler.BiteDamage, 4f));
        if (i == LairNest)
            list.Add((Crawler)Spawn("crawler_mother", NestGroup(i), nest.Center + new Vector3(-6, 0.2f, -3), 0.6f,
                Difficulty.BigMonsterHealth(Crawler.MotherHealth), Difficulty.BigMonsterDamage(Crawler.MotherDamage)));
        GD.Print($"[Wildlife] Crawler nest {i}: {list.Count} crawlers ({GameState.Instance.Players.Count} players)");
        return list;
    }

    /// <summary>Server: one of the Mother's brood joins her in the fight.</summary>
    public Crawler SpawnBrood(Crawler mother)
    {
        var off = new Vector3(_rng.RandfRange(-3, 3), 0.2f, _rng.RandfRange(-3, 3));
        var c = (Crawler)Spawn("crawler", mother.Group, mother.GlobalPosition + off, _rng.RandfRange(0, Mathf.Tau), Crawler.BroodHealth, Crawler.BiteDamage);
        if (mother.Target != null) c?.Hunt(mother.Target);
        return c;
    }

    private void CheckWest(int group)
    {
        if (Enemies.Values.Any(e => e.Group == group && e.Current != Enemy.State.Dead)) return;
        int herd = group - HerdGroupBase, nest = group - NestGroupBase;
        if (herd >= 0 && herd < _herds.Length)
        {
            _herds[herd].Cleared = true;
            _herds[herd].RespawnDay = GameState.Instance.Day + HerdRespawnDays;
        }
        else if (nest >= 0 && nest < _nests.Length)
        {
            _nests[nest].Cleared = true;
            _nests[nest].RespawnDay = GameState.Instance.Day + NestRespawnDays;
            Net.Instance.ToastAll(nest == LairNest
                ? "The Crawler Mother's lair falls silent. The mine is yours… for now."
                : "The skittering in that gallery stops. For now.");
            Journal.Fact(nest == LairNest ? "lair_cleared" : $"nest_cleared:{nest}");
        }
    }

    private bool WestAnchor(int group, out Vector3 at)
    {
        int herd = group - HerdGroupBase, nest = group - NestGroupBase;
        if (herd >= 0 && herd < _herds.Length) { at = _herds[herd].Center; return true; }
        if (nest >= 0 && nest < _nests.Length) { at = _nests[nest].Center; return true; }
        at = default;
        return false;
    }

    public GDict SaveWest()
    {
        var d = new GDict { ["herds"] = SaveSites(_herds), ["nests"] = SaveSites(_nests) };
        SaveSpires(d); // the spire sites ride along (Wildlife.Spires.cs)
        return d;
    }

    public void LoadWest(GDict d)
    {
        LoadSites(_herds, d != null && d.TryGetValue("herds", out var h) ? h.AsGodotArray() : null);
        LoadSites(_nests, d != null && d.TryGetValue("nests", out var n) ? n.AsGodotArray() : null);
        LoadSpires(d);
    }

    private static GArray SaveSites(Camp[] sites)
    {
        var list = new GArray();
        foreach (var c in sites) list.Add(new GDict { ["cleared"] = c.Cleared, ["respawn_day"] = c.RespawnDay });
        return list;
    }

    private static void LoadSites(Camp[] sites, GArray list)
    {
        for (int i = 0; i < sites.Length; i++)
        {
            var d = list != null && i < list.Count ? list[i].AsGodotDictionary() : null;
            sites[i].Cleared = d != null && d["cleared"].AsBool();
            sites[i].RespawnDay = d != null ? d["respawn_day"].AsInt32() : 0;
        }
    }
}
