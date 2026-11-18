using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>A wooden watchtower (GDD §6, raids: "walls and towers matter"). The server has it
/// loose arrows at the nearest hostile it can see within range: raiders, wolves, harpies. Its
/// arrows never hurt players (Projectile, shooter null). Built in code; the scene is just this.</summary>
public partial class Watchtower : StaticBody3D
{
    public const float Range = 26f, Interval = 1.3f, PlatformHeight = 3.6f;

    /// <summary>Arrows loosed by every tower on this machine (tests).</summary>
    public static int Shots { get; private set; }

    private float _cooldown = 1f;

    public override void _Ready() => Build();

    public override void _PhysicsProcess(double delta)
    {
        var net = Net.Instance;
        if (!net.InSession || !net.IsServer || Wildlife.Instance == null) return;
        if (GameState.Instance.FindBuilding(this) == null) return; // the build ghost
        _cooldown -= (float)delta;
        if (_cooldown > 0) return;
        _cooldown = 0.3f; // look again soon

        var from = GlobalPosition + Vector3.Up * (PlatformHeight + 1.3f);
        var space = GetWorld3D().DirectSpaceState;
        Enemy target = null;
        float best = Range;
        foreach (var e in Wildlife.Instance.Enemies.Values)
        {
            if (e.Current == Enemy.State.Dead || e.Health.IsDead) continue;
            float d = new Vector2(e.GlobalPosition.X - GlobalPosition.X, e.GlobalPosition.Z - GlobalPosition.Z).Length();
            if (d >= best || d < 1.5f) continue;
            var q = PhysicsRayQueryParameters3D.Create(from, e.GlobalPosition + Vector3.Up * 0.6f,
                GameState.LayerWorld | GameState.LayerProps, new Godot.Collections.Array<Rid> { GetRid() });
            var hit = space.IntersectRay(q);
            if (hit.Count > 0 && hit["collider"].AsGodotObject() != e) continue; // no line of sight
            best = d;
            target = e;
        }
        if (target == null) return;

        // Lead the target and allow for the arrow's drop.
        var aim = target.GlobalPosition + Vector3.Up * 0.6f;
        float t = from.DistanceTo(aim) / Projectile.ArrowSpeed;
        aim += target.Velocity * t + Vector3.Up * (0.5f * Projectile.ArrowGravity * t * t);
        var dir = (aim - from).Normalized();
        var flat = new Vector3(dir.X, 0, dir.Z).Normalized();
        net.SpawnProjectile(null, Projectile.Arrow, from + flat * 1.7f, dir * Projectile.ArrowSpeed);
        if (!net.IsDedicated) Sfx.At("bow", from, 0f);
        Shots++;
        _cooldown = Interval;
    }

    private void Build()
    {
        var wood = StyleMaterials.Toon(new Color(0.45f, 0.3f, 0.2f));
        var dark = StyleMaterials.Toon(new Color(0.28f, 0.18f, 0.13f));
        var roof = StyleMaterials.Toon(new Color(0.62f, 0.24f, 0.18f), 0.3f);
        void Box(Vector3 size, Vector3 pos, Material m, Vector3 rot = default) =>
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = pos, Rotation = rot });

        // Four leaning posts, cross braces, the platform, a railing and a pointed roof.
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                AddChild(new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.14f, Height = 6.2f, RadialSegments = 6 },
                    MaterialOverride = wood, Position = new Vector3(x * 1.05f, 3.1f, z * 1.05f), Rotation = new Vector3(-z * 0.04f, 0, x * 0.04f),
                });
            }
        foreach (float s in new[] { -1f, 1f })
        {
            Box(new Vector3(0.08f, 0.1f, 2.9f), new Vector3(s * 1.08f, 1.8f, 0), dark, new Vector3(0.75f, 0, 0));
            Box(new Vector3(2.9f, 0.1f, 0.08f), new Vector3(0, 1.8f, s * 1.08f), dark, new Vector3(0, 0, 0.75f));
        }
        Box(new Vector3(2.7f, 0.2f, 2.7f), new Vector3(0, PlatformHeight, 0), wood);
        foreach (float s in new[] { -1f, 1f })
        {
            Box(new Vector3(2.7f, 0.08f, 0.08f), new Vector3(0, PlatformHeight + 1f, s * 1.3f), dark);
            Box(new Vector3(0.08f, 0.08f, 2.7f), new Vector3(s * 1.3f, PlatformHeight + 1f, 0), dark);
            Box(new Vector3(2.7f, 0.5f, 0.06f), new Vector3(0, PlatformHeight + 0.35f, s * 1.3f), wood);
            Box(new Vector3(0.06f, 0.5f, 2.7f), new Vector3(s * 1.3f, PlatformHeight + 0.35f, 0), wood);
        }
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 2.1f, Height = 1.4f, RadialSegments = 4 },
            MaterialOverride = roof, Position = new Vector3(0, 6.6f, 0), Rotation = new Vector3(0, Mathf.Pi / 4f, 0),
        });
        // Ladder up the front.
        foreach (float x in new[] { -0.25f, 0.25f })
            Box(new Vector3(0.06f, PlatformHeight, 0.06f), new Vector3(x, PlatformHeight / 2f, 1.25f), dark);
        for (float y = 0.4f; y < PlatformHeight; y += 0.45f)
            Box(new Vector3(0.5f, 0.05f, 0.05f), new Vector3(0, y, 1.25f), dark);
        // A pennant.
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.8f, 0.4f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.75f, 0.35f), CullMode = BaseMaterial3D.CullModeEnum.Disabled },
            Position = new Vector3(0.4f, 7.5f, 0),
        });
        Box(new Vector3(0.04f, 1.2f, 0.04f), new Vector3(0, 7.4f, 0), dark);

        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.4f, 6.2f, 2.4f) }, Position = new Vector3(0, 3.1f, 0) });
    }
}
