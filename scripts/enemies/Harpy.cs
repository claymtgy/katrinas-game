using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Cliff harpy (Falls Valley, GDD §9). A small flying predator: it circles high over its
/// roost, and when it spots you it climbs above you and swoops down talons-first, then beats back
/// up out of sword reach. Arrows and firebolts reach it anywhere; a sword only as it swoops.
/// Parry the swoop and it tumbles to the ground, stunned: finish it there. A small animal, so more
/// players means more harpies, not tougher ones. Drops feathers (for the glider).
///
/// Two tougher kinds live over the Spire Cliffs (GDD §11, step 2): storm harpies, slate-blue,
/// which dive in pairs (when one swoops, its partner comes in right behind it), and the
/// Storm Harpy Queen, a great storm-grey raptor nesting on the Tall Spire. The Queen dives
/// like the rest, calls storm harpies to her, and every so often rears back and beats her wings: a
/// gale that hurls anyone near her off the spire unless they roll through it.</summary>
public partial class Harpy : Enemy
{
    public const float MaxHealth = 32f;
    public const float ClawDamage = 9f;
    public const float CruiseHeight = 11f;
    public const float SwoopSpeed = 10f;
    public const int Feathers = 2;

    public const float StormHealth = 70f, StormDamage = 14f;
    public const float QueenHealth = 900f, QueenDamage = 30f, QueenArmor = 4f;
    public const int QueenCoins = 40, QueenShards = 4, QueenFeathers = 6;
    /// <summary>The Queen: seconds between calls for storm harpies (at most QueenMaxGuard with her), and between gales.</summary>
    public const float QueenCallInterval = 15f, GaleInterval = 10f, GaleWindup = 1.1f, GaleRadius = 13f, GaleDamage = 10f;
    public const int QueenMaxGuard = 2;

    public enum Variant { Valley, Storm, Queen }
    public Variant Type { get; private set; }
    public bool Storm => Type == Variant.Storm;
    public bool Queen => Type == Variant.Queen;
    private float Size => Queen ? 2.2f : Storm ? 1.25f : 1f;
    private bool OverSpires => Type != Variant.Valley;

    public override string Kind => Queen ? "harpy_queen" : Storm ? "storm_harpy" : "harpy";
    protected override float WalkSpeed => 3.5f;
    protected override float RunSpeed => Queen ? 9f : 8f;
    protected override float AggroRange => Queen ? 30f : 26f;
    protected override float LeashRange => Queen ? 50f : 40f;
    protected override float HomeLeash => Queen ? 34f : 46f;
    protected override float AttackRange => Queen ? 3.4f : 2.1f;
    protected override float AttackReach => Queen ? 4.4f : 3.0f;
    protected override float AttackWindup => Queen ? 0.5f : 0.32f;
    protected override float AttackCooldown => Queen ? 2.6f : Storm ? 3f : 2.4f;
    protected override float HitStagger => Queen ? 0.08f : 0.25f;
    protected override float HitKnockback => Queen ? 0.5f : 3f;
    protected override float WanderRadius => Queen ? 8f : 12f;
    protected override Rect2 Bounds => OverSpires ? HomeCliffs.SpireField : HomeCliffs.ValleyBounds;
    protected override float MinY => OverSpires ? -35f : -1f;
    private float Cruise => Queen ? 9f : CruiseHeight;

    /// <summary>Stuns longer than this knock it out of the air.</summary>
    private const float GroundingStun = 0.6f;

    private Node3D _visual, _body, _head, _tail;
    private Node3D[] _wings, _tips, _legs;
    private ShaderMaterial _feathers;
    private float _flap, _time, _orbit;
    private Tween _tween;

    private readonly record struct Palette(Color Plumage, Color Dark, Color Belly, Color Beak, Color Eye);

    private static readonly Palette ValleyColors = new(new(0.34f, 0.27f, 0.44f), new(0.17f, 0.13f, 0.24f), new(0.74f, 0.64f, 0.66f), new(0.92f, 0.72f, 0.3f), new(1f, 0.35f, 0.25f));
    private static readonly Palette StormColors = new(new(0.3f, 0.37f, 0.5f), new(0.13f, 0.16f, 0.24f), new(0.78f, 0.8f, 0.84f), new(0.75f, 0.76f, 0.8f), new(0.55f, 0.85f, 1f));
    private static readonly Palette QueenColors = new(new(0.4f, 0.42f, 0.5f), new(0.16f, 0.15f, 0.22f), new(0.86f, 0.84f, 0.8f), new(0.95f, 0.75f, 0.3f), new(1f, 0.85f, 0.4f));
    private Palette Colors => Queen ? QueenColors : Storm ? StormColors : ValleyColors;

    /// <summary>On the ground (stunned or dead) rather than in the air.</summary>
    public bool Grounded => IsOnFloor();

    public static Harpy Create(int netId, int roost, Vector3 position, float yaw, float maxHealth = MaxHealth, float damage = ClawDamage,
        Variant type = Variant.Valley)
    {
        var h = new Harpy { Name = $"{type}Harpy_{netId}", Damage = damage, Type = type };
        h.Init(netId, roost, position, yaw, maxHealth, new Vector3(1.1f, 0.7f, 1.1f) * h.Size);
        if (type == Variant.Queen) h.Health.Armor = QueenArmor;
        h._orbit = netId * 1.7f;
        h.BuildVisual();
        return h;
    }

    public const float ShardChance = 1f / 3f;

    public override void ServerDropLoot(Node killer)
    {
        var p = killer as PlayerController;
        if (Queen) { QueenLoot(p); return; }
        int feathers = Storm ? Feathers + 1 : Feathers;
        string msg = $"+{feathers} Harpy Feathers";
        if (Supplies.Give(p, "harpy_feather", feathers) <= 0) msg = "";
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

    private void QueenLoot(PlayerController p)
    {
        var gs = GameState.Instance;
        string msg = $"+{QueenCoins} Coins, +{QueenShards} Rune Shards, +{QueenFeathers} Harpy Feathers";
        Supplies.Give(p, "coin", QueenCoins);
        Supplies.Give(p, Gear.ShardId, QueenShards);
        Supplies.Give(p, "harpy_feather", QueenFeathers);
        bool have = gs.HomeTier >= 3 || gs.Inventory.Count("longhouse_blueprint") > 0
                    || gs.Players.Values.Any(pl => pl.Pack.Count("longhouse_blueprint") > 0);
        if (!have)
        {
            Supplies.Give(p, "longhouse_blueprint", 1);
            msg += ", Longhouse Blueprint!";
            Net.Instance.ToastAll("Woven into the Queen's nest: a builder's plans for a longhouse, wrapped in oilcloth.");
        }
        Journal.Fact("queen_slain");
        if (p != null) Net.Instance.ToastTo(p.PeerId, msg);
    }

    private bool _swooping;
    private float _callIn = QueenCallInterval * 0.5f, _galeIn = GaleInterval * 0.6f, _galeTime;

    /// <summary>Server (storm harpies): this one is diving; its partner in the roost comes in right behind it.</summary>
    private void CallPartner()
    {
        foreach (var e in Wildlife.Instance.Harpies)
        {
            if (e == this || e.Group != Group || !e.Storm || e.Current == State.Dead || e._swooping) continue;
            if (e.Target == null && Target != null) e.Hunt(Target);
            e.AttackCooldownLeft = Mathf.Min(e.AttackCooldownLeft, 0.35f);
            break;
        }
    }

    /// <summary>Server (the Queen): call her guard and raise the gale while she fights. True while
    /// the gale's windup holds her (skip the usual flight).</summary>
    private bool QueenThink(float dt)
    {
        if (Current is State.Dead or State.Stagger or State.Prowl || Target == null) { _galeTime = 0; return false; }
        var wl = Wildlife.Instance;
        _callIn -= dt;
        if (_callIn <= 0)
        {
            _callIn = QueenCallInterval;
            int guard = wl.Harpies.Count(h => h != this && h.Group == Group && h.Current != State.Dead);
            if (guard < QueenMaxGuard)
            {
                wl.SpawnQueenGuard(this);
                Net.Instance.ToastAll("The Queen shrieks, and storm harpies wheel in out of the clouds…");
            }
        }
        if (_galeTime > 0)
        {
            // Rearing back, wings spread: she hangs in the air, then the gale.
            _galeTime -= dt;
            Velocity = Velocity.Lerp(Vector3.Zero, Mathf.Min(1f, 4f * dt));
            MoveAndSlide();
            if (_galeTime <= 0) Gale();
            return true;
        }
        _galeIn -= dt;
        if (_galeIn <= 0 && Current == State.Chase && GlobalPosition.DistanceTo(Target.GlobalPosition) < GaleRadius - 2f)
        {
            _galeIn = GaleInterval;
            _galeTime = GaleWindup;
            wl.BroadcastAnim(this, Wildlife.AnimSpecial);
        }
        return false;
    }

    /// <summary>Server: the Queen's wings: everyone close is hurled away and up (roll through it, or a
    /// raised guard halves it; a parry shrugs it off).</summary>
    private void Gale()
    {
        var net = Net.Instance;
        foreach (var p in GameState.Instance.Players.Values)
        {
            if (!CanHunt(p)) continue;
            var off = p.GlobalPosition - GlobalPosition;
            if (off.Length() > GaleRadius) continue;
            var result = net.ServerDamagePlayer(p, GaleDamage, this);
            if (result is Net.HitResult.Dodged or Net.HitResult.Parried) continue;
            var away = Flat(off);
            away = away.LengthSquared() < 0.01f ? -GlobalBasis.Z : away.Normalized();
            float k = result == Net.HitResult.Blocked ? 0.5f : 1f;
            net.ServerShovePlayer(p, (away * 15f + Vector3.Up * 5f) * k);
        }
    }

    protected override void Think(float dt)
    {
        if (Queen && QueenThink(dt)) return;
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
                       + Vector3.Up * (Cruise + Mathf.Sin(_time * 0.6f + NetId) * 1.5f);
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
                    if (!_swooping) { _swooping = true; if (Storm) CallPartner(); }
                    goal = chest;
                    speed = SwoopSpeed * (Queen ? 1.15f : 1f);
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
                _swooping = false;
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
                // Knocked out of the air over open sky: it catches itself before the clouds.
                if (falling && OverSpires && pos.Y < MinY + 3f) { StaggerFor = 0; falling = false; Enter(Target != null ? State.Chase : State.Prowl); }
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
            var want = to.Length() > 0.3f ? to.Normalized() * speed * SpeedScale : Vector3.Zero;
            want += wildlife.Separation(this) * 2f + SafeZonePush(pos) * 3f + Knockback;
            v = v.Lerp(want, Mathf.Min(1f, 3f * dt)); // wings, not legs: a little inertia
        }
        Knockback = Knockback.Lerp(Vector3.Zero, Mathf.Min(1f, 6f * dt));
        Velocity = v;
        MoveAndSlide();
        GlobalPosition = Wildlife.ClampTo(GlobalPosition, Bounds, MinY);

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
        t.TweenProperty(_feathers, "shader_parameter/tint", Colors.Plumage, 0.25);
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

    /// <summary>Everyone: the Queen rears back with her wings spread wide, then beats them (the gale).</summary>
    public override void PlaySpecial()
    {
        Sfx.At("harpy_screech", GlobalPosition + Vector3.Up, 6f, 0.05f, 100f);
        if (_visual == null || Current == State.Dead) return;
        _gale = GaleWindup + 0.45f;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_visual, "rotation:x", -0.7f, GaleWindup * 0.9).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_visual, "rotation:x", 0.3f, 0.12).SetTrans(Tween.TransitionType.Expo);
        _tween.TweenProperty(_visual, "rotation:x", 0f, 0.4);
        // A ring of wind rolling out at the moment she beats down.
        GetTree().CreateTimer(GaleWindup).Timeout += () =>
        {
            if (!IsInstanceValid(this) || Current == State.Dead) return;
            Fx.Ring(GetParent(), GlobalPosition, new Color(0.9f, 0.95f, 1f, 0.6f), GaleRadius, 0.6f);
            Sfx.At("dodge", GlobalPosition, 8f, 0.1f, 80f);
        };
    }

    private float _gale;

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
        // The Queen's gale: wings flung wide and held, then one great beat.
        if (_gale > 0)
        {
            _gale -= dt;
            bool down2 = _gale < 0.45f;
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                _wings[i].Rotation = new Vector3(0, 0, side * (down2 ? -0.7f : 0.9f));
                _tips[i].Rotation = new Vector3(0, 0, side * (down2 ? -0.5f : 0.4f));
            }
            return;
        }
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
        var c = Colors;
        _feathers = StyleMaterials.CharacterFlat(c.Plumage);
        var dark = StyleMaterials.CharacterFlat(c.Dark);
        var belly = StyleMaterials.CharacterFlat(c.Belly);
        var beak = StyleMaterials.CharacterFlat(c.Beak);
        var eyes = new StandardMaterial3D
        {
            AlbedoColor = c.Eye, EmissionEnabled = true, Emission = c.Eye, EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        _visual = new Node3D { Name = "Visual", Position = new Vector3(0, 0.4f * Size, 0) };
        AddChild(_visual);
        _body = new Node3D { Name = "Body", Scale = Vector3.One * Size };
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
