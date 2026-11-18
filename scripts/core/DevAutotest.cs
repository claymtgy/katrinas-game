using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KatrinasGame;

/// <summary>Scripted smoke test of the single-player loop. Runs only when launched with
///   godot --path . -- --autotest [--shots=C:/some/dir]
/// (Single player runs through the same server code path as co-op; see NetAutotest for two players.)
/// It walks the acceptance checklist, saves screenshots, prints PASS/FAIL lines and quits.</summary>
public partial class DevAutotest : Node
{
    private string _shots;
    private int _fails;

    public static void MaybeStart(Node host)
    {
        var args = OS.GetCmdlineUserArgs();
        if (!args.Contains("--autotest")) return;
        var t = new DevAutotest
        {
            _shots = args.FirstOrDefault(a => a.StartsWith("--shots="))?.Substring("--shots=".Length),
            ProcessMode = ProcessModeEnum.Always,
        };
        host.CallDeferred(Node.MethodName.AddChild, t);
    }

    public override void _Ready() => _ = Run();

    private async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async Task<bool> Until(System.Func<bool> condition, double timeout)
    {
        for (double t = 0; t < timeout; t += 0.1)
        {
            if (condition()) return true;
            await Wait(0.1);
        }
        return condition();
    }

    /// <summary>Turn the camera to face a point (for swinging at moving things).</summary>
    private static void Face(PlayerController p, Vector3 at)
    {
        var d = at - p.GlobalPosition;
        p.Rig.SetLook(Mathf.Atan2(-d.X, -d.Z), -0.1f);
    }

    /// <summary>What the local player has: their pack plus the shared stockpile.</summary>
    private static int Have(string id) =>
        GameState.Instance.Inventory.Count(id) + (GameState.Instance.LocalPlayer?.Pack.Count(id) ?? 0);

    private void Check(bool ok, string what)
    {
        GD.Print($"[AUTOTEST] {(ok ? "PASS" : "FAIL")}: {what}");
        if (!ok) _fails++;
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (string.IsNullOrEmpty(_shots)) return;
        DirAccess.MakeDirRecursiveAbsolute(_shots);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng($"{_shots}/{name}.png");
        GD.Print($"[AUTOTEST] screenshot {name}.png");
    }

    /// <summary>Audio (art pass round 2): the ambience bed is running and the game's actions
    /// asked for their sounds along the way.</summary>
    private void Audio()
    {
        var amb = Ambience.Instance;
        Check(amb != null && amb.GetNodeOrNull<AudioStreamPlayer>("amb_wind")?.Playing == true, "the ambience bed (wind) is playing");
        Check(amb != null && amb.Wind > 0.1f, $"wind is audible ({amb?.Wind:0.00})");
        var root = GetTree().CurrentScene;
        Check(root.FindChildren("*", "AudioStreamPlayer3D", true, false).Count(n => ((AudioStreamPlayer3D)n).Stream == Sfx.Get("amb_waterfall")) >= 4, "every waterfall roars");
        Check(root.FindChild("Volcano", true, false)?.GetNodeOrNull("Sound_amb_lava") != null, "the volcano rumbles");
        foreach (var s in new[] { "chop", "mine", "step", "swing_light", "hit_flesh", "build", "eat", "water", "chest", "pickup", "dodge", "parry" })
            Check(Sfx.Played.GetValueOrDefault(s) > 0, $"sound '{s}' played ({Sfx.Played.GetValueOrDefault(s)}x)");
    }

    /// <summary>Wolves (GDD §9, §16): hunt, bite, die to the sword, drop a pelt; packs come at
    /// nightfall and leave at dawn.</summary>
    private async Task Wolves(GameState gs, PlayerController p)
    {
        var wild = Wildlife.Instance;
        Check(Difficulty.PackSize(Wildlife.SoloPackSize) == 2, "solo wolf packs have 2 wolves");
        Check(gs.InSafeZone(gs.WorldSpawn), "the blossom shrine is a safe zone");

        gs.TimeOfDay = 0.9f;
        p.Health.Reset();
        p.Teleport(new Vector3(-12, 0.1f, 30));
        p.Rig.SetView(false, true);
        p.Rig.SetLook(0, -0.1f);
        await Wait(0.3);
        var wolf = wild.SpawnPack(p.GlobalPosition + new Vector3(0, 0, -10), 1)[0];
        await Until(() => wolf.GlobalPosition.DistanceTo(p.GlobalPosition) < 5.5f, 10);
        var toWolf = wolf.GlobalPosition - p.GlobalPosition;
        p.Rig.SetLook(Mathf.Atan2(-toWolf.X, -toWolf.Z) + 0.35f, -0.12f); // off-centre so the hero doesn't hide it
        await Shot("10_wolf");
        {
            // Close-up of the faceted wolf model (art pass), lit by a lantern so it reads at night.
            wolf.Frozen = true;   // hold still for the photo; it settles into its idle
            await Wait(0.8);
            var cam = new Camera3D { Fov = 50f };
            GetTree().CurrentScene.AddChild(cam);
            var side = wolf.GlobalBasis.X.Normalized();
            cam.GlobalPosition = wolf.GlobalPosition + side * 3.2f + new Vector3(0, 1.1f, 0);
            cam.LookAt(wolf.GlobalPosition + Vector3.Up * 0.6f, Vector3.Up);
            var lamp = new OmniLight3D { LightColor = new Color(1f, 0.85f, 0.7f), LightEnergy = 2.5f, OmniRange = 6f };
            cam.AddChild(lamp);
            cam.MakeCurrent();
            await Wait(0.1);
            await Shot("10c_wolf_closeup");
            wolf.StartHowl();
            await Wait(0.7);
            Check(wolf.Howling && Sfx.Played.GetValueOrDefault("wolf_howl") > 0, "a standing wolf howls (pose and sound)");
            await Shot("10d_wolf_howl");
            wolf.Frozen = false;
            cam.QueueFree();
            p.Rig.Camera.MakeCurrent();
        }
        Check(await Until(() => wolf.GlobalPosition.DistanceTo(p.GlobalPosition) < 2.6f, 10), "a wolf hunts the player down");
        Face(p, wolf.GlobalPosition);
        Check(await Until(() => p.Health.Current < p.Health.Max, 8), $"the wolf bites (HP {p.Health.Current})");

        int pelts = Have("wolf_pelt");
        for (int i = 0; i < 24 && !wolf.Health.IsDead; i++)
        {
            p.RestoreStamina();
            Face(p, wolf.GlobalPosition);
            if (wolf.GlobalPosition.DistanceTo(p.GlobalPosition) < 2.3f) p.Combat.TryAttack(false);
            await Wait(0.5);
        }
        Check(wolf.Health.IsDead, "the sword kills the wolf");
        Check(Have("wolf_pelt") == pelts + 1 && p.Pack.Count("wolf_pelt") >= 1, "a dead wolf leaves a pelt in the killer's pack");
        Check(await Until(() => wild.Wolves.Count == 0, 5), "the body is cleared away");

        // Night spawning and dawn retreat.
        Wildlife.AutoSpawn = true;
        gs.TimeOfDay = 0.7f;
        await Wait(0.3);
        gs.TimeOfDay = 0.79f;
        Check(await Until(() => wild.Wolves.Count == 2, 12), $"nightfall brings a pack of 2 ({wild.Wolves.Count})");
        Check(wild.Wolves.Values.All(w => w.GlobalPosition.DistanceTo(p.GlobalPosition) > 20f), "packs appear away from players");
        gs.TimeOfDay = 0.3f;
        Check(await Until(() => wild.Wolves.Count == 0, 16), "at dawn the wolves slink off");
        Wildlife.AutoSpawn = false;
        p.Health.Reset();
    }

    private static Node Find(string name) =>
        ((SceneTree)Engine.GetMainLoop()).CurrentScene.FindChild(name, true, false);

    /// <summary>Block and parry (GDD §9): a raised guard soaks most of a bite; raised just before
    /// the bite lands it's a parry: no damage and the wolf reels.</summary>
    private async Task BlockAndParry(GameState gs, PlayerController p)
    {
        var wild = Wildlife.Instance;
        gs.TimeOfDay = 0.9f;
        p.Health.Reset();
        p.Teleport(new Vector3(-12, 0.1f, 30));
        p.Rig.SetView(true, true);
        await Wait(0.3);
        var wolf = wild.SpawnPack(p.GlobalPosition + new Vector3(0, 0, -8), 1)[0];

        // Guard up well before the bite: blocked.
        p.Combat.HoldGuard = true;
        await Wait(0.4);
        Check(p.Combat.GuardUp && p.Blocking, "holding block raises the guard (server sees it)");
        Check(await Until(() => { Face(p, wolf.GlobalPosition); p.RestoreStamina(); return p.Health.Current < p.Health.Max; }, 12),
            "the wolf bites into the guard");
        float lost = p.Health.Max - p.Health.Current;
        Check(lost > 0f && lost <= Wolf.BiteDamage * Net.BlockedDamageFactor + 0.01f, $"a blocked bite only takes {lost:0.0} HP (of {Wolf.BiteDamage})");
        await Shot("12_guard_up");

        // Parry: drop the guard, raise it as the wolf winds up.
        p.Combat.HoldGuard = false;
        int parries = wild.Parries;
        for (int i = 0; i < 6 && wild.Parries == parries; i++)
        {
            p.Health.Reset();
            await Until(() => { Face(p, wolf.GlobalPosition); return wolf.Current == Enemy.State.Windup; }, 6);
            await Wait(Wolf.BiteWindup - 0.2f);
            p.RestoreStamina();
            p.Combat.HoldGuard = true;
            await Wait(0.4);
            p.Combat.HoldGuard = false;
            await Wait(0.2);
        }
        Check(wild.Parries > parries && p.Combat.Parries > 0, $"timing the guard parries the bite ({wild.Parries - parries} parries)");
        Check(p.Health.Current >= p.Health.Max, "a parried bite does no damage");

        for (int i = 0; i < 24 && !wolf.Health.IsDead; i++)
        {
            p.RestoreStamina();
            p.Health.Reset();
            Face(p, wolf.GlobalPosition);
            if (wolf.GlobalPosition.DistanceTo(p.GlobalPosition) < 2.3f) p.Combat.TryAttack(false);
            await Wait(0.5);
        }
        Check(wolf.Health.IsDead, "the parried wolf is finished off");
        await Until(() => wild.Wolves.Count == 0, 5);
        p.Rig.SetView(false, true);
        p.Health.Reset();
    }

    /// <summary>The bandit camp (GDD §9): grunts plus a captain, coins and the cottage blueprint.</summary>
    private async Task BanditCamp(GameState gs, PlayerController p)
    {
        var wild = Wildlife.Instance;
        var camp = Wildlife.CampSites[0];
        gs.TimeOfDay = 0.45f;
        p.Teleport(camp + new Vector3(-16, 0.1f, 0));
        Face(p, camp);
        await Wait(0.3);
        // While night spawning was on (wolf test) the camp manned itself; start from a fresh camp.
        Check(wild.Bandits.Count() == Wildlife.SoloCampSize + 1, $"the camp manned itself while spawning was on ({wild.Bandits.Count()})");
        foreach (var b in wild.Bandits.ToList()) wild.Despawn(b);
        await Wait(0.2);
        var bandits = wild.SpawnCamp(0);
        Check(bandits.Count == Wildlife.SoloCampSize + 1, $"solo camp: {Wildlife.SoloCampSize} bandits and a captain ({bandits.Count})");
        var captain = bandits.First(b => b.Captain);
        Check(Mathf.IsEqualApprox(captain.Health.Max, Bandit.CaptainHealth) && Mathf.IsEqualApprox(captain.Damage, Bandit.CaptainDamage),
            $"solo captain has base health and damage ({captain.Health.Max}, {captain.Damage})");
        Check(await Until(() => bandits.Any(b => b.Target == p), 10), "the bandits spot the player and charge");
        await Until(() => bandits.Any(b => b.GlobalPosition.DistanceTo(p.GlobalPosition) < 6f), 10);
        var c = bandits.Aggregate(Vector3.Zero, (acc, b) => acc + b.GlobalPosition) / bandits.Count - p.GlobalPosition;
        p.Rig.SetLook(Mathf.Atan2(-c.X, -c.Z) + 0.3f, -0.05f);
        await Shot("13_bandit_camp");

        int coins = Have("coin");
        int hits = wild.Bites;
        for (int i = 0; i < 200 && bandits.Any(b => !b.Health.IsDead); i++)
        {
            p.RestoreStamina();
            if (p.Health.Current < 50) p.Health.Reset();
            var target = bandits.Where(b => !b.Health.IsDead).OrderBy(b => b.GlobalPosition.DistanceTo(p.GlobalPosition)).First();
            Face(p, target.GlobalPosition);
            if (target.GlobalPosition.DistanceTo(p.GlobalPosition) < 2.4f) p.Combat.TryAttack(false);
            else p.Teleport(target.GlobalPosition + (p.GlobalPosition - target.GlobalPosition).Normalized() * 1.8f + Vector3.Up * 0.1f);
            await Wait(0.5);
        }
        Check(bandits.All(b => b.Health.IsDead), "the sword beats the bandits and their captain");
        Check(wild.Bites > hits, "bandits swing back");
        Check(wild.CampCleared(0), "the camp counts as cleared");
        int expected = Bandit.GruntCoins * Wildlife.SoloCampSize + Bandit.CaptainCoins;
        Check(Have("coin") == coins + expected, $"bandits drop coins ({Have("coin") - coins} of {expected})");
        Check(Have("cottage_blueprint") == 1, "the captain carried the cottage blueprint");
        Check(await Until(() => !wild.Bandits.Any(), 8), "the bodies are cleared away");
        p.Health.Reset();
    }

    /// <summary>Ashby (GDD §11): a safe village with a trader who buys and sells for coins.</summary>
    private async Task Ashby(GameState gs, PlayerController p)
    {
        var center = HomeCliffs.AshbyCenter;
        Check(gs.InSafeZone(center + new Vector3(10, 0, 10)), "Ashby is a safe zone");
        var wolf = Wildlife.Instance.SpawnPack(center + new Vector3(0, 0, -34), 1)[0];
        p.Teleport(center + new Vector3(0, 0.1f, -20));
        await Wait(0.2);
        Check(!wolf.CanHunt(p), "wolves won't hunt a player inside the village");
        Wildlife.Instance.Despawn(wolf);

        // Overview from the road.
        gs.TimeOfDay = 0.42f;
        p.Teleport(center + new Vector3(2, 0.1f, -30));
        p.Rig.SetLook(Mathf.Pi + 0.1f, -0.02f);
        await Wait(0.8);
        await Shot("14_ashby");

        var trader = Find("Trader") as Trader;
        Check(trader != null, "Marta the trader is in Ashby");
        if (trader == null) return;
        p.Teleport(trader.GlobalPosition + new Vector3(0.6f, 0.1f, -2.6f));
        Face(p, trader.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw, -0.05f);
        await Wait(0.5);
        await Shot("15_trader_npc");
        p.Rig.SetView(true, true); // aim with the first-person crosshair
        Face(p, trader.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw, -0.3f);
        await Wait(0.3);
        Check(p.Interactor.Target == trader, $"looking at Marta offers a trade ({p.Interactor.Target?.InteractPrompt})");
        p.Rig.SetView(false, true);
        trader.Interact(p);
        await Wait(0.3);
        await Shot("16_trade_menu");

        // Sell a pelt, then buy nails and seeds.
        Check(!Supplies.AtHome(p), "Ashby is away from home (the stockpile is out of reach)");
        gs.Inventory.Add("wolf_pelt", 5);
        Net.Instance.RequestTrade(trader, "wolf_pelt", buy: false);
        Check(gs.Inventory.Count("wolf_pelt") == 5, "can't sell what's in the stockpile at home");
        gs.Inventory.Remove("wolf_pelt", 5);
        p.Pack.Add("wolf_pelt", 1);
        int coins = Have("coin"), pelts = Have("wolf_pelt");
        Net.Instance.RequestTrade(trader, "wolf_pelt", buy: false);
        Check(Have("coin") == coins + 8 && Have("wolf_pelt") == pelts - 1, "sold a wolf pelt for 8 coins");
        for (int i = 0; i < 6; i++) Net.Instance.RequestTrade(trader, "iron_nails", buy: true);
        Net.Instance.RequestTrade(trader, "turnip_seeds", buy: true);
        Check(p.Pack.Count("iron_nails") == 6 && p.Pack.Count("turnip_seeds") == 1, "bought 6 iron nails and turnip seeds (into the pack)");
        Check(Have("coin") == coins + 8 - 6 * 5 - 3, $"coins paid for them ({Have("coin")} left)");
        int left = Have("coin");
        for (int i = 0; i < 30; i++) Net.Instance.RequestTrade(trader, "iron_nails", buy: true);
        Check(Have("coin") >= 0 && Have("iron_nails") == 6 + left / 5, "can't buy without the coins");
        p.Pack.Remove("iron_nails", left / 5);
        Hud.Instance.CloseMenus();
    }

    /// <summary>Farming and cooking (GDD §10): plots, seeds, growth over game time, harvest,
    /// campfire recipes, eating.</summary>
    private async Task Farming(GameState gs, PlayerController p)
    {
        // Wild grain gives wheat seeds.
        var grain = GetTree().CurrentScene.FindChildren("Res_*", "", true, false).OfType<Harvestable>()
            .Where(h => h.DropItemId == "wheat_seeds").OrderBy(h => h.GlobalPosition.DistanceTo(new Vector3(-20, 0, 8))).First();
        p.Teleport(grain.GlobalPosition + new Vector3(0, 0.1f, 1.4f));
        Face(p, grain.GlobalPosition);
        p.Rig.SetLook(0, -0.35f);
        await Wait(0.3);
        int seeds = Have("wheat_seeds");
        for (int i = 0; i < 8 && Have("wheat_seeds") < seeds + 2; i++)
        {
            p.RestoreStamina();
            p.Combat.TryAttack(false);
            await Wait(0.5);
        }
        Check(Have("wheat_seeds") >= seeds + 2, $"cutting wild grain gives wheat seeds ({Have("wheat_seeds")})");

        // Build a wheat plot and a turnip plot in the yard.
        gs.Inventory.Add("wood", 10);
        gs.Inventory.Add("stone", 4);
        gs.TimeOfDay = 0.35f;
        p.Teleport(new Vector3(0, 0.1f, -10));
        p.Rig.SetView(true, true);
        p.Rig.SetLook(Mathf.Pi / 2f, -0.6f);
        await Wait(0.3);
        var plots = new List<FarmPlot>();
        foreach (var id in new[] { "wheat_plot", "turnip_plot" })
        {
            p.Build.Select(id);
            p.Build.SetActive(true);
            await Wait(0.4);
            int n = gs.PlacedBuildings.Count;
            p.Build.TryPlace();
            p.Build.SetActive(false);
            await Wait(0.2);
            Check(gs.PlacedBuildings.Count == n + 1 && gs.PlacedBuildings.Last().Id == id, $"built a {ItemDb.GetBuilding(id).DisplayName} ('{p.Build.InvalidReason}')");
            if (gs.PlacedBuildings.Last().Node is FarmPlot fp) plots.Add(fp);
            p.Teleport(p.GlobalPosition + new Vector3(0, 0, 2.6f));
            await Wait(0.2);
        }
        if (plots.Count < 2) return;
        var (wheat, turnip) = (plots[0], plots[1]);

        // Plant: costs a seed each.
        p.Teleport(wheat.GlobalPosition + new Vector3(0, 0.1f, 1.8f));
        await Wait(0.2);
        int ws = Have("wheat_seeds"), ts = Have("turnip_seeds");
        wheat.Interact(p);
        turnip.Interact(p);
        Check(wheat.Planted && turnip.Planted && !wheat.Ripe, "planted wheat and turnips");
        Check(Have("wheat_seeds") == ws - 1 && Have("turnip_seeds") == ts - 1, "planting uses a seed each");

        // Dry soil: nothing grows until it's watered.
        gs.TimeOfDay = 0.42f;
        await Wait(0.2);
        Check(!wheat.Wet && wheat.Growth == 0f, $"a dry plot doesn't grow ({wheat.Growth * 100:0}%)");
        wheat.Interact(p);
        Check(!wheat.Wet, "can't water without Water");

        // Fetch water from the pool under the west waterfall, then water both plots.
        var pool = Find("PoolWater") as WaterSource;
        p.Teleport(pool.GlobalPosition + new Vector3(1.4f, 0.1f, 0));
        Face(p, pool.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw, -0.5f);
        await Wait(0.3);
        pool.Interact(p);
        Check(p.Pack.Count("water") == pool.Draw, $"drew {pool.Draw} Water at the pool");
        p.Teleport(wheat.GlobalPosition + new Vector3(0, 0.1f, 1.8f));
        await Wait(0.2);
        wheat.Interact(p);
        turnip.Interact(p);
        Check(wheat.Wet && turnip.Wet && p.Pack.Count("water") == pool.Draw - 2, "watered both plots (a Water each)");

        p.Rig.SetView(false, true);
        p.Teleport(wheat.GlobalPosition + new Vector3(3, 0.1f, 3.5f));
        Face(p, wheat.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw + 0.3f, -0.3f);
        gs.TimeOfDay = 0.57f; // a few hours later
        await Wait(0.5);
        Check(wheat.Growth > 0.2f && wheat.Growth < 1f, $"watered crops grow with the clock ({wheat.Growth * 100:0}%)");
        await Shot("17_farm_growing");

        // Saved and loaded with the world.
        gs.SaveGame(silent: true);
        gs.LoadGame(silent: true);
        await Wait(0.3);
        wheat = gs.PlacedBuildings.Select(b => b.Node).OfType<FarmPlot>().First(f => f.SeedItem == "wheat_seeds");
        turnip = gs.PlacedBuildings.Select(b => b.Node).OfType<FarmPlot>().First(f => f.SeedItem == "turnip_seeds");
        Check(wheat.Planted && turnip.Planted, "planted crops survive save / load");

        // Sleep through the night: ripe by morning.
        var bed = Find("Bed") as Bed;
        p.Teleport(bed.GlobalPosition + new Vector3(0.9f, 0, 0.5f));
        await Wait(0.2);
        bed.Interact(p);
        await Wait(0.3);
        Check(wheat.Ripe && turnip.Ripe, "after a night's sleep the crops are ripe");
        gs.TimeOfDay = 0.4f;
        p.Teleport(wheat.GlobalPosition + new Vector3(3, 0.1f, 3.5f));
        Face(p, wheat.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw + 0.3f, -0.3f);
        await Wait(0.5);
        await Shot("18_farm_ripe");
        p.Teleport(wheat.GlobalPosition + new Vector3(0, 0.1f, 1.8f));
        int wh = Have("wheat"), tu = Have("turnip");
        wheat.Interact(p);
        turnip.Interact(p);
        Check(Have("wheat") == wh + 3 && Have("turnip") == tu + 2, "harvest: 3 wheat and 2 turnips");
        Check(!wheat.Planted && Have("wheat_seeds") >= 2, "harvesting clears the plot and gives seed back");

        // Rain waters everything.
        Check(!wheat.Wet, "by now the soil has dried out");
        wheat.Interact(p);
        Check(wheat.Planted && !wheat.Wet, "replanted the wheat (dry)");
        Weather.Instance.SetRain(true);
        await Wait(0.3);
        Check(gs.Raining && wheat.Wet && turnip.Wet, "rain waters every plot");
        await Wait(3.0);
        await Shot("17b_rain");
        Weather.Instance.SetRain(false);
        Check(!gs.Raining, "the shower passes");

        // Cook at the ledge campfire, then eat.
        var fire = Find("LedgeCampfire") as Campfire;
        p.Teleport(fire.GlobalPosition + new Vector3(1.6f, 0.1f, 0));
        await Wait(0.2);
        fire.Interact(p);
        await Wait(0.3);
        await Shot("19_cooking");
        foreach (var r in fire.Recipes) fire.RequestCraft(r);
        Hud.Instance.CloseMenus();
        Check(Have("bread") == 1 && Have("roast_turnip") == 1, "baked bread and roasted a turnip at the campfire");
        p.Health.TakeDamage(30, null);
        Net.Instance.RequestEat();
        Check(p.Health.Current >= p.Health.Max - 0.01f && Have("bread") == 0, "eating bread (F) heals");
        Check(p.BuffAmount(PlayerController.BuffStamina) > 0.4f, "bread gives a stamina-regen buff");
        Net.Instance.RequestEat();
        Check(Have("roast_turnip") == 0 && Mathf.IsEqualApprox(p.Health.Max, p.BaseMaxHealth + 25f),
            $"at full health a roast turnip is still worth it: +25 max health ({p.Health.Max})");
        Net.Instance.RequestEat();
        Check(Have("roast_turnip") == 0, "nothing more to gain: no eating");
        p.ApplyBuff(PlayerController.BuffVigor, 0f, 0f); // (test) let it wear off
        Check(Mathf.IsEqualApprox(p.Health.Max, p.BaseMaxHealth) && p.Health.Current <= p.Health.Max, "when the buff wears off max health goes back");
        p.ClearBuffs();
    }

    /// <summary>Carried packs (GDD §16): gathered things go in your pack until you empty it into
    /// a storage chest at home; a pack holds 40; a defeat costs half of it.</summary>
    private async Task Packs(GameState gs, PlayerController p)
    {
        Check(p.Pack.Load > 0, $"gathered things are in the pack (pack {p.Pack.Load})");
        var chest = Find("HomeChest") as StorageChest;
        Check(chest != null, "a storage chest stands by the shack");
        p.Teleport(chest.GlobalPosition + new Vector3(0.3f, 0.1f, 2.4f));
        Face(p, chest.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw + 0.25f, -0.25f);
        await Wait(0.4);
        await Shot("03b_storage_chest");
        p.Rig.SetView(true, true);
        p.Teleport(chest.GlobalPosition + new Vector3(0, 0.1f, 1.6f));
        Face(p, chest.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw, -0.6f);
        await Wait(0.3);
        Check(p.Interactor.Target == chest, $"looking at the chest offers it ({p.Interactor.Target?.InteractPrompt})");
        p.Rig.SetView(false, true);
        int carried = p.Pack.Load, wood = Have("wood");
        chest.Interact(p);
        Check(p.Pack.Load == 0 && gs.Inventory.Count("wood") == wood, $"emptied {carried} items into the stockpile");

        // A full pack refuses more.
        p.Pack.Add("stone", Supplies.PackCapacity);
        Check(Supplies.Give(p, "wood", 3) == 0 && p.Pack.Load == Supplies.PackCapacity, "a full pack (40) takes no more");
        Check(Supplies.Give(p, "coin", 5) == 5, "coins weigh nothing");

        // Building at home pays from the pack first, then the stockpile.
        gs.Inventory.Add("wood", 2);
        wood = gs.Inventory.Count("wood");
        Check(Supplies.TryConsume(p, new System.Collections.Generic.Dictionary<string, int> { ["stone"] = 2, ["wood"] = 1 })
              && p.Pack.Count("stone") == Supplies.PackCapacity - 2 && gs.Inventory.Count("wood") == wood - 1, "costs come out of the pack first, then the stockpile");
        gs.Inventory.Add("wood", 1);

        // Defeat: half the pack is lost.
        p.Pack.Clear();
        p.Pack.Add("stone", 10);
        p.Pack.Add("wood", 3);
        p.Health.TakeDamage(1000, null);
        await Wait(0.3);
        Check(p.Pack.Count("stone") == 5 && p.Pack.Count("wood") == 2, $"a defeat drops half the pack (stone {p.Pack.Count("stone")}, wood {p.Pack.Count("wood")})");
        Check(!p.Health.IsDead, "and you're back at the blossom tree");
        p.Pack.Clear();
        await Wait(0.3);
    }

    /// <summary>Dodge roll: a burst of speed, costs stamina, and hits during it miss.</summary>
    private async Task Dodge(GameState gs, PlayerController p)
    {
        p.Teleport(new Vector3(-12, 0.1f, 30));
        p.Rig.SetView(false, true);
        p.Rig.SetLook(0, -0.1f);
        p.RestoreStamina();
        p.Health.Reset();
        await Wait(0.4);
        var start = p.GlobalPosition;
        Check(p.TryDodge(new Vector2(1, 0)), "roll to the right");
        Check(p.Stamina < p.MaxStamina - p.DodgeCost + 1f, "a roll costs stamina");
        var hit = Net.Instance.ServerDamagePlayer(p, 20f, Find("TrainingDummy"));
        Check(hit == Net.HitResult.Dodged && p.Health.Current >= p.Health.Max, "a blow mid-roll misses");
        await Wait(0.15);
        await Shot("10b_dodge");
        await Wait(0.5);
        float moved = new Vector2(p.GlobalPosition.X - start.X, p.GlobalPosition.Z - start.Z).Length();
        Check(moved > 2.2f && p.GlobalPosition.X > start.X, $"the roll carries you sideways ({moved:0.0} m)");
        Check(Net.Instance.ServerDamagePlayer(p, 5f, Find("TrainingDummy")) == Net.HitResult.Hit, "after the roll you can be hit again");
        p.Health.Reset();
        p.RestoreStamina();
    }

    /// <summary>Enemies path around obstacles on a navmesh baked from the world.</summary>
    private async Task Navigation(GameState gs, PlayerController p)
    {
        var nav = WorldNav.Instance;
        Check(nav != null && await Until(() => nav.Ready, 30), "the enemy navmesh is baked");
        if (nav == null || !nav.Ready) return;
        var tree = Find("Res_1") as Node3D; // (Res_0 was chopped down earlier)
        var a = tree.GlobalPosition + new Vector3(0, 0, -3f);
        var b = tree.GlobalPosition + new Vector3(0, 0, 3f);
        var path = nav.Path(a, b);
        float length = 0;
        for (int i = 1; i < path.Length; i++) length += path[i].DistanceTo(path[i - 1]);
        Check(path.Length > 2 && length > 6.1f && path[^1].DistanceTo(b) < 1f, $"a path goes around a tree ({path.Length} points, {length:0.0} m vs 6 m straight)");

        // A building added later is walked around too, after a rebake.
        int bakes = nav.Bakes;
        gs.Inventory.Add("wood", 8);
        var wallSpot = tree.GlobalPosition + new Vector3(6f, 0, 0);
        var def = ItemDb.GetBuilding("palisade") ?? ItemDb.GetBuilding("storage_chest");
        var placed = gs.PlaceBuilding(def, wallSpot, 0);
        Check(await Until(() => nav.Bakes > bakes, 15), "placing a building rebakes the navmesh");
        gs.RemoveBuilding(placed.NetId);
        await Until(() => nav.Bakes > bakes + 1, 15);
    }

    /// <summary>Home tier 2 (GDD §6): pay at the plans board, the shack becomes a cottage.</summary>
    private async Task Cottage(GameState gs, PlayerController p)
    {
        var space = p.GetWorld3D().DirectSpaceState;
        Check(BuildSystem.CheckPlacement(space, ItemDb.GetBuilding("campfire"), new Vector3(12.5f, 0, -18.5f), 0) == "Space kept for the cottage",
            "the cottage's footprint is kept free of new buildings");
        var board = Find("UpgradeBoard") as HomeUpgradeBoard;
        p.Teleport(board.GlobalPosition + new Vector3(0, 0.1f, 2f));
        Face(p, board.GlobalPosition + Vector3.Up);
        await Wait(0.3);
        board.Interact(p);
        await Wait(0.2);
        Check(gs.HomeTier == 1, "the board refuses while the stockpile is short");

        // Test shortcut: top up the bulk materials (the nails, pelts and blueprint were earned above).
        foreach (var kv in HomeUpgradeBoard.CottageCost.Where(kv => kv.Key is "wood" or "stone" or "wolf_pelt"))
            if (Have(kv.Key) < kv.Value) gs.Inventory.Add(kv.Key, kv.Value - Have(kv.Key));
        Check(HomeUpgradeBoard.CottageCost.All(kv => Have(kv.Key) >= kv.Value), "the stockpile has everything for the cottage");
        board.Interact(p);
        await Wait(0.3);
        Check(gs.HomeTier == 2 && Have("cottage_blueprint") == 0 && Have("iron_nails") == 0, "the shack is upgraded to a cottage (cost paid)");
        Check(Find("Shack") == null && Find("Cottage") is Node3D, "the shack is replaced by the cottage");
        Check(Mathf.IsEqualApprox(gs.ClaimRadius, 42f), "the land claim grows to 42 m");
        var beds = Find("Cottage").FindChildren("Bed*", "", false, false).OfType<Bed>().ToList();
        Check(beds.Count == 2, "the cottage has two beds");

        p.Teleport(new Vector3(17, 0.1f, -12.5f));
        Face(p, HomeUpgradeBoard.CottageCenter);
        p.Rig.SetLook(p.Rig.Look.Yaw - 0.25f, -0.02f);
        gs.TimeOfDay = 0.66f;
        await Wait(0.8);
        await Shot("20_cottage");
        p.Teleport(HomeUpgradeBoard.CottageCenter + new Vector3(0, 0.1f, 4.5f));
        p.Rig.SetLook(0, -0.05f);
        await Wait(0.3);
        Input.ActionPress("move_forward");
        await Wait(1.2);
        Input.ActionRelease("move_forward");
        Check(p.GlobalPosition.Z < HomeUpgradeBoard.CottageCenter.Z + 2f, $"you can walk in through the door (z {p.GlobalPosition.Z:0.0})");
        p.Rig.SetView(true, true);
        p.Rig.SetLook(0.4f, -0.2f);
        await Wait(0.4);
        await Shot("21_cottage_inside");
        p.Rig.SetView(false, true);

        // Persists through save / load.
        gs.SaveGame(silent: true);
        gs.SetHomeTier(1);
        Check(Find("Shack") != null, "(test) tier 1 rebuilds the shack");
        gs.LoadGame(silent: true);
        await Wait(0.2);
        Check(gs.HomeTier == 2 && Find("Cottage") != null, "the cottage is saved with the world");
        var bed = Find("Cottage").GetNode<Bed>("Bed2");
        int day = gs.Day;
        p.Teleport(bed.GlobalPosition + new Vector3(-0.9f, 0, 0.5f));
        await Wait(0.2);
        bed.Interact(p);
        await Wait(0.2);
        Check(gs.Day == day + 1, "sleeping in the cottage's second bed works");
    }

    private async Task Run()
    {
        var gs = GameState.Instance;
        Weather.AutoRain = false;
        for (int i = 0; i < 100 && gs.LocalPlayer == null; i++) await Wait(0.2);
        await Wait(2.0);
        var p = gs.LocalPlayer;
        Check(p != null && p.IsOnFloor(), "player spawned on the ground");

        // Views.
        p.Rig.SetLook(0.35f, -0.12f);
        await Wait(0.5);
        await Shot("01_third_person_cliff");
        p.Rig.SetView(true, true);
        await Wait(0.5);
        Check(p.Rig.FirstPerson && !p.Body.GetNode<Node3D>("Mesh").Visible, "first person hides body");
        await Shot("02_first_person_cliff");
        p.Rig.SetView(false, true);

        // Movement: hold W for a moment.
        var start = p.GlobalPosition;
        Input.ActionPress("move_forward");
        await Wait(0.8);
        Input.ActionRelease("move_forward");
        Check(p.GlobalPosition.DistanceTo(start) > 1.5f, $"walking moves the player ({p.GlobalPosition.DistanceTo(start):0.0} m)");

        // Chop the guaranteed tree at ShackPos + (8,0,6) = (17,0,-15).
        p.Teleport(new Vector3(17, 0.1f, -13.2f));
        p.Rig.SetLook(0, -0.1f);
        await Wait(0.4);
        for (int i = 0; i < 14 && Have("wood") < 7; i++)
        {
            p.RestoreStamina();
            p.Combat.TryAttack(false);
            await Wait(0.5);
        }
        Check(Have("wood") >= 7, $"chopping a tree gives wood (have {Have("wood")})");
        Check(p.Pack.Count("wood") >= 7 && gs.Inventory.Count("wood") == 0, "the wood goes into your pack, not the stockpile");

        // Mine the rock at ShackPos + (6,0,12) = (15,0,-9).
        p.Teleport(new Vector3(15.1f, 0.1f, -6.9f));
        p.Rig.SetLook(0, -0.25f);
        await Wait(0.4);
        for (int i = 0; i < 20 && Have("stone") < 3; i++)
        {
            p.RestoreStamina();
            p.Combat.TryAttack(false);
            await Wait(0.45);
        }
        Check(Have("stone") >= 3, $"mining a rock gives stone (have {Have("stone")})");

        // Build a workbench in the yard.
        p.Teleport(new Vector3(14, 0.1f, -12));
        p.Rig.SetLook(Mathf.Pi / 2f, -0.45f); // face west, look down at the yard
        p.Rig.SetView(true, true);
        await Wait(0.3);
        p.Build.Select("workbench");
        p.Build.SetActive(true);
        await Wait(0.4);
        await Shot("03_build_ghost");
        int before = gs.PlacedBuildings.Count;
        p.Build.TryPlace();
        p.Build.SetActive(false);
        await Wait(0.3);
        Check(gs.PlacedBuildings.Count == before + 1 && gs.PlacedBuildings.Last().Id == "workbench",
            $"placed a workbench (wood left {Have("wood")}, reason '{p.Build.InvalidReason}')");

        // Craft a wooden sword (top up wood if the tree was stingy).
        if (Have("wood") < 5) gs.Inventory.Add("wood", 5 - Have("wood"));
        var bench = gs.PlacedBuildings.LastOrDefault()?.Node as CraftingStation;
        var recipe = ItemDb.Recipes.First(r => r.OutputItem == "wooden_sword");
        Check(bench != null && bench.Recipes.Contains(recipe), "workbench offers the wooden sword recipe");
        if (bench != null) bench.Interact(p);
        await Wait(0.3);
        await Shot("04_crafting_menu");
        bench?.RequestCraft(recipe);
        await Wait(0.1);
        Check(p.Equipped == "wooden_sword" && Have("wooden_sword") == 0,
            "crafted and equipped a Wooden Sword (in hand, not in the stockpile)");
        Hud.Instance.CloseMenus();

        // Fight the training dummy at ShackPos + (-4,0,7) = (5,0,-14).
        p.Rig.SetView(false, true);
        p.Teleport(new Vector3(5, 0.1f, -11.9f));
        p.Rig.SetLook(0, -0.05f);
        await Wait(0.4);
        var dummy = GetTree().CurrentScene.FindChild("TrainingDummy", true, false) as Node3D;
        var dummyHp = dummy?.GetNode<Health>("Health");
        bool tookDamage = false, destroyed = false;
        for (int i = 0; i < 16 && dummyHp != null && !destroyed; i++)
        {
            p.RestoreStamina();
            float hp = dummyHp.Current;
            p.Combat.TryAttack(i % 3 == 2);
            await Wait(i % 3 == 2 ? 0.9 : 0.5);
            if (dummyHp.Current < hp || dummyHp.IsDead) tookDamage = true;
            if (dummyHp.IsDead) destroyed = true;
            if (i == 1) await Shot("05_combat_third_person");
        }
        Check(tookDamage, "sword hits damage the dummy");
        Check(destroyed, "dummy can be destroyed");

        // Save / load round trip.
        int wood = Have("wood");
        gs.SaveGame();
        gs.Inventory.Add("wood", 50);
        gs.LoadGame();
        await Wait(0.3);
        Check(Have("wood") == wood && gs.PlacedBuildings.Count == before + 1, "save/load restores inventory and buildings");
        Check(Have("wooden_sword") == 1 && p.Equipped == "", "the held sword was saved into the stockpile");
        Net.Instance.RequestEquip("wooden_sword");
        Check(p.Equipped == "wooden_sword" && Have("wooden_sword") == 0, "equipping takes the sword out of the stockpile");

        // Sleep: alone, the bed skips straight to dawn.
        var bed = GetTree().CurrentScene.FindChild("Bed", true, false) as Bed;
        int day = gs.Day;
        p.Teleport(bed.GlobalPosition + new Vector3(0.9f, 0, 0.5f));
        await Wait(0.2);
        bed.Interact(p);
        await Wait(0.2);
        Check(gs.Day == day + 1 && !p.Sleeping && Mathf.Abs(gs.TimeOfDay - 0.27f) < 0.01f, "sleeping alone skips to the next dawn");

        // Difficulty formulas (GDD §16).
        Check(Difficulty.PackSize(4, 1) == 4 && Difficulty.PackSize(4, 4) == 10, "small-animal packs grow with players (4 -> 10 at 4 players)");
        Check(Mathf.IsEqualApprox(Difficulty.BigMonsterHealth(100, 4), 205f) && Mathf.IsEqualApprox(Difficulty.BigMonsterDamage(10, 4), 14.5f),
            "big monsters scale health x2.05 and damage x1.45 at 4 players");

        await Packs(gs, p);
        await Navigation(gs, p);
        await Dodge(gs, p);
        await Wolves(gs, p);
        await BlockAndParry(gs, p);
        await BanditCamp(gs, p);
        await Ashby(gs, p);
        await Farming(gs, p);
        await Cottage(gs, p);

        // Night look.
        gs.TimeOfDay = 0.745f;
        p.Teleport(new Vector3(-2, 0.1f, -18));
        p.Rig.SetLook(0.4f, 0.05f);
        await Wait(0.6);
        await Shot("06_dusk_blossom");
        gs.TimeOfDay = 0.5f;
        p.Rig.SetLook(-0.6f, 0.05f);
        await Wait(0.6);
        await Shot("07_noon_waterfall");

        // Hero shot framed like the concept art: hero on the lip, blossom tree to the right, sunset beyond.
        Hud.Instance.Visible = false;
        gs.TimeOfDay = 0.748f;
        p.Teleport(new Vector3(-16, 0.1f, -27.5f));
        p.Rig.SetLook(-0.55f, 0.02f);
        await Wait(1.0);
        await Shot("08_hero_shot");
        p.Rig.SetLook(2.6f, -0.1f); // turn around: hero's face, shack and yard
        gs.TimeOfDay = 0.62f;
        await Wait(0.8);
        await Shot("09_hero_front_yard");

        // Concept-art framings (docs/art/concept_01 / 02): shack + tree on the ledge, looking along the cliff.
        var cam = new Camera3D { Far = 3000f, Fov = 60f };
        GetTree().CurrentScene.AddChild(cam);
        cam.GlobalPosition = new Vector3(-30f, 6f, -52f);   // out over the drop, looking back at the ledge
        cam.LookAt(new Vector3(2f, 0f, -21f), Vector3.Up);
        cam.MakeCurrent();
        p.Teleport(new Vector3(0, 0.1f, 20f));
        gs.TimeOfDay = 0.285f;
        await Wait(1.0);
        await Shot("10_concept_dawn");
        gs.TimeOfDay = 0.02f;
        await Wait(1.5);
        await Shot("11_concept_night");
        // Art pass round 2: the sculpted volcano at dusk, seen from out past the cliff edge.
        cam.GlobalPosition = new Vector3(-60f, 30f, -120f);
        cam.LookAt(new Vector3(-400f, 170f, -950f), Vector3.Up);
        gs.TimeOfDay = 0.752f;
        await Wait(1.0);
        await Shot("22_volcano");
        // Character close-up: toon ramp, violet shadows, ink outline.
        gs.TimeOfDay = 0.7f;
        p.Teleport(new Vector3(0, 0.1f, 20f));
        p.Rig.SetLook(0f, 0f);
        await Wait(0.5);
        var face = p.Body.GlobalBasis.Z.Normalized() * -1f;
        cam.Fov = 40f;
        cam.GlobalPosition = p.GlobalPosition + face * 3.2f + new Vector3(1.2f, 1.5f, 0);
        cam.LookAt(p.GlobalPosition + Vector3.Up * 1.0f, Vector3.Up);
        await Wait(0.3);
        await Shot("23_character_toon");
        cam.QueueFree();
        p.Rig.GetNode<Camera3D>("Pivot/SpringArm3D/Camera3D").MakeCurrent();
        Hud.Instance.Visible = true;

        Audio();

        GD.Print(_fails == 0 ? "[AUTOTEST] ALL PASSED" : $"[AUTOTEST] {_fails} FAILED");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }
}
