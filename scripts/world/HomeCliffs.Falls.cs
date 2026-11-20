using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Falls Valley (GDD §8, §9, §11): through a pass in the plateau's east wall. Tall cliffs,
/// the Great Falls pouring off the east wall into a plunge pool, a river that runs west and then
/// north off the cliff into the clouds (its current pushes you along), a mesa with climbable rock
/// faces and the second sanctuary on top, an updraft over the plunge pool that lifts a glider,
/// and a harpy roost on a rock spire. Built after everything else, with its own random numbers,
/// so the home plateau and the vale come out exactly as before.</summary>
public partial class HomeCliffs
{
    /// <summary>The valley's floor (enemies there are kept inside it).</summary>
    public static readonly Rect2 ValleyBounds = new(84, -18, 108, 126);
    /// <summary>The pass through the east wall (z range), between the plateau and the valley.</summary>
    public const float PassMinZ = 34f, PassMaxZ = 51f;
    public static readonly Vector3 PassPos = new(72, 0, 42.5f);

    public static readonly Vector3 MesaCenter = new(148, 0, 22);
    public const float MesaHeight = 16f, MesaHalf = 14f;
    /// <summary>Where the climbable face on the mesa's west side starts (its foot).</summary>
    public static readonly Vector3 MesaClimbFoot = new(MesaCenter.X - MesaHalf - 0.8f, 0, MesaCenter.Z);
    public static readonly Vector3 FallsShrinePos = new(MesaCenter.X + 2, MesaHeight, MesaCenter.Z - 4);

    public static readonly Vector3 GreatFallsPool = new(184, 0, 58);
    /// <summary>The plunge pool's basin (x, z): deep enough to swim in.</summary>
    public static readonly Rect2 FallsPool = new(GreatFallsPool.X - 5, GreatFallsPool.Z - 8, 18, 16);
    public const float UpdraftRadius = 5.5f, UpdraftTop = 36f;
    /// <summary>The river: west from the pool, then north off the cliff (current in m/s).</summary>
    public static readonly Vector3 RiverBend = new(108, 0, 58);
    public static readonly Vector3 RiverMouth = new(108, 0, -26);
    public const float RiverHalfWidth = 3.5f, RiverCurrent = 3.2f;

    private const float ValleyNorthZ = -24f, ValleySouthZ = 114f, ValleyEastX = 198f;

    private static readonly Color GripRock = new(0.62f, 0.57f, 0.5f);
    private static readonly Color GripRockDark = new(0.36f, 0.32f, 0.3f);

    private RandomNumberGenerator _vrng;

    private void BuildFallsValley()
    {
        _vrng = new RandomNumberGenerator { Seed = (ulong)Seed * 31 + 5 };

        // Floor: x 60..200 (the pass and the valley), z -24..114.
        GroundAround(new Vector3(130, -6, (ValleyNorthZ + ValleySouthZ) / 2f), new Vector3(140, 12, ValleySouthZ - ValleyNorthZ), FallsPool);

        BuildValleyWalls();
        BuildMesa();
        BuildGreatFalls();
        BuildRiver();
        BuildRoost();
        ScatterValley();

        // Signs at both ends of the pass.
        ValleySign("FallsSign", new Vector3(58, 0, 47), "Falls Valley", "FALLS →",
            "East through the pass: Falls Valley. Mind the river (it runs off the cliff) and the harpies. " +
            "A second blossom grows on the mesa; climb the pale rock (walk into it), or ride the mist over the falls on a glider.");
    }

    private void ValleySign(string name, Vector3 pos, string title, string label, string message)
    {
        var sign = new InfoSign { Name = name, Title = title, Message = message, Position = pos, CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        AddChild(sign);
        sign.Rotation = new Vector3(0, Mathf.Pi / 2f, 0);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 2.2f, 0.4f) }, Position = new Vector3(0, 1.1f, 0) });
        // The post stands behind the board, so it doesn't cover the lettering.
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(WoodDark), new Vector3(0, 1, -0.115f), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(1.5f, 0.5f, 0.08f) }, Mat(Wood), new Vector3(0, 1.8f, 0), Vector3.Zero, sign);
        sign.AddChild(new Label3D { Text = label, Position = new Vector3(0, 1.8f, 0.06f), FontSize = 48, PixelSize = 0.006f, Modulate = new Color(0.15f, 0.1f, 0.08f) });
    }

    /// <summary>Rock with the valley's own random numbers (see BuildFallsValley).</summary>
    private Node3D VRock(Vector3 pos, Vector3 size, Vector3 rot, bool collide = true)
    {
        var path = Models.AllRocks[_vrng.RandiRange(0, Models.AllRocks.Length - 1)];
        size *= 1.15f;
        // Tall rocks are columnar basalt clusters (the concept art's cliffs); the rest stay rock models.
        Node3D node = Basalt.Suits(size) ? Basalt.Make(Basalt.VariantAt(pos), size, collide)
            : collide ? Models.RockBody(path, size) : Models.Instance(path);
        if (!collide) node.Scale = size;
        node.Position = pos;
        node.Rotation = rot;
        AddChild(node);
        return node;
    }

    private Node3D VCrag(Vector3 basePos, float width, float height, Node3D on = null)
    {
        var path = Models.Crags[_vrng.RandiRange(0, Models.Crags.Length - 1)];
        var node = Models.Instance(path);
        float depth = _vrng.RandfRange(0.8f, 1.1f);
        node.Scale = new Vector3(width, height, width * depth);
        node.Position = basePos;
        node.Rotation = new Vector3(_vrng.RandfRange(-0.08f, 0.08f), _vrng.RandfRange(0, Mathf.Tau), _vrng.RandfRange(-0.08f, 0.08f));
        AddChild(node);
        if (on != null) SeatCrown(node, on, depth);
        return node;
    }

    // ================================================================== walls

    private void BuildValleyWalls()
    {
        // East: the tallest cliffs in the zone (the Great Falls pour off them).
        for (float z = ValleyNorthZ - 10; z <= ValleySouthZ + 4; z += 9)
        {
            float h = _vrng.RandfRange(44, 62);
            var size = new Vector3(_vrng.RandfRange(12, 18), h, _vrng.RandfRange(10, 14));
            var pos = new Vector3(ValleyEastX + 6 + _vrng.RandfRange(0, 5), h / 2f - 4, z + _vrng.RandfRange(-2, 2));
            var wall = VRock(pos, size, new Vector3(_vrng.RandfRange(-0.1f, 0.1f), _vrng.RandfRange(-0.4f, 0.4f), _vrng.RandfRange(0.05f, 0.18f)));
            if (_vrng.Randf() < 0.6f) VCrag(pos + new Vector3(0, h / 2f - 3f, 0), size.X * 0.8f, _vrng.RandfRange(10, 20), on: wall);
        }
        // South.
        for (float x = 76; x <= ValleyEastX + 6; x += 10)
        {
            float h = _vrng.RandfRange(24, 40);
            VRock(new Vector3(x, h / 2f - 3, ValleySouthZ + 5 + _vrng.RandfRange(0, 4)), new Vector3(13, h, 11),
                new Vector3(0, _vrng.RandfRange(-0.3f, 0.3f), _vrng.RandfRange(-0.1f, 0.1f)));
        }
        // North: the valley ends at a drop into the cloud sea, like the home cliff.
        for (float x = 78; x <= ValleyEastX + 8; x += 5.5f)
        {
            if (Mathf.Abs(x - RiverMouth.X) < 5f) continue; // the river spills through here
            float h = _vrng.RandfRange(30, 55);
            var size = new Vector3(_vrng.RandfRange(8, 12), h, _vrng.RandfRange(5, 9));
            VRock(new Vector3(x + _vrng.RandfRange(-2, 2), -h / 2f - _vrng.RandfRange(0.4f, 1.8f), ValleyNorthZ - 1 - _vrng.RandfRange(0, 2)), size,
                new Vector3(_vrng.RandfRange(-0.15f, 0.25f), _vrng.RandfRange(-0.3f, 0.3f), _vrng.RandfRange(-0.1f, 0.1f)));
        }
        // A few spires out in the clouds beyond.
        for (int i = 0; i < 4; i++)
        {
            float h = _vrng.RandfRange(50, 100);
            var pos = new Vector3(_vrng.RandfRange(100, 260), -70 + h / 2f, _vrng.RandfRange(-80, -200));
            var spire = VRock(pos, new Vector3(_vrng.RandfRange(10, 22), h, _vrng.RandfRange(10, 20)),
                new Vector3(_vrng.RandfRange(-0.1f, 0.1f), _vrng.RandfRange(0, 3), _vrng.RandfRange(-0.12f, 0.12f)), collide: false);
            if (InSpireField(pos, 14f)) spire.QueueFree(); // the Spire Cliffs are here now (made anyway: random numbers)
        }
    }

    // ================================================================== mesa + sanctuary

    private void BuildMesa()
    {
        // The rock itself: grass on top, dark rock on the sides (the ground shader does both).
        var mesa = Block(MesaCenter + new Vector3(0, MesaHeight / 2f, 0), new Vector3(MesaHalf * 2, MesaHeight, MesaHalf * 2), Vector3.Zero, Slate);
        mesa.Name = "Mesa";
        BasaltDress(mesa, new Vector3(MesaHalf * 2, MesaHeight, MesaHalf * 2), 3);
        // Broken rock around the foot and the rim so it doesn't read as a box.
        for (int i = 0; i < 14; i++)
        {
            float a = Mathf.Tau * i / 14f + _vrng.RandfRange(-0.15f, 0.15f);
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var edge = MesaCenter + new Vector3(Mathf.Clamp(dir.X * 20f, -MesaHalf, MesaHalf), 0, Mathf.Clamp(dir.Z * 20f, -MesaHalf, MesaHalf));
            if (edge.DistanceTo(MesaClimbFoot) < 7f) continue; // keep the climbing face clear
            VCrag(edge + new Vector3(0, MesaHeight - 1.5f, 0), _vrng.RandfRange(3, 6), _vrng.RandfRange(2, 4));
            if (i % 2 == 0) VRock(edge + dir * 1.5f + new Vector3(0, 1.5f, 0), new Vector3(5, 4, 4), new Vector3(0, _vrng.RandfRange(0, 3), 0), collide: false);
        }

        MesaButtresses();

        // Climbing faces: pale, rough rock with handholds (GDD §8: "lighter/rougher texture").
        ClimbFace(new Vector3(MesaCenter.X - MesaHalf - 0.4f, MesaHeight / 2f, MesaCenter.Z), new Vector3(0.8f, MesaHeight, 8f), "MesaClimbWest");
        ClimbFace(new Vector3(MesaCenter.X + 4, MesaHeight / 2f, MesaCenter.Z + MesaHalf + 0.4f), new Vector3(7f, MesaHeight, 0.8f), "MesaClimbSouth");

        // The Falls Blossom: second sanctuary (respawn point, heals, enemies keep out).
        var shrine = new BlossomShrine
        {
            Name = "FallsShrine", ShrineName = "Falls Blossom", Position = FallsShrinePos,
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        AddChild(shrine);
        shrine.Rotation = new Vector3(0, Mathf.DegToRad(30), 0);
        shrine.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.6f, Height = 4f }, Position = new Vector3(0, 2, 0) });
        var tree = Models.Instance(Models.CherryTree);
        tree.Scale = Vector3.One * 1.05f;
        shrine.AddChild(tree);
        shrine.AddChild(new OmniLight3D { Position = new Vector3(0.3f, 5f, 2f), LightColor = Blush, LightEnergy = 2f, OmniRange = 13f });
        Particles(110, 9f, new Vector3(3, 1.2f, 2.5f), FallsShrinePos + new Vector3(0, 5f, 0), new Color(1f, 0.6f, 0.75f), 0.08f, 0.16f, new Vector3(-0.3f, -0.25f, -0.4f));
        shrine.AddChild(new Label3D
        {
            Text = "Falls Blossom", Position = new Vector3(0, 8.5f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.01f, OutlineSize = 12, Modulate = Blush,
        });
        foreach (var off in new[] { new Vector3(-8, 0, 6), new Vector3(7, 0, 8), new Vector3(-6, 0, -9) })
            Prop(Models.Nature("TwistedTree_" + _vrng.RandiRange(1, 5)), MesaCenter + off + new Vector3(0, MesaHeight, 0), _vrng.RandfRange(0, Mathf.Tau), _vrng.RandfRange(0.8f, 1.1f));
        string[] small = { "Grass_Common_Short", "Grass_Wispy_Tall", "Flower_3_Group", "Flower_4_Group", "Clover_1", "Fern_1" };
        for (int i = 0; i < 40; i++)
        {
            var p = MesaCenter + new Vector3(_vrng.RandfRange(-12, 12), MesaHeight, _vrng.RandfRange(-12, 12));
            if (p.DistanceTo(FallsShrinePos) < 2.5f) continue;
            Prop(Models.Nature(small[_vrng.RandiRange(0, small.Length - 1)]), p, _vrng.RandfRange(0, Mathf.Tau), _vrng.RandfRange(0.8f, 1.3f));
        }
    }

    /// <summary>Rock buttresses and fallen slabs along all four sides of the mesa, so its walls read as
    /// weathered rock instead of a box. They're solid (you walk round them), and leave the two
    /// climbing faces and their approaches clear.</summary>
    private void MesaButtresses()
    {
        bool Clear(Vector3 p) =>
            (Mathf.Abs(p.X - (MesaCenter.X - MesaHalf)) < 3f && Mathf.Abs(p.Z - MesaCenter.Z) < 6.5f)   // west face
            || (Mathf.Abs(p.Z - (MesaCenter.Z + MesaHalf)) < 3f && Mathf.Abs(p.X - (MesaCenter.X + 4)) < 6f); // south face

        // Sides: (outward normal, along-side axis).
        var sides = new[] { (Vector3.Left, Vector3.Back), (Vector3.Right, Vector3.Back), (Vector3.Forward, Vector3.Right), (Vector3.Back, Vector3.Right) };
        foreach (var (outward, along) in sides)
        {
            var mid = MesaCenter + outward * MesaHalf;
            float u = -MesaHalf + _vrng.RandfRange(0.5f, 2f);
            while (u < MesaHalf - 1f)
            {
                float width = _vrng.RandfRange(3.5f, 6f);
                var at = mid + along * (u + width / 2f);
                u += width * _vrng.RandfRange(0.7f, 0.95f);
                if (Clear(at) || Clear(at + along * width * 0.6f) || Clear(at - along * width * 0.6f)) continue;
                float h = MesaHeight * _vrng.RandfRange(0.45f, 1.02f);
                float depth = _vrng.RandfRange(2.4f, 4f);
                // Mostly sunk into the wall: only 0.5–1.6 m stands proud of it.
                var pos = at + outward * (_vrng.RandfRange(0.5f, 1.6f) - depth / 2f) + new Vector3(0, h / 2f - 0.6f, 0);
                var size = outward.X != 0 ? new Vector3(depth, h, width) : new Vector3(width, h, depth);
                VRock(pos, size, new Vector3(_vrng.RandfRange(-0.04f, 0.04f), _vrng.RandfRange(-1, 1) > 0 ? 0 : Mathf.Pi, _vrng.RandfRange(-0.04f, 0.04f)));
                // Now and then a slab that came down, lying at the foot.
                if (_vrng.Randf() < 0.3f)
                {
                    var slab = at + outward * _vrng.RandfRange(2.5f, 4f) + along * _vrng.RandfRange(-1.5f, 1.5f);
                    if (!Clear(slab))
                        VRock(slab + new Vector3(0, 0.4f, 0), new Vector3(_vrng.RandfRange(1.5f, 2.6f), _vrng.RandfRange(0.9f, 1.5f), _vrng.RandfRange(1.5f, 2.6f)),
                            new Vector3(_vrng.RandfRange(-0.2f, 0.2f), _vrng.RandfRange(0, Mathf.Tau), _vrng.RandfRange(-0.2f, 0.2f)));
                }
            }
        }
        // Big rounded shoulders on the corners.
        foreach (var (cx, cz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            var corner = MesaCenter + new Vector3(cx * (MesaHalf - 1.2f), 0, cz * (MesaHalf - 1.2f));
            float h = MesaHeight * _vrng.RandfRange(0.8f, 1.0f);
            VRock(corner + new Vector3(0, h / 2f - 0.6f, 0), new Vector3(6f, h, 6f), new Vector3(0, _vrng.RandfRange(0, Mathf.Tau), 0));
        }
    }

    /// <summary>A climbable slab: walk into it to start climbing (PlayerController).</summary>
    private void ClimbFace(Vector3 center, Vector3 size, string name)
    {
        var body = new StaticBody3D { Name = name, Position = center, CollisionLayer = GameState.LayerWorld, CollisionMask = 0 };
        body.AddToGroup(GameState.ClimbGroup);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = StyleMaterials.Toon(GripRock, 0.35f) });
        AddChild(body);
        // Handholds: little dark ledges scattered over the outer face.
        bool alongX = size.X > size.Z;
        var outward = alongX ? new Vector3(0, 0, Mathf.Sign(center.Z - MesaCenter.Z)) : new Vector3(Mathf.Sign(center.X - MesaCenter.X), 0, 0);
        float width = alongX ? size.X : size.Z;
        var holds = StyleMaterials.Toon(GripRockDark, 0.2f);
        for (int i = 0; i < (int)(width * size.Y / 3.5f); i++)
        {
            float u = _vrng.RandfRange(-width / 2f + 0.4f, width / 2f - 0.4f);
            float v = _vrng.RandfRange(-size.Y / 2f + 0.5f, size.Y / 2f - 0.3f);
            var local = (alongX ? new Vector3(u, v, 0) : new Vector3(0, v, u)) + outward * ((alongX ? size.Z : size.X) / 2f + 0.06f);
            body.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = alongX ? new Vector3(0.45f, 0.14f, 0.18f) : new Vector3(0.18f, 0.14f, 0.45f) },
                MaterialOverride = holds, Position = local, Rotation = new Vector3(0, 0, _vrng.RandfRange(-0.3f, 0.3f)),
            });
        }
    }

    // ================================================================== the Great Falls

    private void BuildGreatFalls()
    {
        // Pours off the east cliff into a plunge pool; its mist is an updraft for gliders.
        Waterfall(new Vector3(ValleyEastX - 1.5f, 30, GreatFallsPool.Z), new Vector2(11, 60), new Vector3(0, -Mathf.Pi / 2f, 0));
        Pool(FallsPool);
        foreach (float side in new[] { -1f, 1f })
            Block(GreatFallsPool + new Vector3(4, -0.4f, side * 8.5f), new Vector3(19, 1.2f, 1), Vector3.Zero, Slate);

        GameState.Instance.Updrafts.Add((GreatFallsPool, UpdraftRadius, UpdraftTop));
        Rainbow(GreatFallsPool + new Vector3(-3, 9, 0), 24f, -Mathf.Pi / 2f);
        var lift = Particles(90, 4f, new Vector3(UpdraftRadius * 0.7f, 2, UpdraftRadius * 0.7f), GreatFallsPool + Vector3.Up * 2,
            new Color(0.95f, 0.97f, 1f, 0.3f), 0.4f, 1.1f, new Vector3(0, 8f, 0));
        lift.Name = "Updraft";
        // Mist columns at the home pool's falls lift a glider too (GDD §8).
        GameState.Instance.Updrafts.Add((PoolPos + new Vector3(-6, 0, 0), 4f, 26f));
        Particles(50, 4f, new Vector3(2.8f, 2, 2.8f), PoolPos + new Vector3(-6, 2, 0), new Color(0.95f, 0.97f, 1f, 0.25f), 0.4f, 1f, new Vector3(0, 6f, 0)).Name = "HomeUpdraft";
    }

    // ================================================================== river

    private void BuildRiver()
    {
        var water = StyleMaterials.Water();
        var bed = Mat(Dirt.Darkened(0.45f));
        void Leg(Vector3 from, Vector3 to)
        {
            var d = to - from;
            var mid = (from + to) / 2f;
            float yaw = Mathf.Atan2(d.X, d.Z);
            Visual(new BoxMesh { Size = new Vector3(RiverHalfWidth * 2f + 1f, 0.03f, d.Length() + RiverHalfWidth) }, bed, mid + new Vector3(0, 0.015f, 0), new Vector3(0, yaw, 0));
            Visual(new BoxMesh { Size = new Vector3(RiverHalfWidth * 2f, 0.12f, d.Length() + RiverHalfWidth) }, water, mid + new Vector3(0, 0.07f, 0), new Vector3(0, yaw, 0));
            GameState.Instance.Currents.Add((from, to, RiverHalfWidth, RiverCurrent));
        }
        Leg(GreatFallsPool + new Vector3(-6, 0, 0), RiverBend);
        Leg(RiverBend, RiverMouth);
        // Spills off the north cliff into the clouds.
        Waterfall(new Vector3(RiverMouth.X, -26, ValleyNorthZ - 2f), new Vector2(6, 52), Vector3.Zero);
        // Stepping stones across the north leg, on the way from the pass to the mesa.
        foreach (float x in new[] { -2.4f, -0.6f, 1.2f, 2.9f })
            Block(new Vector3(RiverBend.X + x, 0.12f, PassPos.Z + _vrng.RandfRange(-0.4f, 0.4f)), new Vector3(1.1f, 0.4f, 1.1f),
                new Vector3(0, _vrng.RandfRange(0, 1), 0), SlateLight);
    }

    // ================================================================== harpy roost

    private void BuildRoost()
    {
        var at = Wildlife.RoostSites[0];
        var spire = VRock(at + new Vector3(0, 8, 0), new Vector3(7, 18, 7), new Vector3(0, 0.4f, 0));
        spire.Name = "HarpySpire";
        // A ragged nest of sticks on top.
        var sticks = Mat(new Color(0.32f, 0.24f, 0.17f));
        for (int i = 0; i < 16; i++)
        {
            float a = Mathf.Tau * i / 16f;
            Visual(new BoxMesh { Size = new Vector3(0.12f, 0.12f, 2.2f) }, sticks,
                at + new Vector3(Mathf.Cos(a) * 1.8f, 18.4f + (i % 3) * 0.12f, Mathf.Sin(a) * 1.8f), new Vector3(0.2f * (i % 2), a, 0));
        }
        // Feathers drifting off it.
        Particles(24, 8f, new Vector3(6, 3, 6), at + new Vector3(0, 14, 0), new Color(0.55f, 0.5f, 0.65f), 0.1f, 0.2f, new Vector3(0.3f, -0.4f, 0.2f));
        // Bones and a dead tree below.
        Prop(Models.Nature("DeadTree_" + _vrng.RandiRange(1, 5)), at + new Vector3(6, 0, 4), 0.5f, 1.2f);
    }

    // ================================================================== scatter

    private void ScatterValley()
    {
        var tree = GD.Load<PackedScene>("res://scenes/props/Tree.tscn");
        var rock = GD.Load<PackedScene>("res://scenes/props/Rock.tscn");
        var placed = new List<Vector3>();
        bool Clear(Vector3 p, float spacing)
        {
            if (Mathf.Abs(p.X - MesaCenter.X) < MesaHalf + 4 && Mathf.Abs(p.Z - MesaCenter.Z) < MesaHalf + 4) return false;
            if (GameState.Instance.CurrentAt(p + Vector3.Up * 0.1f, RiverHalfWidth + 3f) != Vector3.Zero) return false;
            if (p.DistanceTo(GreatFallsPool) < 14 || p.DistanceTo(Wildlife.RoostSites[0]) < 7) return false;
            if (Mathf.Abs(p.Z - PassPos.Z) < 5 && p.X < 112) return false; // path from the pass
            foreach (var q in placed) if (p.DistanceTo(q) < spacing) return false;
            return true;
        }
        int trees = 0, rocks = 0, guard = 0;
        while ((trees < 16 || rocks < 8) && guard++ < 3000)
        {
            var p = new Vector3(_vrng.RandfRange(86, 190), 0, _vrng.RandfRange(-14, 108));
            if (!Clear(p, 6f)) continue;
            // Spawn() uses the main random numbers for rotation and scale; that's fine here (built last).
            if (trees < 16) { Spawn(tree, p, placed); trees++; }
            else { Spawn(rock, p, placed); rocks++; }
        }
        string[] small = { "Grass_Common_Short", "Grass_Common_Tall", "Grass_Wispy_Tall", "Fern_1", "Plant_1", "Plant_7", "Flower_3_Group", "Clover_2", "Mushroom_Common" };
        for (int i = 0, n = 0; n < 160 && i < 3000; i++)
        {
            var p = new Vector3(_vrng.RandfRange(62, 192), 0, _vrng.RandfRange(-20, 110));
            if (!Clear(p, 0f)) continue;
            Prop(Models.Nature(small[_vrng.RandiRange(0, small.Length - 1)]), p, _vrng.RandfRange(0, Mathf.Tau), _vrng.RandfRange(0.8f, 1.5f));
            n++;
        }
        // Ferns and reeds along the riverbanks.
        for (int i = 0; i < 40; i++)
        {
            bool north = i % 2 == 0;
            var p = north
                ? new Vector3(RiverBend.X + (i % 4 < 2 ? -1 : 1) * (RiverHalfWidth + 1f), 0, _vrng.RandfRange(RiverMouth.Z + 4, RiverBend.Z))
                : new Vector3(_vrng.RandfRange(RiverBend.X, GreatFallsPool.X - 10), 0, RiverBend.Z + (i % 4 < 2 ? -1 : 1) * (RiverHalfWidth + 1f));
            Prop(Models.Nature(_vrng.Randf() < 0.5f ? "Fern_1" : "Grass_Wispy_Tall"), p, _vrng.RandfRange(0, Mathf.Tau), _vrng.RandfRange(1f, 1.6f));
        }
    }
}
