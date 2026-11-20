using Godot;

namespace KatrinasGame;

/// <summary>The Spire Cliffs, part 2: marked iron anchor rings for the grapple hook
/// (PlayerController.Grapple). Each is a ring on a bolted bracket with a strip of blue cloth
/// streaming from it, so you can pick them out from the next spire. Rings on a rim haul you up
/// over the edge; rings on a climbing face leave you hanging on the rock. They link the spires
/// so the long hops are fair, and turn the Tall Spire's face into two short pulls instead of one
/// long climb. Two more stand on the Falls Valley mesa.</summary>
public partial class HomeCliffs
{
    /// <summary>A grapple anchor: the ring, the way its rock faces (flat), and whether it's set in a
    /// climbing face (you end up hanging on) rather than a rim (you're hauled over the top).</summary>
    public readonly record struct Anchor(Vector3 Ring, Vector3 Out, bool OntoFace)
    {
        /// <summary>Where the hook leaves you (feet): just out from the rock, hands at the ring.</summary>
        public Vector3 Hang => Ring + Out * 0.65f + Vector3.Down * 1.55f;
    }

    /// <summary>Built on first use: static fields in the other partial files (the spires, the mesa)
    /// may not be set yet when this file's own static fields are.</summary>
    public static Anchor[] Anchors => _anchors ??= MakeAnchors();
    private static Anchor[] _anchors;

    private static Anchor[] MakeAnchors() => new Anchor[]
    {
        // From the lookout's tip to the first spire's near rim: the first, easy pull.
        new(new Vector3(Spires[0].Top.X, -3.5f, Spires[0].Top.Z + Spires[0].Half + 0.15f), Vector3.Back, false),
        // The blossom spire's east rim, from the first spire.
        new(new Vector3(Spires[1].Top.X + Spires[1].Half + 0.15f, Spires[1].Top.Y - 0.5f, Spires[1].Top.Z), Vector3.Right, false),
        // The top of the rust spire's west climbing face, from the blossom spire.
        new(new Vector3(Spires[2].Top.X - Spires[2].Half - 0.95f, Spires[2].Top.Y - 0.6f, Spires[2].Top.Z), Vector3.Left, false),
        // The windward spire's west rim, from the rust spire's north-east corner (mind the gust).
        new(new Vector3(Spires[3].Top.X - Spires[3].Half - 0.15f, Spires[3].Top.Y - 0.5f, Spires[3].Top.Z), Vector3.Left, false),
        // Half way up the tall spire's east face, from the top of the windward spire's updraft…
        new(new Vector3(Spires[4].Top.X + Spires[4].Half + 0.95f, 33f, Spires[4].Top.Z + 2f), Vector3.Right, true),
        // …and its top, from there: up to the nest.
        new(new Vector3(Spires[4].Top.X + Spires[4].Half + 0.95f, Spires[4].Top.Y - 0.6f, Spires[4].Top.Z - 1.5f), Vector3.Right, false),
        // The Falls Valley mesa's north and east rims (its climbing faces are west and south).
        new(new Vector3(MesaCenter.X + 4f, MesaHeight - 0.5f, MesaCenter.Z - MesaHalf - 0.15f), Vector3.Forward, false),
        new(new Vector3(MesaCenter.X + MesaHalf + 0.15f, MesaHeight - 0.5f, MesaCenter.Z + 3f), Vector3.Right, false),
    };

    private static readonly Color AnchorCloth = new(0.35f, 0.62f, 0.9f);

    private void BuildAnchors(Node3D root)
    {
        var iron = StyleMaterials.Toon(new Color(0.36f, 0.37f, 0.4f), 0.55f);
        var cloth = StyleMaterials.Toon(AnchorCloth, 0.4f);
        var glow = new StandardMaterial3D
        {
            AlbedoColor = AnchorCloth, EmissionEnabled = true, Emission = AnchorCloth, EmissionEnergyMultiplier = 1.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        for (int i = 0; i < Anchors.Length; i++)
        {
            var a = Anchors[i];
            var node = new Node3D { Name = $"GrappleAnchor_{i}", Position = a.Ring, Rotation = new Vector3(0, Mathf.Atan2(a.Out.X, a.Out.Z), 0) };
            root.AddChild(node);
            // A plate bolted to the rock, an arm out from it, and the ring hanging off the arm (local +Z is out).
            Visual(new BoxMesh { Size = new Vector3(0.5f, 0.6f, 0.08f) }, iron, new Vector3(0, 0.15f, -0.12f), Vector3.Zero, node);
            for (int b = 0; b < 4; b++)
                Visual(new SphereMesh { Radius = 0.04f, Height = 0.08f }, iron, new Vector3(b % 2 == 0 ? -0.18f : 0.18f, b < 2 ? 0.38f : -0.08f, -0.07f), Vector3.Zero, node);
            Visual(new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.3f) }, iron, new Vector3(0, 0.3f, 0.05f), Vector3.Zero, node);
            Visual(new TorusMesh { InnerRadius = 0.16f, OuterRadius = 0.24f, Rings = 12, RingSegments = 6 }, iron, new Vector3(0, 0.05f, 0.18f), new Vector3(0, 0, Mathf.Pi / 2f), node);
            // The mark: a blue strip of cloth streaming in the wind, and a faint gleam on the ring.
            for (int k = 0; k < 3; k++)
                Visual(new BoxMesh { Size = new Vector3(0.18f, 0.5f, 0.02f) }, cloth,
                    new Vector3(0.12f + k * 0.13f, 0.12f - k * 0.08f, 0.2f), new Vector3(0, 0, -0.9f - k * 0.15f), node);
            Visual(new SphereMesh { Radius = 0.07f, Height = 0.14f }, glow, new Vector3(0, 0.3f, 0.21f), Vector3.Zero, node);
        }
    }

    /// <summary>Keep buttresses and crowns off the rock round an anchor (and the air the hook pulls you through).</summary>
    private static bool NearAnchor(Vector3 p, float pad = 2.5f)
    {
        foreach (var a in Anchors)
        {
            var rel = p - a.Ring;
            var along = new Vector3(a.Out.Z, 0, -a.Out.X);
            if (Mathf.Abs(rel.Dot(along)) < pad && rel.Y < 1.5f && rel.Y > -8f && rel.Dot(a.Out) > -3f) return true;
        }
        return false;
    }
}
