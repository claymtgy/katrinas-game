using Godot;

namespace KatrinasGame;

/// <summary>Shared brain for hostile creatures (wolves, bandits). Subclasses give the tuning
/// numbers, the body and the animations; the state machine, targeting, steering and replication
/// hooks live here.
///
/// Multiplayer: the server runs Think and the physics; clients glide to the positions Wildlife
/// sends and play animations. Health is the usual networked Health component, and a hit on a
/// player goes through Net.ServerDamagePlayer so blocks and parries work.</summary>
public abstract partial class Enemy : CharacterBody3D
{
    public enum State { Prowl, Chase, Windup, Recover, Stagger, Flee, Dead }

    /// <summary>"wolf", "bandit", "captain"... (sent over the network to build the right body).</summary>
    public abstract string Kind { get; }
    public int NetId { get; protected set; }
    /// <summary>Pack (wolves) or camp (bandits). Members alert each other and share an anchor.</summary>
    public int Group { get; protected set; }
    public Health Health { get; protected set; }
    public State Current { get; protected set; } = State.Prowl;
    /// <summary>Tests and photo shots: stop thinking and moving (still animates, idles).</summary>
    public bool Frozen { get; set; }
    public PlayerController Target { get; protected set; }
    /// <summary>Damage per hit (big monsters get it scaled by player count when spawned).</summary>
    public float Damage { get; set; }

    // ---- tuning
    protected abstract float WalkSpeed { get; }
    protected abstract float RunSpeed { get; }
    protected abstract float AggroRange { get; }
    /// <summary>Give up on a target farther than this from the enemy.</summary>
    protected abstract float LeashRange { get; }
    /// <summary>Give up on a target farther than this from the group's anchor (0 = no limit).</summary>
    protected virtual float HomeLeash => 0f;
    protected abstract float AttackRange { get; }
    protected abstract float AttackReach { get; }
    protected abstract float AttackWindup { get; }
    protected abstract float AttackCooldown { get; }
    protected virtual bool FleesByDay => false;
    protected virtual float HitStagger => 0.35f;
    protected virtual float HitKnockback => 5f;
    protected virtual float WanderRadius => 6f;
    /// <summary>Where this enemy is kept (the plateau and vale, or Falls Valley).</summary>
    protected virtual Rect2 Bounds => Wildlife.HomeBounds;
    /// <summary>Server: time to run off (wolves at dawn; raiders in the morning).</summary>
    protected virtual bool ShouldFlee => FleesByDay && Wildlife.IsDay(GameState.Instance.TimeOfDay);

    // ---- server AI
    protected float StateTime;
    protected float AttackCooldownLeft, Gravity, StaggerFor;
    protected Vector3 Knockback;
    /// <summary>Raiders: the building or chest they're going for (null = none).</summary>
    public Node3D ObjectiveNode { get; protected set; }
    private Vector3 _wanderTo;
    private bool _hasWander;
    // ---- path following (WorldNav)
    private Vector3[] _path = System.Array.Empty<Vector3>();
    private int _pathIndex;
    private float _repathIn;
    private Vector3 _pathGoal;
    protected readonly RandomNumberGenerator Rng = new();

    // ---- client interpolation
    private Vector3 _netPos;
    private float _netYaw;
    private bool _hasNet;

    // ---- visual
    protected Vector3 LastPos;
    protected float SpeedSmoothed;

    /// <summary>Common setup; call from the subclass factory before the enemy enters the tree.</summary>
    protected void Init(int netId, int group, Vector3 position, float yaw, float maxHealth, Vector3 colliderSize)
    {
        NetId = netId;
        Group = group;
        CollisionLayer = GameState.LayerProps;
        CollisionMask = GameState.LayerWorld | GameState.LayerProps | GameState.LayerPlayer;
        Position = position;
        Rotation = new Vector3(0, yaw, 0);
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = colliderSize }, Position = new Vector3(0, colliderSize.Y / 2f + 0.05f, 0) });
        Health = new Health { Name = "Health", Max = maxHealth };
        AddChild(Health);
    }

    public override void _Ready()
    {
        Gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
        Rng.Seed = (ulong)NetId * 7919;
        LastPos = GlobalPosition;
        _netPos = GlobalPosition;
        _netYaw = Rotation.Y;
        Health.Damaged += OnDamaged;
        Health.Died += OnDied;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        TickStatus(dt);
        if (Current != State.Dead && !Frozen)
        {
            if (Net.Instance.IsServer) Think(dt);
            else Follow(dt);
        }
        if (!Net.Instance.IsDedicated)
        {
            var pos = GlobalPosition;
            float speed = dt > 0 ? Flat(pos - LastPos).Length() / dt : 0f;
            LastPos = pos;
            SpeedSmoothed = Mathf.Lerp(SpeedSmoothed, Mathf.Min(speed, 10f), Mathf.Min(1f, 10f * dt));
            Animate(dt);
        }
    }

    // ================================================================== server AI

    protected void Enter(State s)
    {
        Current = s;
        StateTime = 0;
        _hasWander = false;
    }

    /// <summary>Server: is this player fair game for us right now?</summary>
    public bool CanHunt(PlayerController p)
    {
        if (p == null || !IsInstanceValid(p) || !p.IsInsideTree() || p.Health.IsDead) return false;
        if (p.Vanished) return false; // Rogue's Vanish
        if (p.Downed) return false;   // a downed player is left alone (GDD §16)
        if (GameState.Instance.InSafeZone(p.GlobalPosition)) return false;
        if (FearsFire && Campfire.WardAt(p.GlobalPosition) != null) return false;
        if (GlobalPosition.DistanceTo(p.GlobalPosition) >= LeashRange) return false;
        if (HomeLeash > 0 && Flat(Wildlife.Instance.GroupAnchor(Group, GlobalPosition) - p.GlobalPosition).Length() > HomeLeash) return false;
        return true;
    }

    /// <summary>Server: chase this player (group alert, being hit).</summary>
    public void Hunt(PlayerController p)
    {
        if (Current is State.Dead or State.Flee || !CanHunt(p)) return;
        Target = p;
        if (Current == State.Prowl) Enter(State.Chase);
    }

    /// <summary>Server clock (Net.Now): slowed until then (the Wizard's Frost Nova).</summary>
    public double SlowUntil { get; private set; }
    private float _slowFactor = 1f;

    /// <summary>Server: move slower for a while (factor 0.5 = half speed).</summary>
    public void Slow(float seconds, float factor)
    {
        if (Current == State.Dead) return;
        SlowUntil = Net.Now + seconds;
        _slowFactor = factor;
    }

    /// <summary>Server: what movement speed is multiplied by now (1 = normal).</summary>
    protected float SpeedScale => Net.Now < SlowUntil ? _slowFactor : 1f;

    /// <summary>Server: knocked off balance (a parried attack). Can't act for a while.</summary>
    public void Stun(float seconds)
    {
        if (Current == State.Dead) return;
        Enter(State.Stagger);
        StaggerFor = seconds;
        Wildlife.Instance.BroadcastAnim(this, Wildlife.AnimStun);
    }

    /// <summary>Server, prowling with no one to fight: go for something else (raiders head for
    /// your buildings). Return true when handled.</summary>
    protected virtual bool Objective(float dt, Vector3 pos, ref Vector3 desired, ref float speed, ref float? faceYaw) => false;

    /// <summary>Server: the windup ended with no player targeted: hit the objective.</summary>
    protected virtual void StrikeObjective() { }

    protected virtual void Think(float dt)
    {
        var wildlife = Wildlife.Instance;
        StateTime += dt;
        AttackCooldownLeft -= dt;
        if (Current != State.Flee && ShouldFlee) Enter(State.Flee);

        Vector3 pos = GlobalPosition;
        Vector3 desired = Vector3.Zero;
        float speed = 0f;
        float? faceYaw = null;

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
                if (Objective(dt, pos, ref desired, ref speed, ref faceYaw)) break;
                // Roam around the group's anchor (packs creep toward the base; camps stay put).
                var anchor = wildlife.GroupAnchor(Group, pos);
                if (!_hasWander || Flat(pos - _wanderTo).Length() < 1f || StateTime > 6f)
                {
                    _wanderTo = anchor + new Vector3(Rng.RandfRange(-WanderRadius, WanderRadius), 0, Rng.RandfRange(-WanderRadius, WanderRadius));
                    _hasWander = true;
                    StateTime = 0;
                }
                desired = Steer(pos, _wanderTo, dt);
                speed = Flat(anchor - pos).Length() > WanderRadius * 2f ? RunSpeed * 0.7f : WalkSpeed;
                break;
            }
            case State.Chase:
            {
                if (!CanHunt(Target)) { Target = null; Enter(State.Prowl); break; }
                var to = Flat(Target.GlobalPosition - pos);
                float dist = to.Length();
                if (dist < AttackRange && AttackCooldownLeft <= 0 && MayAttack)
                {
                    Enter(State.Windup);
                    wildlife.BroadcastAnim(this, Wildlife.AnimAttack);
                    break;
                }
                desired = Steer(pos, Target.GlobalPosition, dt);
                speed = dist > 4f ? RunSpeed : Mathf.Lerp(WalkSpeed, RunSpeed, (dist - 1f) / 3f);
                if (dist < AttackRange) speed = 0; // waiting out the cooldown
                faceYaw = YawOf(to);
                break;
            }
            case State.Windup:
            {
                if (Target != null && IsInstanceValid(Target) && Target.IsInsideTree()) faceYaw = YawOf(Flat(Target.GlobalPosition - pos));
                else if (ObjectiveNode != null && IsInstanceValid(ObjectiveNode) && ObjectiveNode.IsInsideTree()) faceYaw = YawOf(Flat(ObjectiveNode.GlobalPosition - pos));
                if (StateTime >= AttackWindup)
                {
                    AttackCooldownLeft = AttackCooldown;
                    Enter(State.Recover);
                    if (CanHunt(Target) && Flat(Target.GlobalPosition - pos).Length() < AttackReach)
                        wildlife.Strike(this, Target); // may stun us (parry): do it last
                    else if (Target == null) StrikeObjective();
                }
                break;
            }
            case State.Recover:
                if (Target != null && IsInstanceValid(Target)) desired = -Flat(Target.GlobalPosition - pos);
                speed = 1.5f;
                if (StateTime > 0.5f) Enter(Target != null ? State.Chase : State.Prowl);
                break;
            case State.Stagger:
                if (StateTime > Mathf.Max(StaggerFor, 0.05f)) { StaggerFor = 0; Enter(Target != null ? State.Chase : State.Prowl); }
                break;
            case State.Flee:
            {
                var nearest = wildlife.NearestPlayer(pos, out float d);
                desired = nearest != null ? Flat(pos - nearest.GlobalPosition) : Flat(pos - GameState.Instance.ClaimCenter);
                speed = RunSpeed;
                if (nearest == null || d > 40f || StateTime > 12f) wildlife.Despawn(this);
                break;
            }
        }

        if (desired.LengthSquared() > 0.0001f) desired = desired.Normalized();
        desired += wildlife.Separation(this) + Avoid(desired) + SafeZonePush(pos) + (FearsFire ? FirePush(pos) : Vector3.Zero) + ExtraPush(pos);
        if (desired.LengthSquared() > 1f) desired = desired.Normalized();

        var v = Velocity;
        var target = desired * speed * SpeedScale + Knockback;
        float accel = Mathf.Min(1f, 8f * dt);
        v.X = Mathf.Lerp(v.X, target.X, accel);
        v.Z = Mathf.Lerp(v.Z, target.Z, accel);
        v.Y = IsOnFloor() ? -0.5f : v.Y - Gravity * dt;
        Knockback = Knockback.Lerp(Vector3.Zero, Mathf.Min(1f, 6f * dt));
        Velocity = v;
        MoveAndSlide();

        GlobalPosition = Wildlife.ClampTo(GlobalPosition, Bounds);

        float flatSpeed = Flat(Velocity).Length();
        if (faceYaw == null && flatSpeed > 0.4f) faceYaw = YawOf(Flat(Velocity));
        if (faceYaw != null) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, faceYaw.Value, Mathf.Min(1f, 10f * dt)), 0);
    }

    /// <summary>Server: which way to walk to reach goal, following the navmesh around rocks, trees
    /// and buildings. Close up (or before the navmesh is baked) it's just straight at it.</summary>
    protected Vector3 Steer(Vector3 pos, Vector3 goal, float dt)
    {
        var direct = Flat(goal - pos);
        var nav = WorldNav.For(pos);
        if (nav == null || direct.Length() < 2.5f) return direct;

        _repathIn -= dt;
        if (_repathIn <= 0 || Flat(goal - _pathGoal).Length() > 1.5f || _pathIndex >= _path.Length)
        {
            _path = nav.Path(pos, goal);
            _pathIndex = 0;
            _pathGoal = goal;
            _repathIn = 0.5f + Rng.Randf() * 0.2f;
        }
        while (_pathIndex < _path.Length && Flat(_path[_pathIndex] - pos).Length() < 0.7f) _pathIndex++;
        return _pathIndex < _path.Length ? Flat(_path[_pathIndex] - pos) : direct;
    }

    /// <summary>Steer around trees and rocks with a short feeler ray.</summary>
    private Vector3 Avoid(Vector3 dir)
    {
        if (dir.LengthSquared() < 0.01f) return Vector3.Zero;
        var from = GlobalPosition + Vector3.Up * 0.5f;
        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * 1.8f, GameState.LayerProps,
            new Godot.Collections.Array<Rid> { GetRid() });
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is Enemy) return Vector3.Zero;
        var side = new Vector3(-dir.Z, 0, dir.X);
        return side * ((NetId & 1) == 0 ? 1.2f : -1.2f);
    }

    /// <summary>Server: one more steering push (cave crawlers: out of the light).</summary>
    protected virtual Vector3 ExtraPush(Vector3 pos) => Vector3.Zero;

    /// <summary>Server: may start an attack now (cave crawlers only strike from the dark).</summary>
    protected virtual bool MayAttack => true;

    /// <summary>Animals that keep away from a campfire or hearth (wolves).</summary>
    protected virtual bool FearsFire => false;

    /// <summary>Away from the nearest fire, for animals that fear it.</summary>
    protected static Vector3 FirePush(Vector3 pos)
    {
        var fire = Campfire.WardAt(pos, 1.5f);
        if (fire == null) return Vector3.Zero;
        var off = Flat(pos - fire.GlobalPosition);
        return (off.LengthSquared() < 0.01f ? Vector3.Right : off.Normalized()) * 2f;
    }

    /// <summary>Hostiles won't enter a sanctuary (blossom shrine, the village).</summary>
    protected static Vector3 SafeZonePush(Vector3 pos)
    {
        foreach (var (center, radius) in GameState.Instance.SafeZones)
        {
            var off = Flat(pos - center);
            if (off.Length() < radius + 1.5f) return off.Normalized() * 2f;
        }
        return Vector3.Zero;
    }

    private void OnDamaged(float amount, Node source)
    {
        if (!Net.Instance.IsDedicated) FlashHit();
        if (!Net.Instance.IsServer || Health.IsDead) return;
        // Server: flinch away from the hit and go for whoever did it.
        if (source is Node3D s) Knockback = Flat(GlobalPosition - s.GlobalPosition).Normalized() * HitKnockback;
        if (Current != State.Stagger || StaggerFor <= HitStagger) { Enter(State.Stagger); StaggerFor = HitStagger; }
        if (source is PlayerController p)
        {
            Target = p;
            Wildlife.Instance.AlertGroup(this, p);
        }
    }

    private void OnDied(Node source)
    {
        Current = State.Dead;
        CollisionLayer = 0;
        CollisionMask = GameState.LayerWorld;
        Velocity = Vector3.Zero;
        if (!Net.Instance.IsDedicated) PlayDeath();
        if (Net.Instance.IsServer) Wildlife.Instance.OnEnemyKilled(this, source);
    }

    // ================================================================== client

    public void ApplyNetState(Vector3 pos, float yaw)
    {
        _netPos = pos;
        _netYaw = yaw;
        if (!_hasNet) { GlobalPosition = pos; Rotation = new Vector3(0, yaw, 0); }
        _hasNet = true;
    }

    private void Follow(float dt)
    {
        if (!_hasNet) return;
        if (GlobalPosition.DistanceTo(_netPos) > 5f) GlobalPosition = _netPos;
        else GlobalPosition = GlobalPosition.Lerp(_netPos, Mathf.Min(1f, 12f * dt));
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, _netYaw, Mathf.Min(1f, 12f * dt)), 0);
    }

    // ================================================================== subclass hooks

    /// <summary>Server: loot for the shared stockpile. Killer may be null.</summary>
    public abstract void ServerDropLoot(Node killer);
    /// <summary>Everyone: the attack telegraph (windup then strike).</summary>
    public abstract void PlayAttack();
    public virtual void PlayStun() { }
    protected abstract void Animate(float dt);
    protected abstract void FlashHit();
    protected abstract void PlayDeath();

    // ================================================================== helpers

    protected static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
    protected static float YawOf(Vector3 dir) => Mathf.Atan2(-dir.X, -dir.Z);

    /// <summary>Build an enemy of a kind (server spawn, client mirror, snapshot).</summary>
    public static Enemy Create(string kind, int netId, int group, Vector3 pos, float yaw, float maxHealth, float damage) => kind switch
    {
        "wolf" => Wolf.Create(netId, group, pos, yaw, maxHealth, damage),
        "bandit" => Bandit.Create(netId, group, pos, yaw, maxHealth, damage, captain: false),
        "captain" => Bandit.Create(netId, group, pos, yaw, maxHealth, damage, captain: true),
        "raider" => Bandit.Create(netId, group, pos, yaw, maxHealth, damage, captain: false, raider: true),
        "harpy" => Harpy.Create(netId, group, pos, yaw, maxHealth, damage),
        "boar" => Boar.Create(netId, group, pos, yaw, maxHealth, damage),
        "crawler" => Crawler.Create(netId, group, pos, yaw, maxHealth, damage, mother: false),
        "crawler_mother" => Crawler.Create(netId, group, pos, yaw, maxHealth, damage, mother: true),
        _ => null,
    };
}
