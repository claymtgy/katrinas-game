using Godot;

namespace KatrinasGame;

/// <summary>Cliff harpy (Falls Valley, GDD §9). A small flying predator: it circles high over its
/// roost, and when it spots you it climbs above you and swoops down talons-first, then beats back
/// up out of sword reach. Arrows and firebolts reach it anywhere; a sword only as it swoops.
/// Parry the swoop and it tumbles to the ground, stunned: finish it there. A small animal, so more
/// players means more harpies, not tougher ones. Drops feathers (for the glider).</summary>
public partial class Harpy : Enemy
{
    public const float MaxHealth = 32f;
    public const float ClawDamage = 9f;
    public const float CruiseHeight = 11f;
    public const float SwoopSpeed = 10f;
    public const int Feathers = 2;

    public override string Kind => "harpy";
    protected override float WalkSpeed => 3.5f;
    protected override float RunSpeed => 8f;
    protected override float AggroRange => 26f;
    protected override float LeashRange => 40f;
    protected override float HomeLeash => 46f;
    protected override float AttackRange => 2.1f;
    protected override float AttackReach => 3.0f;
    protected override float AttackWindup => 0.32f;
    protected override float AttackCooldown => 2.4f;
    protected override float HitStagger => 0.25f;
    protected override float HitKnockback => 3f;
    protected override float WanderRadius => 12f;
    protected override Rect2 Bounds => HomeCliffs.ValleyBounds;

    /// <summary>Stuns longer than this knock it out of the air.</summary>
    private const float GroundingStun = 0.6f;

    private Node3D _visual, _body, _head, _tail;
    private Node3D[] _wings, _tips, _legs;
    private ShaderMaterial _feathers;
    private float _flap, _time, _orbit;
    private Tween _tween;

    private static readonly Color Plumage = new(0.34f, 0.27f, 0.44f);
    private static readonly Color PlumageDark = new(0.17f, 0.13f, 0.24f);
    private static readonly Color Belly = new(0.74f, 0.64f, 0.66f);
    private static readonly Color Beak = new(0.92f, 0.72f, 0.3f);
    private static readonly Color EyeColor = new(1f, 0.35f, 0.25f);

    /// <summary>On the ground (stunned or dead) rather than in the air.</summary>
    public bool Grounded => IsOnFloor();

    public static Harpy Create(int netId, int roost, Vector3 position, float yaw, float maxHealth = MaxHealth, float damage = ClawDamage)
    {
        var h = new Harpy { Name = $"Harpy_{netId}", Damage = damage };
        h.Init(netId, roost, position, yaw, maxHealth, new Vector3(1.1f, 0.7f, 1.1f));
        h._orbit = netId * 1.7f;
        h.BuildVisual();
        return h;
    }

    public const float ShardChance = 1f / 3f;

    public override void ServerDropLoot(Node killer)
    {
        var p = killer as PlayerController;
        string msg = $"+{Feathers} Harpy Feathers";
        if (Supplies.Give(p, "harpy_feather", Feathers) <= 0) msg = "";
        // One harpy in three has swallowed a rune shard.
        if (GD.Randf() < ShardChance && Supplies.Give(p, Gear.ShardId, 1) > 0) msg += (msg == "" ? "" : ", ") + "+1 Rune Shard";
        if (p != null && msg != "") Net.Instance.ToastTo(p.PeerId, msg);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        // Dead: every machine lets the body drop out of the sky by itself.
        if (Current == State.Dead && !IsOnFloor())
        {
            var v = Velocity;
            v.X *= 0.95f;
            v.Z *= 0.95f;
            v.Y -= Gravity * (float)delta;
            Velocity = v;
            MoveAndSlide();
        }
    }

    // ================================================================== server: flight

    protected override void Think(float dt)
    {
        var wildlife = Wildlife.Instance;
        StateTime += dt;
        AttackCooldownLeft -= dt;
        var pos = GlobalPosition;
        var anchor = wildlife.GroupAnchor(Group, pos);
        var goal = pos;
        float speed = 0f;
        float? faceYaw = null;
        bool falling = false;

        switch (Current)
        {
            case State.Prowl:
            {
                var found = wildlife.FindTarget(this, AggroRange);
                if (found != null)
                {
                    Target = found;
                    wildlife.AlertGroup(this, found);
                    Enter(State.Chase);
                    break;
                }
                // Lazy circles over the roost.
                _orbit += dt * 0.3f;
                goal = anchor + new Vector3(Mathf.Cos(_orbit), 0, Mathf.Sin(_orbit)) * WanderRadius
                       + Vector3.Up * (CruiseHeight + Mathf.Sin(_time * 0.6f + NetId) * 1.5f);
                speed = WalkSpeed * 1.4f;
                break;
            }
            case State.Chase:
            {
                if (!CanHunt(Target)) { Target = null; Enter(State.Prowl); break; }
                var chest = Target.GlobalPosition + Vector3.Up * 1.1f;
                if (AttackCooldownLeft > 0)
                {
                    // Wheel overhead, out of reach, until ready to dive again.
                    var away = Flat(pos - chest);
                    away = away.LengthSquared() < 0.01f ? Vector3.Forward : away.Normalized();
                    goal = chest + away * 6f + Vector3.Up * 6.5f;
                    speed = RunSpeed * 0.7f;
                }
                else
                {
                    goal = chest;
                    speed = SwoopSpeed;
                    if ((chest - pos).Length() < AttackRange)
                    {
                        Enter(State.Windup);
                        wildlife.BroadcastAnim(this, Wildlife.AnimAttack);
                    }
                }
                faceYaw = YawOf(Flat(chest - pos));
                break;
            }
            case State.Windup:
            {
                if (Target != null && IsInstanceValid(Target)) faceYaw = YawOf(Flat(Target.GlobalPosition - pos));
                if (StateTime >= AttackWindup)
                {
                    AttackCooldownLeft = AttackCooldown;
                    Enter(State.Recover);
                    if (CanHunt(Target) && (Target.GlobalPosition + Vector3.Up * 1.1f - pos).Length() < AttackReach)
                        wildlife.Strike(this, Target); // may stun us (parry): do it last
                }
                break;
            }
            case State.Recover:
            {
                var from = Target != null && IsInstanceValid(Target) ? Target.GlobalPosition : pos;
                var away = Flat(pos - from);
                away = away.LengthSquared() < 0.01f ? -GlobalBasis.Z : away.Normalized();
                goal = pos + away * 4f + Vector3.Up * 4f;
                speed = RunSpeed;
                if (StateTime > 1.1f) Enter(Target != null ? State.Chase : State.Prowl);
                break;
            }
            case State.Stagger:
                falling = StaggerFor > GroundingStun;
                if (falling && !IsOnFloor()) StateTime = 0f; // the stun counts from when it hits the ground
                if (StateTime > Mathf.Max(StaggerFor, 0.05f)) { StaggerFor = 0; Enter(Target != null ? State.Chase : State.Prowl); }
                break;
            case State.Flee:
                Enter(State.Prowl);
                break;
        }

        var v = Velocity;
        if (falling)
        {
            // Knocked out of the air: drop and lie there until the stun wears off.
            v.X *= 0.9f;
            v.Z *= 0.9f;
            v.Y = IsOnFloor() ? -0.5f : v.Y - Gravity * dt;
        }
        else
        {
            var to = goal - pos;
            var want = to.Length() > 0.3f ? to.Normalized() * speed : Vector3.Zero;
            want += wildlife.Separation(this) * 2f + SafeZonePush(pos) * 3f + Knockback;
            v = v.Lerp(want, Mathf.Min(1f, 3f * dt)); // wings, not legs: a little inertia
        }
        Knockback = Knockback.Lerp(Vector3.Zero, Mathf.Min(1f, 6f * dt));
        Velocity = v;
        MoveAndSlide();
        GlobalPosition = Wildlife.ClampTo(GlobalPosition, Bounds);

        if (faceYaw == null && Flat(Velocity).Length() > 0.4f) faceYaw = YawOf(Flat(Velocity));
        if (faceYaw != null) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, faceYaw.Value, Mathf.Min(1f, 6f * dt)), 0);
    }

    // ================================================================== visual

    protected override void FlashHit()
    {
        Sfx.At(Health.IsDead ? "harpy_screech" : "hit_flesh", GlobalPosition + Vector3.Up * 0.4f, 0f, 0.15f);
        if (_feathers == null) return;
        var t = CreateTween();
        t.TweenProperty(_feathers, "shader_parameter/tint", new Color(0.9f, 0.35f, 0.35f), 0.05);
        t.TweenProperty(_feathers, "shader_parameter/tint", Plumage, 0.25);
    }

    protected override void PlayDeath()
    {
        if (_visual == null) return;
        _tween?.Kill();
        _tween = CreateTween().SetParallel();
        _tween.TweenProperty(_visual, "rotation:z", Mathf.Pi * 0.55f, 0.6).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        foreach (var w in _wings) _tween.TweenProperty(w, "rotation:z", 0f, 0.4);
    }

    /// <summary>Everyone: the screech and the talons-first lunge of the swoop.</summary>
    public override void PlayAttack()
    {
        Sfx.At("harpy_screech", GlobalPosition + Vector3.Up * 0.4f, 2f, 0.12f, 70f);
        if (_visual == null || Current == State.Dead) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:x", 0.5f, AttackWindup * 0.7).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "rotation:x", -0.2f, 0.12).SetTrans(Tween.TransitionType.Expo);
        _tween.TweenProperty(_visual, "rotation:x", 0f, 0.35);
    }

    public override void PlayStun()
    {
        Sfx.At("harpy_screech", GlobalPosition + Vector3.Up * 0.4f, -2f, 0.2f);
        if (_visual == null || Current == State.Dead) return;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:z", 1.2f, 0.25);
        _tween.TweenInterval(0.9);
        _tween.TweenProperty(_visual, "rotation:z", 0f, 0.4);
    }

    protected override void Animate(float dt)
    {
        _time += dt;
        if (_wings == null || Current == State.Dead) return;
        bool down = Current == State.Stagger && IsOnFloor();
        float climb = Velocity.Y;
        // Hard beats when climbing or swooping back up, a slow soar when cruising.
        float rate = down ? 0f : Current is State.Recover or State.Windup || climb > 1f ? 11f : 5.5f;
        float amp = down ? 0.05f : Current == State.Prowl && climb < 0.5f ? 0.35f : 0.8f;
        _flap += dt * rate;
        float beat = Mathf.Sin(_flap);
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            float fold = down ? 1.1f : 0f;
            _wings[i].Rotation = new Vector3(0, 0, side * (beat * amp + 0.1f - fold));
            _tips[i].Rotation = new Vector3(0, 0, side * (Mathf.Sin(_flap - 0.8f) * amp * 0.6f - fold * 0.8f));
        }
        _body.Position = new Vector3(0, -beat * amp * 0.06f, 0);
        float tilt = Mathf.Clamp(-Flat(Velocity).Length() * 0.03f, -0.35f, 0f);
        _body.Rotation = new Vector3(tilt, 0, 0);
        _head.Rotation = new Vector3(Mathf.Sin(_time * 1.3f + NetId) * 0.15f, Mathf.Sin(_time * 0.7f + NetId) * 0.4f, 0);
        _tail.Rotation = new Vector3(-tilt * 0.8f, 0, Mathf.Sin(_time * 2f) * 0.1f);
        // Talons forward for the strike, tucked while cruising.
        float talons = Current is State.Windup or State.Chase ? 0.9f : 0.2f;
        foreach (var l in _legs) l.Rotation = new Vector3(Mathf.Lerp(l.Rotation.X, -talons, Mathf.Min(1f, 6f * dt)), 0, 0);
    }

    private void BuildVisual()
    {
        // Low-poly faceted raptor-woman of the cliffs: dusky violet plumage, pale breast, a wide
        // two-part wing on each side. Forward is -Z.
        _feathers = StyleMaterials.CharacterFlat(Plumage);
        var dark = StyleMaterials.CharacterFlat(PlumageDark);
        var belly = StyleMaterials.CharacterFlat(Belly);
        var beak = StyleMaterials.CharacterFlat(Beak);
        var eyes = new StandardMaterial3D
        {
            AlbedoColor = EyeColor, EmissionEnabled = true, Emission = EyeColor, EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        _visual = new Node3D { Name = "Visual", Position = new Vector3(0, 0.4f, 0) };
        AddChild(_visual);
        _body = new Node3D { Name = "Body" };
        _visual.AddChild(_body);

        Seg(_body, new Vector3(0, 0.02f, -0.36f), new Vector3(0, 0.06f, 0.34f), 0.22f, 0.12f, _feathers, 6);   // torso
        Seg(_body, new Vector3(0, -0.06f, -0.3f), new Vector3(0, -0.02f, 0.12f), 0.18f, 0.1f, belly, 6);      // breast
        Seg(_body, new Vector3(0, 0.14f, -0.3f), new Vector3(0, 0.12f, 0.1f), 0.12f, 0.08f, dark, 5);        // mantle

        _head = new Node3D { Name = "Head", Position = new Vector3(0, 0.12f, -0.48f) };
        _body.AddChild(_head);
        _head.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.14f, Height = 0.26f, RadialSegments = 6, Rings = 3 }, MaterialOverride = _feathers });
        Seg(_head, new Vector3(0, 0f, -0.1f), new Vector3(0, -0.07f, -0.3f), 0.06f, 0.01f, beak, 4);
        foreach (float side in new[] { -1f, 1f })
        {
            _head.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.025f, 0.02f) }, MaterialOverride = eyes,
                Position = new Vector3(side * 0.08f, 0.03f, -0.11f), Rotation = new Vector3(0, 0, side * -0.3f),
            });
            // Crest plumes swept back.
            Seg(_head, new Vector3(side * 0.04f, 0.1f, 0f), new Vector3(side * 0.08f, 0.2f, 0.28f), 0.035f, 0.005f, dark, 4);
        }

        _tail = new Node3D { Name = "Tail", Position = new Vector3(0, 0.06f, 0.34f) };
        _body.AddChild(_tail);
        foreach (float a in new[] { -0.35f, 0f, 0.35f })
            _tail.AddChild(new MeshInstance3D
            {
                Mesh = new PrismMesh { Size = new Vector3(0.16f, 0.5f, 0.03f) }, MaterialOverride = a == 0 ? dark : _feathers,
                Position = new Vector3(Mathf.Sin(a) * 0.12f, 0, 0.22f), Rotation = new Vector3(-Mathf.Pi / 2f, 0, a),
            });

        _wings = new Node3D[2];
        _tips = new Node3D[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            _wings[i] = new Node3D { Name = $"Wing{i}", Position = new Vector3(side * 0.16f, 0.12f, -0.16f) };
            _body.AddChild(_wings[i]);
            // Inner wing: a broad flat panel out to the wrist.
            _wings[i].AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.62f, 0.04f, 0.5f) }, MaterialOverride = _feathers,
                Position = new Vector3(side * 0.31f, 0, 0.1f),
            });
            Seg(_wings[i], Vector3.Zero, new Vector3(side * 0.62f, 0.02f, -0.06f), 0.06f, 0.04f, dark, 5); // leading edge
            _tips[i] = new Node3D { Name = "Tip", Position = new Vector3(side * 0.62f, 0, 0) };
            _wings[i].AddChild(_tips[i]);
            // Outer wing: long primaries fanned toward the back.
            for (int f = 0; f < 4; f++)
                _tips[i].AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.62f - f * 0.08f, 0.025f, 0.12f) }, MaterialOverride = f % 2 == 0 ? dark : _feathers,
                    Position = new Vector3(side * (0.3f - f * 0.03f), 0, -0.02f + f * 0.12f), Rotation = new Vector3(0, side * (0.05f + f * 0.12f), 0),
                });
        }

        _legs = new Node3D[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            _legs[i] = new Node3D { Name = $"Leg{i}", Position = new Vector3(side * 0.09f, -0.1f, 0.05f) };
            _body.AddChild(_legs[i]);
            Seg(_legs[i], Vector3.Zero, new Vector3(0, -0.24f, 0.04f), 0.05f, 0.03f, belly, 5);
            for (int t = -1; t <= 1; t++)
                Seg(_legs[i], new Vector3(0, -0.24f, 0.04f), new Vector3(t * 0.05f, -0.3f, -0.08f), 0.015f, 0.004f, beak, 4);
        }
    }

    /// <summary>A tapered n-sided segment from a to b (radius ra at a, rb at b).</summary>
    private static void Seg(Node3D parent, Vector3 a, Vector3 b, float ra, float rb, Material mat, int sides)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.001f) return;
        var y = d / len;
        var x = Mathf.Abs(y.Dot(Vector3.Forward)) > 0.95f ? y.Cross(Vector3.Right).Normalized() : y.Cross(Vector3.Forward).Normalized();
        var z = x.Cross(y);
        parent.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = rb, BottomRadius = ra, Height = len, RadialSegments = sides, Rings = 1 },
            MaterialOverride = mat,
            Transform = new Transform3D(new Basis(x, y, z), (a + b) / 2f),
        });
    }
}
