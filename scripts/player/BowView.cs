using Godot;

namespace KatrinasGame;

/// <summary>The archer's bow, built from code: a recurved wooden stave with a wrapped grip, a
/// string that pulls back, and a nocked arrow. The bow stands along +Y with its grip at the
/// origin; the string is on the +Z side (towards the archer), so arrows fly along -Z.
/// <see cref="Loose"/> plays a quick draw, the snap of the release, and the re-nock.</summary>
public partial class BowView : Node3D
{
    public const float Height = 1.1f;
    private const float Half = Height * 0.5f;
    private const float Brace = 0.13f;     // grip to string at rest
    private const float MaxDraw = 0.32f;   // how far the string comes back

    private MeshInstance3D _upperString, _lowerString;
    private Node3D _arrow;
    private Vector3 _topTip, _bottomTip;
    private float _draw;
    private Tween _tween;

    /// <summary>0 = at rest, 1 = fully drawn.</summary>
    public float Draw
    {
        get => _draw;
        set { _draw = Mathf.Clamp(value, 0f, 1f); UpdateString(); }
    }

    /// <summary>Keep an arrow on the string between shots (first person) or only while shooting.</summary>
    public bool RestNocked { get; set; } = true;

    /// <summary>Is an arrow on the string?</summary>
    public bool Nocked
    {
        get => _arrow?.Visible ?? false;
        set { if (_arrow != null) _arrow.Visible = value; }
    }

    public override void _Ready()
    {
        var wood = StyleMaterials.Toon(new Color(0.52f, 0.32f, 0.17f), 0.3f);
        var dark = StyleMaterials.Toon(new Color(0.24f, 0.14f, 0.08f), 0.2f);
        var gold = StyleMaterials.Toon(new Color(0.85f, 0.66f, 0.3f), 0.4f);
        var cord = StyleMaterials.Toon(new Color(0.93f, 0.9f, 0.8f), 0.1f);

        // The stave: a bent arc of tapering pieces, with the tips flicked forward (a recurve).
        foreach (int side in new[] { 1, -1 })
        {
            Vector3 prev = Vector3.Zero;
            const int pieces = 9;
            for (int i = 1; i <= pieces; i++)
            {
                float t = i / (float)pieces;
                float y = t * Half;
                float z = Brace * t * t;                               // the limb bends back towards the string
                if (t > 0.8f) z -= (t - 0.8f) * (t - 0.8f) * 2.2f;    // ...and the tip curls away again
                var p = new Vector3(0, y * side, z);
                float r0 = Mathf.Lerp(0.026f, 0.011f, (i - 1) / (float)pieces);
                float r1 = Mathf.Lerp(0.026f, 0.011f, t);
                AddChild(Segment(prev, p, r0, r1, wood));
                prev = p;
            }
            if (side > 0) _topTip = prev; else _bottomTip = prev;
            // Gold nocks on the tips.
            AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 0.016f, Height = 0.032f, RadialSegments = 6, Rings = 3 },
                MaterialOverride = gold, Position = prev,
            });
        }
        // Leather-wrapped grip with gold bands, and an arrow rest.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.032f, BottomRadius = 0.032f, Height = 0.15f, RadialSegments = 8 },
            MaterialOverride = dark,
        });
        foreach (float y in new[] { 0.078f, -0.078f })
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.014f, RadialSegments = 8 },
                MaterialOverride = gold, Position = new Vector3(0, y, 0),
            });

        _upperString = new MeshInstance3D { MaterialOverride = cord };
        _lowerString = new MeshInstance3D { MaterialOverride = cord };
        AddChild(_upperString);
        AddChild(_lowerString);

        _arrow = BuildArrow();
        AddChild(_arrow);
        _arrow.Visible = RestNocked;
        _draw = RestNocked ? 0.12f : 0f;
        UpdateString();
    }

    /// <summary>Draw, release (the string snaps forward and the arrow is gone), then nock
    /// another once <paramref name="renock"/> seconds have passed.</summary>
    public void Loose(float renock = 0.45f)
    {
        _tween?.Kill();
        Nocked = true;
        var set = Callable.From<float>(v => Draw = v);
        _tween = CreateTween();
        _tween.TweenMethod(set, _draw, 1f, 0.07).SetEase(Tween.EaseType.Out);
        _tween.TweenCallback(Callable.From(() => Nocked = false));
        _tween.TweenMethod(set, 1f, 0f, 0.09).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenInterval(Mathf.Max(0.05f, renock - 0.16f));
        _tween.TweenCallback(Callable.From(() => Nocked = RestNocked));
        _tween.TweenMethod(set, 0f, RestNocked ? 0.12f : 0f, 0.12).SetEase(Tween.EaseType.Out);
    }

    private Vector3 NockPoint => new(0, 0, Brace + _draw * MaxDraw);

    private void UpdateString()
    {
        if (_upperString == null) return;
        var nock = NockPoint;
        Place(_upperString, _topTip, nock, 0.0045f);
        Place(_lowerString, nock, _bottomTip, 0.0045f);
        // The arrow's nock sits on the string; it rests on the left of the grip.
        if (_arrow != null) _arrow.Position = new Vector3(-0.03f, 0.005f, nock.Z);
    }

    private static Node3D BuildArrow()
    {
        const float length = 0.78f;
        var arrow = new Node3D { Name = "Arrow" };
        arrow.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.0075f, BottomRadius = 0.0075f, Height = length, RadialSegments = 5 },
            MaterialOverride = StyleMaterials.Toon(new Color(0.62f, 0.45f, 0.27f)),
            RotationDegrees = new Vector3(90, 0, 0), Position = new Vector3(0, 0, -length * 0.5f),
        });
        arrow.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(0.032f, 0.06f, 0.012f) },
            MaterialOverride = StyleMaterials.Toon(new Color(0.78f, 0.78f, 0.84f), 0.5f),
            RotationDegrees = new Vector3(-90, 0, 0), Position = new Vector3(0, 0, -length - 0.025f),
        });
        foreach (float a in new[] { 0f, 120f, 240f })
            arrow.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.003f, 0.032f, 0.1f) },
                MaterialOverride = StyleMaterials.Toon(a == 0f ? new Color(0.8f, 0.25f, 0.2f) : new Color(0.95f, 0.92f, 0.86f)),
                Position = new Vector3(0, 0, -0.08f), RotationDegrees = new Vector3(0, 0, a),
            });
        return arrow;
    }

    private static MeshInstance3D Segment(Vector3 a, Vector3 b, float ra, float rb, Material mat)
    {
        var m = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = rb, BottomRadius = ra, Height = 1f, RadialSegments = 6, Rings = 1 },
            MaterialOverride = mat,
        };
        Place(m, a, b, 0f, keepMesh: true);
        return m;
    }

    /// <summary>Stretch a unit-height cylinder from a to b (radius 0 keeps the mesh's own radii).</summary>
    private static void Place(MeshInstance3D m, Vector3 a, Vector3 b, float radius, bool keepMesh = false)
    {
        if (!keepMesh)
            m.Mesh ??= new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 1f, RadialSegments = 4, Rings = 1 };
        var dir = b - a;
        float len = Mathf.Max(dir.Length(), 0.001f);
        var y = dir / len;
        var x = Mathf.Abs(y.Dot(Vector3.Right)) > 0.9f ? Vector3.Forward.Cross(y).Normalized() : y.Cross(Vector3.Right).Normalized();
        var z = x.Cross(y);
        m.Transform = new Transform3D(new Basis(x, y * len, z), (a + b) * 0.5f);
    }
}
