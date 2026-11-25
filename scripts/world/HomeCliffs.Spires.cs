using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The Spire Cliffs (GDD §11, step 2), part 1: the place itself. A field of tall rock
/// spires standing out of the cloud sea north of Falls Valley, reached by gliding off the Spire
/// Lookout on the valley's north rim. Cracks in the rock breathe updrafts that lift you to the
/// next, higher spire; wind gusts (streaks in the air) shove a glider sideways; rust-red iron
/// seams run up the pale climbing faces, so you mine them while hanging on. The Spire Blossom
/// (a sanctuary with fast travel) grows on the second spire. Built with its own random numbers.
/// The grapple anchors are in HomeCliffs.Grapple.cs; storm harpies, goats and the Storm Harpy
/// Queen live in Wildlife.Spires.cs. Placed relative to <see cref="Regions.Spires"/> (<see cref="InSpires"/>),
/// which moves with Falls Valley: the lookout is on the valley's rim.</summary>
public partial class HomeCliffs
{
    /// <summary>A place in the Spire Cliffs, given where it is in their own layout.</summary>
    public static Vector3 InSpires(float x, float y, float z) => Regions.Spires + new Vector3(x, y, z);

    /// <summary>Where the spire field is (x, z): the home and valley scenery spires are kept out of it.</summary>
    public static readonly Rect2 SpireField = Regions.Move(new Rect2(112, -170, 112, 148), Regions.Spires);

    /// <summary>The ledge you glide off, on Falls Valley's north rim.</summary>
    public static readonly Vector3 SpireLookout = InSpires(160, 0, -28);

    /// <summary>One spire: the centre of its top, and half its width.</summary>
    public readonly record struct SpireDef(string Name, Vector3 Top, float Half);

    public static readonly SpireDef[] Spires =
    {
        new("LedgeSpire", InSpires(160, -3, -47), 5f),     // the first hop off the lookout
        new("BlossomSpire", InSpires(138, 6, -70), 7f),    // the sanctuary
        new("RustSpire", InSpires(167, 2, -95), 6f),       // iron seams on two faces
        new("WindwardSpire", InSpires(201, 10, -120), 5f), // across the gusts
        new("TallSpire", InSpires(172, 44, -152), 6f),     // the tallest (the Queen nests here later)
    };

    public static Vector3 SpireBlossomPos => Spires[1].Top + new Vector3(-2, 0, 1);

    /// <summary>Updrafts out of cracks in a spire's top: base (the crack), radius, top height.</summary>
    public static readonly (Vector3 Base, float Radius, float Top)[] SpireUpdrafts =
    {
        (InSpires(157, -3, -49), 2.6f, 18f),   // lifts you from the first spire to the blossom's
        (InSpires(170, 2, -98), 2.6f, 24f),    // the rust spire: up and over the gusts
        (InSpires(203, 10, -122), 2.4f, 34f),  // the windward spire: toward the tall spire's face
    };

    /// <summary>Wind gusts (GDD §11 step 2): a box (centre, half size) where a glider gets shoved
    /// along Push (m/s) while the gust blows. Each blows for a bit, then drops, on a cycle.</summary>
    public static readonly (Vector3 Center, Vector3 Half, Vector3 Push, float Period, float Phase)[] SpireGusts =
    {
        (InSpires(152, 4, -83), new Vector3(7, 10, 7), new Vector3(3.5f, 0, 2.5f), 8f, 0f),       // gentle: a first taste
        (InSpires(184, 10, -108), new Vector3(10, 16, 9), new Vector3(-4.5f, 0, -4f), 7f, 2f),     // out toward the open sky
        (InSpires(187, 28, -137), new Vector3(9, 16, 9), new Vector3(6f, 0, 0), 6f, 4f),          // across the tall spire's face
    };

    /// <summary>Fraction of each gust cycle the wind blows (it rises and falls within that).</summary>
    public const float GustOnShare = 0.45f;

    /// <summary>How hard a gust blows right now (0 lull, 1 full), from the clock.</summary>
    public static float GustStrength(int i, double now)
    {
        var g = SpireGusts[i];
        float f = (float)(((now + g.Phase) % g.Period + g.Period) % g.Period / g.Period);
        return f < GustOnShare ? Mathf.Sin(Mathf.Pi * f / GustOnShare) : 0f;
    }

    /// <summary>The gust's shove on a glider at <paramref name="pos"/> (zero outside the gusts).</summary>
    public static Vector3 GustAt(Vector3 pos, double now)
    {
        var push = Vector3.Zero;
        for (int i = 0; i < SpireGusts.Length; i++)
        {
            var g = SpireGusts[i];
            var d = pos - g.Center;
            if (Mathf.Abs(d.X) > g.Half.X || Mathf.Abs(d.Y) > g.Half.Y || Mathf.Abs(d.Z) > g.Half.Z) continue;
            push += g.Push * GustStrength(i, now);
        }
        return push;
    }

    public static bool InSpireField(Vector3 p, float margin = 0f) =>
        p.X > SpireField.Position.X - margin && p.X < SpireField.End.X + margin
        && p.Z > SpireField.Position.Y - margin && p.Z < SpireField.End.Y + margin;

    /// <summary>On (or hanging from) one of the spires.</summary>
    public static bool OnSpires(Vector3 p) => InSpireField(p) && p.Z < Regions.Spires.Z - 36f && p.Y > -30f;

    private RandomNumberGenerator _srng;
    private readonly List<(GpuParticles3D Streaks, AudioStreamPlayer3D Sound)> _gustFx = new();
    private int _seamCount;

    private static readonly Color Rust = new(0.62f, 0.3f, 0.18f);
    private static readonly Color RustDark = new(0.4f, 0.18f, 0.12f);

    private void BuildSpireCliffs()
    {
        _srng = new RandomNumberGenerator { Seed = (ulong)Seed * 71 + 3 };
        var root = new Node3D { Name = "SpireCliffs" };
        AddChild(root);

        BuildLookout(root);
        foreach (var s in Spires) BuildSpire(s, root);

        // Climbing faces with iron seams: the rust spire's west and south faces, the windward
        // spire's south face, and the tall spire's east face (the long climb).
        foreach (var f in Faces) SpireFace(f.Spire, f.Out, f.Along, f.Bottom, f.Width, f.Seams, root);

        foreach (var u in SpireUpdrafts) Crack(u.Base, u.Radius, u.Top, root);
        for (int i = 0; i < SpireGusts.Length; i++) Gust(i, root);
        BuildSpireBlossom(root);
        BuildEmptyNest(Spires[4].Top, root);
        foreach (var r in Wildlife.StormRoostSites) BuildStormNest(r, root);
        BuildAnchors(root);
        SpireScenery(root);
    }

    /// <summary>Everyone, each frame: gust streaks thicken and the wind roars while a gust blows.</summary>
    private void TickSpires()
    {
        double now = Net.Now;
        for (int i = 0; i < _gustFx.Count; i++)
        {
            float s = GustStrength(i, now);
            var (streaks, sound) = _gustFx[i];
            streaks.AmountRatio = 0.12f + 0.88f * s;
            if (sound != null) sound.VolumeDb = Mathf.Lerp(-30f, -4f, s);
        }
    }

    // ================================================================== the lookout

    private void BuildLookout(Node3D root)
    {
        // A rock shelf jutting off the valley's north rim, over the clouds.
        var shelf = Block(SpireLookout + new Vector3(0, -1.5f, -2f), new Vector3(8, 3, 12), Vector3.Zero, Slate, root);
        shelf.Name = "SpireLookout";
        BasaltDress(shelf, new Vector3(8, 3, 12), 7);
        // Broken rock under it, so it reads as a spur of the cliff, not a slab.
        for (int i = 0; i < 4; i++)
        {
            float z = SpireLookout.Z - 6f + i * 3f;
            SRock(new Vector3(SpireLookout.X + _srng.RandfRange(-1.5f, 1.5f), -6f - i, z), new Vector3(7, 8 + i * 3, 4),
                new Vector3(_srng.RandfRange(-0.2f, 0.2f), _srng.RandfRange(0, 3), 0), root, collide: false);
        }
        foreach (float side in new[] { -1f, 1f })
            SRock(SpireLookout + new Vector3(side * 4.6f, 0.2f, -4f + side), new Vector3(1.6f, 1.2f, 2.4f), new Vector3(0, _srng.RandfRange(0, 3), 0), root);
        // Prayer flags on a pole at the tip, streaming in the wind.
        var tip = SpireLookout + new Vector3(2.5f, 0, -6.5f);
        Visual(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 4.5f }, Mat(WoodDark), tip + Vector3.Up * 2.25f, Vector3.Zero, root);
        var flagColors = new[] { new Color(0.85f, 0.35f, 0.3f), new Color(0.95f, 0.8f, 0.4f), new Color(0.45f, 0.6f, 0.85f), Blush };
        for (int i = 0; i < 4; i++)
            Visual(new BoxMesh { Size = new Vector3(0.9f, 0.4f, 0.02f) }, Mat(flagColors[i]),
                tip + new Vector3(0.5f, 4.1f - i * 0.5f, 0), new Vector3(0, 0, -0.12f * i), root);

        SpireSign("SpireSign", SpireLookout + new Vector3(-2.6f, 0, 4.5f), "Spire Cliffs", "SPIRES ↑",
            "The Spire Cliffs: rock spires standing out of the clouds. Glide from spire to spire; cracks in the rock " +
            "breathe updrafts, and gusts (streaks in the air) shove a glider sideways, so wait for a lull. Iron runs in " +
            "rust-red seams up the pale rock: climb it (fly or walk into it) and mine while you hang on. " +
            "A blossom grows on the second spire. Fall, and the clouds send you home.", root);
    }

    private void SpireSign(string name, Vector3 pos, string title, string label, string message, Node3D root)
    {
        var sign = new InfoSign { Name = name, Title = title, Message = message, Position = pos, CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
        root.AddChild(sign);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 2.2f, 0.4f) }, Position = new Vector3(0, 1.1f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(WoodDark), new Vector3(0, 1, -0.115f), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(1.5f, 0.5f, 0.08f) }, Mat(Wood), new Vector3(0, 1.8f, 0), Vector3.Zero, sign);
        sign.AddChild(new Label3D { Text = label, Position = new Vector3(0, 1.8f, 0.06f), FontSize = 48, PixelSize = 0.006f, Modulate = new Color(0.15f, 0.1f, 0.08f) });
    }

    // ================================================================== spires

    /// <summary>Rock with the spires' own random numbers.</summary>
    private Node3D SRock(Vector3 pos, Vector3 size, Vector3 rot, Node3D root, bool collide = true)
    {
        var path = Models.AllRocks[_srng.RandiRange(0, Models.AllRocks.Length - 1)];
        size *= 1.15f;
        // Tall rocks are columnar basalt clusters (the concept art's cliffs); the rest stay rock models.
        Node3D node = Basalt.Suits(size) ? Basalt.Make(Basalt.VariantAt(pos), size, collide)
            : collide ? Models.RockBody(path, size) : Models.Instance(path);
        if (!collide) node.Scale = size;
        node.Position = pos;
        node.Rotation = rot;
        root.AddChild(node);
        return node;
    }

    /// <summary>A spire: a column from deep in the clouds up to a grassy top, with rock buttresses
    /// and broken crowns round it so it doesn't read as a box. Its faces are clear where a
    /// climbing face goes (see <see cref="SpireFace"/>).</summary>
    private void BuildSpire(SpireDef s, Node3D root)
    {
        const float Bottom = -75f;
        float h = s.Top.Y - Bottom;
        var core = Block(new Vector3(s.Top.X, Bottom + h / 2f, s.Top.Z), new Vector3(s.Half * 2, h, s.Half * 2), Vector3.Zero, Slate, root);
        core.Name = s.Name;
        BasaltDress(core, new Vector3(s.Half * 2, h, s.Half * 2), (int)s.Top.X);

        // Buttresses down every side (solid above the clouds, scenery below), leaving the
        // climbing faces clear, and a wider foot that vanishes into the cloud sea.
        foreach (var outward in new[] { Vector3.Left, Vector3.Right, Vector3.Forward, Vector3.Back })
        {
            var along = new Vector3(outward.Z, 0, -outward.X);
            for (float y = s.Top.Y - 3f; y > -40f; y -= _srng.RandfRange(6f, 10f))
                for (int k = 0; k < 2; k++)
                {
                    float u = (k == 0 ? -1f : 1f) * s.Half * _srng.RandfRange(0.3f, 0.75f);
                    var at = s.Top + outward * s.Half + along * u;
                    at.Y = y;
                    if (FaceClear(s, at) || NearAnchor(at)) continue;
                    float rh = _srng.RandfRange(4f, 9f);
                    var size = new Vector3(_srng.RandfRange(2.5f, 4.5f), rh, _srng.RandfRange(2.5f, 4f));
                    // Mostly sunk into the column: 0.5–1.4 m stands proud of it.
                    var pos = at + outward * (_srng.RandfRange(0.5f, 1.4f) - size.Z / 2f) + new Vector3(0, -rh * 0.4f, 0);
                    SRock(pos, size, new Vector3(_srng.RandfRange(-0.1f, 0.1f), outward.X != 0 ? Mathf.Pi / 2f : 0f, _srng.RandfRange(-0.1f, 0.1f)),
                        root, collide: y > -25f);
                }
        }
        // Broken crowns on the rim (low enough to step past), here and there.
        for (int i = 0; i < 5; i++)
        {
            float a = Mathf.Tau * i / 5f + _srng.RandfRange(-0.3f, 0.3f);
            var edge = s.Top + new Vector3(Mathf.Clamp(Mathf.Cos(a) * 9f, -s.Half, s.Half) * 0.92f, -0.6f, Mathf.Clamp(Mathf.Sin(a) * 9f, -s.Half, s.Half) * 0.92f);
            if (FaceClear(s, edge, 3f) || NearAnchor(edge, 2f)) continue;
            SRock(edge, new Vector3(_srng.RandfRange(1.2f, 2.2f), _srng.RandfRange(1.4f, 2.4f), _srng.RandfRange(1.2f, 2f)),
                new Vector3(_srng.RandfRange(-0.2f, 0.2f), _srng.RandfRange(0, Mathf.Tau), 0), root);
        }
        // Grass and the odd wind-bent tree on top.
        string[] small = { "Grass_Common_Short", "Grass_Wispy_Tall", "Clover_1", "Flower_4_Group", "Fern_1" };
        int tufts = (int)(s.Half * s.Half * 0.6f);
        for (int i = 0; i < tufts; i++)
        {
            var p = s.Top + new Vector3(_srng.RandfRange(-s.Half + 0.6f, s.Half - 0.6f), 0, _srng.RandfRange(-s.Half + 0.6f, s.Half - 0.6f));
            Prop(Models.Nature(small[_srng.RandiRange(0, small.Length - 1)]), p, _srng.RandfRange(0, Mathf.Tau), _srng.RandfRange(0.8f, 1.3f), root);
        }
        if (s.Half >= 6f)
            Prop(Models.Nature("TwistedTree_" + _srng.RandiRange(1, 5)), s.Top + new Vector3(-s.Half * 0.55f, 0, s.Half * 0.55f), _srng.RandfRange(0, Mathf.Tau), 0.8f, root);
    }

    /// <summary>The climbing faces: spire, outward side, offset along it, bottom height, width, iron seams.</summary>
    public static readonly (SpireDef Spire, Vector3 Out, float Along, float Bottom, float Width, int Seams)[] Faces =
    {
        (Spires[2], Vector3.Left, 0f, -14f, 9f, 3),
        (Spires[2], Vector3.Back, 1f, -10f, 7f, 2),
        (Spires[3], Vector3.Back, 0f, -6f, 6f, 2),
        (Spires[4], Vector3.Right, 0f, 22f, 7f, 2), // the long climb up the tallest spire
    };

    /// <summary>Keep scenery off a climbing face (and the air in front of it).</summary>
    private bool FaceClear(SpireDef s, Vector3 p, float pad = 1.5f)
    {
        foreach (var f in Faces)
        {
            if (f.Spire.Name != s.Name) continue;
            var along = new Vector3(f.Out.Z, 0, -f.Out.X);
            var mid = s.Top + f.Out * s.Half + along * f.Along;
            var rel = p - mid;
            if (Mathf.Abs(rel.Dot(along)) < f.Width / 2f + pad && rel.Dot(f.Out) > -2.5f && p.Y > f.Bottom - 4f) return true;
        }
        return false;
    }

    /// <summary>A pale climbing face from <paramref name="bottom"/> to the spire's top, a ledge at
    /// its foot to land on, rust streaks, and iron seams to mine while you hang there.</summary>
    private void SpireFace(SpireDef s, Vector3 outward, float alongOffset, float bottom, float width, int seams, Node3D root)
    {
        var along = new Vector3(outward.Z, 0, -outward.X);
        float height = s.Top.Y - bottom;
        var surface = s.Top + outward * s.Half + along * alongOffset;
        var center = new Vector3(surface.X, bottom + height / 2f, surface.Z) + outward * 0.4f;
        bool alongX = Mathf.Abs(along.X) > 0.5f;
        var size = alongX ? new Vector3(width, height, 0.8f) : new Vector3(0.8f, height, width);

        var body = new StaticBody3D { Name = $"{s.Name}Climb{(alongX ? "Z" : "X")}", Position = center, CollisionLayer = GameState.LayerWorld, CollisionMask = 0 };
        body.AddToGroup(GameState.ClimbGroup);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        root.AddChild(body);

        // Handholds, and rust bleeding down the rock below the seams.
        var holds = StyleMaterials.Toon(GripRockDark, 0.2f);
        float faceOut = 0.4f + 0.06f;
        for (int i = 0; i < (int)(width * height / 3.5f); i++)
        {
            float u = _srng.RandfRange(-width / 2f + 0.4f, width / 2f - 0.4f);
            float v = _srng.RandfRange(-height / 2f + 0.5f, height / 2f - 0.3f);
            body.AddChild(new MeshInstance3D
            {
                Mesh = ClimbRock.Hold(), MaterialOverride = holds, Position = along * u + Vector3.Up * v + outward * faceOut,
                Rotation = new Vector3(0, alongX ? 0f : Mathf.Pi / 2f, _srng.RandfRange(-0.3f, 0.3f)),
            });
        }
        // Rust bleeds down the rock below the seams: painted into the face's own colours.
        var streaks = new List<Rect2>();
        for (int i = 0; i < 5; i++)
        {
            float u = _srng.RandfRange(-width / 2f + 0.5f, width / 2f - 0.5f), len = _srng.RandfRange(2f, 5f);
            float v = _srng.RandfRange(-height / 2f + len / 2f, height / 2f - len / 2f);
            streaks.Add(new Rect2(u - 0.3f, v - len / 2f, 0.6f, len));
        }
        body.AddChild(ClimbRock.Make(size, outward, GripRock, GripRockDark, (int)GD.Hash(body.Name), streaks, Rust));

        // A ledge at the foot: glide in, land, start climbing.
        var ledge = Block(new Vector3(surface.X, bottom - 0.5f, surface.Z) + outward * 1.6f,
            alongX ? new Vector3(width + 1.5f, 1f, 3.2f) : new Vector3(3.2f, 1f, width + 1.5f), Vector3.Zero, Slate, root);
        ((MeshInstance3D)ledge.GetChild(0)).MaterialOverride = GroundMaterial;
        SRock(new Vector3(surface.X, bottom - 2.6f, surface.Z) + outward * 1.4f, new Vector3(3.4f, 3f, 3.2f), new Vector3(0, _srng.RandfRange(0, 3), 0), root, collide: false);

        // The seams, spread up the face (the highest near the top: the reward for the climb).
        for (int i = 0; i < seams; i++)
        {
            float v = bottom + height * (seams == 1 ? 0.5f : 0.25f + 0.6f * i / (seams - 1)) + _srng.RandfRange(-0.8f, 0.8f);
            float u = _srng.RandfRange(-width / 2f + 1.2f, width / 2f - 1.2f);
            var at = new Vector3(surface.X, v, surface.Z) + along * u + outward * (0.8f + 0.05f);
            Seam(at, outward, root);
        }
    }

    /// <summary>An iron seam set into a climbing face: a pickaxe breaks it (you can swing one while
    /// climbing). It grows back after 5 minutes, like the valley's ore.</summary>
    private void Seam(Vector3 at, Vector3 outward, Node3D root)
    {
        var n = GD.Load<PackedScene>("res://scenes/props/Rock.tscn").Instantiate<Harvestable>();
        n.Name = $"SpireSeam_{_seamCount++}";
        n.DropItemId = "iron_ore";
        n.Tool = "pick";
        n.RequiresTool = true;
        n.DamagePerDrop = 20f;
        n.BonusOnBreak = 3;
        n.RespawnSeconds = 300f;
        n.AddToGroup("iron_ore");
        root.AddChild(n);
        n.Position = at + Vector3.Down * 0.45f;
        n.Rotation = new Vector3(0, Mathf.Atan2(outward.X, outward.Z), 0);
        // Flattened against the rock, so it doesn't push you off the face.
        foreach (var c in n.GetChildren())
            if (c is CollisionShape3D shape)
            {
                shape.Shape = new BoxShape3D { Size = new Vector3(1.3f, 1f, 0.45f) };
                shape.Position = new Vector3(0, 0.5f, 0);
            }
        // Squash the rock model (not Visual itself: breaking and regrowing tween that back to 1).
        var visual = n.GetNode<Node3D>("Visual");
        if (visual.GetChildCount() > 0 && visual.GetChild(0) is Node3D model) model.Scale *= new Vector3(0.85f, 0.75f, 0.32f);
        var ore = StyleMaterials.Rock(new Color(0.34f, 0.22f, 0.2f), Rust, 0.55f, 0.5f);
        foreach (var m in visual.FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)m).MaterialOverride = ore;
        var nugget = StyleMaterials.Toon(new Color(0.85f, 0.45f, 0.24f), 0.6f);
        for (int k = 0; k < 7; k++)
        {
            float a = k * 2.4f + _seamCount, r = 0.45f + 0.12f * (k % 3);
            Visual(new BoxMesh { Size = Vector3.One * (0.16f + 0.05f * (k % 2)) }, nugget,
                new Vector3(Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * 0.4f, 0.22f), new Vector3(a, a * 0.7f, 0.4f), visual);
        }
        // A rust stain running down from it.
        Visual(new BoxMesh { Size = new Vector3(0.5f, 1.6f, 0.02f) }, StyleMaterials.Toon(RustDark, 0.3f), new Vector3(0, -0.6f, -0.03f), Vector3.Zero, n);
    }

    /// <summary>A crack in a spire's top that breathes an updraft (glide into it to rise).</summary>
    private void Crack(Vector3 at, float radius, float top, Node3D root)
    {
        GameState.Instance.Updrafts.Add((at, radius, top));
        var dark = Mat(new Color(0.08f, 0.07f, 0.1f));
        for (int i = 0; i < 3; i++)
            Visual(new BoxMesh { Size = new Vector3(0.45f - i * 0.1f, 0.04f, radius * 1.6f - i * 0.6f) }, dark,
                at + new Vector3(i * 0.3f - 0.3f, 0.02f, i * 0.2f), new Vector3(0, 0.5f + i * 0.4f, 0), root);
        // Pale wisps rising out of it, and dust.
        var lift = Particles(70, 3.5f, new Vector3(radius * 0.6f, 1, radius * 0.6f), at + Vector3.Up * 1.5f,
            new Color(0.95f, 0.97f, 1f, 0.28f), 0.35f, 0.9f, new Vector3(0, 9f, 0));
        lift.Name = "SpireUpdraft";
        Particles(20, 2.5f, new Vector3(radius * 0.4f, 0.3f, radius * 0.4f), at + Vector3.Up * 0.4f,
            new Color(0.7f, 0.65f, 0.6f, 0.5f), 0.05f, 0.1f, new Vector3(0, 4f, 0));
        Sfx.Loop(root, "amb_wind", at + Vector3.Up * 2f, -12f, 30f, 4f);
    }

    /// <summary>A gust zone: wind streaks blowing along it (thicker while it blows), and a roar.</summary>
    private void Gust(int i, Node3D root)
    {
        var g = SpireGusts[i];
        var dir = g.Push.Normalized();
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        var streaks = new GpuParticles3D
        {
            Name = $"Gust{i}",
            Amount = 60,
            Lifetime = 1.1f,
            Preprocess = 1.1f,
            Position = g.Center - dir * g.Half.X * 0.6f,
            DrawPass1 = new BoxMesh { Size = new Vector3(0.04f, 2.6f, 0.04f), Material = mat },
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = g.Half,
                Direction = dir,
                Spread = 4f,
                InitialVelocityMin = g.Push.Length() * 2.6f,
                InitialVelocityMax = g.Push.Length() * 3.4f,
                Gravity = Vector3.Zero,
                ParticleFlagAlignY = true,
                Color = new Color(1f, 1f, 1f, 0.38f),
                ScaleMin = 0.6f,
                ScaleMax = 1.2f,
            },
            VisibilityAabb = new Aabb(-g.Half - Vector3.One * 30, (g.Half + Vector3.One * 30) * 2),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(streaks);
        var sound = Sfx.Loop(root, "amb_wind", g.Center, -30f, 45f, 10f);
        _gustFx.Add((streaks, sound));
    }

    // ================================================================== the Spire Blossom

    private void BuildSpireBlossom(Node3D root)
    {
        var shrine = new BlossomShrine
        {
            Name = "SpireShrine", ShrineName = "Spire Blossom", SafeRadius = 7f, Position = SpireBlossomPos,
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        AddChild(shrine);
        shrine.Rotation = new Vector3(0, Mathf.DegToRad(-20), 0);
        shrine.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.6f, Height = 4f }, Position = new Vector3(0, 2, 0) });
        var tree = Models.Instance(Models.CherryTree);
        tree.Scale = Vector3.One * 0.95f;
        shrine.AddChild(tree);
        shrine.AddChild(new OmniLight3D { Position = new Vector3(0.3f, 5f, 2f), LightColor = Blush, LightEnergy = 1.8f, OmniRange = 11f });
        Particles(80, 9f, new Vector3(3, 1.2f, 2.5f), SpireBlossomPos + new Vector3(0, 5f, 0), new Color(1f, 0.6f, 0.75f), 0.08f, 0.16f,
            new Vector3(0.8f, -0.25f, -0.3f));
        shrine.AddChild(new Label3D
        {
            Text = "Spire Blossom", Position = new Vector3(0, 8f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.01f, OutlineSize = 12, Modulate = Blush,
        });
    }

    /// <summary>A small stick nest on a spire top where storm harpies roost.</summary>
    private void BuildStormNest(Vector3 at, Node3D root)
    {
        var sticks = Mat(new Color(0.3f, 0.25f, 0.2f));
        var spot = at + new Vector3(2f, 0, -2f);
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.Tau * i / 10f;
            Visual(new BoxMesh { Size = new Vector3(0.1f, 0.1f, 1.6f) }, sticks,
                spot + new Vector3(Mathf.Cos(a) * 1.1f, 0.15f + (i % 2) * 0.1f, Mathf.Sin(a) * 1.1f), new Vector3(0.2f * (i % 2), a, 0), root);
        }
        Visual(new SphereMesh { Radius = 0.14f, Height = 0.2f }, Mat(new Color(0.75f, 0.8f, 0.86f)), spot + new Vector3(0.2f, 0.12f, 0.1f), Vector3.Zero, root);
    }

    /// <summary>A great ragged nest of sticks and bones on the tallest spire: the Storm Harpy Queen's.</summary>
    private void BuildEmptyNest(Vector3 top, Node3D root)
    {
        var sticks = Mat(new Color(0.32f, 0.24f, 0.17f));
        var bone = Mat(new Color(0.85f, 0.82f, 0.72f));
        for (int ring = 0; ring < 2; ring++)
            for (int i = 0; i < 18; i++)
            {
                float a = Mathf.Tau * i / 18f + ring * 0.17f, r = 3.2f - ring * 0.8f;
                Visual(new BoxMesh { Size = new Vector3(0.14f, 0.14f, 3f) }, sticks,
                    top + new Vector3(Mathf.Cos(a) * r, 0.25f + ring * 0.25f + (i % 3) * 0.08f, Mathf.Sin(a) * r),
                    new Vector3(0.25f * (i % 2), a, 0), root);
            }
        for (int i = 0; i < 4; i++)
            Visual(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 0.9f }, bone,
                top + new Vector3(_srng.RandfRange(-1.5f, 1.5f), 0.12f, _srng.RandfRange(-1.5f, 1.5f)), new Vector3(Mathf.Pi / 2f, _srng.RandfRange(0, 3), 0), root);
        Particles(30, 8f, new Vector3(5, 3, 5), top + new Vector3(0, 3, 0), new Color(0.55f, 0.55f, 0.7f), 0.1f, 0.2f, new Vector3(0.6f, -0.4f, 0.2f));
    }

    /// <summary>More spires further out (scenery only), so the field fades into the haze.</summary>
    private void SpireScenery(Node3D root)
    {
        for (int i = 0; i < 9; i++)
        {
            var p = new Vector3(_srng.RandfRange(SpireField.Position.X - 20, SpireField.End.X + 30), 0, _srng.RandfRange(SpireField.Position.Y - 40, SpireField.Position.Y + 40));
            bool near = false;
            foreach (var s in Spires)
                if (new Vector2(p.X - s.Top.X, p.Z - s.Top.Z).Length() < s.Half + 16f) near = true;
            if (near) continue;
            float h = _srng.RandfRange(45, 110);
            p.Y = -70 + h / 2f;
            SRock(p, new Vector3(_srng.RandfRange(8, 16), h, _srng.RandfRange(8, 16)),
                new Vector3(_srng.RandfRange(-0.08f, 0.08f), _srng.RandfRange(0, 3), _srng.RandfRange(-0.08f, 0.08f)), root, collide: false);
        }
    }
}
