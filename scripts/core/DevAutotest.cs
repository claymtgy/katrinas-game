using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KatrinasGame;

/// <summary>Scripted smoke test of the greybox loop. Runs only when launched with
///   godot --path . -- --autotest [--shots=C:/some/dir]
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

    private async Task Run()
    {
        var gs = GameState.Instance;
        await Wait(2.5);
        var p = gs.Player;
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
        for (int i = 0; i < 14 && gs.Inventory.Count("wood") < 7; i++)
        {
            p.RestoreStamina();
            p.Combat.TryAttack(false);
            await Wait(0.5);
        }
        Check(gs.Inventory.Count("wood") >= 7, $"chopping a tree gives wood (have {gs.Inventory.Count("wood")})");

        // Mine the rock at ShackPos + (6,0,12) = (15,0,-9).
        p.Teleport(new Vector3(15.1f, 0.1f, -6.9f));
        p.Rig.SetLook(0, -0.25f);
        await Wait(0.4);
        for (int i = 0; i < 20 && gs.Inventory.Count("stone") < 3; i++)
        {
            p.RestoreStamina();
            p.Combat.TryAttack(false);
            await Wait(0.45);
        }
        Check(gs.Inventory.Count("stone") >= 3, $"mining a rock gives stone (have {gs.Inventory.Count("stone")})");

        // Build a workbench in the yard.
        p.Teleport(new Vector3(14, 0.1f, -12));
        p.Rig.SetLook(Mathf.Pi / 2f, -0.45f); // face west, look down at the yard
        p.Rig.SetView(true, true);
        await Wait(0.3);
        p.Build.SetActive(true);
        await Wait(0.4);
        await Shot("03_build_ghost");
        int before = gs.PlacedBuildings.Count;
        p.Build.TryPlace();
        p.Build.SetActive(false);
        await Wait(0.3);
        Check(gs.PlacedBuildings.Count == before + 1 && gs.PlacedBuildings.Last().Id == "workbench",
            $"placed a workbench (wood left {gs.Inventory.Count("wood")}, reason '{p.Build.InvalidReason}')");

        // Craft a wooden sword (top up wood if the tree was stingy).
        if (gs.Inventory.Count("wood") < 5) gs.Inventory.Add("wood", 5 - gs.Inventory.Count("wood"));
        var bench = gs.PlacedBuildings.LastOrDefault()?.Node as CraftingStation;
        var recipe = ItemDb.Recipes.First(r => r.OutputItem == "wooden_sword");
        Check(bench != null && bench.Recipes.Contains(recipe), "workbench offers the wooden sword recipe");
        if (bench != null) bench.Interact(p);
        await Wait(0.3);
        await Shot("04_crafting_menu");
        Check(CraftingStation.TryCraft(recipe) && gs.Equipped == "wooden_sword", "crafted and equipped a Wooden Sword");
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
        int wood = gs.Inventory.Count("wood");
        gs.SaveGame();
        gs.Inventory.Add("wood", 50);
        gs.LoadGame();
        await Wait(0.3);
        Check(gs.Inventory.Count("wood") == wood && gs.PlacedBuildings.Count == before + 1, "save/load restores inventory and buildings");

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
        Hud.Instance.Visible = true;

        GD.Print(_fails == 0 ? "[AUTOTEST] ALL PASSED" : $"[AUTOTEST] {_fails} FAILED");
        GetTree().Quit(_fails == 0 ? 0 : 1);
    }
}
