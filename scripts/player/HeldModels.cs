using Godot;

namespace KatrinasGame;

/// <summary>Low-poly models for what's in your hand, built from primitives in the toon style:
/// the first-person fist, axes, pickaxes and the club. All are laid out the same way as a sword:
/// the grip at the origin, the business end up +Y, the edge or point toward +X.</summary>
public static class HeldModels
{
    private static readonly Color Handle = new(0.5f, 0.32f, 0.16f);
    private static readonly Color Lashing = new(0.36f, 0.24f, 0.14f);
    private static readonly Color DefaultSkin = new(0.93f, 0.72f, 0.6f);
    private static readonly Color DefaultSleeve = new(0.2f, 0.45f, 0.35f);

    private static MeshInstance3D Part(Node3D parent, Mesh mesh, Color color, Vector3 pos, Vector3 rotDeg, Vector3? scale = null, float rim = 0.3f)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = StyleMaterials.Toon(color, rim), Position = pos,
            RotationDegrees = rotDeg, Scale = scale ?? Vector3.One,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(mi);
        return mi;
    }

    private static Mesh Capsule(float radius, float length) => new CapsuleMesh { Radius = radius, Height = length, RadialSegments = 8, Rings = 2 };
    private static Mesh Ball => new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 8, Rings = 4 };
    private static Mesh Rod(float radius, float length, float top = -1f) =>
        new CylinderMesh { TopRadius = top < 0 ? radius : top, BottomRadius = radius, Height = length, RadialSegments = 8 };

    /// <summary>Skin and sleeve colours from a look ("" = the model's defaults).</summary>
    public static (Color Skin, Color Sleeve) Colors(Appearance look) =>
        (look != null && look.Skin != "" ? new Color(look.Skin) : DefaultSkin,
         look != null && look.Outfit != "" ? new Color(look.Outfit) : DefaultSleeve);

    /// <summary>A closed right fist around the grip (the origin; the grip runs along Y), posed for
    /// the first-person camera: the back of the hand faces +X, the knuckles +Z, the thumb wraps over
    /// the index finger on the -X side, and the forearm leaves the wrist down and back out of view.
    /// <paramref name="holding"/> leaves a tunnel for a handle; a bare fist closes up.</summary>
    public static Node3D Fist(Color skin, Color sleeve, bool holding)
    {
        var root = new Node3D { Name = "Fist" };
        skin = skin.Darkened(0.06f);              // the toon light washes bare skin out up close
        float r = holding ? 0.026f : 0.012f;       // radius of the tunnel the fingers wrap round
        var knuckle = skin.Darkened(0.06f);
        var shade = skin.Darkened(0.14f);
        // Back of the hand: a flat slab beside the grip, knuckles forward (+Z), wrist back (-Z).
        Part(root, new BoxMesh { Size = new Vector3(0.026f, 0.082f, 0.075f) }, skin, new Vector3(r + 0.014f, -0.004f, -0.006f), new Vector3(0, -6, 0), null, 0.12f);
        // Palm pad and the heel of the thumb on the far side.
        Part(root, new BoxMesh { Size = new Vector3(0.03f, 0.07f, 0.03f) }, shade, new Vector3(0.004f, -0.012f, -r - 0.016f), Vector3.Zero, null, 0.08f);
        // Fingers, index on top, each in three bones curling round the tunnel.
        for (int i = 0; i < 4; i++)
        {
            float y = 0.03f - i * 0.0215f;
            float w = i == 3 ? 0.0085f : 0.0105f;    // the little finger is thinner and shorter
            float k = i == 3 ? 0.85f : 1f;
            var k0 = new Vector3(r + 0.016f, y, 0.028f);                 // the big knuckle
            var k1 = new Vector3(0.006f, y, r + 0.02f) * new Vector3(1, 1, k);   // across the front
            var k2 = new Vector3(-r - 0.014f, y, 0.008f * k);            // round the far side
            var k3 = new Vector3(-r * 0.4f - 0.006f, y, -r - 0.004f);    // tip tucked into the palm
            Seg(root, k0, k1, w * 1.08f, skin);
            Seg(root, k1, k2, w, skin);
            Seg(root, k2, k3, w * 0.9f, shade);
            Part(root, Ball, knuckle, k0, Vector3.Zero, Vector3.One * w * 1.12f, 0.12f);
            Part(root, Ball, skin, k1, Vector3.Zero, Vector3.One * w * 1.05f, 0.12f);
            Part(root, Ball, skin, k2, Vector3.Zero, Vector3.One * w * 0.98f, 0.12f);
        }
        // Thumb: from the heel of the hand up the far side and across the index and middle fingers.
        var t0 = new Vector3(0.0f, -0.03f, -r - 0.02f);
        var t1 = new Vector3(-r - 0.024f, 0.0f, -0.012f);
        var t2 = new Vector3(-r - 0.026f, 0.026f, r * 0.6f + 0.012f);
        Seg(root, t0, t1, 0.0135f, skin);
        Seg(root, t1, t2, 0.0115f, skin);
        Part(root, Ball, skin, t1, Vector3.Zero, Vector3.One * 0.0135f, 0.12f);
        Part(root, Ball, knuckle, t2, Vector3.Zero, Vector3.One * 0.0112f, 0.12f);
        // Wrist and forearm, going down and back toward the camera's lower edge; a cuff of sleeve.
        var wrist = new Vector3(0.012f, -0.03f, -0.05f);
        var arm = new Vector3(0.12f, -0.78f, -0.6f).Normalized();
        Seg(root, wrist, wrist + arm * 0.07f, 0.026f, skin, 8);
        Seg(root, wrist + arm * 0.065f, wrist + arm * 0.32f, 0.04f, sleeve, 8, 0.2f);
        Seg(root, wrist + arm * 0.06f, wrist + arm * 0.09f, 0.044f, sleeve.Darkened(0.25f), 8, 0.2f);
        return root;
    }

    /// <summary>A low-poly limb segment from a to b.</summary>
    private static void Seg(Node3D parent, Vector3 a, Vector3 b, float radius, Color color, int sides = 6, float rim = 0.12f)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        var y = d / len;
        var x = Mathf.Abs(y.Dot(Vector3.Forward)) > 0.95f ? y.Cross(Vector3.Right).Normalized() : y.Cross(Vector3.Forward).Normalized();
        var z = x.Cross(y);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = len, RadialSegments = sides, Rings = 1 },
            MaterialOverride = StyleMaterials.Toon(color, rim),
            Transform = new Transform3D(new Basis(x, y, z), (a + b) / 2f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>A felling axe: a long haft, a wedge head with a bright edge, lashed on.</summary>
    public static Node3D Axe(Color head)
    {
        var root = new Node3D { Name = "Axe" };
        Part(root, Rod(0.022f, 0.8f, 0.019f), Handle, new Vector3(0, 0.28f, 0), Vector3.Zero);
        Part(root, new BoxMesh { Size = new Vector3(0.07f, 0.09f, 0.06f) }, head, new Vector3(0.0f, 0.62f, 0), Vector3.Zero);
        // The bit: a prism pointing out along +X.
        Part(root, new PrismMesh { Size = new Vector3(0.2f, 0.17f, 0.035f), LeftToRight = 0.5f }, head,
            new Vector3(0.12f, 0.62f, 0), new Vector3(0, 0, -90));
        Part(root, new BoxMesh { Size = new Vector3(0.02f, 0.19f, 0.038f) }, head.Lightened(0.45f), new Vector3(0.205f, 0.62f, 0), Vector3.Zero, null, 0.6f);
        Part(root, Rod(0.027f, 0.05f), Lashing, new Vector3(0, 0.56f, 0), Vector3.Zero);
        Part(root, Rod(0.026f, 0.1f), Lashing, new Vector3(0, -0.02f, 0), Vector3.Zero); // grip wrap
        return root;
    }

    /// <summary>A pickaxe: a haft and a curved two-pointed head.</summary>
    public static Node3D Pickaxe(Color head)
    {
        var root = new Node3D { Name = "Pickaxe" };
        Part(root, Rod(0.022f, 0.85f, 0.019f), Handle, new Vector3(0, 0.3f, 0), Vector3.Zero);
        Part(root, new BoxMesh { Size = new Vector3(0.07f, 0.07f, 0.06f) }, head, new Vector3(0, 0.66f, 0), Vector3.Zero);
        foreach (float side in new[] { 1f, -1f })
        {
            // Each arm in two pieces, bending down toward its point.
            Part(root, new BoxMesh { Size = new Vector3(0.14f, 0.05f, 0.045f) }, head, new Vector3(side * 0.1f, 0.665f, 0), new Vector3(0, 0, -side * 8));
            Part(root, new PrismMesh { Size = new Vector3(0.05f, 0.14f, 0.04f) }, head.Lightened(side > 0 ? 0.2f : 0f),
                new Vector3(side * 0.225f, 0.635f, 0), new Vector3(0, 0, -side * 105));
        }
        Part(root, Rod(0.027f, 0.05f), Lashing, new Vector3(0, 0.6f, 0), Vector3.Zero);
        Part(root, Rod(0.026f, 0.1f), Lashing, new Vector3(0, -0.02f, 0), Vector3.Zero);
        return root;
    }

    /// <summary>A knobbly club: a haft swelling into a stone-studded head.</summary>
    public static Node3D Club(Color head)
    {
        var root = new Node3D { Name = "Club" };
        Part(root, Rod(0.024f, 0.5f, 0.04f), Handle, new Vector3(0, 0.2f, 0), Vector3.Zero);
        Part(root, Ball, Handle.Darkened(0.1f), new Vector3(0, 0.55f, 0), Vector3.Zero, new Vector3(0.085f, 0.15f, 0.085f));
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.Tau / 6f;
            Part(root, new BoxMesh { Size = Vector3.One * 0.045f }, head, new Vector3(Mathf.Cos(a) * 0.075f, 0.5f + (i % 2) * 0.09f, Mathf.Sin(a) * 0.075f),
                new Vector3(30 * i, 45, 20));
        }
        Part(root, Rod(0.026f, 0.1f), Lashing, new Vector3(0, -0.02f, 0), Vector3.Zero);
        return root;
    }
}
