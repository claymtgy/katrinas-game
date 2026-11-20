using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GDict = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;

namespace KatrinasGame;

/// <summary>Where the player should go next: a chain of goals that walks through the game
/// (gather → stash → workbench → a weapon → the first night → Ashby → … → the cottage), a few
/// side goals, and errands from the Ashby trader for coin.
///
/// The world owns it, so co-op players share it. The server decides when a goal is done (from
/// what it can see: buildings, players, the stockpile, and "facts" other systems report with
/// <see cref="Fact"/>, such as "deposited" or "slept"), saves it with the world, and sends it to
/// everyone (Net.C_Journal). Clients show it: the tracker under the clock, a marker on the
/// screen, the Journal tab of the character sheet (J), and a banner when a goal is done.</summary>
public partial class Journal : Node
{
    public static Journal Instance { get; private set; }

    /// <summary>A goal. Done runs on the server; Progress and Target run on each screen and only
    /// read what clients mirror (the stockpile, your pack, buildings, the home tier).</summary>
    public record Goal(string Id, string Title, string Hint, bool Side, Func<Journal, bool> Done,
        Func<string> Progress = null, Func<Vector3?> Target = null);

    public record Errand(string Item, int Count, int Reward);

    private static readonly Errand[] ErrandPool =
    {
        new("wood", 15, 12), new("stone", 12, 12), new("wolf_pelt", 3, 20), new("wheat", 6, 14), new("turnip", 6, 14),
        new("bread", 2, 18), new("harpy_feather", 3, 24), new("water", 4, 8), new("roast_turnip", 2, 16),
    };

    public readonly HashSet<string> Completed = new();
    public readonly HashSet<string> Facts = new();
    public int ErrandsDone { get; private set; }
    public Errand CurrentErrand { get; private set; } = ErrandPool[0];

    /// <summary>Anything changed (redraw the tracker and the journal).</summary>
    public event Action Changed;
    /// <summary>A goal was just finished (the banner).</summary>
    public event Action<Goal> GoalCompleted;

    private double _nextCheck;
    private readonly RandomNumberGenerator _rng = new();

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _rng.Randomize();
    }

    // ================================================================== the goals

    private static GameState Gs => GameState.Instance;

    public static readonly Goal[] Goals =
    {
        new("gather", "Gather wood and stone",
            "Strike trees and rocks ({attack}). What you gather rides in your pack.",
            false, j => j.Facts.Contains("deposited") || Gs.PlacedBuildings.Count > 0 || (Total("wood") >= 8 && Total("stone") >= 4),
            () => $"Wood {Mine("wood")}/8 · Stone {Mine("stone")}/4"),
        new("stash", "Empty your pack into the chest",
            "Use the chest by the shack ({interact}). It moves your pack into the shared stockpile, which building and crafting draw on.",
            false, j => j.Facts.Contains("deposited"), null, () => Landmark("HomeChest")),
        new("workbench", "Build a Workbench",
            "Open build mode ({build_mode}), choose the Workbench ({next_build}) and place it ({place}) near the shack.",
            false, j => Built("workbench"), null, () => Landmark("HomeChest")),
        new("weapon", "Craft a weapon",
            "Use the workbench ({interact}) to make a wooden sword or a stone club. Switch weapons with {weapons}.",
            false, j => j.Facts.Contains("crafted_weapon") || HasWeapon(), null, () => BuildingPos("workbench")),
        new("sleep", "Sleep through a night",
            "Wolves hunt after dark. When night falls, sleep in the bed in the shack ({interact}) to skip to morning.",
            false, j => j.Facts.Contains("slept"), null, () => Landmark("Bed")),
        new("ashby", "Follow the road south to Ashby",
            "The dirt road leads south through the gap in the wall. Ashby is safe ground.",
            false, j => AnyPlayer(p => p.GlobalPosition.DistanceTo(HomeCliffs.AshbyCenter) < 32f), null, () => HomeCliffs.AshbyCenter),
        new("trade", "Trade with Marta in Ashby",
            "Sell what you don't need and buy seeds. She has errands that pay coin.",
            false, j => j.Facts.Contains("traded") || j.ErrandsDone > 0, null, () => Landmark("Trader")),
        new("camp", "Clear the bandit camp",
            "It's east of the Ashby road. Bring a weapon and food ({eat} to eat); block ({block}) as they strike to parry.",
            false, j => j.Facts.Contains("camp_cleared") || (Wildlife.Instance?.CampCleared(0) ?? false), null, () => Wildlife.CampSites[0]),
        new("farm", "Grow a crop",
            "Build a wheat or turnip plot, plant seeds ({interact}), water it (draw water at the well or the pool), then harvest it when it's ripe.",
            false, j => j.Facts.Contains("harvested"), null, () => BuildingPos("wheat_plot") ?? BuildingPos("turnip_plot")),
        new("armour", "Craft armour and wear it",
            "Wolf pelts make leather at the workbench. Open your character sheet ({inventory}) and choose a piece to put it on.",
            false, j => AnyPlayer(p => p.Worn.Count > 0)),
        new("watchtower", "Build a watchtower",
            "From day 3 raiders come at dusk. A watchtower shoots at them; palisades slow them down.",
            false, j => Built("watchtower")),
        new("falls", "Cross the pass to Falls Valley",
            "East of home, past the cliffs. The valley has a river, harpies and the Great Falls.",
            false, j => AnyPlayer(p => p.GlobalPosition.X > HomeCliffs.PassPos.X + 8f), null, () => HomeCliffs.PassPos),
        new("mesa", "Climb the mesa to the Falls Blossom",
            "Walk into pale rock to climb ({jump} leaps off). Pray at the shrine on top ({interact}).",
            false, j => j.Facts.Contains("prayed:FallsShrine"), null, () => HomeCliffs.FallsShrinePos),
        new("enchant", "Enchant a piece of armour",
            "Build a Rune Altar. Each enchantment costs rune shards (raiders and harpies drop them) and coins.",
            false, j => j.Facts.Contains("enchanted"), null, () => BuildingPos(Gear.AltarStation)),
        new("cottage", "Build the cottage",
            "Buy the blueprint in Ashby, gather the materials, then use the board by the shack.",
            false, j => Gs.HomeTier >= 2, null, () => HomeUpgradeBoard.CottageCenter),
        new("forest", "Take the Forest Road to the Hollow Mine",
            "West out of Ashby, through the gap in the vale wall and the trees. Wild boars graze there: leave them be, or take their hides.",
            false, j => AnyPlayer(p => p.GlobalPosition.X < HomeCliffs.MineMouth.X + 6f), null, () => HomeCliffs.MineMouth),
        new("light", "Bring light for the mine",
            "Craft a Lantern at the workbench and wear it as your charm, or build torches (they go up anywhere). Cave crawlers keep out of the light.",
            false, j => j.Facts.Contains("crafted:lantern") || Built("torch") || Built("wall_torch")),
        new("mineblossom", "Find the Mine Blossom",
            "Deep in the mine, daylight falls down a collapsed shaft. Pray at the blossom there ({interact}).",
            false, j => j.Facts.Contains("prayed:MineShrine"), null, () => HomeCliffs.MineShaft),
        new("bronze", "Smelt bronze",
            "Mine copper and tin in the mine, build a Smelter at home, and melt 3 copper and 1 tin with charcoal into bronze.",
            false, j => j.Facts.Contains("crafted:bronze_ingot"), null, () => BuildingPos("smelter")),
        new("mother", "Slay the Crawler Mother",
            "Her lair is at the bottom of the mine, north of the deep gallery. Bronze armour from the anvil will help; she calls her brood as she fights.",
            false, j => j.Facts.Contains("mother_slain"), null, () => Wildlife.NestSites[Wildlife.LairNest]),

        new("glider", "Craft a glider and ride an updraft",
            "Made at the workbench. Jump, then press {jump} in the air to open it.",
            true, j => j.Facts.Contains("crafted:glider")),
        new("roost", "Silence the harpy roost",
            "Harpies nest on a spire in Falls Valley. They drop feathers and rune shards.",
            true, j => j.Facts.Contains("roost_cleared") || (Wildlife.Instance?.RoostCleared(0) ?? false), null, () => Wildlife.RoostSites[0]),
        new("fullset", "Wear armour in every slot",
            "Head, body, hands, feet, cloak and charm.",
            true, j => AnyPlayer(p => p.Worn.Count >= Gear.Slots.Length)),
        new("errands", "Run three errands for Marta",
            "She always has one going: bring her what she asks for.",
            true, j => j.ErrandsDone >= 3, () => $"{Instance?.ErrandsDone ?? 0}/3", () => Landmark("Trader")),
    };

    public static Goal Get(string id) => Goals.FirstOrDefault(g => g.Id == id);

    /// <summary>The goal to work on now: the first main goal not done (null when all are).</summary>
    public Goal Current => Goals.FirstOrDefault(g => !g.Side && !Completed.Contains(g.Id));

    public bool AllMainDone => Current == null;

    // ---------------------------------------------------------------- helpers (shared by goals)

    private static int Total(string item) =>
        Gs.Inventory.Count(item) + Gs.Players.Values.Sum(p => p.Pack.Count(item));

    private static int Mine(string item) =>
        Gs.Inventory.Count(item) + (Gs.LocalPlayer?.Pack.Count(item) ?? 0);

    private static bool Built(string id) => Gs.PlacedBuildings.Any(b => b.Id == id);

    private static Vector3? BuildingPos(string id) => Gs.PlacedBuildings.FirstOrDefault(b => b.Id == id)?.Position;

    private static bool AnyPlayer(Func<PlayerController, bool> test) =>
        Gs.Players.Values.Any(p => GodotObject.IsInstanceValid(p) && test(p));

    private static bool HasWeapon() =>
        Gs.Inventory.Items.Keys.Any(id => ItemDb.Get(id)?.IsWeapon ?? false) || AnyPlayer(p => p.Equipped != "");

    private static readonly Dictionary<string, Node3D> Landmarks = new();

    /// <summary>A named node in the world (the home chest, the bed, the trader).</summary>
    private static Vector3? Landmark(string name)
    {
        if (!Landmarks.TryGetValue(name, out var node) || !GodotObject.IsInstanceValid(node))
        {
            node = Gs.GetTree().CurrentScene?.FindChild(name, true, false) as Node3D;
            if (node == null) return null;
            Landmarks[name] = node;
        }
        return node.GlobalPosition;
    }

    // ================================================================== server

    /// <summary>Server: something happened that goals care about ("deposited", "slept",
    /// "crafted:glider", "prayed:FallsShrine"). Checked on the next tick.</summary>
    public static void Fact(string fact)
    {
        if (Instance == null || !Net.Instance.IsServer) return;
        if (Instance.Facts.Add(fact)) Instance._nextCheck = 0;
    }

    public override void _Process(double delta)
    {
        var net = Net.Instance;
        if (net == null || !net.InSession || !net.IsServer || Gs.Players.Count == 0) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextCheck) return;
        _nextCheck = now + 0.5;
        foreach (var g in Goals)
        {
            if (Completed.Contains(g.Id)) continue;
            bool done;
            try { done = g.Done(this); }
            catch (Exception) { done = false; }
            if (!done) continue;
            Completed.Add(g.Id);
            GD.Print($"[Journal] Goal done: {g.Id}");
            net.BroadcastJournal(g.Id);
            net.ServerAwardXpAll(g.Side ? Levels.SideGoal : Levels.MainGoal, $"goal: {g.Title}");
        }
    }

    /// <summary>Server: a player hands the trader the errand's goods (from their pack).</summary>
    public void ServerHandIn(PlayerController p)
    {
        var e = CurrentErrand;
        var net = Net.Instance;
        if (p.Pack.Count(e.Item) < e.Count)
        {
            net.ToastTo(p.PeerId, $"Marta wants {e.Count} {ItemDb.NameOf(e.Item)}; you carry {p.Pack.Count(e.Item)}.");
            return;
        }
        p.Pack.Remove(e.Item, e.Count);
        p.Pack.Add("coin", e.Reward);
        ErrandsDone++;
        NewErrand();
        net.ToastTo(p.PeerId, $"Errand done: +{e.Reward} coins. Marta has another job for you.");
        net.ServerAwardXp(p, Levels.Errand, "errand for Marta");
        _nextCheck = 0;
        net.BroadcastJournal("");
    }

    private void NewErrand()
    {
        var pool = ErrandPool.Where(e => e != CurrentErrand).ToArray();
        CurrentErrand = pool[_rng.RandiRange(0, pool.Length - 1)];
    }

    // ================================================================== state

    public GDict ToDict() => new()
    {
        ["done"] = new GArray(Completed.Select(s => Variant.From(s))),
        ["facts"] = new GArray(Facts.Select(s => Variant.From(s))),
        ["errands"] = ErrandsDone,
        ["errand"] = new GArray { CurrentErrand.Item, CurrentErrand.Count, CurrentErrand.Reward },
    };

    /// <summary>Load or receive the journal. completed = a goal that was just done (banner), or "".</summary>
    public void Apply(GDict d, string completed = "")
    {
        Completed.Clear();
        Facts.Clear();
        if (d != null)
        {
            if (d.TryGetValue("done", out var done)) foreach (var v in done.AsGodotArray()) Completed.Add(v.AsString());
            if (d.TryGetValue("facts", out var facts)) foreach (var v in facts.AsGodotArray()) Facts.Add(v.AsString());
            ErrandsDone = d.TryGetValue("errands", out var n) ? n.AsInt32() : 0;
            if (d.TryGetValue("errand", out var ev) && ev.AsGodotArray() is { Count: 3 } e && ItemDb.Get(e[0].AsString()) != null)
                CurrentErrand = new Errand(e[0].AsString(), e[1].AsInt32(), e[2].AsInt32());
        }
        Changed?.Invoke();
        var goal = Get(completed);
        if (goal != null) GoalCompleted?.Invoke(goal);
    }

    /// <summary>A new world (or back at the main menu).</summary>
    public void Reset()
    {
        Completed.Clear();
        Facts.Clear();
        ErrandsDone = 0;
        CurrentErrand = ErrandPool[0];
        Landmarks.Clear();
        Changed?.Invoke();
    }
}
