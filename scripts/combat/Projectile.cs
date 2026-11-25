using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>An arrow (Archer) or firebolt (Wizard). The server flies the real one, sweeping a
/// ray along each step, and deals the damage; every other screen flies a copy with the same
/// start and velocity (Net.C_Projectile) and drops it when it hits something locally or when
/// the server says it ended (Net.C_ProjectileEnd). Arrows fall under gravity; firebolts fly
/// straight and burst, scorching what's around the impact.</summary>
public partial class Projectile : Node3D
{
    /// <summary>Volley: the Archer's level-5 fan of arrows (weaker; otherwise an arrow).</summary>
    public const string Arrow = "arrow", Firebolt = "firebolt", Volley = "volley";
    public const float VolleyDamage = 10f;

    private static bool IsArrowKind(string kind) => kind is Arrow or Volley;
    private bool IsArrow => IsArrowKind(Kind);

    public const float ArrowSpeed = 46f, ArrowGravity = 6f, ArrowDamage = 16f, ArrowLongShot = 15f, ArrowLongBonus = 1.5f;
    public const float BoltSpeed = 26f, BoltDamage = 22f, BoltSplashDamage = 10f, BoltSplashRadius = 2.5f;
    public const float Lifetime = 3f;

    public int NetId { get; private set; }
    public string Kind { get; private set; }
    public PlayerController Shooter { get; private set; }
    public Vector3 Velocity => _velocity;

    private Vector3 _velocity, _origin;
    private float _gravity, _life;
    private bool _server, _ended;

    /// <summary>Server-side live projectiles by id (tests).</summary>
    public static readonly Dictionary<int, Projectile> Live = new();
    /// <summary>How many this machine has spawned (tests).</summary>
    public static int Spawned { get; private set; }

    public static Projectile Spawn(Node parent, int netId, string kind, Vector3 origin, Vector3 velocity, PlayerController shooter, bool server)
    {
        var p = new Projectile
        {
            Name = $"Projectile_{netId}", NetId = netId, Kind = kind, Shooter = shooter,
            _velocity = velocity, _origin = origin, _server = server,
            _gravity = IsArrowKind(kind) ? ArrowGravity : 0f,
        };
        parent.AddChild(p);
        p.GlobalPosition = origin;
        p.BuildVisual();
        p.Orient();
        if (server) Live[netId] = p;
        Spawned++;
        return p;
    }

    public override void _ExitTree() => Live.Remove(NetId);

    public override void _PhysicsProcess(double delta)
    {
        if (_ended) return;
        float dt = (float)delta;
        _life += dt;
        var from = GlobalPosition;
        _velocity.Y -= _gravity * dt;
        var to = from + _velocity * dt;

        bool pvp = Net.Instance.Pvp;
        var query = PhysicsRayQueryParameters3D.Create(from, to,
            GameState.LayerWorld | GameState.LayerProps | (pvp ? GameState.LayerPlayer : 0));
        if (IsInstanceValid(Shooter)) query.Exclude = new Godot.Collections.Array<Rid> { Shooter.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            var point = hit["position"].AsVector3();
            GlobalPosition = point;
            if (_server) ServerImpact(hit["collider"].AsGodotObject(), point);
            End(point);
            return;
        }
        GlobalPosition = to;
        Orient();
        if (_life > Lifetime)
        {
            if (_server) Net.Instance.ServerProjectileEnded(NetId, GlobalPosition);
            End(GlobalPosition, silent: true);
        }
    }

    private void Orient()
    {
        if (_velocity.LengthSquared() > 0.01f)
            LookAt(GlobalPosition + _velocity, Mathf.Abs(_velocity.Normalized().Y) > 0.98f ? Vector3.Forward : Vector3.Up);
    }

    // ------------------------------------------------------------------ server

    private void ServerImpact(GodotObject collider, Vector3 point)
    {
        var shooter = IsInstanceValid(Shooter) ? Shooter : null;
        if (IsArrow)
        {
            float longShot = Talents.Has(shooter, Talents.LongDraw) ? Talents.LongDrawRange : ArrowLongShot;
            // The bow sets the arrow's weight; a volley's arrows are lighter in the same proportion.
            float bow = Gear.BowOf(shooter)?.Damage ?? ArrowDamage;
            float dmg = (Kind == Volley ? VolleyDamage * bow / ArrowDamage : bow) * (point.DistanceTo(_origin) > longShot ? ArrowLongBonus : 1f);
            if (Damage(collider, dmg, shooter, Talents.Has(shooter, Talents.Piercing)) && shooter != null) Net.Instance.HitMarkerTo(shooter.PeerId);
        }
        else
        {
            float power = Gear.AbilityPower(shooter);
            bool hitAny = Damage(collider, BoltDamage * power + (Talents.Has(shooter, Talents.Pyromancer) ? Talents.PyromancerDamage : 0f), shooter);
            // Burst: scorch everything else close by.
            var q = new PhysicsShapeQueryParameters3D
            {
                Shape = new SphereShape3D { Radius = BoltSplashRadius + (Talents.Has(shooter, Talents.Kindling) ? Talents.KindlingRadius : 0f) },
                Transform = new Transform3D(Basis.Identity, point),
                CollisionMask = GameState.LayerProps | (Net.Instance.Pvp ? GameState.LayerPlayer : 0),
            };
            var seen = new HashSet<ulong> { collider?.GetInstanceId() ?? 0 };
            foreach (var h in GetWorld3D().DirectSpaceState.IntersectShape(q, 16))
            {
                var c = h["collider"].AsGodotObject();
                if (c == null || c == shooter || !seen.Add(c.GetInstanceId())) continue;
                hitAny |= Damage(c, BoltSplashDamage * power, shooter);
            }
            if (hitAny && shooter != null) Net.Instance.HitMarkerTo(shooter.PeerId);
        }
        Net.Instance.ServerProjectileEnded(NetId, point);
    }

    /// <summary>Server: hurt a collider if it's a creature, a dummy, or (with PVP) a player.
    /// Arrows and fire don't chop trees or mine rock.</summary>
    private static bool Damage(GodotObject collider, float amount, PlayerController shooter, bool pierce = false)
    {
        if (collider is Harvestable) return false;
        var health = Health.FindOn(collider);
        if (health == null || health.IsDead) return false;
        if (collider is PlayerController target)
        {
            if (!Net.Instance.Pvp || shooter == null || target == shooter) return false;
            Net.Instance.ServerDamagePlayer(target, amount, shooter);
        }
        else health.TakeDamage(amount, shooter, pierce);
        return true;
    }

    // ------------------------------------------------------------------ every machine

    /// <summary>Stop: impact effect where it landed, then go.</summary>
    public void End(Vector3 at, bool silent = false)
    {
        if (_ended) return;
        _ended = true;
        GlobalPosition = at;
        if (!silent && !Net.Instance.IsDedicated)
        {
            if (Kind == Firebolt)
            {
                Sfx.At("explode", at, 2f, 0.1f, 60f);
                Fx.Burst(GetParent(), at, new Color(1f, 0.55f, 0.15f), BoltSplashRadius, 0.35f);
            }
            else Sfx.At("arrow_hit", at, 0f, 0.12f);
        }
        if (IsArrow && !silent)
        {
            // Leave the arrow stuck where it hit for a moment.
            var t = CreateTween();
            t.TweenInterval(2.5);
            t.TweenCallback(Callable.From(QueueFree));
        }
        else QueueFree();
    }

    private void BuildVisual()
    {
        if (Net.Instance.IsDedicated) return;
        if (IsArrow)
        {
            var wood = StyleMaterials.Toon(new Color(0.55f, 0.38f, 0.22f));
            var shaft = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.75f, RadialSegments = 5 },
                MaterialOverride = wood, RotationDegrees = new Vector3(90, 0, 0), Position = new Vector3(0, 0, 0.3f),
            };
            AddChild(shaft);
            AddChild(new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(0.05f, 0.09f, 0.02f) },
                MaterialOverride = StyleMaterials.Toon(new Color(0.75f, 0.75f, 0.8f)), RotationDegrees = new Vector3(-90, 0, 0),
                Position = new Vector3(0, 0, -0.08f),
            });
            foreach (float a in new[] { 0f, 120f, 240f })
                AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.004f, 0.05f, 0.14f) },
                    MaterialOverride = StyleMaterials.Toon(new Color(0.95f, 0.9f, 0.85f)),
                    Position = new Vector3(0, 0, 0.6f), RotationDegrees = new Vector3(0, 0, a),
                });
        }
        else
        {
            var fire = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.7f, 0.3f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.1f),
                EmissionEnergyMultiplier = 6f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f, RadialSegments = 8, Rings = 4 }, MaterialOverride = fire });
            AddChild(new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 3f, OmniRange = 5f });
            AddChild(Fx.Trail(new Color(1f, 0.5f, 0.15f, 0.8f)));
        }
    }
}
