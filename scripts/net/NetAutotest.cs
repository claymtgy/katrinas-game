using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KatrinasGame;

/// <summary>Scripted two-machine co-op test. Run a host and a client side by side:
///   godot --path . -- --autotest-mp=host --pvp --name=Hosty
///   godot --path . -- --autotest-mp=client --name=Joiny
/// Or point the client at a dedicated server (--headless -- --server): it then checks the
/// solo-on-a-server cases and skips the ones that need a second player.
/// Each side prints [NETTEST] PASS/FAIL lines and quits with 0 when everything passed.</summary>
public partial class NetAutotest : Node
{
    private string _role;
    private string _shots;
    private int _fails;

    public static void Start(Node host, string role)
    {
        var shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--shots="))?.Substring("--shots=".Length);
        var t = new NetAutotest { _role = role, _shots = shots, ProcessMode = ProcessModeEnum.Always };
        // The client joins as a red knight (its look goes in the hello), and changes it later.
        if (role == "client") Appearance.Local = new Appearance { Class = PlayerClasses.ArcherId, Body = Appearance.Knight, Outfit = "#8c2c3a", Helmet = true };
        host.CallDeferred(Node.MethodName.AddChild, t);
    }

    public override void _Ready() => _ = Run();

    private async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async Task<bool> Until(Func<bool> condition, double timeout)
    {
        for (double t = 0; t < timeout; t += 0.1)
        {
            if (condition()) return true;
            await Wait(0.1);
        }
        return condition();
    }

    private async Task Shot(string name)
    {
        if (string.IsNullOrEmpty(_shots) || DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        DirAccess.MakeDirRecursiveAbsolute(_shots);
        GetViewport().GetTexture().GetImage().SavePng($"{_shots}/{_role}_{name}.png");
    }

    private void Check(bool ok, string what)
    {
        GD.Print($"[NETTEST:{_role}] {(ok ? "PASS" : "FAIL")}: {what}");
        if (!ok) _fails++;
    }

    private void Finish()
    {
        GD.Print(_fails == 0 ? $"[NETTEST:{_role}] ALL PASSED" : $"[NETTEST:{_role}] {_fails} FAILED");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }

    private async Task Run()
    {
        // Overall watchdog so a hung test never blocks forever.
        GetTree().CreateTimer(540).Timeout += () => { Check(false, "finished within 540 s"); Finish(); };
        try
        {
            if (_role == "host") await Host();
            else await Client();
        }
        catch (Exception e)
        {
            Check(false, $"exception: {e}");
        }
        Finish();
    }

    // ------------------------------------------------------------------ host

    private async Task Host()
    {
        var gs = GameState.Instance;
        var net = Net.Instance;
        Weather.AutoRain = false;
        Check(await Until(() => gs.LocalPlayer != null, 30), "host spawned its own player");
        Check(net.Mode == NetMode.Host, "running as host");
        var me = gs.LocalPlayer;

        Check(await Until(() => gs.Players.Count == 2, 90), "a client joined (2 players)");
        var other = gs.Players.Values.FirstOrDefault(p => !p.IsLocal);
        Check(other != null && other.Look.Body == Appearance.Knight && other.Look.Outfit == "#8c2c3a", "the client's look arrived with its hello (red knight)");
        Check(other != null && other.Class.Id == PlayerClasses.ArcherId, "...and its class (Archer)");
        Check(other?.Body.GetNode<CharacterModel>("Mesh").FindChild("Knight", false, false) != null, "...and the host draws it as a knight");

        Check(await Until(() => other != null && other.Pack.Count("wood") >= 3, 60), $"client's chopping fills its pack on the host (wood {other?.Pack.Count("wood")})");
        Check(gs.Inventory.Count("wood") == 0, "...and not the shared stockpile");
        Check(other != null && other.GlobalPosition.DistanceTo(new Vector3(17, 0, -13.2f)) < 4f,
            $"client's movement replicates to the host (at {other?.GlobalPosition})");
        Check(await Until(() => gs.PlacedBuildings.Any(b => b.Id == "campfire"), 60), "client's campfire exists on the host");
        Check(await Until(() => gs.Inventory.Count("stone") > 0 && other.Pack.Load == 0, 30), "the client emptied its pack into the shared stockpile");
        Check(await Until(() => other.Xp > 0 && me.Xp > 0, 15), $"both players earned XP from the shared goals (host {me.Xp}, client {other.Xp})");
        Check(other.GetNodeOrNull<Label3D>("Nameplate")?.Text == $"{other.DisplayName} · {other.Level}", "the client's nameplate shows its level");
        Check(await Until(() => other.DodgeUntil > 0, 30), "the client's dodge roll reached the server");
        Check(await Until(() => other.Look.Body == Appearance.Rogue && other.Look.Outfit == "#2c5c8c", 30), "the client changed its look mid-game (blue wanderer)");
        Check(await Until(() => Projectile.Spawned > 0, 30), "the client's arrow flew on the server");

        // Sleep vote: host lies down first; the night must not pass until the client does too.
        await Until(() => other == null || other.GlobalPosition.DistanceTo(new Vector3(14, 0, -12)) > 5f || gs.PlacedBuildings.Any(b => b.Id == "campfire"), 5);
        var bed = GetTree().CurrentScene.FindChild("Bed", true, false) as Bed;
        int day = gs.Day;
        me.Teleport(bed.GlobalPosition + new Vector3(0.9f, 0, 0.5f));
        await Wait(0.5);
        bed.Interact(me);
        await Wait(0.3);
        Check(me.Sleeping && gs.Day == day && net.SleepingCount == 1 && net.SleepTotal == 2, "host asleep alone: the night doesn't pass (1/2)");
        Check(await Until(() => gs.Day == day + 1, 60), "the night passes once the client is in bed too");
        Check(!me.Sleeping, "everyone wakes at dawn");

        // PVP (host started with --pvp): the client punches us.
        Check(net.Pvp, "PVP is on for this session");
        Check(await Until(() => me.Health.Current < me.Health.Max, 60), $"PVP: the client's punches hurt the host (HP {me.Health.Current})");

        // Wolves: 2 players -> packs of 3. The client walks out to the meadow (away from its
        // campfire: wolves won't come near a fire); a pack comes for it. The host keeps the
        // client on its feet: three dire wolves would otherwise down it.
        var wild = Wildlife.Instance;
        Check(Difficulty.PackSize(Wildlife.SoloPackSize) == 3, "with 2 players wolf packs have 3 wolves");
        Check(await Until(() => Net.FlatDistance(other.GlobalPosition, DownedSpot) < 3f, 60), "client is out in the meadow");
        bool keepUp = true;
        async Task KeepUp() { while (keepUp) { if (!other.Downed && other.Health.Current < other.Health.Max) other.Health.Reset(); await Wait(0.3); } }
        _ = KeepUp();
        gs.TimeOfDay = 0.9f;
        var pack = wild.SpawnPack(other.GlobalPosition + new Vector3(0, 0, 12), Difficulty.PackSize(Wildlife.SoloPackSize));
        Check(pack.Count == 3, "spawned a pack of 3");
        Check(await Until(() => wild.Bites > 0, 40), "the pack hunts and bites");
        Check(await Until(() => pack.Any(w => w.Health.Current < w.Health.Max), 40), "the client's sword hits a wolf (resolved on the host)");
        await Wait(1.0);
        foreach (var w in wild.Wolves.Values.ToList()) wild.Despawn(w);
        keepUp = false;
        gs.TimeOfDay = 0.4f;

        // Vertical slice in co-op. Supplies for the client's errands: coins in its pack (trading
        // is done from the pack), building materials in the stockpile at home.
        other.Pack.Add("coin", 20);
        gs.Inventory.Add("wood", 10);
        gs.Inventory.Add("stone", 4);

        // Bandit camp, scaled for 2: more grunts, a tougher captain.
        var camp = wild.SpawnCamp(0);
        var captain = camp.First(b => b.Captain);
        Check(camp.Count(b => !b.Captain) == 3, $"2 players: the camp has 3 bandits ({camp.Count(b => !b.Captain)})");
        Check(Mathf.IsEqualApprox(captain.Health.Max, Bandit.CaptainHealth * 1.35f) && Mathf.IsEqualApprox(captain.Damage, Bandit.CaptainDamage * 1.15f),
            $"2 players: the captain has x1.35 health and x1.15 damage ({captain.Health.Max}, {captain.Damage})");

        // The client raises its guard: the server sees it.
        Check(await Until(() => other.Blocking, 40), "the client's guard is seen by the server");
        Check(await Until(() => !other.Blocking, 10), "...and lowered again");

        // The client buys seeds from Marta and plants them at home.
        Check(await Until(() => other.Pack.Count("wheat_seeds") >= 1 && other.Pack.Count("coin") == 18, 40), "the client bought wheat seeds (paid from its pack)");
        Check(await Until(() => gs.PlacedBuildings.Select(b => b.Node).OfType<FarmPlot>().Any(f => f.Planted), 40), "the client's wheat is planted on the server");

        // Upgrade the home: the client must see the cottage.
        foreach (var kv in HomeUpgradeBoard.CottageCost) gs.Inventory.Add(kv.Key, kv.Value);
        var board = GetTree().CurrentScene.FindChild("UpgradeBoard", true, false) as HomeUpgradeBoard;
        board.ServerInteract(me);
        Check(gs.HomeTier == 2, "the host upgraded the shack to a cottage");
        Weather.Instance.SetRain(true);
        Check(gs.Raining, "the host starts a shower");

        // Falls Valley and raids in co-op: the client glides (the host sees it), harpies and a raid
        // show up on the client.
        Supplies.Give(other, "glider", 1);
        Check(await Until(() => other.Gliding, 40), "the host sees the client gliding");
        var wildlife = Wildlife.Instance;
        var harpies = wildlife.SpawnRoost(0);
        Check(harpies.Count == Difficulty.PackSize(Wildlife.SoloRoostSize), $"2 players: a bigger roost ({harpies.Count} harpies)");
        gs.TimeOfDay = 0.72f; // dusk: raiders run off in the morning
        var raid = wildlife.StartRaid(new Vector3(2, 0, 60));
        Check(raid.Count == Difficulty.PackSize(Wildlife.SoloRaidSize), $"2 players: a bigger raid ({raid.Count} raiders)");

        // The journal is shared: a goal done here is done for the client too.
        Journal.Fact("deposited");
        Check(await Until(() => Journal.Instance.Completed.Contains("stash"), 5), "the host's journal ticks off a goal");

        // Armour in co-op: the host puts on a helm (the client must see it); the client is handed
        // a jerkin and wears it (the host must see that, and it must protect the client).
        Supplies.Give(me, "iron_helm", 1);
        Net.Instance.RequestWear("iron_helm");
        Check(me.Worn.GetValueOrDefault(Gear.Head) == "iron_helm", "the host wears an iron helm");
        Supplies.Give(other, "leather_jerkin@ward", 1);
        Check(await Until(() => other.Worn.GetValueOrDefault(Gear.Body) == "leather_jerkin@ward", 40), "the host sees the client put on its enchanted jerkin");
        Check(Mathf.IsEqualApprox(Gear.Armor(other), 10f), $"...and the server counts its armour ({Gear.Armor(other)})");

        // Talents in co-op: the host levels the client to 3 and the client picks one.
        net.ServerAwardXp(other, Math.Max(0, Levels.StartOf(3) - other.Xp), "a test");
        Check(await Until(() => Talents.Has(other, Talents.LongDraw), 40), "the client's talent pick (Long Draw) reached the server");

        // The host becomes a level-12 Healer (for Rekindle, below).
        me.Teleport(gs.WorldSpawn + new Vector3(3, 0.1f, 4));
        await Wait(0.3);
        var look = me.Look.Clone();
        look.Class = PlayerClasses.HealerId;
        Appearance.Local = look;
        net.RequestLook(look);
        Check(await Until(() => me.Class.Id == PlayerClasses.HealerId, 3), "the host becomes a Healer at home");
        net.ServerLoadXp(me, Levels.StartOf(12));

        // Downed and revived (GDD §16). The client waits in the meadow; the host stands beside it.
        var spot = DownedSpot;
        async Task<bool> DownClient()
        {
            await Until(() => Net.FlatDistance(other.GlobalPosition, spot) < 1.5f && Mathf.IsEqualApprox(other.Health.Current, other.Health.Max), 60);
            await Wait(0.5);
            other.Health.TakeDamage(other.Health.Current + 100f, null, true);
            return other.Downed && !other.Health.IsDead && other.Health.Current >= 1f;
        }
        me.Teleport(spot + new Vector3(0, 0.1f, 1.8f));
        me.Health.Reset();
        Check(await Until(() => Net.FlatDistance(other.GlobalPosition, spot) < 1.5f, 60), "the client waits in the meadow");
        other.Health.Reset();
        Check(await DownClient(), "a killing blow with a friend nearby downs the client instead");
        other.Health.TakeDamage(50f, null, true);
        Check(other.Downed && other.Health.Current >= 1f && !other.Health.IsDead, "...and nothing hurts it while it's down");
        gs.TimeOfDay = 0.9f;
        var stalker = wild.SpawnPack(spot + new Vector3(0, 0, -9), 1)[0];
        await Wait(3.0);
        Check(stalker.Target != other, "a wolf leaves the downed player alone");
        wild.Despawn(stalker);
        gs.TimeOfDay = 0.4f;
        me.Health.Reset();
        me.Teleport(spot + new Vector3(0, 0.1f, 1.8f));
        await Wait(0.5);
        Check(me.Interactor.ReviveTarget == other, "the host is close enough to revive the client");
        var tc = other.GlobalPosition - me.GlobalPosition;
        me.Rig.SetLook(Mathf.Atan2(-tc.X, -tc.Z), -0.3f);
        await Wait(0.2);
        await Shot("08_downed_friend");
        me.Interactor.HoldRevive = true;
        Check(await Until(() => other.ReviveProgress > 0.3f, 3), "holding Interact fills the revive");
        Check(await Until(() => !other.Downed, Net.ReviveSeconds + 2), "...and gets the client back up");
        me.Interactor.HoldRevive = false;
        Check(Mathf.IsEqualApprox(other.Health.Current, other.Health.Max * Net.ReviveHealth, 0.5f),
            $"...with 30% health ({other.Health.Current:0}/{other.Health.Max:0})");
        await Wait(1.5);   // let the client see its 30% before we top it up
        other.Health.Reset();

        // Down again: the host's Rekindle raises it from a distance, at once, with half health.
        Check(await DownClient(), "downed for Rekindle");
        me.Teleport(spot + new Vector3(0, 0.1f, 8f));
        await Wait(1.5);   // let the client see itself go down
        me.RestoreStamina();
        me.Ability3.ResetCooldown();
        Check(me.Ability3.TryUse(), "the host casts Rekindle from 8 m");
        Check(await Until(() => !other.Downed, 1), "...and the client is up at once");
        Check(Mathf.IsEqualApprox(other.Health.Current, other.Health.Max * Net.RekindleHealth, 0.5f),
            $"...with half its health ({other.Health.Current:0}/{other.Health.Max:0})");
        await Wait(1.5);
        other.Health.Reset();
        me.Teleport(spot + new Vector3(0, 0.1f, 1.8f));

        // Down again: the client gives up (holds Jump) and goes home.
        Check(await DownClient(), "downed again");
        Check(await Until(() => !other.Downed, 15), "the client gave up");
        Check(Mathf.IsEqualApprox(other.Health.Current, other.Health.Max), "...was defeated and restored");
        Check(await Until(() => Net.FlatDistance(other.GlobalPosition, spot) > 8f, 10), "...and sent home");

        // Again: nobody revives it in time.
        Check(await DownClient(), "downed a third time");
        other.DownedUntil = Net.Now + 1.0;
        Check(await Until(() => !other.Downed, 4) && Mathf.IsEqualApprox(other.Health.Current, other.Health.Max), "the downed timer runs out: defeated");
        await Until(() => Net.FlatDistance(other.GlobalPosition, spot) > 8f, 10);

        // And again: the host walks off, leaving nobody to help.
        Check(await DownClient(), "downed a fourth time");
        await Wait(1.0);   // let the client see it go down
        me.Teleport(spot + new Vector3(70, 0.1f, 0));
        Check(await Until(() => !other.Downed, 3), "with nobody left within 60 m, the client is defeated");
        me.Teleport(spot + new Vector3(0, 0.1f, 1.8f));

        // The client leaves at the end.
        Check(await Until(() => gs.Players.Count == 1, 60), "client left; the host keeps running");
        Check(FileAccess.FileExists(GameState.SavePath), "the host wrote the save file");
    }

    /// <summary>Where the downed tests happen: open meadow north of the shack.</summary>
    private static readonly Vector3 DownedSpot = new(6, 0.1f, 24);

    // ------------------------------------------------------------------ client

    private async Task Client()
    {
        var gs = GameState.Instance;
        var net = Net.Instance;
        Weather.AutoRain = false;
        Check(await Until(() => gs.LocalPlayer != null, 60), "client joined and spawned");
        Check(net.Mode == NetMode.Client && !net.IsServer, "running as client");
        await Wait(1.5);
        var me = gs.LocalPlayer;
        var host = gs.Players.Values.FirstOrDefault(p => !p.IsLocal);
        bool dedicated = host == null;
        Check(dedicated || host.DisplayName != me.DisplayName, dedicated ? "alone on a dedicated server" : $"sees the host ({host.DisplayName})");
        if (!dedicated)
        {
            me.Teleport(host.GlobalPosition + new Vector3(1.2f, 0, 4.5f));
            var d = host.GlobalPosition - me.GlobalPosition;
            me.Rig.SetLook(Mathf.Atan2(-d.X, -d.Z) + 0.25f, -0.08f);
            await Wait(0.6);
            await Shot("01_meet_the_host");
        }

        // Gather: drops land in the shared stockpile, mirrored back to us.
        me.Teleport(new Vector3(17, 0.1f, -13.2f));
        me.Rig.SetLook(0, -0.1f);
        await Wait(0.5);
        for (int i = 0; i < 20 && me.Pack.Count("wood") < 7; i++)
        {
            me.RestoreStamina();
            me.Combat.TryAttack(false);
            await Wait(0.5);
        }
        Check(me.Pack.Count("wood") >= 3, $"client sees its wood in its pack (mirrored from the server) ({me.Pack.Count("wood")})");
        var tree = GetTree().CurrentScene.FindChild("Res_0", true, false)?.GetNode<Health>("Health");
        Check(tree != null && tree.Current < tree.Max, "tree damage is mirrored to the client");

        me.Teleport(new Vector3(15.1f, 0.1f, -6.9f));
        me.Rig.SetLook(0, -0.25f);
        await Wait(0.5);
        for (int i = 0; i < 24 && me.Pack.Count("stone") < 3; i++)
        {
            me.RestoreStamina();
            me.Combat.TryAttack(false);
            await Wait(0.45);
        }
        Check(me.Pack.Count("stone") >= 3, $"client mined stone ({me.Pack.Count("stone")})");

        // Build a campfire: the server checks the spot and pays from the stockpile.
        me.Teleport(new Vector3(14, 0.1f, -12));
        me.Rig.SetLook(Mathf.Pi / 2f, -0.45f);
        me.Rig.SetView(true, true);
        await Wait(0.3);
        me.Build.Select("campfire");
        me.Build.SetActive(true);
        await Wait(0.5);
        int woodBefore = me.Pack.Count("wood");
        me.Build.TryPlace();
        me.Build.SetActive(false);
        me.Rig.SetView(false, true);
        Check(await Until(() => gs.PlacedBuildings.Any(b => b.Id == "campfire"), 10), $"client's campfire was built (reason '{me.Build.InvalidReason}')");
        Check(await Until(() => me.Pack.Count("wood") == woodBefore - 3, 5), "building cost came out of the client's pack");

        // Empty the pack into the chest by the shack.
        var chest = GetTree().CurrentScene.FindChild("HomeChest", true, false) as StorageChest;
        me.Teleport(chest.GlobalPosition + new Vector3(0, 0.1f, 1.6f));
        await Wait(0.4);
        chest.Interact(me);
        Check(await Until(() => me.Pack.Load == 0 && gs.Inventory.Count("stone") > 0, 10), "client emptied its pack into the stockpile (mirrored)");

        // XP (GDD §6): goals are world milestones, so both players earn them; levels show on nameplates.
        Check(await Until(() => me.Xp > 0, 15), $"the client earned XP from the shared goals ({me.Xp})");
        if (!dedicated)
            Check(await Until(() => host.Xp > 0 && host.GetNodeOrNull<Label3D>("Nameplate")?.Text == $"{host.DisplayName} · {host.Level}", 15),
                $"the client sees the host's XP and level on its nameplate ({host.Xp} XP)");

        // Change our look from the pause menu's creator: everyone sees it.
        var creator = Hud.Instance.OpenCreator();
        await Wait(0.2);
        creator.Set(a => { a.Body = Appearance.Rogue; a.Outfit = "#2c5c8c"; });
        creator.Done();
        Hud.Instance.CloseMenus();
        Check(await Until(() => me.Look.Body == Appearance.Rogue && me.Body.GetNode<CharacterModel>("Mesh").FindChild("Rogue", false, false) != null, 10),
            "client changed its look (rebuilt after the server's echo)");
        Check(me.Class.Id == PlayerClasses.ArcherId, "client is an Archer");
        me.RestoreStamina();
        Check(me.Ability.TryUse(), "client shoots an arrow");
        Check(await Until(() => Projectile.Spawned > 0, 5), "the server's arrow shows up on the client");

        // A dodge roll, seen by the server.
        me.RestoreStamina();
        Check(me.TryDodge(new Vector2(0, 1)), "client rolls");
        await Wait(0.6);

        // Sleep vote.
        var bed = GetTree().CurrentScene.FindChild("Bed", true, false) as Bed;
        int day = gs.Day;
        if (!dedicated)
        {
            Check(await Until(() => host.Sleeping, 60), "client sees the host asleep");
            Check(net.SleepingCount == 1 && net.SleepTotal == 2, $"sleep vote shows 1/2 (got {net.SleepingCount}/{net.SleepTotal})");
            me.Teleport(bed.GlobalPosition + new Vector3(0.9f, 0, 1.6f));
            var b = host.GlobalPosition - me.GlobalPosition;
            me.Rig.SetLook(Mathf.Atan2(-b.X, -b.Z) + 0.3f, -0.3f);
            await Wait(0.5);
            await Shot("02_sleep_vote");
            day = gs.Day;
        }
        me.Teleport(bed.GlobalPosition + new Vector3(0.9f, 0, 1.2f));
        await Wait(0.5);
        bed.Interact(me);
        Check(await Until(() => gs.Day == day + 1, 10), dedicated ? "alone on the server, sleeping skips the night" : "both asleep: the night passed for the client");
        Check(await Until(() => !me.Sleeping, 5), "client woke at dawn");

        if (!dedicated)
        {
            // PVP: walk up to the host and punch.
            Check(net.Pvp, "client knows PVP is on");
            float hp = host.Health.Current;
            for (int i = 0; i < 12 && host.Health.Current >= hp; i++)
            {
                var h = host.GlobalPosition;
                me.Teleport(h + new Vector3(0, 0, 1.1f));
                me.Rig.SetLook(0, 0);
                await Wait(0.3);
                me.RestoreStamina();
                me.Combat.TryAttack(false);
                await Wait(0.6);
            }
            Check(host.Health.Current < hp, $"PVP: the client's punch damaged the host ({hp} -> {host.Health.Current})");

            // Wolves: wait in the meadow (away from our campfire) for the host's pack, then fight back.
            me.Teleport(DownedSpot);
            var wild = Wildlife.Instance;
            Check(await Until(() => wild.Wolves.Count == 3, 60), $"client sees the pack of 3 ({wild.Wolves.Count})");
            await Until(() => wild.Wolves.Values.Any(w => w.GlobalPosition.DistanceTo(me.GlobalPosition) < 8f), 20);
            var c = wild.Wolves.Values.Aggregate(Vector3.Zero, (acc, w) => acc + w.GlobalPosition) / wild.Wolves.Count - me.GlobalPosition;
            me.Rig.SetLook(Mathf.Atan2(-c.X, -c.Z) + 0.3f, -0.12f);
            await Wait(0.2);
            await Shot("03_wolf_pack");
            Check(await Until(() => wild.Wolves.Values.Any(w => w.GlobalPosition.DistanceTo(me.GlobalPosition) < 3f), 30),
                "wolves move on the client (they come for it)");
            bool hit = false;
            for (int i = 0; i < 30 && !hit; i++)
            {
                var wolf = wild.Wolves.Values.Where(w => w.Current != Wolf.State.Dead)
                    .OrderBy(w => w.GlobalPosition.DistanceTo(me.GlobalPosition)).FirstOrDefault();
                if (wolf == null) break;
                var d = wolf.GlobalPosition - me.GlobalPosition;
                me.Rig.SetLook(Mathf.Atan2(-d.X, -d.Z), -0.1f);
                me.RestoreStamina();
                if (d.Length() < 2.3f) me.Combat.TryAttack(false);
                await Wait(0.5);
                hit = wild.Wolves.Values.Any(w => w.Health.Current < w.Health.Max);
            }
            Check(hit, "client's hit on a wolf shows up in the mirrored wolf health");
            Check(me.Health.Current < me.Health.Max || wild.Bites == 0, "client took wolf bites (server-applied)");

            // Bandits scaled on the server, mirrored here with the same max health.
            Check(await Until(() => wild.Bandits.Count() == 4, 60), $"client sees the bandit camp (3 + captain) ({wild.Bandits.Count()})");
            var captain = wild.Bandits.FirstOrDefault(b => b.Captain);
            Check(captain != null && Mathf.IsEqualApprox(captain.Health.Max, Bandit.CaptainHealth * 1.35f), $"client's captain has the scaled health ({captain?.Health.Max})");

            // Guard up for a moment (the host checks it sees it).
            me.Combat.HoldGuard = true;
            await Wait(2.0);
            Check(me.Combat.GuardUp, "client's guard is up");
            me.Combat.HoldGuard = false;
            await Wait(1.0);

            // Buy wheat seeds from Marta with the coins in our pack.
            Check(await Until(() => me.Pack.Count("coin") >= 20, 20), "client sees the coins the host put in its pack");
            var trader = GetTree().CurrentScene.FindChild("Trader", true, false) as Trader;
            me.Teleport(trader.GlobalPosition + new Vector3(0.4f, 0.1f, -2.6f));
            var t = trader.GlobalPosition - me.GlobalPosition;
            me.Rig.SetLook(Mathf.Atan2(-t.X, -t.Z), -0.15f);
            await Wait(0.6);
            trader.Interact(me);
            await Wait(0.3);
            await Shot("04_trading_in_ashby");
            Hud.Instance.CloseMenus();
            net.RequestTrade(trader, "wheat_seeds", buy: true);
            Check(await Until(() => me.Pack.Count("wheat_seeds") >= 1, 10), "client bought wheat seeds (into its pack)");

            // Build a wheat plot at home and plant it.
            me.Teleport(new Vector3(0, 0.1f, -10));
            me.Rig.SetView(true, true);
            me.Rig.SetLook(Mathf.Pi / 2f, -0.6f);
            await Wait(0.4);
            me.Build.Select("wheat_plot");
            me.Build.SetActive(true);
            await Wait(0.5);
            me.Build.TryPlace();
            me.Build.SetActive(false);
            me.Rig.SetView(false, true);
            Check(await Until(() => gs.PlacedBuildings.Any(b => b.Id == "wheat_plot"), 10), $"client built a wheat plot ('{me.Build.InvalidReason}')");
            var plot = gs.PlacedBuildings.FirstOrDefault(b => b.Id == "wheat_plot")?.Node as FarmPlot;
            if (plot != null)
            {
                me.Teleport(plot.GlobalPosition + new Vector3(0, 0.1f, 1.8f));
                await Wait(0.3);
                plot.Interact(me);
                Check(await Until(() => plot.Planted, 10), "client planted wheat (state replicated back from the server)");
            }

            // The host raises the cottage.
            Check(await Until(() => GetTree().CurrentScene.FindChild("Cottage", true, false) != null, 60), "client sees the cottage replace the shack");
            Check(gs.HomeTier == 2 && GetTree().CurrentScene.FindChild("Shack", true, false) == null, "client's home tier is 2");
            me.Teleport(new Vector3(17, 0.1f, -12.5f));
            var cc = HomeUpgradeBoard.CottageCenter - me.GlobalPosition;
            me.Rig.SetLook(Mathf.Atan2(-cc.X, -cc.Z) - 0.25f, -0.02f);
            await Wait(0.6);
            await Shot("05_cottage");
            Check(await Until(() => gs.Raining, 20), "client sees the host's rain shower");

            // The host hands us a glider: jump off something high and glide.
            Check(await Until(() => me.HasGlider, 20), "client has the glider the host gave it (pack mirrored)");
            me.Teleport(new Vector3(0, 14f, 30f));
            await Wait(0.1);
            Check(me.TryGlide() && me.Gliding, "client opens its glider");
            await Wait(1.5);
            await Shot("06_glider");
            Check(await Until(() => me.IsOnFloor() && !me.Gliding, 15), "client glides down and lands");
            var wl = Wildlife.Instance;
            Check(await Until(() => wl.Harpies.Count() == Difficulty.PackSize(Wildlife.SoloRoostSize), 20), $"client sees the harpies ({wl.Harpies.Count()})");
            Check(await Until(() => wl.Raiders.Count() == Difficulty.PackSize(Wildlife.SoloRaidSize), 20), $"client sees the raiders ({wl.Raiders.Count()})");
            var flyer = wl.Harpies.FirstOrDefault();
            await Wait(1.0);
            Check(flyer != null && flyer.GlobalPosition.Y > 5f, $"client's harpies fly ({flyer?.GlobalPosition.Y:0.0} m up)");

            Check(await Until(() => Journal.Instance.Completed.Contains("stash"), 30), "client's journal shows the goal the host finished");

            // Armour: the host's helm shows here; we wear the jerkin the host handed us.
            var hostP = gs.GetPlayer(1);
            Check(await Until(() => hostP?.Worn.GetValueOrDefault(Gear.Head) == "iron_helm", 20), "client sees the host's iron helm");
            Check(await Until(() => me.Pack.Count("leather_jerkin@ward") == 1, 20), "client got an enchanted jerkin (pack mirrored)");
            Net.Instance.RequestWear("leather_jerkin@ward");
            Check(await Until(() => me.Worn.GetValueOrDefault(Gear.Body) == "leather_jerkin@ward" && me.Pack.Count("leather_jerkin@ward") == 0, 10), "client wears it");
            Hud.Instance.OpenCharacter();
            await Wait(0.5);
            await Shot("07_character_sheet");
            Hud.Instance.CloseMenus();

            // Talents: the host levels us to 3; take Long Draw on the Talents tab.
            Check(await Until(() => me.Level >= 3, 30), $"client reached level 3 ({me.Level})");
            Hud.Instance.OpenTalents();
            await Wait(0.4);
            var draw = Hud.Instance.TalentButton(Talents.LongDraw);
            Check(draw != null && !draw.Disabled, "client's Talents tab offers Long Draw");
            draw?.EmitSignal(BaseButton.SignalName.Pressed);
            Check(await Until(() => Talents.Has(me, Talents.LongDraw), 10), "client took Long Draw (the server's word came back)");
            await Shot("08_talents");
            Hud.Instance.CloseMenus();

            // Downed (GDD §16): wait in the meadow five times. 1: revived by the host; 2: the
            // host's Rekindle; 3: give up (hold Jump); 4: the timer runs out; 5: the host leaves.
            for (int round = 1; round <= 5; round++)
            {
                me.Teleport(DownedSpot);
                me.Rig.SetLook(0, -0.1f);
                Check(await Until(() => me.Downed, 60), $"round {round}: the client is downed");
                await Wait(0.3);
                Check(Hud.Instance.DownedScreenShown, $"round {round}: the downed screen is up");
                if (round == 1)
                {
                    var start = me.GlobalPosition;
                    Input.ActionPress("move_forward");
                    await Wait(0.8);
                    Input.ActionRelease("move_forward");
                    Check(Net.FlatDistance(me.GlobalPosition, start) < 1.5f, $"downed, you only crawl ({Net.FlatDistance(me.GlobalPosition, start):0.0} m)");
                    me.Teleport(DownedSpot);
                    await Shot("09_downed");
                    Check(await Until(() => !me.Downed, 20), "the host revived the client");
                    Check(me.Health.Current < me.Health.Max * 0.5f && !Hud.Instance.DownedScreenShown, $"...up with some health ({me.Health.Current:0})");
                    await Wait(1.0);
                    continue;
                }
                if (round == 2)
                {
                    Check(await Until(() => !me.Downed, 20), "the host's Rekindle got the client up");
                    Check(me.Health.Current > me.Health.Max * 0.4f && me.Health.Current < me.Health.Max * 0.6f, $"...with half its health ({me.Health.Current:0})");
                    await Wait(1.0);
                    continue;
                }
                if (round == 3)
                {
                    for (int i = 0; i < 60 && me.Downed; i++) { Input.ActionPress("jump"); await Wait(0.05); }
                    Input.ActionRelease("jump");
                }
                Check(await Until(() => !me.Downed, 15), round switch
                {
                    3 => "holding Jump gives up",
                    4 => "the timer ran out",
                    _ => "nobody left to help: defeated",
                });
                Check(await Until(() => Net.FlatDistance(me.GlobalPosition, DownedSpot) > 8f && Mathf.IsEqualApprox(me.Health.Current, me.Health.Max), 5),
                    $"round {round}: ...and home at the blossom tree, healed");
                await Wait(0.5);
            }
        }
        else
        {
            var wild = Wildlife.Instance;
            Check(await Until(() => wild.Bandits.Count() == Wildlife.SoloCampSize + 1, 30),
                $"the dedicated server mans the bandit camp for 1 player ({wild.Bandits.Count()})");
        }

        net.Leave();
        await Wait(1.0);
        Check(net.Mode == NetMode.Offline && gs.Players.Count == 0, "left the server cleanly");
    }
}
