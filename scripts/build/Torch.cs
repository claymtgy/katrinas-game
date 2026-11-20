using Godot;

namespace KatrinasGame;

/// <summary>A torch you build (GDD §11, Hollow Mine): a standing one on a post, or one in an
/// iron bracket on a wall. It burns for good and lights <see cref="LightSources.TorchRadius"/>
/// around it, which cave crawlers won't enter. Unlike most buildings it can go up anywhere,
/// so you can light your way down the mine.</summary>
public partial class Torch : StaticBody3D
{
    /// <summary>Hung on a wall: the origin is on the wall and local +Z faces out of it.</summary>
    [Export] public bool Wall { get; set; }

    private OmniLight3D _light;
    private Node3D _flame;
    private float _t;

    private static StandardMaterial3D _wood, _iron, _fire;

    public override void _Ready()
    {
        _wood ??= StyleMaterials.Toon(new Color(0.36f, 0.24f, 0.15f));
        _iron ??= StyleMaterials.Toon(new Color(0.22f, 0.22f, 0.25f));
        _fire ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.6f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.5f, 0.15f),
            EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        CollisionLayer = GameState.LayerProps;
        CollisionMask = 0;

        // Where the flame sits, relative to the origin.
        Vector3 head;
        if (Wall)
        {
            // Bracket plate on the wall, an arm out, and the torch tilted away from the rock.
            Add(new BoxMesh { Size = new Vector3(0.18f, 0.3f, 0.04f) }, _iron, new Vector3(0, 0, 0.02f), Vector3.Zero);
            Add(new BoxMesh { Size = new Vector3(0.05f, 0.05f, 0.28f) }, _iron, new Vector3(0, -0.05f, 0.16f), Vector3.Zero);
            Add(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.035f, Height = 0.55f, RadialSegments = 6 }, _wood,
                new Vector3(0, 0.12f, 0.3f), new Vector3(0.3f, 0, 0));
            head = new Vector3(0, 0.42f, 0.4f);
            AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.3f, 0.7f, 0.45f) }, Position = new Vector3(0, 0.15f, 0.22f) });
        }
        else
        {
            Add(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 1.5f, RadialSegments = 6 }, _wood, new Vector3(0, 0.75f, 0), Vector3.Zero);
            Add(new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.06f, Height = 0.16f, RadialSegments = 6 }, _iron, new Vector3(0, 1.48f, 0), Vector3.Zero);
            // A few stones round the foot.
            for (int k = 0; k < 4; k++)
            {
                float a = k * 1.7f;
                Add(new SphereMesh { Radius = 0.09f, Height = 0.12f, RadialSegments = 5, Rings = 2 }, _iron,
                    new Vector3(Mathf.Cos(a) * 0.14f, 0.04f, Mathf.Sin(a) * 0.14f), Vector3.Zero);
            }
            head = new Vector3(0, 1.62f, 0);
            AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.15f, Height = 1.7f }, Position = new Vector3(0, 0.85f, 0) });
        }

        _flame = new Node3D { Name = "Flame", Position = head };
        AddChild(_flame);
        _flame.AddChild(new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(0.14f, 0.28f, 0.14f) }, MaterialOverride = _fire,
            Position = new Vector3(0, 0.1f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _flame.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.08f, Height = 0.14f, RadialSegments = 6, Rings = 3 }, MaterialOverride = _fire,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _light = new OmniLight3D
        {
            Name = "Light", Position = head + new Vector3(0, 0.15f, 0),
            LightColor = new Color(1f, 0.66f, 0.38f), LightEnergy = 2.4f, OmniRange = LightSources.TorchRadius + 3f,
            OmniAttenuation = 1.2f, ShadowEnabled = false,
        };
        AddChild(_light);
        _t = (float)(GD.Hash(Name) % 100);

        // The build-mode ghost is a copy that doesn't run: it must not count as a light.
        if (CanProcess()) LightSources.Register(this, LightSources.TorchRadius);
        if (!Net.Instance.IsDedicated && CanProcess()) Sfx.Loop(this, "amb_fire", head, -12f, 8f, 1.5f);
    }

    public override void _ExitTree() => LightSources.Unregister(this);

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float flicker = 0.85f + 0.15f * Mathf.Sin(_t * 11f) * Mathf.Sin(_t * 6.1f + 0.7f);
        if (_light != null) _light.LightEnergy = 2.4f * flicker;
        if (_flame != null) _flame.Scale = new Vector3(1, 0.85f + 0.3f * flicker, 1);
    }

    private void Add(Mesh mesh, Material mat, Vector3 pos, Vector3 rot) =>
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot });
}
