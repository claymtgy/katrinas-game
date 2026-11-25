using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The Cinder Foothills (GDD §11, step 3): a burnt highland standing out of the cloud sea
/// north-west of the Spire Cliffs, under the volcano. Reached over the Warlord's rope bridge from
/// the top of the Tall Spire (a stretch of it is broken: jump, glide, or grapple the ring past
/// the gap). Ash-grey hills round a low basin, dead trees, black coal seams in the rock, steam
/// vents and glowing ground that make the place hot, cool springs and the Ashen Blossom to cool
/// off at, the ruin of an old forge, the Bandit Warlord's palisade fort, and the locked gate of the
/// Ember Pass. Its ground is a terrain island of its own (<see cref="Terrain.Island"/>), registered
/// in <see cref="FoothillsTerrain"/>. Placed relative to <see cref="Regions.Foothills"/>
/// (<see cref="InFoothills"/>). Enemies: Wildlife.Foothills.cs; the heat: Net.Heat.cs.</summary>
public partial class HomeCliffs
{
    /// <summary>A place in the Foothills, given where it is in their own layout.</summary>
    public static Vector3 InFoothills(float x, float y, float z) => Regions.Foothills + new Vector3(x, y, z);

    /// <summary>The island's half widths (an ellipse, with a ragged edge).</summary>
    private const float IslandRx = 95f, IslandRz = 80f;
    /// <summary>Where the island is (x, z), with room for its ragged edge.</summary>
    public static readonly Rect2 FoothillsRect = Regions.Move(new Rect2(-104, -90, 208, 180), Regions.Foothills);

    /// <summary>The bridge's landing, on the island's south-east lip (at a fixed height, so the
    /// bridge and its grapple ring can be worked out before the ground is).</summary>
    public static readonly Vector3 FootLanding = InFoothills(27, 0, 60);
    public const float LandingHeight = 24f;
    public static readonly Vector3 FortCenter = InFoothills(-45, 0, -30);
    public static readonly Vector3 AshenBlossomPos = InFoothills(36, 0, 24);
    public static readonly Vector3 ForgeRuinPos = InFoothills(6, 0, 2);
    public static readonly Vector3 EmberGatePos = InFoothills(-56, 0, -42);

    /// <summary>Cool springs: stand in one to shed the heat fast.</summary>
    public static readonly Vector3[] CoolSprings = { InFoothills(28, 0, 33), InFoothills(-22, 0, -12), InFoothills(-60, 0, 8), InFoothills(2, 0, -44) };
    public const float SpringRadius = 3f;
    /// <summary>Steam vents: scalding close up.</summary>
    public static readonly Vector3[] SteamVents =
    {
        InFoothills(12, 0, 38), InFoothills(-6, 0, 18), InFoothills(52, 0, -26), InFoothills(-36, 0, 10), InFoothills(-40, 0, -54), InFoothills(22, 0, -22),
    };
    public const float VentRadius = 4f;
    /// <summary>Patches of glowing, cracked ground (centre, radius).</summary>
    public static readonly (Vector3 At, float Radius)[] HotGround =
    {
        (InFoothills(-12, 0, -6), 8f), (InFoothills(-30, 0, -58), 7f), (InFoothills(40, 0, -38), 7f),
    };

    // ---------------------------------------------------------------- the bridge

    public const float BridgeSag = 2.4f, BridgeHalfWidth = 1.15f;
    /// <summary>The broken stretch (as a fraction of the way from the spire).</summary>
    public const float GapFrom = 0.5f, GapTo = 0.56f;

    /// <summary>From the Tall Spire toward the landing (flat). Properties, not fields: the spires
    /// are set up in another file, and fields there may not be ready when these would be.</summary>
    public static Vector3 BridgeDir => new Vector3(FootLanding.X - Spires[4].Top.X, 0, FootLanding.Z - Spires[4].Top.Z).Normalized();

    /// <summary>The bridge's spire end: on the Tall Spire's top, just in from its edge.</summary>
    public static Vector3 BridgeSpireEnd
    {
        get
        {
            var d = BridgeDir;
            float edge = Spires[4].Half / Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Z));
            return Spires[4].Top + d * (edge - 0.6f) + Vector3.Up * 0.05f;
        }
    }

    public static Vector3 BridgeIslandEnd => new(FootLanding.X, LandingHeight + 0.05f, FootLanding.Z);

    /// <summary>The middle of the deck at <paramref name="t"/> (0 at the spire, 1 at the landing): it sags.</summary>
    public static Vector3 BridgeAt(float t) =>
        BridgeSpireEnd.Lerp(BridgeIslandEnd, t) + Vector3.Down * BridgeSag * 4f * t * (1f - t);

    /// <summary>The grapple ring hung from the arch just past the gap (in HomeCliffs.Grapple's list).</summary>
    public static Vector3 BridgeAnchorRing => BridgeAt(GapTo + 0.015f) + Vector3.Up * 1.95f;

    /// <summary>On the Foothills (their island, or over it).</summary>
    public static bool InFoothillsArea(Vector3 p) => FoothillsRect.HasPoint(new Vector2(p.X, p.Z)) && p.Y > -30f;

    // ---------------------------------------------------------------- the ground

    private static FastNoiseLite _footHills, _footEdge;

    /// <summary>Flattened places: centre (local), flat radius, blend radius, height (NaN: the
    /// ground's own trend there).</summary>
    private static readonly (Vector2 C, float Inner, float Outer, float H)[] FootFlats = BuildFootFlats();

    private static (Vector2, float, float, float)[] BuildFootFlats()
    {
        Vector2 L(Vector3 w) => new(w.X - Regions.Foothills.X, w.Z - Regions.Foothills.Z);
        var list = new List<(Vector2, float, float, float)>
        {
            (L(FootLanding), 8f, 16f, LandingHeight),
            (L(AshenBlossomPos), 7f, 14f, float.NaN),
            (L(FortCenter), 17f, 27f, 24f),
            (L(ForgeRuinPos), 7f, 13f, float.NaN),
            (L(EmberGatePos), 7f, 14f, 30f),
        };
        foreach (var s in CoolSprings) list.Add((L(s), SpringRadius + 1.5f, SpringRadius + 6f, float.NaN));
        foreach (var v in SteamVents) list.Add((L(v), 2.5f, 7f, float.NaN));
        foreach (var (at, r) in HotGround) list.Add((L(at), r, r + 6f, float.NaN));
        return list.ToArray();
    }

    /// <summary>The bowl: low (≈9 m) in the middle, ≈24 m at the rim, higher still (≈32 m) to the
    /// north-west where the land climbs toward the volcano.</summary>
    private static float FootTrend(float lx, float lz)
    {
        float r = Mathf.Sqrt(lx * lx / (IslandRx * IslandRx) + lz * lz / (IslandRz * IslandRz));
        float bowl = 9f + 15f * Mathf.SmoothStep(0.15f, 0.85f, r);
        float nw = 9f * Mathf.Clamp(-(lx + lz) / 150f, 0f, 1f);
        return bowl + nw;
    }

    /// <summary>How far out from the middle (1 = the edge), with a ragged edge (but never ragged
    /// at the landing, where the bridge meets it).</summary>
    private static float FootEdge(float lx, float lz)
    {
        float r = Mathf.Sqrt(lx * lx / (IslandRx * IslandRx) + lz * lz / (IslandRz * IslandRz));
        float a = Mathf.Atan2(lz, lx);
        float rag = _footEdge.GetNoise2D(Mathf.Cos(a) * 60f, Mathf.Sin(a) * 60f) * 0.07f;
        var landing = new Vector2(FootLanding.X - Regions.Foothills.X, FootLanding.Z - Regions.Foothills.Z);
        float away = Mathf.SmoothStep(12f, 32f, new Vector2(lx, lz).DistanceTo(landing));
        return r + rag * away - (1f - away) * 0.04f;
    }

    private static float FootHeight(float x, float z)
    {
        float lx = x - Regions.Foothills.X, lz = z - Regions.Foothills.Z;
        float h = FootTrend(lx, lz) + _footHills.GetNoise2D(x, z) * 5f + _footEdge.GetNoise2D(x * 3f, z * 3f) * 0.6f;
        var p = new Vector2(lx, lz);
        foreach (var (c, inner, outer, fh) in FootFlats)
        {
            float d = p.DistanceTo(c);
            if (d >= outer) continue;
            float target = float.IsNaN(fh) ? FootTrend(c.X, c.Y) : fh;
            float k = d <= inner ? 1f : 1f - Mathf.SmoothStep(inner, outer, d);
            h = Mathf.Lerp(h, target, k);
        }
        // Off the rim: cliffs down into the clouds.
        float e = FootEdge(lx, lz);
        return Mathf.Lerp(h, -42f, Mathf.SmoothStep(0.86f, 1.0f, e));
    }

    private static bool FootInside(float x, float z) => FootEdge(x - Regions.Foothills.X, z - Regions.Foothills.Z) < 1f;

    /// <summary>The ground: soot-black on the slopes, ash grey and scorched brown on the flats.</summary>
    private static ShaderMaterial _ashGround;
    private static ShaderMaterial AshGround => _ashGround ??= StyleMaterials.Terrain(
        new Color(0.08f, 0.07f, 0.08f), new Color(0.3f, 0.28f, 0.28f), new Color(0.34f, 0.24f, 0.19f), new Color(0.46f, 0.32f, 0.24f));

    /// <summary>Register the island with the terrain (before it's locked).</summary>
    private void FoothillsTerrain()
    {
        _footHills ??= new FastNoiseLite { Seed = 9113, Frequency = 0.022f, FractalOctaves = 3, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        _footEdge ??= new FastNoiseLite { Seed = 4127, Frequency = 0.03f, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };
        Terrain.Island(FoothillsRect, FootHeight, FootInside, AshGround);
    }

    // ================================================================== building

    private RandomNumberGenerator _frng;
    private Node3D _foot;

    private static float FGround(Vector3 p) => Terrain.HeightAt(p.X, p.Z);
    private static Vector3 OnFGround(Vector3 p, float lift = 0f) => new(p.X, FGround(p) + lift, p.Z);

    /// <summary>After the terrain is laid out (heights are needed): the whole region, under one
    /// root that isn't seated on the hills (everything here is put at its own height).</summary>
    private void BuildFoothills()
    {
        _frng = new RandomNumberGenerator { Seed = (ulong)Seed * 89 + 11 };
        _foot = new Node3D { Name = "CinderFoothills" };
        AddChild(_foot);
        _unseated.Add(_foot);

        BuildWarlordBridge();
        BuildLanding();
        BuildAshenBlossom();
        foreach (var s in CoolSprings) Spring(s);
        foreach (var v in SteamVents) Vent(v);
        foreach (var (at, r) in HotGround) HotPatch(at, r);
        BuildForgeRuin();
        BuildFort();
        BuildEmberGate();
        CoalVeins();
        FootScenery();
        GD.Print($"[Foothills] ground at the landing {FGround(FootLanding):0.0} m (bridge {LandingHeight:0} m, {BridgeSpireEnd.DistanceTo(BridgeIslandEnd):0} m long), " +
                 $"blossom {FGround(AshenBlossomPos):0.0}, fort {FGround(FortCenter):0.0}, forge {FGround(ForgeRuinPos):0.0}, gate {FGround(EmberGatePos):0.0}; " +
                 $"{_coalCount} coal seams; island covers the landing: {Terrain.Covers(FootLanding.X, FootLanding.Z)}");
    }

    /// <summary>Particles made by the shared helper (which adds to the world root) moved under the
    /// Foothills, so the hills don't lift them.</summary>
    private GpuParticles3D FParticles(int amount, float lifetime, Vector3 extents, Vector3 position, Color color,
        float sizeMin, float sizeMax, Vector3 drift, float emissive = 0f)
    {
        var p = Particles(amount, lifetime, extents, position, color, sizeMin, sizeMax, drift, emissive);
        RemoveChild(p);
        _foot.AddChild(p);
        return p;
    }

    /// <summary>Rock with the Foothills' own random numbers.</summary>
    private Node3D FRock(Vector3 pos, Vector3 size, float yaw, bool collide = true, float tilt = 0.12f)
    {
        var path = Models.AllRocks[_frng.RandiRange(0, Models.AllRocks.Length - 1)];
        size *= 1.15f;
        Node3D node = Basalt.Suits(size) ? Basalt.Make(Basalt.VariantAt(pos), size, collide)
            : collide ? Models.RockBody(path, size) : Models.Instance(path);
        if (!collide) node.Scale = size;
        node.Position = pos;
        node.Rotation = new Vector3(_frng.RandfRange(-tilt, tilt), yaw, _frng.RandfRange(-tilt, tilt));
        _foot.AddChild(node);
        return node;
    }

    private void FootSign(string name, Vector3 at, float yaw, string title, string label, string message)
    {
        var sign = new InfoSign
        {
            Name = name, Title = title, Message = message, Position = OnFGround(at), Rotation = new Vector3(0, yaw, 0),
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        _foot.AddChild(sign);
        sign.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, 2.2f, 0.4f) }, Position = new Vector3(0, 1.1f, 0) });
        Visual(new BoxMesh { Size = new Vector3(0.15f, 2f, 0.15f) }, Mat(new Color(0.16f, 0.12f, 0.1f)), new Vector3(0, 1, -0.115f), Vector3.Zero, sign);
        Visual(new BoxMesh { Size = new Vector3(1.5f, 0.5f, 0.08f) }, Mat(new Color(0.3f, 0.22f, 0.16f)), new Vector3(0, 1.8f, 0), Vector3.Zero, sign);
        sign.AddChild(new Label3D { Text = label, Position = new Vector3(0, 1.8f, 0.06f), FontSize = 48, PixelSize = 0.006f, Modulate = new Color(1f, 0.75f, 0.5f) });
    }

    private void FootLandmark(string name, string title, Vector3 at, float radius) =>
        _foot.AddChild(new Landmark { Name = "Landmark_" + name, Title = title, Radius = radius, Position = OnFGround(at) });

    // ================================================================== the Warlord's bridge

    /// <summary>A rope bridge of tarred planks, sagging from the Tall Spire down to the island,
    /// with rope rails (and walls you can't see, so nobody walks off the side). One stretch is
    /// broken: planks gone, one rail snapped; a grapple ring hangs from an arch past it.</summary>
    private void BuildWarlordBridge()
    {
        var root = new Node3D { Name = "WarlordBridge" };
        _foot.AddChild(root);
        var body = new StaticBody3D { Name = "BridgeDeck", CollisionLayer = GameState.LayerWorld, CollisionMask = 0 };
        root.AddChild(body);
        var plankMat = StyleMaterials.Toon(new Color(0.3f, 0.21f, 0.15f), 0.2f);
        var plankDark = StyleMaterials.Toon(new Color(0.2f, 0.14f, 0.1f), 0.2f);
        var rope = StyleMaterials.Toon(new Color(0.55f, 0.45f, 0.32f), 0.15f);
        var post = StyleMaterials.Toon(new Color(0.18f, 0.13f, 0.1f), 0.2f);

        Basis Frame(float t)
        {
            var fwd = (BridgeAt(Mathf.Min(1f, t + 0.003f)) - BridgeAt(Mathf.Max(0f, t - 0.003f))).Normalized();
            var right = fwd.Cross(Vector3.Up).Normalized();
            return new Basis(right, right.Cross(fwd), -fwd);
        }
        bool InGap(float t) => t > GapFrom && t < GapTo;

        float length = BridgeSpireEnd.DistanceTo(BridgeIslandEnd);
        int n = Mathf.CeilToInt(length / 0.95f);
        for (int i = 0; i < n; i++)
        {
            float t = (i + 0.5f) / n;
            if (InGap(t)) continue;
            var b = Frame(t);
            var at = BridgeAt(t);
            float wob = _frng.RandfRange(-0.04f, 0.04f);
            var mi = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(BridgeHalfWidth * 2f + _frng.RandfRange(-0.1f, 0.1f), 0.1f, 0.78f) },
                MaterialOverride = i % 4 == 0 ? plankDark : plankMat,
                Transform = new Transform3D(b.Rotated(b.Y, wob), at),
            };
            root.AddChild(mi);
            // The deck and its side walls.
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(BridgeHalfWidth * 2f + 0.2f, 0.16f, 1.02f) }, Transform = new Transform3D(b, at - b.Y * 0.03f) });
            foreach (float s in new[] { -1f, 1f })
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.12f, 1.3f, 1.02f) }, Transform = new Transform3D(b, at + b.X * s * (BridgeHalfWidth + 0.1f) + b.Y * 0.65f) });
            // Hangers down from the rails, every third plank.
            if (i % 3 == 0)
                foreach (float s in new[] { -1f, 1f })
                    Visual(new BoxMesh { Size = new Vector3(0.03f, 1f, 0.03f) }, rope, at + b.X * s * BridgeHalfWidth + b.Y * 0.5f, b.GetEuler(), root);
        }

        // Rope rails (the right one snapped over the gap, its ends dangling).
        int segs = n;
        foreach (float s in new[] { -1f, 1f })
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                if (s > 0 && (InGap(t0) || InGap(t1))) continue;
                var a = BridgeAt(t0) + Frame(t0).X * s * BridgeHalfWidth + Vector3.Up * 1.0f;
                var c = BridgeAt(t1) + Frame(t1).X * s * BridgeHalfWidth + Vector3.Up * 1.0f;
                RopeSeg(a, c, rope, root);
            }
        foreach (float t in new[] { GapFrom, GapTo })
        {
            var a = BridgeAt(t) + Frame(t).X * BridgeHalfWidth + Vector3.Up * 1.0f;
            RopeSeg(a, a + Vector3.Down * 2.4f + Frame(t).Z * (t < 0.53f ? 0.3f : -0.3f), rope, root);
            // A broken plank hanging off each side of the gap.
            var edge = BridgeAt(t);
            Visual(new BoxMesh { Size = new Vector3(BridgeHalfWidth * 1.6f, 0.08f, 0.6f) }, plankDark,
                edge + Vector3.Down * 0.9f, new Vector3(t < 0.53f ? 1.2f : -1.2f, BridgeDir.SignedAngleTo(Vector3.Back, Vector3.Down), 0.3f), root);
        }

        // Posts at each end, and the arch past the gap with its grapple ring (the ring itself is
        // drawn with the others, in HomeCliffs.Grapple).
        foreach (float t in new[] { 0f, 1f })
        {
            var b = Frame(t);
            foreach (float s in new[] { -1f, 1f })
                Visual(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.15f, Height = 2.2f, RadialSegments = 6 }, post,
                    BridgeAt(t) + b.X * s * (BridgeHalfWidth + 0.15f) + Vector3.Up * 0.6f, Vector3.Zero, root);
        }
        {
            float t = GapTo + 0.015f;
            var b = Frame(t);
            var at = BridgeAt(t);
            foreach (float s in new[] { -1f, 1f })
                Visual(new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.12f, Height = 2.6f, RadialSegments = 6 }, post,
                    at + b.X * s * (BridgeHalfWidth + 0.15f) + Vector3.Up * 1.2f, Vector3.Zero, root);
            Visual(new BoxMesh { Size = new Vector3(BridgeHalfWidth * 2f + 0.6f, 0.16f, 0.16f) }, post, at + Vector3.Up * 2.45f, b.GetEuler(), root);
        }
    }

    private void RopeSeg(Vector3 a, Vector3 b, Material mat, Node3D parent)
    {
        var d = b - a;
        if (d.LengthSquared() < 1e-4f) return;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.045f, 0.045f, d.Length() + 0.02f) }, MaterialOverride = mat,
            Transform = new Transform3D(Basis.LookingAt(d, Mathf.Abs(d.Normalized().Y) > 0.95f ? Vector3.Forward : Vector3.Up), (a + b) / 2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    // ================================================================== the landing and the blossom

    private void BuildLanding()
    {
        var at = OnFGround(FootLanding);
        FootSign("FoothillsSign", FootLanding - BridgeDir * 3f + new Vector3(BridgeDir.Z, 0, -BridgeDir.X) * 2.6f, Mathf.Atan2(-BridgeDir.X, -BridgeDir.Z),
            "Cinder Foothills", "FOOTHILLS",
            "The Cinder Foothills: burnt hills under the volcano. The heat builds the longer you stay, faster by the steam vents and on " +
            "glowing ground: cool off in a spring (the pale blue pools) or by the Ashen Blossom before the heat bar fills, or it burns. " +
            "An Ember-glass Charm slows it. Coal runs black in the rock: it feeds a Blast Forge, which makes steel. " +
            "The Bandit Warlord holds the fort to the north-west.");
        FootLandmark("Foothills", "Cinder Foothills", FootLanding, 18f);
        // Charred stakes and a brazier: the bandits' old watch post at the bridgehead.
        for (int i = 0; i < 5; i++)
        {
            var p = FootLanding - BridgeDir * _frng.RandfRange(4f, 7f) + new Vector3(BridgeDir.Z, 0, -BridgeDir.X) * _frng.RandfRange(-5f, 5f);
            if (Mathf.Abs(new Vector3(BridgeDir.Z, 0, -BridgeDir.X).Dot(p - FootLanding)) < 2.2f) continue;
            Block(OnFGround(p, 0.7f), new Vector3(0.3f, 1.8f, 0.3f), new Vector3(_frng.RandfRange(-0.2f, 0.2f), 0, _frng.RandfRange(-0.2f, 0.2f)),
                new Color(0.1f, 0.08f, 0.07f), _foot, layer: GameState.LayerProps);
        }
        var brazier = OnFGround(FootLanding - BridgeDir * 4f - new Vector3(BridgeDir.Z, 0, -BridgeDir.X) * 3f);
        Visual(new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.25f, Height = 0.6f, RadialSegments = 8 }, Mat(new Color(0.22f, 0.2f, 0.2f)), brazier + Vector3.Up * 0.8f, Vector3.Zero, _foot);
        Visual(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 0.6f, RadialSegments = 5 }, Mat(new Color(0.22f, 0.2f, 0.2f)), brazier + Vector3.Up * 0.3f, Vector3.Zero, _foot);
        FParticles(24, 1.4f, new Vector3(0.25f, 0.1f, 0.25f), brazier + Vector3.Up * 1.2f, new Color(1f, 0.5f, 0.15f), 0.08f, 0.2f, new Vector3(0, 1.5f, 0), emissive: 2f);
        _foot.AddChild(new OmniLight3D { Position = brazier + Vector3.Up * 1.6f, LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.4f, OmniRange = 7f });
        if (!Net.Instance.IsDedicated) Sfx.Loop(_foot, "amb_fire", brazier + Vector3.Up, -10f, 10f, 2f);
        _ = at;
    }

    /// <summary>The Ashen Blossom: a cherry tree that somehow lives here, grey bark, pale petals,
    /// by the first spring. A sanctuary with fast travel, like the others (and cool: the heat
    /// drains away beside it).</summary>
    private void BuildAshenBlossom()
    {
        var at = OnFGround(AshenBlossomPos);
        var shrine = new BlossomShrine
        {
            Name = "AshenShrine", ShrineName = "Ashen Blossom", SafeRadius = 8f, Position = at,
            CollisionLayer = GameState.LayerProps, CollisionMask = 0,
        };
        _foot.AddChild(shrine);
        shrine.Rotation = new Vector3(0, 0.6f, 0);
        shrine.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.6f, Height = 4f }, Position = new Vector3(0, 2, 0) });
        var tree = Models.Instance(Models.CherryTree);
        tree.Scale = Vector3.One * 0.9f;
        shrine.AddChild(tree);
        var pale = new Color(0.95f, 0.86f, 0.9f);
        shrine.AddChild(new OmniLight3D { Position = new Vector3(0.3f, 5f, 2f), LightColor = pale, LightEnergy = 1.6f, OmniRange = 11f });
        FParticles(70, 9f, new Vector3(3, 1.2f, 2.5f), at + new Vector3(0, 5f, 0), pale, 0.08f, 0.16f, new Vector3(0.6f, -0.25f, -0.3f));
        shrine.AddChild(new Label3D
        {
            Text = "Ashen Blossom", Position = new Vector3(0, 8f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, PixelSize = 0.01f, OutlineSize = 12, Modulate = pale,
        });
    }

    // ================================================================== heat and cool

    private void Spring(Vector3 c)
    {
        var at = OnFGround(c);
        var water = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.35f, 0.72f, 0.78f, 0.78f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.08f, Metallic = 0.1f, EmissionEnabled = true, Emission = new Color(0.12f, 0.3f, 0.34f),
        };
        Visual(new CylinderMesh { TopRadius = SpringRadius, BottomRadius = SpringRadius * 0.8f, Height = 0.12f, RadialSegments = 18 }, water, at + Vector3.Up * 0.06f, Vector3.Zero, _foot)
            .CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        // Pale rim stones.
        int stones = 9;
        for (int i = 0; i < stones; i++)
        {
            float a = Mathf.Tau * i / stones + _frng.RandfRange(-0.15f, 0.15f);
            var p = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (SpringRadius + 0.35f);
            Visual(new BoxMesh { Size = new Vector3(_frng.RandfRange(0.5f, 0.9f), _frng.RandfRange(0.25f, 0.45f), _frng.RandfRange(0.4f, 0.7f)) },
                Mat(new Color(0.55f, 0.6f, 0.6f)), OnFGround(p, 0.12f), new Vector3(0, a, _frng.RandfRange(-0.2f, 0.2f)), _foot);
        }
        FParticles(12, 5f, new Vector3(SpringRadius * 0.7f, 0.2f, SpringRadius * 0.7f), at + Vector3.Up * 0.4f, new Color(0.85f, 0.95f, 1f, 0.25f), 0.5f, 1.1f, new Vector3(0, 0.25f, 0));
        if (!Net.Instance.IsDedicated) Sfx.Loop(_foot, "water", at, -18f, 12f, 2f);
    }

    private void Vent(Vector3 c)
    {
        var at = OnFGround(c);
        FRock(at + Vector3.Down * 0.5f, new Vector3(2.6f, 1.2f, 2.4f), _frng.RandfRange(0, Mathf.Tau), collide: false, tilt: 0.05f);
        var glow = StyleMaterials.Toon(new Color(1f, 0.45f, 0.15f));
        glow.EmissionEnabled = true;
        glow.Emission = new Color(1f, 0.4f, 0.1f);
        glow.EmissionEnergyMultiplier = 2.5f;
        Visual(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.55f, Height = 0.05f, RadialSegments = 8 }, glow, at + Vector3.Up * 0.72f, Vector3.Zero, _foot);
        var steam = FParticles(50, 3.5f, new Vector3(0.4f, 0.2f, 0.4f), at + Vector3.Up * 1f, new Color(0.92f, 0.92f, 0.95f, 0.35f), 0.6f, 1.6f, new Vector3(0, 4f, 0));
        steam.Name = "SteamVent";
        _foot.AddChild(new OmniLight3D { Position = at + Vector3.Up * 1.5f, LightColor = new Color(1f, 0.45f, 0.15f), LightEnergy = 1.2f, OmniRange = 5f });
        if (!Net.Instance.IsDedicated) Sfx.Loop(_foot, "amb_wind", at + Vector3.Up, -16f, 14f, 2f);
    }

    /// <summary>Cracked, glowing ground: a dark scorch with ember-orange seams across it.</summary>
    private void HotPatch(Vector3 c, float r)
    {
        var at = OnFGround(c);
        var scorch = Visual(new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.02f, RadialSegments = 20 }, Mat(new Color(0.07f, 0.05f, 0.05f)), at + Vector3.Up * 0.04f, Vector3.Zero, _foot);
        scorch.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        var crack = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.45f, 0.12f), EmissionEnabled = true, Emission = new Color(1f, 0.38f, 0.08f),
            EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        int cracks = (int)(r * 2.2f);
        for (int i = 0; i < cracks; i++)
        {
            float a = _frng.RandfRange(0, Mathf.Tau), d = _frng.RandfRange(0, r * 0.8f);
            var p = at + new Vector3(Mathf.Cos(a) * d, 0.07f, Mathf.Sin(a) * d);
            Visual(new BoxMesh { Size = new Vector3(0.12f, 0.02f, _frng.RandfRange(1f, 2.6f)) }, crack, p, new Vector3(0, _frng.RandfRange(0, Mathf.Tau), 0), _foot)
                .CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        _foot.AddChild(new OmniLight3D { Position = at + Vector3.Up * 1.2f, LightColor = new Color(1f, 0.42f, 0.12f), LightEnergy = 1.5f, OmniRange = r + 3f });
        FParticles(30, 4f, new Vector3(r * 0.6f, 0.2f, r * 0.6f), at + Vector3.Up * 0.5f, new Color(1f, 0.45f, 0.12f), 0.04f, 0.09f, new Vector3(0, 1.2f, 0), emissive: 2f);
    }

    // ================================================================== the old forge

    /// <summary>A ruined forge in the basin: broken brick walls, a cold hearth, an anvil, and the
    /// forge-master's strongbox (steel, coal and iron: a taste of the Blast Forge).</summary>
    private void BuildForgeRuin()
    {
        var at = OnFGround(ForgeRuinPos);
        var root = new Node3D { Name = "ForgeRuin", Position = at };
        _foot.AddChild(root);
        var brick = new Color(0.4f, 0.22f, 0.18f);
        foreach (var (pos, size) in new[]
                 {
                     (new Vector3(-3.5f, 0, 0), new Vector3(0.6f, 2.6f, 6f)), (new Vector3(0, 0, -3.2f), new Vector3(7f, 1.6f, 0.6f)),
                     (new Vector3(3.4f, 0, -1.5f), new Vector3(0.6f, 1.1f, 3f)), (new Vector3(-2f, 0, 3.1f), new Vector3(2.6f, 0.8f, 0.6f)),
                 })
            Block(pos + Vector3.Up * size.Y / 2f, size, new Vector3(0, 0, _frng.RandfRange(-0.04f, 0.04f)), brick, root);
        // The cold hearth: a squat brick box with a black mouth, and a fallen chimney.
        Block(new Vector3(-2.2f, 0.7f, -1.8f), new Vector3(1.6f, 1.4f, 1.4f), Vector3.Zero, brick.Darkened(0.2f), root);
        Visual(new BoxMesh { Size = new Vector3(0.8f, 0.5f, 0.05f) }, Mat(new Color(0.05f, 0.04f, 0.04f)), new Vector3(-2.2f, 0.6f, -1.08f), Vector3.Zero, root);
        Block(new Vector3(1.5f, 0.35f, 2f), new Vector3(0.7f, 0.7f, 3.4f), new Vector3(0, 0.4f, 0), brick.Darkened(0.1f), root, collide: false);
        // The anvil.
        Block(new Vector3(0.8f, 0.35f, -0.6f), new Vector3(0.4f, 0.7f, 0.4f), Vector3.Zero, new Color(0.25f, 0.18f, 0.12f), root);
        Visual(new BoxMesh { Size = new Vector3(0.9f, 0.25f, 0.35f) }, Mat(new Color(0.25f, 0.26f, 0.29f)), new Vector3(0.8f, 0.82f, -0.6f), Vector3.Zero, root);
        Cache(root, "ForgeStrongbox", "forge-master's strongbox", new Vector3(-0.8f, 0, 1.2f), new Vector3(1.0f, 0.6f, 0.65f),
            new Color(0.25f, 0.2f, 0.18f), 0.4f, ("steel_ingot", 2), ("coal", 5), ("iron_ingot", 2), ("coin", 15));
        FootLandmark("ForgeRuin", "The Old Forge", ForgeRuinPos, 12f);
        FootSign("ForgeSign", ForgeRuinPos + new Vector3(4.5f, 0, 3.5f), 0.8f, "The Old Forge", "OLD FORGE",
            "A forge-master worked here once, burning the hills' coal. Iron and coal together, hot enough, make steel: " +
            "build a Blast Forge at home (bricks, iron, stone and coal) and you can make it too.");
    }

    // ================================================================== the Warlord's fort

    /// <summary>A ring palisade of sharpened logs with its gate toward the landing, tents and a war
    /// tent inside, a fire, racks and the Warlord's black banner.</summary>
    private void BuildFort()
    {
        var at = OnFGround(FortCenter);
        var root = new Node3D { Name = "WarlordFort", Position = at };
        _foot.AddChild(root);
        var toLanding = new Vector3(FootLanding.X - FortCenter.X, 0, FootLanding.Z - FortCenter.Z).Normalized();
        float gateAngle = Mathf.Atan2(toLanding.Z, toLanding.X);
        var logMat = StyleMaterials.Toon(new Color(0.22f, 0.16f, 0.12f), 0.2f);
        var walls = new StaticBody3D { Name = "Palisade", CollisionLayer = GameState.LayerWorld, CollisionMask = 0 };
        root.AddChild(walls);
        const float R = 14f;
        int logs = (int)(Mathf.Tau * R / 0.55f);
        for (int i = 0; i < logs; i++)
        {
            float a = Mathf.Tau * i / logs;
            if (Mathf.Abs(Mathf.AngleDifference(a, gateAngle)) < 0.2f) continue; // the gate (≈5 m)
            var p = new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R);
            float h = _frng.RandfRange(3.4f, 4.2f);
            float ground = FGround(at + p) - at.Y;
            Visual(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.26f, Height = h, RadialSegments = 6 }, logMat, p + Vector3.Up * (ground + h / 2f - 0.3f),
                new Vector3(_frng.RandfRange(-0.05f, 0.05f), 0, _frng.RandfRange(-0.05f, 0.05f)), root);
            Visual(new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.22f, Height = 0.5f, RadialSegments = 6 }, logMat, p + Vector3.Up * (ground + h - 0.05f), Vector3.Zero, root);
            if (i % 2 == 0)
                walls.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = new Vector3(1.2f, 4f, 0.5f) },
                    Transform = new Transform3D(new Basis(Vector3.Up, -a + Mathf.Pi / 2f), p + Vector3.Up * (ground + 1.7f)),
                });
        }
        // Gate towers either side.
        foreach (float s in new[] { -1f, 1f })
        {
            float a = gateAngle + s * 0.26f;
            var p = new Vector3(Mathf.Cos(a) * R, 0, Mathf.Sin(a) * R);
            Block(p + Vector3.Up * 2.8f, new Vector3(1.4f, 5.6f, 1.4f), new Vector3(0, -a, 0), new Color(0.2f, 0.15f, 0.11f), root);
            Visual(new BoxMesh { Size = new Vector3(2f, 0.2f, 2f) }, logMat, p + Vector3.Up * 5.7f, new Vector3(0, -a, 0), root);
            var banner = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(0.9f, 1.8f) }, Position = p + Vector3.Up * 4.4f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.75f,
                Rotation = new Vector3(0, Mathf.Pi / 2f - a, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.06f, 0.06f), CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            };
            root.AddChild(banner);
        }

        // Tents, the war tent at the back, the fire, weapon racks and crates.
        var canvas = StyleMaterials.Toon(new Color(0.36f, 0.32f, 0.28f), 0.2f);
        var war = StyleMaterials.Toon(new Color(0.16f, 0.08f, 0.07f), 0.25f);
        void Tent(Vector3 p, float yaw, Vector3 size, Material mat)
        {
            var tent = new StaticBody3D { Position = p + Vector3.Up * (FGround(at + p) - at.Y), Rotation = new Vector3(0, yaw, 0), CollisionLayer = GameState.LayerProps, CollisionMask = 0 };
            root.AddChild(tent);
            tent.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = size }, MaterialOverride = mat, Position = new Vector3(0, size.Y / 2f, 0) });
            tent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(size.X * 0.3f, size.Y * 0.55f, 0.05f) }, MaterialOverride = Mat(new Color(0.06f, 0.05f, 0.05f)), Position = new Vector3(0, size.Y * 0.28f, size.Z / 2f + 0.01f) });
            tent.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size * new Vector3(0.85f, 0.85f, 0.95f) }, Position = new Vector3(0, size.Y * 0.42f, 0) });
        }
        float back = gateAngle + Mathf.Pi;
        var warAt = new Vector3(Mathf.Cos(back), 0, Mathf.Sin(back)) * 8.5f;
        Tent(warAt, Mathf.Atan2(-warAt.X, -warAt.Z) + Mathf.Pi, new Vector3(4.6f, 3.2f, 5.2f), war);
        foreach (float off in new[] { -1.3f, 1.3f, 2.2f })
        {
            var p = new Vector3(Mathf.Cos(back + off), 0, Mathf.Sin(back + off)) * 9f;
            Tent(p, Mathf.Atan2(-p.X, -p.Z) + Mathf.Pi, new Vector3(2.6f, 1.9f, 3.2f), canvas);
        }
        var fire = GD.Load<PackedScene>("res://scenes/buildings/Campfire.tscn").Instantiate<Node3D>();
        fire.Name = "FortFire";
        fire.Position = Vector3.Up * 0.02f;
        root.AddChild(fire);
        foreach (float off in new[] { 0.9f, -0.9f })
        {
            var p = new Vector3(Mathf.Cos(gateAngle + off), 0, Mathf.Sin(gateAngle + off)) * 10f;
            Prop(Models.Village("Prop_Crate"), p + Vector3.Up * (FGround(at + p) - at.Y), _frng.RandfRange(0, 3), 0.9f, root);
        }
        // The Warlord's black banner on a tall pole by his tent.
        var pole = warAt * 0.7f + new Vector3(warAt.Z, 0, -warAt.X).Normalized() * 3.4f;
        Visual(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.09f, Height = 7f, RadialSegments = 6 }, logMat, pole + Vector3.Up * 3.5f, Vector3.Zero, root);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2.2f, 1.4f) }, Position = pole + new Vector3(1.15f, 6.1f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.04f, 0.04f), CullMode = BaseMaterial3D.CullModeEnum.Disabled },
        });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 8, Rings = 4 }, Position = pole + new Vector3(1.15f, 6.1f, 0.02f),
            MaterialOverride = EnemyMesh.Glow(new Color(1f, 0.4f, 0.12f), 2f),
        });
        FootLandmark("WarlordFort", "The Warlord's Fort", FortCenter, 20f);
    }

    // ================================================================== the Ember Pass

    /// <summary>A stone arch with an iron gate where the land climbs north-west toward the volcano:
    /// the Ember Pass, locked (the Warlord has the key). Beyond, a narrow defile choked with fresh
    /// lava (the Ember Wastes come later).</summary>
    private void BuildEmberGate()
    {
        var at = OnFGround(EmberGatePos);
        // Facing back into the island (toward its middle).
        var inward = new Vector3(Regions.Foothills.X - EmberGatePos.X, 0, Regions.Foothills.Z - EmberGatePos.Z).Normalized();
        float yaw = Mathf.Atan2(inward.X, inward.Z);
        var root = new Node3D { Name = "EmberPass", Position = at, Rotation = new Vector3(0, yaw, 0) };
        _foot.AddChild(root);
        var stone = new Color(0.2f, 0.18f, 0.19f);
        foreach (float s in new[] { -1f, 1f })
            Block(new Vector3(s * 2.6f, 2.6f, 0), new Vector3(1.4f, 5.2f, 1.6f), Vector3.Zero, stone, root);
        Block(new Vector3(0, 5.6f, 0), new Vector3(6.8f, 1.0f, 1.8f), Vector3.Zero, stone.Darkened(0.15f), root);
        var gate = new EmberGate { Name = "EmberGate", CollisionLayer = GameState.LayerWorld | GameState.LayerProps, CollisionMask = 0 };
        root.AddChild(gate);
        gate.Build();
        // Cliffs either side of the defile, so the only way on is through the gate.
        for (int i = 0; i < 6; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            var local = new Vector3(side * _frng.RandfRange(5.5f, 7.5f), 0, -i * 2.8f + _frng.RandfRange(-0.8f, 0.8f));
            var world = at + new Basis(Vector3.Up, yaw) * local;
            float h = _frng.RandfRange(13f, 20f);
            FRock(new Vector3(world.X, FGround(world) + h * 0.35f, world.Z), new Vector3(_frng.RandfRange(5f, 7f), h, _frng.RandfRange(5f, 7f)), _frng.RandfRange(0, 3));
        }
        // Beyond the gate: the lava-choked cleft, glowing.
        var lava = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.42f, 0.1f), EmissionEnabled = true, Emission = new Color(1f, 0.35f, 0.08f),
            EmissionEnergyMultiplier = 3.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        Visual(new BoxMesh { Size = new Vector3(4.5f, 0.3f, 10f) }, lava, new Vector3(0, 0.2f, -8f), Vector3.Zero, root);
        Block(new Vector3(0, 1.5f, -14f), new Vector3(9f, 6f, 2f), new Vector3(0.1f, 0, 0), stone.Darkened(0.3f), root);
        root.AddChild(new OmniLight3D { Position = new Vector3(0, 2.5f, -7f), LightColor = new Color(1f, 0.42f, 0.12f), LightEnergy = 3f, OmniRange = 16f });
        FParticles(60, 5f, new Vector3(3, 1, 5), at + new Basis(Vector3.Up, yaw) * new Vector3(0, 1.5f, -8f), new Color(1f, 0.45f, 0.1f), 0.05f, 0.12f, new Vector3(0, 2f, 0), emissive: 2f);
        if (!Net.Instance.IsDedicated) Sfx.Loop(_foot, "amb_lava", at + new Basis(Vector3.Up, yaw) * new Vector3(0, 1, -8f), -6f, 30f, 6f);
        FootLandmark("EmberPass", "The Ember Pass", EmberGatePos, 14f);
    }

    // ================================================================== coal

    private int _coalCount;

    /// <summary>Coal seams: black, glinting rock that a pickaxe breaks (it grows back after 5 minutes).</summary>
    private void CoalVeins()
    {
        Vector3[] spots =
        {
            InFoothills(18, 0, -40), InFoothills(26, 0, -52), InFoothills(-20, 0, -56), InFoothills(54, 0, -30), InFoothills(-62, 0, 30),
            InFoothills(-52, 0, 42), InFoothills(60, 0, 10), InFoothills(-8, 0, -30), InFoothills(34, 0, -8), InFoothills(-4, 0, 50),
        };
        var ore = StyleMaterials.Rock(new Color(0.07f, 0.065f, 0.075f), new Color(0.24f, 0.22f, 0.26f), 0.55f, 0.6f);
        var glint = StyleMaterials.Toon(new Color(0.5f, 0.5f, 0.56f), 0.8f);
        foreach (var s in spots)
        {
            var n = GD.Load<PackedScene>("res://scenes/props/Rock.tscn").Instantiate<Harvestable>();
            n.Name = $"CoalVein_{_coalCount++}";
            n.DropItemId = "coal";
            n.Tool = "pick";
            n.RequiresTool = true;
            n.DamagePerDrop = 20f;
            n.BonusOnBreak = 2;
            n.RespawnSeconds = 300f;
            n.AddToGroup("coal");
            _foot.AddChild(n);
            n.Position = OnFGround(s, -0.1f);
            n.Rotation = new Vector3(0, _frng.RandfRange(0, Mathf.Tau), 0);
            var visual = n.GetNode<Node3D>("Visual");
            foreach (var m in visual.FindChildren("*", "MeshInstance3D", true, false))
                ((MeshInstance3D)m).MaterialOverride = ore;
            for (int k = 0; k < 6; k++)
            {
                float a = k * 2.4f + _coalCount, r = 0.45f + 0.1f * (k % 3);
                Visual(new BoxMesh { Size = Vector3.One * (0.12f + 0.04f * (k % 2)) }, glint,
                    new Vector3(Mathf.Cos(a) * r, 0.55f + Mathf.Sin(a * 1.7f) * 0.25f, Mathf.Sin(a) * r), new Vector3(a, a * 0.7f, 0.4f), visual);
            }
        }
    }

    // ================================================================== scenery

    /// <summary>Dead trees, ash-grey boulders, crags along the north-west rim (toward the volcano),
    /// and ash falling over everything.</summary>
    private void FootScenery()
    {
        bool Clear(Vector3 p, float pad)
        {
            var f = new Vector2(p.X, p.Z);
            bool Near(Vector3 c, float r) => f.DistanceTo(new Vector2(c.X, c.Z)) < r + pad;
            if (Near(FootLanding, 9f) || Near(AshenBlossomPos, 8f) || Near(FortCenter, 17f) || Near(ForgeRuinPos, 7f) || Near(EmberGatePos, 10f)) return false;
            foreach (var s in CoolSprings) if (Near(s, SpringRadius + 1f)) return false;
            foreach (var v in SteamVents) if (Near(v, 2.5f)) return false;
            // Keep the bridge's line clear near the island.
            var rel = p - FootLanding;
            if (rel.Dot(BridgeDir) > -14f && Mathf.Abs(rel.Dot(new Vector3(BridgeDir.Z, 0, -BridgeDir.X))) < 4f) return false;
            float lx = p.X - Regions.Foothills.X, lz = p.Z - Regions.Foothills.Z;
            return FootEdge(lx, lz) < 0.82f;
        }
        Vector3 Somewhere() => InFoothills(_frng.RandfRange(-IslandRx, IslandRx), 0, _frng.RandfRange(-IslandRz, IslandRz));

        int placed = 0;
        for (int i = 0; i < 120 && placed < 26; i++)
        {
            var p = Somewhere();
            if (!Clear(p, 1.5f)) continue;
            Prop(Models.Nature("DeadTree_" + _frng.RandiRange(1, 5)), OnFGround(p, -0.2f), _frng.RandfRange(0, Mathf.Tau), _frng.RandfRange(1.0f, 1.5f), _foot);
            placed++;
        }
        placed = 0;
        for (int i = 0; i < 120 && placed < 30; i++)
        {
            var p = Somewhere();
            if (!Clear(p, 2f)) continue;
            var size = new Vector3(_frng.RandfRange(1.2f, 3.2f), _frng.RandfRange(0.8f, 2.4f), _frng.RandfRange(1.2f, 3f));
            FRock(OnFGround(p, size.Y * 0.2f), size, _frng.RandfRange(0, Mathf.Tau));
            placed++;
        }
        // Crags along the north-west rim: the land climbing toward the volcano.
        for (int i = 0; i < 9; i++)
        {
            float a = Mathf.Lerp(2.6f, 4.3f, i / 8f) + _frng.RandfRange(-0.08f, 0.08f); // west round to north
            var p = InFoothills(Mathf.Cos(a) * IslandRx * 0.86f, 0, Mathf.Sin(a) * IslandRz * 0.86f);
            var gate = new Vector2(EmberGatePos.X, EmberGatePos.Z);
            if (new Vector2(p.X, p.Z).DistanceTo(gate) < 16f) continue;
            float h = _frng.RandfRange(16f, 30f);
            FRock(new Vector3(p.X, FGround(p) + h * 0.3f, p.Z), new Vector3(_frng.RandfRange(7f, 11f), h, _frng.RandfRange(7f, 11f)), _frng.RandfRange(0, 3), collide: true, tilt: 0.06f);
        }
        // Ash drifting down over the whole island, and embers rising from the basin.
        FParticles(260, 12f, new Vector3(IslandRx * 0.9f, 8f, IslandRz * 0.9f), InFoothills(0, 40, 0), new Color(0.7f, 0.68f, 0.68f, 0.8f), 0.05f, 0.1f,
            new Vector3(0.4f, -1.2f, 0.2f)).Name = "Ashfall";
        FParticles(90, 8f, new Vector3(30, 2, 26), InFoothills(-4, 14, -6), new Color(1f, 0.45f, 0.12f), 0.04f, 0.08f, new Vector3(0.2f, 1.4f, 0.1f), emissive: 2f)
            .Name = "BasinEmbers";
    }
}
