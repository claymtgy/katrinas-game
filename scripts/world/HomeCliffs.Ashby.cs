using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>South of the plateau: the vale with the road to the Village of Ashby (trader, safe
/// haven) and the bandit camp off the road to the east. Also the home tiers (shack → cottage),
/// rebuilt whenever GameState.HomeTier changes. GDD §6, §11.</summary>
public partial class HomeCliffs
{
    public static readonly Vector3 AshbyCenter = new(0, 0, 195);
    public const float AshbySafeRadius = 28f;
    private const float ValeEndZ = 246f;

    private static readonly Color Plaster = new(0.86f, 0.8f, 0.68f);
    private static readonly Color Cloth = new(0.62f, 0.2f, 0.18f);
    private static readonly Color Canvas = new(0.72f, 0.64f, 0.5f);

    private Node3D _home;

    // ================================================================== the vale

    private void BuildVale()
    {
        // Ground continues south through the gap in the wall (z 90..246).
        var ground = Block(new Vector3(0, -6, (90 + ValeEndZ) / 2f), new Vector3(120, 12, ValeEndZ - 90), Vector3.Zero, Grass);
        ((MeshInstance3D)ground.GetChild(0)).MaterialOverride = GroundMaterial;

        // Rock walls east and west, and the far end.
        foreach (float side in new[] { -1f, 1f })
            for (float z = 101; z <= ValeEndZ; z += 9)
            {
                float h = _rng.RandfRange(16, 34);
                var size = new Vector3(_rng.RandfRange(12, 18), h, _rng.RandfRange(10, 14));
                var pos = new Vector3(side * (64 + _rng.RandfRange(0, 6)), h / 2f - 4, z + _rng.RandfRange(-2, 2));
                Rock(pos, size, new Vector3(_rng.RandfRange(-0.1f, 0.1f), _rng.RandfRange(-0.4f, 0.4f), side * _rng.RandfRange(0.05f, 0.2f)),
                    _rng.Randf() < 0.35f ? SlateLight : Slate);
                if (_rng.Randf() < 0.5f) Crag(pos + new Vector3(0, h / 2f - 3f, 0), size.X * 0.8f, _rng.RandfRange(8, 16));
            }
        for (float x = -64; x <= 64; x += 10)
        {
            float h = _rng.RandfRange(18, 32);
            Rock(new Vector3(x, h / 2f - 3, ValeEndZ + 4 + _rng.RandfRange(0, 4)), new Vector3(13, h, 11),
                new Vector3(0, _rng.RandfRange(-0.3f, 0.3f), _rng.RandfRange(-0.1f, 0.1f)), Slate);
        }

        // The road on to Ashby, and a track east to the bandit camp.
        var road = Mat(Dirt.Darkened(0.05f));
        Visual(new BoxMesh { Size = new Vector3(4, 0.04f, 92) }, road, new Vector3(1, 0.02f, 134), new Vector3(0, 0.02f, 0));
        var camp = Wildlife.CampSites[0];
        var track = new Vector3(camp.X - 2f, 0, camp.Z) - new Vector3(1, 0, 118);
        Visual(new BoxMesh { Size = new Vector3(2.2f, 0.035f, track.Length()) }, Mat(Dirt.Darkened(0.15f)),
            new Vector3(1, 0.018f, 118) + track / 2f, new Vector3(0, Mathf.Atan2(track.X, track.Z), 0));

        ScatterVale();
    }

    private void ScatterVale()
    {
        var tree = GD.Load<PackedScene>("res://scenes/props/Tree.tscn");
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var grain = GD.Load<PackedScene>("res://scenes/props/WildGrain.tscn");
        var camp = Wildlife.CampSites[0];
        var placed = new List<Vector3> { AshbyCenter, camp };

        bool Clear(Vector3 p, float spacing)
        {
            if (Mathf.Abs(p.X - 1) < 5) return false;                 // road
            if (p.DistanceTo(AshbyCenter) < 30) return false;         // village
            if (p.DistanceTo(camp) < 14) return false;                // camp
            foreach (var q in placed) if (p.DistanceTo(q) < spacing) return false;
            return true;
        }

        int trees = 0, rocks = 0, grains = 0, guard = 0;
        while ((trees < 22 || rocks < 9 || grains < 12) && guard++ < 3000)
        {
            var p = new Vector3(_rng.RandfRange(-54, 54), 0, _rng.RandfRange(102, 238));
            if (!Clear(p, 5f)) continue;
            if (trees < 22) { Spawn(tree, p, placed); trees++; }
            else if (rocks < 9) { Spawn(rock, p, placed); rocks++; }
            else { Spawn(grain, p, placed); grains++; }
        }

        // A few wild grain tufts on the home plateau, so farming can start before Ashby.
        foreach (var p in new[] { new Vector3(-20, 0, 8), new Vector3(-24, 0, 14), new Vector3(24, 0, 20), new Vector3(28, 0, 26), new Vector3(-30, 0, 40) })
            Spawn(grain, p, placed);

        string[] small = { "Grass_Common_Short", "Grass_Common_Tall", "Grass_Wispy_Tall", "Flower_3_Group", "Flower_4_Group", "Clover_1", "Fern_1", "Plant_7" };
        for (int i = 0, n = 0; n < 150 && i < 2000; i++)
        {
            var p = new Vector3(_rng.RandfRange(-56, 56), 0, _rng.RandfRange(100, 240));
            if (Mathf.Abs(p.X - 1) < 2.5f || p.DistanceTo(AshbyCenter) < 16 || p.DistanceTo(camp) < 8) continue;
            Prop(Models.Nature(small[_rng.RandiRange(0, small.Length - 1)]), p, _rng.RandfRange(0, Mathf.Tau), _rng.RandfRange(0.8f, 1.4f));
            n++;
        }
    }

    // ================================================================== Ashby

    private void BuildAshby()
    {
        var root = new Node3D { Name = "Ashby", Position = AshbyCenter };
        AddChild(root);
        GameState.Instance.SafeZones.Add((AshbyCenter, AshbySafeRadius));

        // Village green and square.
        Visual(new BoxMesh { Size = new Vector3(30, 0.04f, 30) }, Mat(new Color(0.58f, 0.5f, 0.4f)), new Vector3(0, 0.025f, 0), Vector3.Zero, root);

        // Houses around the square, doors toward the well.
        House(root, new Vector3(-14, 0, -5), 3, 4, "Plaster", 0);
        House(root, new Vector3(14, 0, -7), 3, 3, "UnevenBrick", 1);
        House(root, new Vector3(-12, 0, 12), 4, 4, "Plaster", 2);
        House(root, new Vector3(12, 0, 13), 3, 4, "Plaster", 3);
        House(root, new Vector3(0, 0, 20), 4, 4, "UnevenBrick", 4);

        // Well in the middle.
        var well = new WaterSource
        {
            Name = "AshbyWell", SourceName = "village well", Position = new Vector3(0, 0, 3), BuildVisual = true,
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        root.AddChild(well);
        well.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.95f, Height = 0.9f }, Position = new Vector3(0, 0.45f, 0) });

        // Lamps (they glow at night like everything else lit).
        foreach (var p in new[] { new Vector3(-10, 0, -10), new Vector3(6, 0, -8), new Vector3(-8, 0, 7), new Vector3(8, 0, 7), new Vector3(2, 0, -18) })
        {
            Visual(new BoxMesh { Size = new Vector3(0.16f, 2.6f, 0.16f) }, Mat(WoodDark), p + new Vector3(0, 1.3f, 0), Vector3.Zero, root);
            var glow = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.75f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.65f, 0.3f), EmissionEnergyMultiplier = 3f };
            Visual(new BoxMesh { Size = new Vector3(0.32f, 0.36f, 0.32f) }, glow, p + new Vector3(0, 2.75f, 0), Vector3.Zero, root);
            root.AddChild(new OmniLight3D { Position = p + new Vector3(0, 2.7f, 0), LightColor = new Color(1f, 0.7f, 0.4f), LightEnergy = 1.6f, OmniRange = 9f });
        }

        BuildTraderStall(root);

        var board = new InfoSign
        {
            Name = "NoticeBoard",
            Title = "Ashby notice board",
            Message = "BANDITS have taken the old camp east of the road. Their captain carries plans stolen from the carpenter. " +
                      "Marta buys pelts and crops, and sells seeds and iron nails. No beast or brigand dares the village lamps.",
            Position = new Vector3(4, 0, -12),
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        root.AddChild(board);
        board.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.8f, 2.2f, 0.4f) }, Position = new Vector3(0, 1.1f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(WoodDark), new Vector3(-0.8f, 1, 0), Vector3.Zero, board);
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(WoodDark), new Vector3(0.8f, 1, 0), Vector3.Zero, board);
        Visual(new BoxMesh { Size = new Vector3(1.8f, 1.0f, 0.08f) }, Mat(Wood), new Vector3(0, 1.5f, 0), Vector3.Zero, board);
        board.AddChild(new Label3D { Text = "NOTICES", Position = new Vector3(0, 1.75f, -0.06f), FontSize = 40, PixelSize = 0.006f, Rotation = new Vector3(0, Mathf.Pi, 0), Modulate = new Color(0.15f, 0.1f, 0.08f) });

        var label = new Label3D
        {
            Text = "Village of Ashby", Position = new Vector3(0, 7f, -14f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.01f, OutlineSize = 12,
            Modulate = Gold,
        };
        root.AddChild(label);
    }

    private static readonly Color Gold = new(1f, 0.82f, 0.45f);

    private void BuildTraderStall(Node3D village)
    {
        var stallPos = new Vector3(-5.5f, 0, -6);
        var trader = new Trader
        {
            Name = "Trader",
            TraderName = "Marta the Trader",
            Position = stallPos,
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        village.AddChild(trader);
        trader.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.8f, 1.8f, 0.8f) }, Position = new Vector3(0, 0.9f, 0) });
        var model = HumanoidModel.Create(Models.Hero, 1.7f,
            new[] { "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable" }, new Color(0.2f, 0.45f, 0.3f, 0.25f));
        trader.AddChild(model);
        trader.AddChild(new Label3D
        {
            Text = "Marta · Trader", Position = new Vector3(0, 2.2f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, PixelSize = 0.006f, OutlineSize = 10,
            Modulate = Gold,
        });

        // Counter, awning and goods.
        // The counter is part of the trader's body, so looking at it starts a trade.
        trader.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.6f, 1f, 0.6f) }, Position = new Vector3(0, 0.5f, -1.1f) });
        Visual(new BoxMesh { Size = new Vector3(2.6f, 1f, 0.6f) }, Mat(Wood), new Vector3(0, 0.5f, -1.1f), Vector3.Zero, trader);
        foreach (float x in new[] { -1.4f, 1.4f })
            Visual(new BoxMesh { Size = new Vector3(0.12f, 2.6f, 0.12f) }, Mat(WoodDark), stallPos + new Vector3(x, 1.3f, -1.3f), Vector3.Zero, village);
        Visual(new BoxMesh { Size = new Vector3(3.2f, 0.06f, 2.2f) }, Mat(Cloth), stallPos + new Vector3(0, 2.6f, -0.4f), new Vector3(0.25f, 0, 0), village);
        Prop(Models.Village("Prop_Crate"), stallPos + new Vector3(-1.9f, 0, 0.5f), 0.3f, 0.8f, village);
        Prop(Models.Village("Prop_Wagon"), stallPos + new Vector3(3.2f, 0, 0.8f), 1.3f, 1f, village);
        var basket = StyleMaterials.Toon(new Color(0.85f, 0.55f, 0.75f), 0.2f);
        Visual(new SphereMesh { Radius = 0.18f, Height = 0.3f }, basket, stallPos + new Vector3(-0.6f, 1.1f, -1.1f), Vector3.Zero, village);
        Visual(new SphereMesh { Radius = 0.18f, Height = 0.3f }, StyleMaterials.Toon(new Color(0.95f, 0.8f, 0.35f), 0.2f), stallPos + new Vector3(0.5f, 1.1f, -1.1f), Vector3.Zero, village);
    }

    /// <summary>A cottage-style house from the village kit: w × d wall segments (2 m each), door in
    /// the middle of the local +Z side, gabled tile roof. Solid (one collider) unless open = true,
    /// in which case each wall gets its own collider and the doorway is passable.</summary>
    private Node3D House(Node3D parent, Vector3 localPos, int w, int d, string style, int seed, bool open = false, string name = null)
    {
        var house = new Node3D { Name = name ?? $"House{seed}", Position = localPos };
        parent.AddChild(house);
        if (!open)
        {
            // Face the door toward the parent's origin (the village well).
            var to = -localPos;
            house.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
        }
        float hw = w, hd = d;
        string wall = $"Wall_{style}_Straight", window = $"Wall_{style}_Window_Wide_Round", door = $"Wall_{style}_Door_Round";
        if (style == "UnevenBrick") window = "Wall_UnevenBrick_Window_Wide_Round";

        void Side(Vector3 start, Vector3 step, float rotY, int count, bool front)
        {
            for (int i = 0; i < count; i++)
            {
                bool isDoor = front && i == count / 2;
                bool isWindow = !isDoor && (i % 2 == 1 || (front && count == 4 && i == 0));
                var pos = start + step * (i + 0.5f);
                var piece = Prop(Models.Village(isDoor ? door : isWindow ? window : wall), Vector3.Zero, rotY, 1f, house);
                piece.Position = pos;
                if (isDoor && !open)
                {
                    var leaf = Prop(Models.Village("Door_1_Round"), Vector3.Zero, rotY, 1f, house);
                    leaf.Position = pos + new Basis(Vector3.Up, rotY) * new Vector3(-0.53f, 0, -0.1f);
                }
                if (open)
                {
                    var basis = new Basis(Vector3.Up, rotY);
                    if (isDoor)
                    {
                        foreach (float x in new[] { -0.8f, 0.8f })
                            WallCollider(house, pos + basis * new Vector3(x, 1.6f, -0.1f), new Vector3(0.45f, 3.2f, 0.4f), rotY);
                        WallCollider(house, pos + basis * new Vector3(0, 2.85f, -0.1f), new Vector3(2f, 0.6f, 0.4f), rotY);
                    }
                    else WallCollider(house, pos + basis * new Vector3(0, 1.6f, -0.1f), new Vector3(2f, 3.2f, 0.4f), rotY);
                }
            }
        }
        // Wall pieces span x -1..1; their outside face is local +Z.
        Side(new Vector3(-hw, 0, hd), new Vector3(2, 0, 0), 0f, w, true);               // front (+Z, door)
        Side(new Vector3(hw, 0, -hd), new Vector3(-2, 0, 0), Mathf.Pi, w, false);       // back
        Side(new Vector3(-hw, 0, -hd), new Vector3(0, 0, 2), -Mathf.Pi / 2f, d, false); // west
        Side(new Vector3(hw, 0, hd), new Vector3(0, 0, -2), Mathf.Pi / 2f, d, false);   // east
        foreach (var c in new[] { new Vector3(-hw, 0, -hd), new Vector3(hw, 0, -hd), new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd) })
            Prop(Models.Village("Corner_Exterior_Wood"), Vector3.Zero, 0, 1f, house).Position = c;

        // Floor, roof and gables.
        for (float x = -hw + 1; x < hw; x += 2)
            for (float z = -hd + 1; z < hd; z += 2)
                Prop(Models.Village("Floor_WoodDark"), Vector3.Zero, 0, 1f, house).Position = new Vector3(x, 0.02f, z);
        string roof = w >= 4 ? (d >= 4 ? "Roof_RoundTiles_8x8" : "Roof_RoundTiles_8x8") : (d >= 4 ? "Roof_RoundTiles_6x8" : "Roof_RoundTiles_6x6");
        Prop(Models.Village(roof), Vector3.Zero, 0, 1f, house).Position = new Vector3(0, 3.12f, 0);
        string gable = w >= 4 ? "Roof_Front_Brick8" : "Roof_Front_Brick6";
        Prop(Models.Village(gable), Vector3.Zero, 0, 1f, house).Position = new Vector3(0, 3.12f, hd);
        Prop(Models.Village(gable), Vector3.Zero, Mathf.Pi, 1f, house).Position = new Vector3(0, 3.12f, -hd);
        Prop(Models.Village("Prop_Chimney"), Vector3.Zero, 0, 0.8f, house).Position = new Vector3(hw * 0.5f, 4.2f, -hd * 0.4f);

        if (!open)
        {
            var body = new StaticBody3D { CollisionLayer = GameState.LayerWorld, CollisionMask = 0, Position = new Vector3(0, 1.8f, 0) };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(hw * 2 + 0.4f, 3.6f, hd * 2 + 0.4f) } });
            house.AddChild(body);
        }
        // Warm windows at night.
        house.AddChild(new OmniLight3D { Position = new Vector3(0, 1.8f, 0), LightColor = new Color(1f, 0.68f, 0.38f), LightEnergy = 1.4f, OmniRange = hw * 2.2f });
        return house;
    }

    private static void WallCollider(Node3D parent, Vector3 pos, Vector3 size, float rotY)
    {
        var body = new StaticBody3D
        {
            CollisionLayer = GameState.LayerWorld | GameState.LayerProps, CollisionMask = 0,
            Position = pos, Rotation = new Vector3(0, rotY, 0),
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

    // ================================================================== bandit camp

    private void BuildBanditCamp()
    {
        var center = Wildlife.CampSites[0];
        var root = new Node3D { Name = "BanditCamp", Position = center };
        AddChild(root);
        Visual(new CylinderMesh { TopRadius = 10, BottomRadius = 10, Height = 0.04f, RadialSegments = 14 }, Mat(Dirt.Darkened(0.3f)), new Vector3(0, 0.02f, 0), Vector3.Zero, root);

        // Rough stake fence with a wide mouth toward the road (west).
        for (int i = 0; i < 18; i++)
        {
            float a = Mathf.Tau * i / 18f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            if (dir.X < -0.55f || i % 3 == 0) continue;
            float h = _rng.RandfRange(1.6f, 2.4f);
            Block(dir * 10.5f + new Vector3(0, h / 2f - 0.2f, 0), new Vector3(0.35f, h, 0.35f),
                new Vector3(_rng.RandfRange(-0.15f, 0.15f), 0, _rng.RandfRange(-0.2f, 0.2f)), WoodDark, root, layer: GameState.LayerProps);
        }

        // Tents.
        foreach (var (pos, rot) in new[] { (new Vector3(5, 0, -5), 0.6f), (new Vector3(6, 0, 4), -0.5f), (new Vector3(-1, 0, 7), 1.5f) })
        {
            var tent = new StaticBody3D { Position = pos, Rotation = new Vector3(0, rot, 0), CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
            root.AddChild(tent);
            tent.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(2.8f, 1.9f, 3.4f) }, MaterialOverride = StyleMaterials.Toon(Canvas, 0.2f), Position = new Vector3(0, 0.95f, 0) });
            tent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 1.1f, 0.05f) }, MaterialOverride = Mat(new Color(0.1f, 0.08f, 0.08f)), Position = new Vector3(0, 0.55f, 1.71f) });
            tent.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.4f, 1.6f, 3.2f) }, Position = new Vector3(0, 0.8f, 0) });
        }

        // Their fire, crates and a red banner.
        var fire = GD.Load<PackedScene>("res://scenes/buildings/Campfire.tscn").Instantiate<Node3D>();
        fire.Name = "BanditFire";
        fire.Position = new Vector3(-1, 0, -1);
        root.AddChild(fire);
        Prop(Models.Village("Prop_Crate"), new Vector3(-4, 0, -6), 0.4f, 0.9f, root);
        Prop(Models.Village("Prop_Crate"), new Vector3(-4.6f, 0, -4.9f), 1.1f, 0.7f, root);
        Prop(Models.Village("Prop_Wagon"), new Vector3(2, 0, -8.2f), 0.2f, 1f, root);
        Visual(new BoxMesh { Size = new Vector3(0.14f, 4.5f, 0.14f) }, Mat(WoodDark), new Vector3(-6, 2.25f, 3), Vector3.Zero, root);
        var flag = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(1.6f, 1.0f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Cloth, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            Position = new Vector3(-5.2f, 3.9f, 3),
        };
        root.AddChild(flag);
    }

    // ================================================================== home tiers

    private void BuildHome(int tier)
    {
        if (_home != null && IsInstanceValid(_home))
        {
            RemoveChild(_home);
            _home.QueueFree();
        }
        _home = tier >= 2 ? BuildCottage() : BuildShackTier();
    }

    private void OnHomeTierChanged(int tier) => BuildHome(tier);

    private Node3D BuildCottage()
    {
        // Faces the yard (+Z); enterable: per-wall colliders and an open doorway.
        var house = House(this, HomeUpgradeBoard.CottageCenter, 3, 3, "Plaster", 9, open: true, name: "Cottage");

        AddBed(house, "Bed", new Vector3(-1.9f, 0.2f, -1.6f));
        AddBed(house, "Bed2", new Vector3(1.9f, 0.2f, -1.6f));
        Visual(new BoxMesh { Size = new Vector3(1.2f, 0.8f, 0.8f) }, Mat(Wood), new Vector3(0, 0.4f, -2.3f), Vector3.Zero, house); // table
        Prop(Models.Village("Prop_Crate"), Vector3.Zero, 0.3f, 0.7f, house).Position = new Vector3(2.2f, 0, 1.6f);
        return house;
    }

    private void AddBed(Node3D parent, string name, Vector3 pos)
    {
        var bed = new Bed { Name = name, Position = pos, CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        parent.AddChild(bed);
        bed.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.0f, 0.5f, 2.0f) }, Position = new Vector3(0, 0.25f, 0) });
        Visual(new BoxMesh { Size = new Vector3(1.0f, 0.4f, 2.0f) }, StyleMaterials.Toon(Wood), new Vector3(0, 0.2f, 0), Vector3.Zero, bed);
        Visual(new BoxMesh { Size = new Vector3(0.9f, 0.14f, 1.38f) }, Mat(new Color(0.55f, 0.25f, 0.3f)), new Vector3(0, 0.465f, 0.26f), Vector3.Zero, bed);
        Visual(new BoxMesh { Size = new Vector3(0.7f, 0.18f, 0.38f) }, Mat(new Color(0.9f, 0.85f, 0.78f)), new Vector3(0, 0.49f, -0.7f), Vector3.Zero, bed);
    }

    private void BuildUpgradeBoard()
    {
        var board = new HomeUpgradeBoard
        {
            Name = "UpgradeBoard",
            Position = ShackPos + new Vector3(-1.8f, 0, 5.2f),
            CollisionLayer = GameState.LayerProps,
            CollisionMask = 0,
        };
        AddChild(board);
        board.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.4f, 1.8f, 0.4f) }, Position = new Vector3(0, 0.9f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.12f, 1.6f, 0.12f) }, Mat(WoodDark), new Vector3(-0.6f, 0.8f, 0), Vector3.Zero, board);
        Visual(new BoxMesh { Size = new Vector3(0.12f, 1.6f, 0.12f) }, Mat(WoodDark), new Vector3(0.6f, 0.8f, 0), Vector3.Zero, board);
        Visual(new BoxMesh { Size = new Vector3(1.4f, 0.8f, 0.06f) }, Mat(new Color(0.85f, 0.8f, 0.68f)), new Vector3(0, 1.3f, 0), Vector3.Zero, board);
        board.AddChild(new Label3D { Text = "HOME PLANS", Position = new Vector3(0, 1.45f, 0.05f), FontSize = 36, PixelSize = 0.006f, Modulate = new Color(0.2f, 0.15f, 0.3f) });
    }
}
