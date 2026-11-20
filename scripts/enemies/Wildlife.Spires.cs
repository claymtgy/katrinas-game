using System.Collections.Generic;
using System.Linq;
using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>The Spire Cliffs (GDD §11, step 2): storm harpy roosts over two spires, cliff goats
/// grazing on two spire tops, and the Storm Harpy Queen's nest on the Tall Spire. Each fills like
/// the other sites: emptied, it comes back after a few days, never while anyone is near.
/// Saved with the west sites (SaveWest).</summary>
public partial class Wildlife
{
    /// <summary>Spire tops storm harpies roost over (the rust and windward spires).</summary>
    public static readonly Vector3[] StormRoostSites = { HomeCliffs.Spires[2].Top, HomeCliffs.Spires[3].Top };
    public const int SoloStormRoost = 2; // a pair (two pairs with more players)
    public const int StormRespawnDays = 1;

    /// <summary>Spires goats graze on (the first one, and the rust spire), and how many for one player.</summary>
    public static readonly int[] GoatSpires = { 0, 2 };
    public static readonly int[] SoloGoats = { 1, 2 };
    public const int GoatRespawnDays = 1;

    /// <summary>The Queen's nest: over the Tall Spire's top.</summary>
    public static Vector3 QueenNest => HomeCliffs.Spires[4].Top;
    public const int QueenRespawnDays = 3;

    private const int StormGroupBase = 60000, GoatGroupBase = 61000, QueenGroupId = 62000;

    private readonly Camp[] _stormRoosts = StormRoostSites.Select(c => new Camp { Center = c }).ToArray();
    private readonly Camp[] _goatHerds = GoatSpires.Select(i => new Camp { Center = HomeCliffs.Spires[i].Top }).ToArray();
    private readonly Camp _queenNest = new() { Center = HomeCliffs.Spires[4].Top };

    public static int StormGroup(int i) => StormGroupBase + i;
    public static int GoatGroup(int i) => GoatGroupBase + i;
    public static int QueenGroup => QueenGroupId;
    public bool QueenCleared => _queenNest.Cleared;
    public IEnumerable<Goat> Goats => Enemies.Values.OfType<Goat>();

    private static bool IsSpireGroup(int group) => group >= StormGroupBase && group <= QueenGroupId;

    /// <summary>A goat's patch: the top of its own spire, a little in from the edge.</summary>
    public static Rect2 GoatBounds(int group)
    {
        int i = group - GoatGroupBase;
        var s = HomeCliffs.Spires[GoatSpires[Mathf.Clamp(i, 0, GoatSpires.Length - 1)]];
        float h = s.Half - 1.2f;
        return new Rect2(s.Top.X - h, s.Top.Z - h, h * 2, h * 2);
    }

    /// <summary>Server, once a second: fill empty roosts, herds and the nest.</summary>
    private void TendSpires()
    {
        var gs = GameState.Instance;
        if (!AutoSpawn || !Net.Instance.Config.Enemies || gs.Players.Count == 0) return;
        for (int i = 0; i < _stormRoosts.Length; i++)
            if (Due(_stormRoosts[i], StormGroup(i), 30f)) SpawnStormRoost(i);
        for (int i = 0; i < _goatHerds.Length; i++)
            if (Due(_goatHerds[i], GoatGroup(i), 20f)) SpawnGoats(i);
        if (Due(_queenNest, QueenGroup, 40f)) SpawnQueen();
    }

    public List<Harpy> SpawnStormRoost(int i)
    {
        var roost = _stormRoosts[i];
        roost.Cleared = false;
        int count = Difficulty.PackSize(SoloStormRoost);
        var list = new List<Harpy>();
        for (int n = 0; n < count; n++)
        {
            float a = Mathf.Tau * n / count;
            var pos = roost.Center + new Vector3(Mathf.Cos(a) * 6f, Harpy.CruiseHeight + n, Mathf.Sin(a) * 6f);
            list.Add((Harpy)Spawn("storm_harpy", StormGroup(i), pos, a, Harpy.StormHealth, Harpy.StormDamage));
        }
        GD.Print($"[Wildlife] Storm roost {i}: {count} storm harpies");
        return list;
    }

    public List<Goat> SpawnGoats(int i)
    {
        _goatHerds[i].Cleared = false;
        int count = Difficulty.PackSize(SoloGoats[i]);
        var list = new List<Goat>();
        for (int n = 0; n < count; n++)
            list.Add((Goat)SpawnAround("goat", GoatGroup(i), _goatHerds[i].Center, n, count, Goat.MaxHealth, Goat.ButtDamage, 2f));
        return list;
    }

    /// <summary>Server: the Queen, circling over her nest (one of her, scaled for co-op like the other bosses).</summary>
    public Harpy SpawnQueen()
    {
        _queenNest.Cleared = false;
        var q = (Harpy)Spawn("harpy_queen", QueenGroup, QueenNest + new Vector3(4, 9, 0), 0f,
            Difficulty.BigMonsterHealth(Harpy.QueenHealth), Difficulty.BigMonsterDamage(Harpy.QueenDamage));
        GD.Print($"[Wildlife] The Storm Harpy Queen ({GameState.Instance.Players.Count} players)");
        return q;
    }

    /// <summary>Server: a storm harpy answers the Queen's call.</summary>
    public Harpy SpawnQueenGuard(Harpy queen)
    {
        var off = new Vector3(_rng.RandfRange(-8, 8), _rng.RandfRange(4, 8), _rng.RandfRange(-8, 8));
        var h = (Harpy)Spawn("storm_harpy", queen.Group, queen.GlobalPosition + off, _rng.RandfRange(0, Mathf.Tau), Harpy.StormHealth, Harpy.StormDamage);
        if (queen.Target != null) h?.Hunt(queen.Target);
        return h;
    }

    private void CheckSpires(int group)
    {
        var gs = GameState.Instance;
        if (group == QueenGroup)
        {
            // The nest is clear once the Queen is down (her guard may linger).
            if (Enemies.Values.Any(e => e.Group == group && e is Harpy { Queen: true } && e.Current != Enemy.State.Dead)) return;
            _queenNest.Cleared = true;
            _queenNest.RespawnDay = gs.Day + QueenRespawnDays;
            Net.Instance.ToastAll("The Storm Harpy Queen falls into the clouds. The spires are quiet… for now.");
            Journal.Fact("queen_cleared");
            return;
        }
        if (Enemies.Values.Any(e => e.Group == group && e.Current != Enemy.State.Dead)) return;
        int storm = group - StormGroupBase, goat = group - GoatGroupBase;
        if (storm >= 0 && storm < _stormRoosts.Length)
        {
            _stormRoosts[storm].Cleared = true;
            _stormRoosts[storm].RespawnDay = gs.Day + StormRespawnDays;
            Net.Instance.ToastAll("The storm harpies over that spire are gone. For now.");
            Journal.Fact("storm_roost_cleared");
        }
        else if (goat >= 0 && goat < _goatHerds.Length)
        {
            _goatHerds[goat].Cleared = true;
            _goatHerds[goat].RespawnDay = gs.Day + GoatRespawnDays;
        }
    }

    private bool SpireAnchor(int group, out Vector3 at)
    {
        int storm = group - StormGroupBase, goat = group - GoatGroupBase;
        if (storm >= 0 && storm < _stormRoosts.Length) { at = _stormRoosts[storm].Center; return true; }
        if (goat >= 0 && goat < _goatHerds.Length) { at = _goatHerds[goat].Center; return true; }
        if (group == QueenGroup) { at = _queenNest.Center; return true; }
        at = default;
        return false;
    }

    private void SaveSpires(GDict d)
    {
        d["storm"] = SaveSites(_stormRoosts);
        d["goats"] = SaveSites(_goatHerds);
        d["queen"] = SaveSites(new[] { _queenNest });
    }

    private void LoadSpires(GDict d)
    {
        LoadSites(_stormRoosts, d != null && d.TryGetValue("storm", out var s) ? s.AsGodotArray() : null);
        LoadSites(_goatHerds, d != null && d.TryGetValue("goats", out var g) ? g.AsGodotArray() : null);
        LoadSites(new[] { _queenNest }, d != null && d.TryGetValue("queen", out var q) ? q.AsGodotArray() : null);
    }
}
