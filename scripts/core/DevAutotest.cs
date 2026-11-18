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

    /// <summary>Character customisation: the creator from the pause menu, a live rebuild of the
    /// body (Knight, painted, taller, helmet off) that keeps the weapon hand, and a bad look from
    /// the network cleaned up.</summary>
    private async Task Looks(GameState gs, PlayerController p)
    {
        var mesh = p.Body.GetNode<CharacterModel>("Mesh");
        Check(mesh.FindChild("Rogue", false, false) != null, "the default look is the Wanderer (Rogue) body");
        gs.TimeOfDay = 0.45f;
        p.Rig.SetView(false, true);
        p.Teleport(new Vector3(0, 0.1f, 20f));
        await Wait(0.3);

        var hud = Hud.Instance;
        hud.OpenGameMenu();
        var cc = hud.OpenCreator();
        await Wait(0.3);
        cc.Set(a =>
        {
            a.Body = Appearance.Knight; a.Skin = "#a06a44"; a.Hair = "#e0b45a";
            a.Outfit = "#2c5c8c"; a.Trim = "#d8b048"; a.Helmet = false; a.Cape = true; a.Height = 1.08f;
        });
        await Wait(0.6);
        Check(cc.PreviewBody?.Name == "Knight", "the creator's preview shows the Knight body");
        await Shot("24_character_creator");
        cc.Done();
        hud.CloseMenus();
        Check(await Until(() => p.Look.Body == Appearance.Knight, 3), "Done gives the player the new look");
        Check(Appearance.Local.Outfit == "#2c5c8c" && Mathf.IsEqualApprox(p.Look.Height, 1.08f), "...colours and height included");
        await Wait(0.2);
        Check(mesh.FindChild("Knight", false, false) != null && mesh.FindChild("Rogue", false, false) == null, "the body was rebuilt as a Knight");
        var bodyMesh = mesh.FindChild("Knight_Body", true, false) as MeshInstance3D;
        var mat = bodyMesh?.GetSurfaceOverrideMaterial(0) as ShaderMaterial;
        Check(mat != null && mat.GetShaderParameter("slot_mask_2").AsInt32() != 0 && mat.GetShaderParameter("slot_mask_3").AsInt32() != 0,
            "outfit and armour colours are painted onto the body");
        Check(mesh.FindChild("Knight_Helmet", true, false) is Node3D helmet && !helmet.Visible, "the helmet is off, as chosen");
        Check(mesh.RightHandSlot?.GetNodeOrNull("Hand") != null, "the weapon hand moved to the new body's hand");

        var bad = Appearance.FromDict(new Godot.Collections.Dictionary { ["body"] = "dragon", ["skin"] = "not a colour", ["height"] = 5f, ["outfit"] = 7 });
        Check(bad.Body == Appearance.Rogue && bad.Skin == "" && bad.Outfit == "" && Mathf.IsEqualApprox(bad.Height, Appearance.MaxHeight),
            "a bad look from the network is cleaned up");
    }

    /// <summary>Classes (GDD §6): passives, each ability doing its job on the server, and the
    /// rule that class changes only happen at home.</summary>
    private async Task Classes(GameState gs, PlayerController p)
    {
        var wild = Wildlife.Instance;
        gs.TimeOfDay = 0.45f;
        var home = gs.WorldSpawn + new Vector3(3, 0.1f, 4);

        async Task Become(string id)
        {
            var a = p.Look.Clone();
            a.Class = id;
            Appearance.Local = a;
            Net.Instance.RequestLook(a);
            await Until(() => p.Class.Id == id, 3);
            p.Ability.ResetCooldown();
            p.Ability.ServerReadyAt = 0;
            p.RestoreStamina();
            p.Health.Reset();
        }
        // A wolf standing still (Frozen) in front of us, at a distance.
        async Task<Wolf> Target(Vector3 at)
        {
            var w = wild.SpawnPack(at, 1)[0];
            w.Frozen = true;
            await Wait(0.3);
            w.GlobalPosition = at;
            return w;
        }
        void AimAt(Vector3 point)
        {
            var d = point - p.Rig.Camera.GlobalPosition;
            p.Rig.SetLook(Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
        }
        void Kill(Wolf w) { if (IsInstanceValid(w) && !w.Health.IsDead) w.Health.TakeDamage(1000, null); }

        p.Teleport(home);
        p.Rig.SetView(true, true);
        await Wait(0.3);
        await Become(PlayerClasses.WarriorId);
        Check(Mathf.IsEqualApprox(p.Health.Max, p.BaseMaxHealth + p.LevelHealth + 30f), $"a Warrior has +30 health ({p.Health.Max})");

        // Away from home the class stays.
        p.Teleport(HomeCliffs.AshbyCenter + new Vector3(0, 0.1f, 10));
        await Wait(0.3);
        var away = p.Look.Clone();
        away.Class = PlayerClasses.WizardId;
        Net.Instance.RequestLook(away);
        await Wait(0.3);
        Check(p.Class.Id == PlayerClasses.WarriorId && Appearance.Local.Class == PlayerClasses.WarriorId, "the class can't be changed away from home");
        p.Teleport(home);
        await Wait(0.3);

        // Warrior: Whirlwind hits all around.
        var fwd = new Vector3(0, 0, -1);
        var w1 = await Target(p.GlobalPosition + fwd * 2f);
        var w2 = await Target(p.GlobalPosition - fwd * 2f);
        Check(p.Ability.TryUse(), "Warrior uses Whirlwind");
        Check(await Until(() => w1.Health.Current < w1.Health.Max && w2.Health.Current < w2.Health.Max, 2), "Whirlwind strikes the wolves in front and behind");
        Kill(w1); Kill(w2);

        // Archer: an arrow carries, and hits harder from far away.
        await Become(PlayerClasses.ArcherId);
        var far = await Target(p.GlobalPosition + fwd * 18f);
        AimAt(far.GlobalPosition + Vector3.Up * 0.45f);
        await Wait(0.1);
        int before = Projectile.Spawned;
        Check(p.Ability.TryUse(), "Archer shoots");
        await Wait(0.12);
        Check(p.Combat.Bow != null && p.Combat.Bow.GetParent() == p.Rig.Camera, "first person: the archer holds a bow");
        Check(p.Combat.Bow != null && !p.Combat.Bow.Nocked, "...and the arrow has left the string");
        await Shot("26_arrow");
        Check(Projectile.Spawned > before, "an arrow flies");
        Check(await Until(() => far.Health.Current < far.Health.Max, 3), $"the arrow hits a wolf 18 m away");
        Check(Mathf.IsEqualApprox(far.Health.Max - far.Health.Current, Projectile.ArrowDamage * Projectile.ArrowLongBonus),
            $"...for the long-shot damage ({far.Health.Max - far.Health.Current:0.#})");
        Kill(far);
        await Wait(0.6);
        Check(p.Combat.Bow != null && p.Combat.Bow.Nocked, "...and another is nocked");
        await Shot("26b_bow_ready");
        p.Rig.SetView(false, true);
        await Wait(0.2);
        Check(p.Combat.Bow != null && p.Combat.Bow.GetParent() is BoneAttachment3D, "third person: the bow is in the archer's left hand");
        p.Ability.ResetCooldown();
        p.Ability.TryUse();
        await Wait(0.15);
        await Shot("26c_bow_third_person");
        var look = p.Rig.Look;
        p.Rig.SetLook(look.Yaw - Mathf.Pi * 0.5f, -0.15f);
        p.Ability.ResetCooldown();
        p.Ability.TryUse();
        await Wait(0.12);
        await Shot("26d_bow_side");
        p.Rig.SetLook(look.Yaw, look.Pitch);
        p.Rig.SetView(true, true);
        await Wait(0.2);

        // Wizard: a firebolt bursts on its target and scorches the one beside it.
        await Become(PlayerClasses.WizardId);
        Check(Mathf.IsEqualApprox(p.Health.Max, p.BaseMaxHealth + p.LevelHealth - 20f), "a Wizard has -20 health");
        var bolt1 = await Target(p.GlobalPosition + fwd * 10f);
        var bolt2 = await Target(p.GlobalPosition + fwd * 10f + new Vector3(1.4f, 0, 0));
        AimAt(bolt1.GlobalPosition + Vector3.Up * 0.45f);
        await Wait(0.1);
        Check(p.Ability.TryUse(), "Wizard casts Firebolt");
        await Wait(0.18);
        await Shot("27_firebolt");
        Check(await Until(() => bolt1.Health.Current < bolt1.Health.Max, 3), "the firebolt hits");
        Check(Mathf.IsEqualApprox(bolt1.Health.Max - bolt1.Health.Current, Projectile.BoltDamage)
              && Mathf.IsEqualApprox(bolt2.Health.Max - bolt2.Health.Current, Projectile.BoltSplashDamage),
            $"direct hit {bolt1.Health.Max - bolt1.Health.Current:0.#}, splash {bolt2.Health.Max - bolt2.Health.Current:0.#}");
        Kill(bolt1); Kill(bolt2);

        // Healer: Mend, then regeneration out of combat.
        await Become(PlayerClasses.HealerId);
        p.Rig.SetView(false, true);
        p.Health.TakeDamage(60, null);
        float hurt = p.Health.Current;
        Check(p.Ability.TryUse(), "Healer casts Mend");
        Check(await Until(() => p.Health.Current >= hurt + Net.MendHeal - 0.01f, 2), $"Mend heals 30 ({hurt:0} -> {p.Health.Current:0})");
        await Wait(0.25);
        await Shot("28_mend");
        float mended = p.Health.Current;
        Check(await Until(() => p.Health.Current > mended, 9), "a Healer regenerates out of combat");

        // Rogue: cheaper rolls, Vanish (the wolf gives up), and a double-damage strike out of it.
        await Become(PlayerClasses.RogueId);
        float st = p.Stamina;
        p.TryDodge(new Vector2(0, 1));
        Check(Mathf.IsEqualApprox(st - p.Stamina, p.DodgeCost * 0.6f), $"a Rogue's roll costs less ({st - p.Stamina:0.#})");
        await Wait(0.8);
        p.Rig.SetView(true, true);
        gs.TimeOfDay = 0.9f; // wolves hunt at night
        var hunt = new Vector3(-12, 0.1f, 30);
        p.Teleport(hunt);
        var stalker = wild.SpawnPack(hunt + new Vector3(0, 0, -8), 1)[0];
        Check(await Until(() => stalker.Target == p, 6), "a wolf hunts the rogue");
        p.Ability.ResetCooldown();
        p.RestoreStamina();
        Check(p.Ability.TryUse(), "Rogue vanishes");
        Check(await Until(() => p.Vanished && stalker.Target != p, 2), "while vanished the wolf loses track");
        stalker.Frozen = true;
        await Wait(0.2);
        p.Teleport(stalker.GlobalPosition + new Vector3(0, 0.1f, 1.4f));
        await Wait(0.15);
        Face(p, stalker.GlobalPosition);
        float hpBefore = stalker.Health.Current;
        float expected = p.Combat.LightDamage * 2f;
        p.RestoreStamina();
        p.Combat.TryAttack(false);
        Check(await Until(() => stalker.Health.Current < hpBefore, 2), "the rogue strikes out of Vanish");
        Check(Mathf.IsEqualApprox(hpBefore - stalker.Health.Current, expected) && !p.Vanished,
            $"...for double damage ({hpBefore - stalker.Health.Current:0.#} of {expected:0.#}), which ends Vanish");
        Kill(stalker);

        p.Teleport(home);
        await Become(PlayerClasses.WarriorId);
        p.Rig.SetView(false, true);
    }

    /// <summary>Falls Valley (GDD §8, §9, §11): the pass, climbing the mesa (and losing your grip),
    /// the second sanctuary, crafting and flying the glider, the updraft, the river's current, and
    /// the harpies (swoop, parry drop, sword, arrow, feathers, the cleared roost).</summary>
    /// <summary>Fast travel between sanctuaries (GDD §10): praying attunes one for the world and
    /// opens the list of the others; travel is refused just after you were hurt; it takes you
    /// there, heals you and sets your respawn point.</summary>
    private async Task FastTravel(GameState gs, PlayerController p, BlossomShrine falls)
    {
        var home = BlossomShrine.Find("BlossomShrine");
        Check(home != null && home.Attuned, "the home blossom starts attuned");
        Check(falls != null && falls.Attuned && Journal.Instance.Facts.Contains("prayed:FallsShrine"), "praying attunes the Falls Blossom (a shared journal fact)");
        Check(Hud.Instance.TravelOpen, "praying where another sanctuary is attuned opens the travel list");
        await Wait(0.3);
        await Shot("30c_fast_travel");
        Hud.Instance.CloseMenus();
        if (home == null || falls == null) return;

        p.LastHurtAt = Net.Now;
        Net.Instance.RequestTravel(falls, home);
        await Wait(0.4);
        Check(p.GlobalPosition.DistanceTo(HomeCliffs.FallsShrinePos) < 5f, "no travelling straight after being hurt");
        p.LastHurtAt = -100;
        p.Health.TakeDamage(10, null);
        p.LastHurtAt = -100;
        Net.Instance.RequestTravel(falls, home);
        bool there = await Until(() => p.GlobalPosition.DistanceTo(home.ArrivalPoint) < 2.5f, 3);
        Check(there, $"fast travel takes you to the home blossom ({p.GlobalPosition.DistanceTo(home.ArrivalPoint):0.0} m off)");
        Check(p.SpawnPoint.DistanceTo(home.ArrivalPoint) < 0.5f && p.Health.Current >= p.Health.Max, "arriving heals you and sets your respawn point there");
        Check(gs.Players.Count == 1 && Journal.Instance.Facts.Contains("travelled"), "travel is noted in the journal");
        await Wait(0.3);
        Net.Instance.RequestTravel(home, falls);
        Check(await Until(() => p.GlobalPosition.DistanceTo(falls.ArrivalPoint) < 2.5f, 3), "and back to the Falls Blossom");
        await Wait(0.3);
    }

    /// <summary>Swimming (GDD §8): a fall into the home pool is broken by the water; you float with
    /// your head out; diving holds your breath (stamina) and tints the screen; you bob back up; you
    /// can't attack in the water; swimming into an edge hauls you out.</summary>
    private async Task Swimming(GameState gs, PlayerController p)
    {
        var pool = HomeCliffs.HomePool;
        var mid = new Vector3(pool.GetCenter().X, 0, pool.GetCenter().Y);
        float surface = HomeCliffs.PoolSurface;
        Check(Mathf.IsEqualApprox(gs.WaterSurfaceAt(mid + Vector3.Down), surface) && float.IsNaN(gs.WaterSurfaceAt(mid + new Vector3(0, 0, 20))), "the home pool is swimmable water, the plateau is not");
        p.Rig.SetView(false, true);
        p.Health.Reset();
        float hp = p.Health.Current;
        p.Teleport(mid + Vector3.Up * 14f);
        p.Rig.SetLook(Mathf.Pi, -0.2f);
        Check(await Until(() => p.Swimming, 4), "jumping into the pool: you swim");
        await Wait(2.5);
        Check(p.Health.Current >= hp, $"the water breaks a 14 m fall (health {p.Health.Current:0}/{hp:0})");
        float floatY = surface - p.FloatDepth;
        Check(Mathf.Abs(p.GlobalPosition.Y - floatY) < 0.35f && !p.Underwater, $"you float with your head out (feet at {p.GlobalPosition.Y:0.00}, want {floatY:0.00})");
        float before = p.Stamina;
        p.Combat.TryAttack(false);
        Check(p.Stamina >= before, "no swinging a sword while swimming");
        await Shot("32_swimming");

        // Dive: look down and swim forward.
        p.RestoreStamina();
        p.Teleport(mid + new Vector3(0, floatY, 3f));
        p.Rig.SetLook(0, -0.9f); // facing -Z (north), down
        Input.ActionPress("move_forward");
        bool under = await Until(() => p.Underwater, 3);
        await Wait(0.8);
        Input.ActionRelease("move_forward");
        Check(under, $"looking down as you swim dives under (feet at {p.GlobalPosition.Y:0.0})");
        Check(p.Stamina < p.MaxStamina - 5f, $"holding your breath uses stamina ({p.Stamina:0})");
        p.Rig.SetLook(0, 0.2f);
        await Wait(0.2);
        await Shot("32b_underwater");
        Check(await Until(() => !p.Underwater && Mathf.Abs(p.GlobalPosition.Y - floatY) < 0.4f, 5), "let go and you bob back up to the surface");

        // Out of breath: you drown slowly.
        p.Health.Reset();
        p.Teleport(mid + new Vector3(0, -2.6f, 0));
        p.DrainStamina(1000);
        Input.ActionPress("move_forward");
        p.Rig.SetLook(0, -1.2f);
        await Wait(1.2);
        Input.ActionRelease("move_forward");
        Check(p.Health.Current < p.Health.Max, $"out of breath under water hurts (health {p.Health.Current:0})");
        p.Health.Reset();

        // Climb out over the south edge.
        p.Teleport(mid + new Vector3(0, floatY, -2f));
        p.Rig.SetLook(Mathf.Pi, -0.05f); // facing +Z
        await Wait(0.3);
        Input.ActionPress("move_forward");
        bool outOf = await Until(() => !p.Swimming && p.IsOnFloor() && p.GlobalPosition.Y > -0.1f, 8);
        Input.ActionRelease("move_forward");
        Check(outOf, $"swim into the edge to climb out (y {p.GlobalPosition.Y:0.0}, z {p.GlobalPosition.Z:0.0})");

        // The Great Falls pool in the valley is deep too.
        var falls = HomeCliffs.FallsPool;
        Check(!float.IsNaN(gs.WaterSurfaceAt(new Vector3(falls.GetCenter().X, -1f, falls.GetCenter().Y))), "the Great Falls plunge pool is swimmable");
        p.Teleport(new Vector3(pool.GetCenter().X + 2, 0.1f, pool.End.Y + 6f));
        p.Rig.SetLook(0, -0.35f);
        await Wait(0.4);
        await Shot("32c_home_pool");
    }

    private async Task FallsValley(GameState gs, PlayerController p)
    {
        var wild = Wildlife.Instance;
        var space = p.GetWorld3D().DirectSpaceState;
        gs.TimeOfDay = 0.42f;
        p.Rig.SetView(false, true);
        p.Health.Reset();

        // The pass: open through the east wall, with ground under it.
        var across = space.IntersectRay(PhysicsRayQueryParameters3D.Create(
            new Vector3(50, 1.2f, HomeCliffs.PassPos.Z), new Vector3(90, 1.2f, HomeCliffs.PassPos.Z), GameState.LayerWorld));
        Check(across.Count == 0, $"the pass through the east wall is open{(across.Count > 0 ? $" (blocked at {across["position"]})" : "")}");
        var floor = space.IntersectRay(PhysicsRayQueryParameters3D.Create(new Vector3(140, 5, 70), new Vector3(140, -5, 70), GameState.LayerWorld));
        Check(floor.Count > 0 && Mathf.Abs(floor["position"].AsVector3().Y) < 0.1f, "Falls Valley has a floor");
        p.Teleport(HomeCliffs.PassPos + new Vector3(16, 0.1f, 0));
        p.Rig.SetLook(-Mathf.Pi / 2f - 0.15f, 0.08f);
        Hud.Instance.Visible = false;
        await Wait(0.8);
        await Shot("29_falls_valley");
        // The mesa from the south-west: rock buttresses break up its sides.
        var mesaView = HomeCliffs.MesaCenter + new Vector3(-32f, 0.1f, 30f);
        p.Teleport(mesaView);
        var toMesa = HomeCliffs.MesaCenter - mesaView;
        p.Rig.SetLook(Mathf.Atan2(-toMesa.X, -toMesa.Z), 0.12f);
        await Wait(0.6);
        await Shot("29b_mesa");
        Hud.Instance.Visible = true;

        // Climbing: walk into the pale rock on the mesa's west face and keep going up.
        var foot = HomeCliffs.MesaClimbFoot;
        p.Teleport(foot + new Vector3(-1.4f, 0.1f, 0));
        p.Rig.SetLook(-Mathf.Pi / 2f, -0.05f); // facing +X, the rock
        await Wait(0.3);
        p.RestoreStamina();
        Input.ActionPress("move_forward");
        Check(await Until(() => p.Climbing, 3), "walking into the pale rock starts a climb");
        var climbMesh = p.Body.GetNode<CharacterModel>("Mesh");
        Check(climbMesh.HasAnimation(ClimbAnimation.Climb) && climbMesh.HasAnimation(ClimbAnimation.Hang), "the hero has climbing clips");
        await Wait(1.0);
        Check(climbMesh.CurrentAnimation == ClimbAnimation.Climb, $"climbing plays the climb clip ({climbMesh.CurrentAnimation})");
        await Wait(1.5);
        Check(p.GlobalPosition.Y > 3f && p.Stamina < p.MaxStamina - 10f, $"climbing goes up and costs stamina (y {p.GlobalPosition.Y:0.0}, stamina {p.Stamina:0})");
        p.Rig.SetLook(-Mathf.Pi / 2f + 0.5f, -0.25f);
        await Wait(0.3);
        await Shot("30_climbing");
        p.Rig.SetLook(-Mathf.Pi / 2f + 1.25f, -0.1f);
        await Wait(0.25);
        await Shot("30b_climbing_side");
        p.Rig.SetLook(-Mathf.Pi / 2f, -0.05f);
        bool top = await Until(() => !p.Climbing && p.IsOnFloor() && p.GlobalPosition.Y > HomeCliffs.MesaHeight - 0.5f, 15);
        Input.ActionRelease("move_forward");
        Check(top, $"over the top: the climber hauls up onto the mesa (y {p.GlobalPosition.Y:0.0})");
        Check(gs.InSafeZone(HomeCliffs.FallsShrinePos), "the Falls Blossom is a sanctuary");

        // The second sanctuary: pray there to respawn there.
        var shrine = Find("FallsShrine") as BlossomShrine;
        var oldSpawn = p.SpawnPoint;
        p.Teleport(HomeCliffs.FallsShrinePos + new Vector3(0, 0.1f, 2.2f));
        await Wait(0.2);
        shrine?.Interact(p);
        await Wait(0.2);
        Check(shrine != null && p.SpawnPoint.DistanceTo(HomeCliffs.FallsShrinePos) < 5f, "praying at the Falls Blossom sets your respawn point there");
        await FastTravel(gs, p, shrine);
        p.SpawnPoint = oldSpawn;

        // Losing your grip: out of stamina, you fall.
        p.Teleport(foot + new Vector3(-1.4f, 0.1f, 0));
        p.Rig.SetLook(-Mathf.Pi / 2f, -0.05f);
        await Wait(0.3);
        p.RestoreStamina();
        Input.ActionPress("move_forward");
        await Until(() => p.Climbing, 3);
        await Wait(0.6);
        p.DrainStamina(1000);
        bool slipped = await Until(() => !p.Climbing, 1);
        Input.ActionRelease("move_forward");
        Check(slipped, "with no stamina left, your grip fails");
        await Until(() => p.IsOnFloor(), 3);
        p.Health.Reset();

        // The glider: none yet, so no gliding; craft one (it goes into your pack and stays there).
        Check(!p.HasGlider && !p.TryGlide(), "no glider, no gliding");
        var bench = gs.PlacedBuildings.FirstOrDefault(b => b.Id == "workbench")?.Node as CraftingStation;
        var recipe = ItemDb.Recipes.First(r => r.OutputItem == "glider");
        gs.Inventory.Add("wood", 6);
        gs.Inventory.Add("wolf_pelt", 2);
        gs.Inventory.Add("harpy_feather", 4);
        if (bench != null) p.Teleport(bench.GlobalPosition + new Vector3(0, 0.1f, 1.6f));
        await Wait(0.2);
        Check(bench != null && bench.Recipes.Contains(recipe), "the workbench offers the glider");
        bench?.RequestCraft(recipe);
        await Wait(0.2);
        Check(p.Pack.Count("glider") == 1 && gs.Inventory.Count("glider") == 0, "the glider goes into the crafter's pack");
        Supplies.Deposit(p);
        Check(p.HasGlider, "emptying your pack at a chest keeps the glider");
        Hud.Instance.CloseMenus();

        // Glide off the mesa, south over the valley: a slow sink and a soft landing.
        p.Teleport(HomeCliffs.MesaCenter + new Vector3(0, 18.5f, HomeCliffs.MesaHalf + 3f));
        p.Rig.SetLook(Mathf.Pi, -0.15f); // facing +Z
        await Wait(0.05);
        Check(p.TryGlide() && p.Gliding, "Space in the air opens the glider");
        var gStart = p.GlobalPosition;
        float hp = p.Health.Current;
        await Wait(2.0);
        await Shot("31_gliding");
        float flew = new Vector2(p.GlobalPosition.X - gStart.X, p.GlobalPosition.Z - gStart.Z).Length();
        float sank = gStart.Y - p.GlobalPosition.Y;
        Check(p.Gliding && flew > 10f && sank < 7f, $"gliding carries you far and sinks slowly ({flew:0.0} m out, {sank:0.0} m down)");
        bool landed = false;
        for (int i = 0; i < 40 && !landed; i++)
        {
            await Wait(0.25);
            landed = p.IsOnFloor();
        }
        Check(landed, "the glider lands");
        await Wait(0.2);
        Check(!p.Gliding && p.Health.Current >= hp, $"a glider landing does no fall damage ({hp:0} -> {p.Health.Current:0})");

        // The updraft over the Great Falls' plunge pool lifts a glider.
        var pool = HomeCliffs.GreatFallsPool;
        p.Teleport(pool + new Vector3(HomeCliffs.UpdraftRadius - 1f, 3f, 0));
        p.Rig.SetLook(Mathf.Pi / 2f, 0.1f); // facing -X, across the column
        await Wait(0.05);
        p.TryGlide();
        await Wait(3.0);
        Check(p.GlobalPosition.Y > 12f, $"the waterfall's updraft lifts the glider (y {p.GlobalPosition.Y:0.0})");
        p.Teleport(pool + new Vector3(-20, 0.1f, -20));
        await Wait(0.3);

        // The river's current carries you toward the cliff.
        var inRiver = new Vector3(HomeCliffs.RiverBend.X, 0.2f, 20);
        p.Teleport(inRiver);
        await Wait(1.5);
        Check(inRiver.Z - p.GlobalPosition.Z > 2.5f, $"the river's current pushes you downstream ({inRiver.Z - p.GlobalPosition.Z:0.0} m)");
        p.Teleport(new Vector3(124, 0.1f, 52));
        await Wait(0.3);

        // Harpies: they circle high, spot you, swoop, and claw.
        foreach (var h in wild.Harpies.ToList()) wild.Despawn(h);
        await Wait(0.2);
        var harpies = wild.SpawnRoost(0);
        Check(harpies.Count == Wildlife.SoloRoostSize, $"solo roost: {Wildlife.SoloRoostSize} harpies ({harpies.Count})");
        await Wait(1.5);
        Check(harpies.All(h => h.GlobalPosition.Y > 5f), $"harpies fly high (lowest {harpies.Min(h => h.GlobalPosition.Y):0.0} m)");
        p.Teleport(Wildlife.RoostSites[0] + new Vector3(4, 0.1f, -14));
        Check(await Until(() => harpies.Any(h => h.Target == p), 6), "the harpies spot the player");
        int bites = wild.Bites;
        bool clawed = false;
        for (int i = 0; i < 120 && !clawed; i++)
        {
            p.RestoreStamina();
            if (p.Health.Current < 50) p.Health.Reset();
            var near = harpies.OrderBy(h => h.GlobalPosition.DistanceTo(p.GlobalPosition)).First();
            if (i == 20) { Face(p, near.GlobalPosition); await Shot("32_harpies"); }
            await Wait(0.1);
            clawed = wild.Bites > bites;
        }
        Check(clawed, "a harpy swoops and claws");

        // A parried (stunned) harpy drops out of the air, where a sword can reach it.
        var downed = harpies[0];
        downed.Stun(1.6f);
        Check(await Until(() => downed.Grounded && downed.GlobalPosition.Y < 3f, 4), $"a stunned harpy tumbles to the ground (y {downed.GlobalPosition.Y:0.0})");
        downed.Frozen = true;
        p.Teleport(downed.GlobalPosition + new Vector3(0, 0.1f, 1.5f));
        await Wait(0.1);
        Face(p, downed.GlobalPosition);
        p.Rig.SetLook(p.Rig.Look.Yaw, -0.3f);
        float hh = downed.Health.Current;
        for (int i = 0; i < 4 && downed.Health.Current >= hh; i++)
        {
            p.RestoreStamina();
            p.Combat.TryAttack(false);
            await Wait(0.5);
        }
        Check(downed.Health.Current < hh, "the sword hits a grounded harpy");

        // An arrow reaches one in the air.
        var flier = harpies[1];
        flier.Frozen = true;
        flier.GlobalPosition = p.GlobalPosition + new Vector3(0, 6f, -9f);
        await Wait(0.1);
        var from = flier.GlobalPosition + new Vector3(0, 0.4f, 5f); // open air beside it
        var to = flier.GlobalPosition + Vector3.Up * 0.4f;
        float tt = from.DistanceTo(to) / Projectile.ArrowSpeed;
        var aim = (to + Vector3.Up * 0.5f * Projectile.ArrowGravity * tt * tt - from).Normalized();
        float fh = flier.Health.Current;
        Net.Instance.SpawnProjectile(p, Projectile.Arrow, from, aim * Projectile.ArrowSpeed);
        Check(await Until(() => flier.Health.Current < fh, 3), $"an arrow hits a harpy in the air ({flier.GlobalPosition.Y:0.0} m up)");

        // Close-up, then finish them: feathers, a falling body, a quiet roost.
        var cam = new Camera3D { Fov = 45f };
        GetTree().CurrentScene.AddChild(cam);
        cam.GlobalPosition = flier.GlobalPosition + new Vector3(2.2f, 0.6f, 2.2f);
        cam.LookAt(flier.GlobalPosition + Vector3.Up * 0.4f, Vector3.Up);
        cam.MakeCurrent();
        Hud.Instance.Visible = false;
        await Wait(0.4);
        await Shot("33_harpy_closeup");
        cam.QueueFree();
        p.Rig.GetNode<Camera3D>("Pivot/SpringArm3D/Camera3D").MakeCurrent();
        Hud.Instance.Visible = true;

        int feathers = Have("harpy_feather");
        foreach (var h in harpies) if (!h.Health.IsDead) h.Health.TakeDamage(1000, p);
        await Wait(0.2);
        Check(Have("harpy_feather") == feathers + Harpy.Feathers * harpies.Count, $"each harpy drops {Harpy.Feathers} feathers ({Have("harpy_feather") - feathers})");
        Check(wild.RoostCleared(0), "the roost counts as cleared");
        Check(await Until(() => flier.GlobalPosition.Y < 1f, 4), "a dead harpy falls out of the sky");
        Check(await Until(() => !wild.Harpies.Any(), 6), "the bodies are cleared away");

        // The cleared roost is saved.
        gs.SaveGame(true);
        gs.LoadGame(true);
        await Wait(0.3);
        Check(wild.RoostCleared(0), "the cleared roost survives save/load");
        p.Health.Reset();
    }

    /// <summary>Armour, enchanting and the medieval UI (GDD §6 "Armour, gear and enchantments"):
    /// craft pieces at the workbench, wear them (stats, looks), keep them at a chest, rune shards
    /// from raiders, enchant at a Rune Altar, save / load, and the new boards and HUD.</summary>
    private async Task GearAndUi(GameState gs, PlayerController p)
    {
        Hud.Instance.Visible = true;
        Hud.Instance.CloseMenus();
        gs.TimeOfDay = 0.42f;
        var net = Net.Instance;
        Check(UiTheme.Tex("panel") != null && UiTheme.Tex("button") != null && UiTheme.Icon("leather_cap") != null, "the UI kit and item icons load");
        Check(ItemDb.Recipes.Count(r => ItemDb.Get(r.OutputItem)?.IsWearable ?? false) >= 10, "the workbench has armour recipes");
        Check(ItemDb.Get("leather_cap@thorns")?.DisplayName == "Leather Cap of Thorns" && ItemDb.Get("leather_cap@bogus") == null, "enchanted ids make named copies (and bad ones don't)");
        Check(!ItemDb.Items.ContainsKey("leather_cap@thorns"), "enchanted copies stay out of the trader's lists");

        // Craft at the workbench.
        var bench = gs.PlacedBuildings.FirstOrDefault(b => b.Id == "workbench")?.Node as CraftingStation;
        Check(bench != null, "there's a workbench to craft at");
        if (bench == null) return;
        p.Teleport(bench.GlobalPosition + new Vector3(1.4f, 0.1f, 0.6f));
        await Wait(0.2);
        gs.Inventory.Add("wolf_pelt", 24);
        gs.Inventory.Add("harpy_feather", 12);
        gs.Inventory.Add("iron_nails", 20);
        gs.Inventory.Add("wood", 6);
        gs.Inventory.Add("stone", 4);
        bench.Interact(p);
        await Wait(0.4);
        await Shot("35_crafting_armour");
        foreach (var item in new[] { "leather_jerkin", "leather_cap", "feather_cloak", "grip_boots", "iron_helm" })
            bench.RequestCraft(ItemDb.Recipes.First(r => r.OutputItem == item));
        Check(await Until(() => p.Pack.Count("leather_jerkin") == 1 && p.Pack.Count("iron_helm") == 1, 5), "crafted armour goes into the crafter's pack");
        Hud.Instance.CloseMenus();
        Supplies.Deposit(p);
        Check(p.Pack.Count("leather_jerkin") == 1 && p.Pack.Count("grip_boots") == 1, "armour stays in your pack at the chest (personal gear)");

        // Wear it.
        float baseMax = p.Health.Max;
        foreach (var item in new[] { "leather_jerkin", "leather_cap", "feather_cloak", "grip_boots" }) net.RequestWear(item);
        Check(await Until(() => p.Worn.Count == 4, 3), $"wearing four pieces ({p.Worn.Count})");
        Check(p.Pack.Count("leather_jerkin") == 0, "a worn piece leaves the pack");
        Check(Mathf.IsEqualApprox(Gear.Armor(p), 12f), $"leather set armour adds up ({Gear.Armor(p)})");
        net.RequestWear("iron_helm");
        Check(await Until(() => p.Worn.GetValueOrDefault(Gear.Head) == "iron_helm", 3) && p.Pack.Count("leather_cap") == 1, "wearing a helm swaps the cap back into the pack");
        Check(Mathf.IsEqualApprox(Gear.Armor(p), 15f) && Gear.StaminaRegenMult(p) < 1f, $"the iron helm: more armour, heavy ({Gear.Armor(p)}, regen ×{Gear.StaminaRegenMult(p):0.00})");
        Check(Gear.FallDamageMult(p) < 0.45f && Gear.Climb(p).IgnoreWet, "feather cloak and grip boots do their thing");

        // Armour cuts damage.
        p.Health.Heal(1000);
        await Wait(0.1);
        float hp = p.Health.Current;
        net.ServerDamagePlayer(p, 20f, null);
        float expected = 20f * (1f - Gear.Reduction(p));
        Check(Mathf.Abs(hp - p.Health.Current - expected) < 0.2f, $"armour takes {Gear.Reduction(p) * 100f:0}% off a hit ({hp - p.Health.Current:0.0} of 20)");
        p.Health.Heal(1000);

        // Looks: helmet (Knight) or cap (Rogue), and the cloak's colour on the cape.
        var mesh = p.Body.GetNode<CharacterModel>("Mesh");
        await Wait(0.3);
        bool helmed = p.Look.Body == Appearance.Knight
            ? mesh.FindChild("Knight_Helmet", true, false) is Node3D { Visible: true }
            : mesh.FindChild("HeadGear", true, false) != null;
        Check(helmed, $"head armour shows on the {p.Look.Body}");
        Check(mesh.Look.CapeColor != "" && mesh.Look.Cape, "the cloak shows as a coloured cape");

        // Character sheet.
        Hud.Instance.OpenCharacter();
        await Wait(0.5);
        Check(Hud.Instance.CharacterOpen && gs.UiOpen, "I opens the character sheet");
        await Shot("36_character_sheet");
        net.RequestTakeOff(Gear.Head);
        Check(await Until(() => !p.Worn.ContainsKey(Gear.Head) && p.Pack.Count("iron_helm") == 1, 3), "taking a piece off puts it back in the pack");
        Hud.Instance.CloseMenus();

        // Rune shards: a raider carries one.
        int shards = p.Pack.Count(Gear.ShardId);
        var raid = Wildlife.Instance.StartRaid(p.GlobalPosition + new Vector3(0, 0, 14));
        await Wait(0.3);
        raid[0].Health.TakeDamage(9999, p);
        Check(await Until(() => p.Pack.Count(Gear.ShardId) == shards + 1, 3), "a raider drops a rune shard");
        foreach (var r in raid) if (!r.Health.IsDead) r.Health.TakeDamage(9999, p);
        await Wait(0.5);

        // The Rune Altar: enchant the jerkin you wear, and the cap in your pack.
        var altarPos = bench.GlobalPosition + new Vector3(-3.5f, 0, 2f);
        var altar = gs.PlaceBuilding(ItemDb.GetBuilding("rune_altar"), altarPos, 0.4f)?.Node as CraftingStation;
        Check(altar is RuneAltar && altar.StationId == Gear.AltarStation, "a Rune Altar can be built");
        if (altar == null) return;
        p.Teleport(altarPos + new Vector3(0, 0.1f, 1.8f));
        p.Rig.SetLook(Mathf.Pi, -0.2f);
        await Wait(0.2);
        Supplies.Give(p, Gear.ShardId, 5);
        Supplies.Give(p, "coin", 40);
        int coins = Supplies.Count(p, "coin");
        net.RequestEnchant("leather_jerkin", Gear.Ward, altar);
        Check(await Until(() => p.Worn.GetValueOrDefault(Gear.Body) == "leather_jerkin@ward", 3), "enchanting a worn piece swaps it for the enchanted one");
        Check(Mathf.IsEqualApprox(Gear.Armor(p), 13f), $"Warding adds 4 armour (jerkin, cloak, boots: 9 + 4) ({Gear.Armor(p)})");
        Check(Supplies.Count(p, "coin") == coins - Gear.CoinCost, "enchanting costs coins (and shards)");
        float max = p.Health.Max;
        net.RequestEnchant("leather_cap", Gear.Vigor, altar);
        Check(await Until(() => p.Pack.Count("leather_cap@vigor") == 1 && p.Pack.Count("leather_cap") == 0, 3), "enchanting a piece in the pack");
        net.RequestWear("leather_cap@vigor");
        Check(await Until(() => Mathf.IsEqualApprox(p.Health.Max, max + 12f), 3), $"Vigor adds max health ({max} → {p.Health.Max})");
        net.RequestEnchant("leather_jerkin@ward", Gear.Thorns, altar);
        Check(await Until(() => p.Worn.GetValueOrDefault(Gear.Body) == "leather_jerkin@thorns", 3), "enchanting again replaces the old enchantment");
        altar.Interact(p);
        await Wait(0.5);
        await Shot("37_enchanting");
        Hud.Instance.CloseMenus();

        // Thorns: a wolf that bites you takes some back.
        var wolf = Wildlife.Instance.SpawnPack(p.GlobalPosition + new Vector3(2, 0, 0), 1).FirstOrDefault();
        await Wait(0.2);
        if (wolf != null)
        {
            wolf.Frozen = true;
            wolf.GlobalPosition = p.GlobalPosition + new Vector3(1.5f, 0, 0);
            float wolfHp = wolf.Health.Current;
            net.ServerDamagePlayer(p, 20f, wolf);
            Check(wolf.Health.Current < wolfHp, $"thorns hurt the attacker ({wolfHp:0} → {wolf.Health.Current:0})");
            wolf.Health.TakeDamage(9999, p);
        }
        p.Health.Heal(1000);

        // Gear survives save / load.
        var wornBefore = new System.Collections.Generic.Dictionary<string, string>(p.Worn);
        gs.SaveGame(silent: true);
        net.RequestTakeOff(Gear.Body);
        await Wait(0.2);
        gs.LoadGame(silent: true);
        await Wait(0.5);
        Check(p.Worn.Count == wornBefore.Count && wornBefore.All(kv => p.Worn.GetValueOrDefault(kv.Key) == kv.Value), "worn gear is saved and loaded");

        // The look in armour, and the new HUD.
        p.Rig.SetView(false, true);
        p.Teleport(gs.ClaimCenter + new Vector3(0, 0.1f, 14f));
        p.Rig.SetLook(Mathf.Pi + 0.5f, -0.15f);
        await Wait(0.8);
        await Shot("38_armour_look");
        p.Rig.SetLook(0.3f, 0f);
        await Wait(0.4);
        await Shot("39_hud");
        Hud.Instance.OpenGameMenu();
        await Wait(0.4);
        await Shot("40_game_menu");
        Hud.Instance.CloseMenus();
    }

    /// <summary>Raids (GDD §6, §9): bandits march on the base at dusk; a watchtower shoots them;
    /// they wreck buildings and loot the stockpile; killing them gets the loot back; the base is
    /// patched up at dawn.</summary>
    private static void Pad(JoyButton button, bool pressed) =>
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed, Device = 0 });

    private async Task Tap(JoyButton button)
    {
        Pad(button, true);
        await Wait(0.08);
        Pad(button, false);
        await Wait(0.12);
    }

    private static void Stick(JoyAxis axis, float value) =>
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = axis, AxisValue = value, Device = 0 });

    /// <summary>Goals, the journal, errands, tips, the marker; then the same game on a controller:
    /// stick move and look, a trigger swing (one per pull), menus with the d-pad, button prompts.</summary>
    private async Task ControllerAndGuidance(GameState gs, PlayerController p)
    {
        var hud = Hud.Instance;
        var journal = Journal.Instance;
        hud.CloseMenus();
        gs.TimeOfDay = 0.4f;
        await Wait(1.0);

        // By now the run has gathered, stored, built, crafted, slept, been to Ashby, traded,
        // farmed, cleared the camp, worn and enchanted armour and built the cottage.
        string[] expect = { "gather", "stash", "workbench", "weapon", "ashby", "camp", "farm", "armour", "falls", "enchant", "cottage" };
        var missing = expect.Where(id => !journal.Completed.Contains(id)).ToList();
        Check(missing.Count == 0, $"goals ticked off by playing ({journal.Completed.Count} done{(missing.Count > 0 ? ", missing " + string.Join(", ", missing) : "")})");

        // Save and load keep the journal.
        var saved = journal.ToDict();
        journal.Reset();
        Check(journal.Current?.Id == "gather", "a new world starts on the first goal");
        journal.Apply(saved);
        Check(journal.Completed.Count == ((Godot.Collections.Array)saved["done"]).Count, "the journal saves and loads");

        // The tracker and the marker, with the journal rewound to "Follow the road south to Ashby".
        journal.Apply(new Godot.Collections.Dictionary
        {
            ["done"] = new Godot.Collections.Array { "gather", "stash", "workbench", "weapon", "sleep" },
            ["facts"] = new Godot.Collections.Array(), ["errands"] = 0,
        });
        Check(journal.Current?.Id == "ashby", "the tracker moves on to the next goal");
        p.Teleport(new Vector3(6, 0.1f, -4));
        p.Rig.SetView(false, true);
        p.Rig.SetLook(Mathf.Pi, -0.15f); // face south, down the road
        await Wait(0.6);
        Check(hud.MarkerShown, "a marker points to Ashby");
        await Shot("41_goal_tracker");
        p.Rig.SetLook(0, -0.15f); // face away: the marker goes to the edge
        await Wait(0.3);
        Check(hud.MarkerShown, "...and stays on the edge when it's behind you");
        hud.OpenCharacter(journal: true);
        await Wait(0.5);
        Check(hud.CharacterOpen && hud.JournalTabOpen, "the journal tab opens (J)");
        await Shot("42_journal");
        hud.CloseMenus();
        journal.Apply(saved);

        // A goal done while you watch: the banner.
        int done = journal.Completed.Count;
        journal.Completed.Remove("cottage");
        journal.Apply(journal.ToDict());
        Check(journal.Current?.Id == "cottage", "an unfinished goal is current");
        Check(await Until(() => journal.Completed.Contains("cottage"), 3), "the server notices the goal is done");
        await Wait(0.3);
        await Shot("43_goal_complete");

        // An errand for Marta.
        var trader = GetTree().CurrentScene.FindChild("Trader", true, false) as Trader;
        Check(trader != null, "Marta is in Ashby");
        if (trader != null)
        {
            p.Teleport(trader.GlobalPosition + trader.GlobalBasis.Z * 1.6f + Vector3.Up * 0.1f);
            await Wait(0.3);
            var errand = journal.CurrentErrand;
            int coins = p.Pack.Count("coin");
            Supplies.Give(p, errand.Item, errand.Count);
            hud.OpenTrade(trader);
            await Wait(0.4);
            await Shot("44_errand");
            Net.Instance.RequestErrand(trader);
            Check(await Until(() => journal.ErrandsDone == 1, 3), "an errand is handed in");
            Check(p.Pack.Count("coin") == coins + errand.Reward && journal.CurrentErrand != errand, $"...pays {errand.Reward} coins and Marta asks for something else");
            hud.CloseMenus();
        }

        // Tips: once each, when they matter.
        Hud.TipsEnabled = true;
        hud.ResetTips();
        p.Health.TakeDamage(p.Health.Max * 0.75f, null);
        Check(await Until(() => hud.TipShowing == "hurt", 2), "a tip when you're badly hurt");
        await Wait(0.4);
        await Shot("45_tip");
        p.Health.Heal(p.Health.Max);
        Hud.TipsEnabled = false;
        hud.ResetTips();

        // ---- Controller.
        p.Teleport(new Vector3(6, 0.1f, -4));
        p.Rig.SetView(true, true);
        p.Rig.SetLook(0, -0.1f);
        await Wait(0.3);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        await Tap(JoyButton.Y); // any button: the controller is in use now (and it's the ability)
        Check(Gamepad.UsingPad && Gamepad.ButtonName("interact") == "X", "pressing a pad button switches the prompts to the controller (X to use)");
        Gamepad.SetDevice(true, 0, Gamepad.Style.PlayStation);
        Check(Gamepad.ButtonName("interact") == "□" && Gamepad.Format("{jump} jump") == "✕ jump", "a DualSense shows its own symbols");
        p.Ability.ResetCooldown();

        // Right stick turns; left stick walks.
        float yaw = p.Rig.Look.Yaw;
        Stick(JoyAxis.RightX, 1f);
        await Wait(0.4);
        Stick(JoyAxis.RightX, 0f);
        Check(Mathf.Abs(Mathf.AngleDifference(yaw, p.Rig.Look.Yaw)) > 0.5f, $"the right stick turns the camera ({Mathf.RadToDeg(Mathf.AngleDifference(yaw, p.Rig.Look.Yaw)):0}°)");
        var from = p.GlobalPosition;
        Stick(JoyAxis.LeftY, -1f);
        await Wait(0.7);
        Stick(JoyAxis.LeftY, 0f);
        Check(p.GlobalPosition.DistanceTo(from) > 1.5f, $"the left stick walks ({p.GlobalPosition.DistanceTo(from):0.0} m)");

        // A trigger pull is one swing, however many motion events it sends.
        p.RestoreStamina();
        await Wait(0.6);
        float st = p.Stamina;
        Stick(JoyAxis.TriggerRight, 0.6f);
        Stick(JoyAxis.TriggerRight, 0.8f);
        Stick(JoyAxis.TriggerRight, 1f);
        await Wait(0.05);
        float spent = st - p.Stamina;
        Stick(JoyAxis.TriggerRight, 0f);
        Check(spent >= p.Combat.LightStamina - 1f && spent < p.Combat.LightStamina * 1.5f, $"RT swings once per pull (spent {spent:0.#} stamina)");
        await Wait(0.6);

        // Menus: Options opens, the d-pad moves, B goes back, and B doesn't also roll.
        await Tap(JoyButton.Start);
        Check(gs.UiOpen && hud.MenuOpen, "Options / Menu opens the menu");
        await Wait(0.1);
        var first = GetViewport().GuiGetFocusOwner();
        Check(first is Button { Text: "Resume" }, $"the first button has the focus ({(first as Button)?.Text ?? "none"})");
        await Tap(JoyButton.DpadDown);
        var second = GetViewport().GuiGetFocusOwner();
        Check(second != null && second != first, $"the d-pad moves the focus ({(second as Button)?.Text ?? "none"})");
        await Shot("46_pad_menu");
        await Tap(JoyButton.B);
        Check(!gs.UiOpen, "○ / B closes the menu");
        Check(!p.Dodging, "...without rolling");

        // D-pad down: the character sheet; R1 flips to the journal.
        await Tap(JoyButton.DpadDown);
        Check(hud.CharacterOpen, "d-pad ▼ opens the character sheet");
        await Tap(JoyButton.RightShoulder);
        Check(hud.JournalTabOpen, "R1 / RB switches to the journal");
        await Wait(0.3);
        await Shot("47_pad_journal");
        await Tap(JoyButton.B);
        Check(!gs.UiOpen, "...and B closes it");

        // Prompts on the HUD follow the pad.
        hud.OpenHelp();
        await Wait(0.4);
        await Shot("48_pad_help");
        hud.CloseMenus();
        Gamepad.Rumble(0.2f, 0.2f, 0.05f);

        // Back to the keyboard.
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Shift, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Shift, Pressed = false });
        await Wait(0.1);
        Check(!Gamepad.UsingPad && Gamepad.ButtonName("interact") == "E", "a key press switches the prompts back (E)");
    }

    private async Task Raids(GameState gs, PlayerController p)
    {
        var wild = Wildlife.Instance;
        gs.TimeOfDay = 0.72f;
        foreach (var e in wild.Enemies.Values.ToList()) wild.Despawn(e);
        await Wait(0.3);
        // Watch from the Falls Blossom (a sanctuary, far from the fight).
        p.Teleport(HomeCliffs.FallsShrinePos + new Vector3(3, 0.1f, 3));
        await Wait(0.2);

        // A watchtower south of the yard, on the raiders' road.
        var towerPos = new Vector3(4, 0, 6);
        var tower = gs.PlaceBuilding(ItemDb.GetBuilding("watchtower"), towerPos, 0f);
        Check(tower?.Node is Watchtower, "a watchtower can be built");
        await Wait(2.0); // let the navmesh catch up

        int shots = Watchtower.Shots;
        var raiders = wild.StartRaid(new Vector3(2, 0, 34));
        Check(raiders.Count == Wildlife.SoloRaidSize && raiders.All(r => r.Raider) && wild.RaidActive,
            $"a raid: {Wildlife.SoloRaidSize} raiders with torches ({raiders.Count})");
        Check(await Until(() => Watchtower.Shots > shots, 10), "the watchtower shoots at the raiders");
        Check(await Until(() => raiders.Any(r => r.Health.Current < r.Health.Max), 10), "...and its arrows hit");
        Check(await Until(() => raiders.Any(r => r.Health.IsDead), 30), "the watchtower brings down a raider");
        // The view from the yard: raiders with torches, the tower shooting.
        var cam = new Camera3D { Fov = 55f };
        GetTree().CurrentScene.AddChild(cam);
        var mid = raiders.Where(r => !r.Health.IsDead).Select(r => r.GlobalPosition).DefaultIfEmpty(towerPos).Aggregate((a, b) => (a + b) / 2f);
        cam.GlobalPosition = towerPos + new Vector3(-9, 4, -9);
        cam.LookAt(mid + Vector3.Up, Vector3.Up);
        cam.MakeCurrent();
        await Wait(0.3);
        await Shot("34_raid");
        cam.QueueFree();
        p.Rig.GetNode<Camera3D>("Pivot/SpringArm3D/Camera3D").MakeCurrent();
        foreach (var r in raiders.Where(r => !r.Health.IsDead)) r.Health.TakeDamage(1000, p);
        await Wait(0.3);
        Check(!wild.RaidActive, "with the raiders dead, the raid is over");
        if (gs.FindBuilding(tower.NetId) != null) Net.Instance.ServerRemoveBuilding(tower, refund: false);
        await Until(() => !wild.Raiders.Any(), 8);

        // Second raid, right by the home chest: they wreck a bedroll and loot the stockpile.
        gs.Inventory.Add("stone", 30);
        var chest = Find("HomeChest") as Node3D;
        var bedPos = chest.GlobalPosition + new Vector3(3.5f, 0, 5f);
        var bedroll = gs.PlaceBuilding(ItemDb.GetBuilding("bedroll"), bedPos, 0f);
        await Wait(2.0);
        var stock = gs.Inventory.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
        int stockTotal = stock.Values.Sum();
        raiders = wild.StartRaid(bedPos + new Vector3(0, 0, 3f));
        Check(await Until(() => gs.FindBuilding(bedroll.NetId) == null, 20), "raiders wreck a bedroll");
        Check(await Until(() => raiders.Any(r => r.CarriedCount > 0), 20), $"raiders loot the stockpile ({raiders.Sum(r => r.CarriedCount)} items)");
        await Wait(1.5);
        int carried = raiders.Sum(r => r.CarriedCount);
        Check(gs.Inventory.Items.Values.Sum() == stockTotal - carried, "what they carry is gone from the stockpile");
        foreach (var r in raiders) r.Health.TakeDamage(1000, p);
        await Wait(0.3);
        Check(gs.Inventory.Items.Values.Sum() == stockTotal, $"killing the raiders gets the loot back ({gs.Inventory.Items.Values.Sum()} of {stockTotal})");
        Check(!wild.RaidActive && wild.RaidCount >= 2, "the second raid is beaten off");
        await Until(() => !wild.Raiders.Any(), 8);

        // Damage is patched up at dawn.
        var bench = gs.PlacedBuildings.FirstOrDefault(b => b.Id == "workbench");
        if (bench != null) bench.Damage = 30f;
        gs.TimeOfDay = 0.95f;
        await Wait(0.3);
        gs.TimeOfDay = 0.3f;
        await Wait(0.3);
        Check(bench != null && bench.Damage == 0f, "damaged buildings are repaired at dawn");
        p.Teleport(gs.WorldSpawn + new Vector3(3, 0.1f, 4));
        p.Health.Reset();
        await Wait(0.3);
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
        foreach (var s in new[] { "chop", "mine", "step", "swing_light", "hit_flesh", "build", "eat", "water", "chest", "pickup", "dodge", "parry", "whirlwind", "bow", "firebolt", "explode", "heal", "vanish", "harpy_screech", "raid_horn", "glider_open" })
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
        Check(Have("roast_turnip") == 0 && Mathf.IsEqualApprox(p.Health.Max, p.BaseMaxHealth + p.LevelHealth + p.Class.BonusHealth + 25f),
            $"at full health a roast turnip is still worth it: +25 max health ({p.Health.Max})");
        Net.Instance.RequestEat();
        Check(Have("roast_turnip") == 0, "nothing more to gain: no eating");
        p.ApplyBuff(PlayerController.BuffVigor, 0f, 0f); // (test) let it wear off
        Check(Mathf.IsEqualApprox(p.Health.Max, p.BaseMaxHealth + p.LevelHealth + p.Class.BonusHealth) && p.Health.Current <= p.Health.Max, "when the buff wears off max health goes back");
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

    /// <summary>XP and levels (GDD §6): the curve, what the run so far earned, a level-up with
    /// its health and stamina, the banner and bar, and a save/load round trip.</summary>
    private async Task Xp(GameState gs, PlayerController p)
    {
        var net = Net.Instance;
        Check(Levels.LevelFor(0) == 1 && Levels.LevelFor(Levels.StartOf(5)) == 5 && Levels.LevelFor(Levels.StartOf(5) - 1) == 4
            && Levels.LevelFor(int.MaxValue / 2) == Levels.MaxLevel && Levels.ToNext(1) == 120,
            $"the level curve (level 5 at {Levels.StartOf(5)} XP, level 20 at {Levels.StartOf(20)})");
        Check(p.Xp > 0 && p.XpFirsts.Contains("kill:wolf") && p.XpFirsts.Contains("craft:wooden_sword"),
            $"the run so far earned XP from kills and crafts ({p.Xp} XP, level {p.Level}, {p.XpFirsts.Count} firsts)");

        p.Teleport(new Vector3(0, 0.1f, 20f));
        await Wait(0.3);
        int level = p.Level, xpBefore = p.Xp;
        float maxHealth = p.Health.Max;
        net.ServerAwardXp(p, Levels.StartOf(level + 1) - p.Xp, "a test");
        await Wait(0.3);
        Check(p.Level == level + 1, $"enough XP levels you up ({level} -> {p.Level})");
        Check(Mathf.IsEqualApprox(p.Health.Max, maxHealth + Levels.HealthPerLevel) && Mathf.IsEqualApprox(p.Health.Current, p.Health.Max),
            $"a level adds 2 max health, healed ({p.Health.Current:0} / {p.Health.Max:0})");
        Check(Mathf.IsEqualApprox(p.MaxStamina, p.BaseStamina + (p.Level - 1) * Levels.StaminaPerLevel), $"and 1 max stamina per level ({p.MaxStamina:0})");
        Check(Hud.Instance.LevelBannerShown && Hud.Instance.XpBarLevelText == $"Lv {p.Level}", "the level-up banner and the XP bar show the new level");
        await Shot("24_level_up");

        int saved = p.Xp;
        gs.SaveGame(silent: true);
        net.ServerAwardXp(p, 5000);
        p.XpFirsts.Remove("kill:wolf");
        gs.LoadGame(silent: true);
        await Wait(0.3);
        Check(p.Xp == saved && p.Level == level + 1 && p.XpFirsts.Contains("kill:wolf"), $"save/load keeps XP, level and firsts ({p.Xp} XP)");
        Check(saved > xpBefore, "XP only goes up in play");
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
        await Looks(gs, p);
        await Classes(gs, p);
        await FallsValley(gs, p);
        await Swimming(gs, p);
        await Raids(gs, p);
        await GearAndUi(gs, p);
        await ControllerAndGuidance(gs, p);
        await Xp(gs, p);

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
