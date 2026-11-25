using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Harmless animals that make the world feel lived in (never hostile, never networked:
/// each screen has its own, around its own player, and nothing here touches gameplay).
///
/// - **Birds in the trees:** walk near a tree by day and a few birds burst out of its crown and
///   fly off, flapping, away from you; the tree fills again after a while. Chopping a tree
///   flushes them too. Now and then a small flock crosses the sky.
/// - **Rabbits** nibble in the grass on the plateau, in the vale and the country, and hop off
///   when you come close.
/// - **Deer** graze out in the hills in twos and threes, and bolt when you get near.
/// - **Butterflies** flutter low over the grass by day.
///
/// All low-poly, built in code like the wolves, in the toon materials.</summary>
public partial class AmbientLife : Node3D
{
    public static AmbientLife Instance { get; private set; }

    /// <summary>Off in the scripted tests (they count nodes and take screenshots).</summary>
    public static bool Enabled = true;

    private const float FlushRadius = 9f, TreeRefill = 120f;
    private const int MaxRabbits = 4, MaxDeer = 3, MaxButterflies = 6;

    private readonly RandomNumberGenerator _rng = new();
    private readonly List<(Node3D Tree, double ReadyAt)> _trees = new();
    private readonly List<Bird> _birds = new();
    private readonly List<Critter> _critters = new();
    private readonly List<Butterfly> _butterflies = new();
    private float _scan, _spawn, _flockIn = 25f;
    private bool _treesFound;

    public int BirdCount => _birds.Count;
    public int CritterCount => _critters.Count;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready() => _rng.Randomize();

    private static bool Daylight(float t) => t > 0.23f && t < 0.77f;

    public override void _Process(double delta)
    {
        if (!Enabled) return;
        float dt = (float)delta;
        var gs = GameState.Instance;
        var me = gs.LocalPlayer;
        if (me == null || !IsInstanceValid(me)) return;
        var pos = me.GlobalPosition;
        bool day = Daylight(gs.TimeOfDay);

        if (!_treesFound) FindTrees();

        _scan -= dt;
        if (_scan <= 0f)
        {
            _scan = 0.4f;
            if (day) FlushNear(pos);
        }

        _flockIn -= dt;
        if (_flockIn <= 0f)
        {
            _flockIn = _rng.RandfRange(35f, 70f);
            if (day && !Underground(pos)) Flock(pos);
        }

        _spawn -= dt;
        if (_spawn <= 0f)
        {
            _spawn = 1.5f;
            Populate(pos, day);
        }

        for (int i = _birds.Count - 1; i >= 0; i--)
            if (!_birds[i].Step(dt)) { _birds[i].QueueFree(); _birds.RemoveAt(i); }
        for (int i = _butterflies.Count - 1; i >= 0; i--)
            if (!_butterflies[i].Step(dt, pos, day)) { _butterflies[i].QueueFree(); _butterflies.RemoveAt(i); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled) return;
        var me = GameState.Instance.LocalPlayer;
        if (me == null || !IsInstanceValid(me)) return;
        var space = GetWorld3D().DirectSpaceState;
        for (int i = _critters.Count - 1; i >= 0; i--)
            if (!_critters[i].Step((float)delta, me.GlobalPosition, space)) { _critters[i].QueueFree(); _critters.RemoveAt(i); }
    }

    private static bool Underground(Vector3 p) => HomeCliffs.InMine(p);

    // ------------------------------------------------------------------ birds in trees

    private void FindTrees()
    {
        _treesFound = true;
        foreach (var n in GetTree().Root.FindChildren("*", "StaticBody3D", true, false))
            if (n is Harvestable { DropItemId: "wood" } h) _trees.Add((h, 0));
    }

    /// <summary>A tree near this point lets its birds go (chopping it, walking under it).</summary>
    public void FlushTree(Node3D tree, Vector3 from)
    {
        for (int i = 0; i < _trees.Count; i++)
        {
            if (_trees[i].Tree != tree) continue;
            if (Net.Now < _trees[i].ReadyAt) return;
            _trees[i] = (tree, Net.Now + TreeRefill * _rng.RandfRange(0.7f, 1.3f));
            Burst(tree.GlobalPosition + Vector3.Up * _rng.RandfRange(3.5f, 5f), from);
            return;
        }
    }

    private void FlushNear(Vector3 pos)
    {
        for (int i = 0; i < _trees.Count; i++)
        {
            var (tree, ready) = _trees[i];
            if (!IsInstanceValid(tree)) continue;
            if (Net.Now < ready) continue;
            var tp = tree.GlobalPosition;
            if (Mathf.Abs(tp.X - pos.X) > FlushRadius || Mathf.Abs(tp.Z - pos.Z) > FlushRadius) continue;
            if (tp.DistanceTo(pos) > FlushRadius || !tree.Visible) continue;
            // Not every tree has birds in it.
            if (_rng.Randf() < 0.45f) { _trees[i] = (tree, Net.Now + TreeRefill); continue; }
            _trees[i] = (tree, Net.Now + TreeRefill * _rng.RandfRange(0.7f, 1.3f));
            Burst(tp + Vector3.Up * _rng.RandfRange(3.5f, 5f), pos);
        }
    }

    private void Burst(Vector3 crown, Vector3 from)
    {
        if (_birds.Count > 30) return;
        var away = crown - from;
        away.Y = 0;
        away = away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Forward;
        int n = _rng.RandiRange(2, 5);
        var tint = BirdColors[_rng.RandiRange(0, BirdColors.Length - 1)];
        for (int i = 0; i < n; i++)
        {
            var dir = away.Rotated(Vector3.Up, _rng.RandfRange(-0.9f, 0.9f));
            var vel = dir * _rng.RandfRange(6f, 9f) + Vector3.Up * _rng.RandfRange(3.5f, 6f);
            var start = crown + new Vector3(_rng.RandfRange(-1.2f, 1.2f), _rng.RandfRange(-0.6f, 0.8f), _rng.RandfRange(-1.2f, 1.2f));
            AddBird(start, vel, tint, _rng.RandfRange(0.05f, 0.35f));
        }
        Sfx.At("rustle", crown, -8f, 0.25f, 30f);
    }

    /// <summary>A small flock crossing high overhead, from one side to the other.</summary>
    private void Flock(Vector3 pos)
    {
        float a = _rng.RandfRange(0, Mathf.Tau);
        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        var side = new Vector3(-dir.Z, 0, dir.X);
        var start = pos - dir * 70f + side * _rng.RandfRange(-25f, 25f) + Vector3.Up * _rng.RandfRange(22f, 34f);
        int n = _rng.RandiRange(4, 8);
        var tint = BirdColors[_rng.RandiRange(0, BirdColors.Length - 1)];
        for (int i = 0; i < n; i++)
        {
            // A loose V.
            int k = (i + 1) / 2 * (i % 2 == 0 ? 1 : -1);
            var off = -dir * Mathf.Abs(k) * 1.6f + side * k * 1.4f + Vector3.Up * _rng.RandfRange(-0.4f, 0.4f);
            AddBird(start + off, dir * 9f, tint, 0f, life: 16f);
        }
    }

    private static readonly Color[] BirdColors =
    {
        new(0.32f, 0.24f, 0.2f), new(0.24f, 0.26f, 0.32f), new(0.5f, 0.36f, 0.26f), new(0.2f, 0.32f, 0.5f), new(0.15f, 0.14f, 0.16f),
    };

    private void AddBird(Vector3 at, Vector3 vel, Color tint, float delay, float life = 7f)
    {
        var b = new Bird(tint, vel, delay, life, _rng.RandfRange(0, 10f));
        AddChild(b);
        b.GlobalPosition = at;
        _birds.Add(b);
    }

    // ------------------------------------------------------------------ rabbits, deer, butterflies

    private void Populate(Vector3 pos, bool day)
    {
        if (Underground(pos) || pos.Y > 40f || pos.Y < -20f) return;
        int rabbits = 0, deer = 0;
        foreach (var c in _critters) { if (c.Deer) deer++; else rabbits++; }
        bool dusk = !day && GameState.Instance.TimeOfDay is > 0.18f and < 0.85f;

        if (rabbits < (day ? MaxRabbits : dusk ? 2 : 0) && GrassAt(RandomAround(pos, 22f, 40f), out var r))
            AddCritter(new Critter(false, _rng), r);
        // Deer keep to the open country (the hills), well away from Ashby.
        if (deer < MaxDeer && (day || dusk) && Terrain.Domain.HasPoint(new Vector2(pos.X, pos.Z)) && _rng.Randf() < 0.35f)
        {
            var at = RandomAround(pos, 45f, 70f);
            if (GrassAt(at, out var d) && Terrain.HeightAt(d) > 2f && new Vector2(d.X - HomeCliffs.AshbyCenter.X, d.Z - HomeCliffs.AshbyCenter.Z).Length() > 70f)
            {
                int herd = _rng.RandiRange(1, MaxDeer - deer);
                for (int i = 0; i < herd; i++)
                    if (GrassAt(d + new Vector3(_rng.RandfRange(-4, 4), 0, _rng.RandfRange(-4, 4)), out var spot))
                        AddCritter(new Critter(true, _rng, stag: i == 0 && _rng.Randf() < 0.5f), spot);
            }
        }
        if (day && _butterflies.Count < MaxButterflies && GrassAt(RandomAround(pos, 6f, 16f), out var bf))
        {
            var b = new Butterfly(_rng);
            AddChild(b);
            b.GlobalPosition = bf + Vector3.Up * _rng.RandfRange(0.4f, 1.2f);
            _butterflies.Add(b);
        }
    }

    private void AddCritter(Critter c, Vector3 at)
    {
        AddChild(c);
        c.GlobalPosition = at;
        c.Rotation = new Vector3(0, _rng.RandfRange(0, Mathf.Tau), 0);
        _critters.Add(c);
    }

    private Vector3 RandomAround(Vector3 p, float min, float max)
    {
        float a = _rng.RandfRange(0, Mathf.Tau), r = _rng.RandfRange(min, max);
        return p + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
    }

    /// <summary>Open, grassy, dry ground at (x, z): the plateau, the vale and the country, Falls Valley.</summary>
    private bool GrassAt(Vector3 at, out Vector3 ground)
    {
        ground = default;
        var p2 = new Vector2(at.X, at.Z);
        bool land = Terrain.Domain.HasPoint(p2) || new Rect2(-58, -26, 116, 114).HasPoint(p2) || HomeCliffs.FallsLand.HasPoint(p2);
        if (!land || HomeCliffs.InMine(at)) return false;
        var space = GetWorld3D().DirectSpaceState;
        var from = new Vector3(at.X, Mathf.Max(at.Y, Terrain.HeightAt(at)) + 30f, at.Z);
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 60f, GameState.LayerWorld | GameState.LayerProps));
        if (hit.Count == 0) return false;
        // Ground, not a roof, a rock or a building.
        if (hit["collider"].AsGodotObject() is Node n && (n is not StaticBody3D || n.Name.ToString().StartsWith("Ground") == false && !n.Name.ToString().StartsWith("Chunk_"))) return false;
        var normal = hit["normal"].AsVector3();
        if (normal.Y < 0.85f) return false;
        ground = hit["position"].AsVector3();
        if (!float.IsNaN(GameState.Instance.WaterSurfaceAt(ground + Vector3.Up * 0.3f))) return false;
        return true;
    }

    // ================================================================== the animals

    private static StandardMaterial3D Toon(Color c) => StyleMaterials.Toon(c, 0.3f);

    /// <summary>A small bird: a body and two flapping wings. Flies along its velocity (curving up,
    /// then levelling off), and fades out of existence far away.</summary>
    private partial class Bird : Node3D
    {
        private readonly Node3D _left, _right;
        private Vector3 _vel;
        private float _delay, _life, _t;
        private readonly float _phase;

        public Bird() { } // (Godot wants one)

        public Bird(Color tint, Vector3 vel, float delay, float life, float phase)
        {
            _vel = vel; _delay = delay; _life = life; _phase = phase;
            Visible = delay <= 0f;
            var body = Toon(tint);
            var belly = Toon(tint.Lightened(0.35f));
            AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 6, Rings = 3 }, MaterialOverride = body, Scale = new Vector3(0.8f, 0.75f, 1.6f) });
            AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 5, Rings = 3 }, MaterialOverride = belly, Position = new Vector3(0, 0.03f, -0.13f) });
            AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(0.05f, 0.05f, 0.05f) }, MaterialOverride = Toon(new Color(0.85f, 0.65f, 0.25f)), Position = new Vector3(0, 0.03f, -0.2f), Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0) });
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.015f, 0.12f) }, MaterialOverride = body, Position = new Vector3(0, 0.01f, 0.17f) }); // tail
            _left = Wing(body, -1f);
            _right = Wing(body, 1f);
        }

        private Node3D Wing(Material m, float side)
        {
            var pivot = new Node3D { Position = new Vector3(side * 0.05f, 0.03f, -0.02f) };
            AddChild(pivot);
            pivot.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.012f, 0.11f) }, MaterialOverride = m, Position = new Vector3(side * 0.11f, 0, 0) });
            return pivot;
        }

        public bool Step(float dt)
        {
            if (_delay > 0f) { _delay -= dt; if (_delay <= 0f) Visible = true; return true; }
            _t += dt;
            // Climb out of the tree, then level off.
            _vel.Y = Mathf.MoveToward(_vel.Y, 0.6f, 2.2f * dt);
            GlobalPosition += _vel * dt;
            var flat = new Vector3(_vel.X, 0, _vel.Z);
            if (flat.LengthSquared() > 0.01f) Rotation = new Vector3(Mathf.Atan2(_vel.Y, flat.Length()) * 0.5f, Mathf.Atan2(-_vel.X, -_vel.Z), 0);
            float flap = Mathf.Sin((_t + _phase) * 22f) * 0.9f;
            if (_vel.Y < 1f && Mathf.Sin((_t + _phase) * 1.3f) > 0.4f) flap = 0.15f; // glide a moment
            _left.Rotation = new Vector3(0, 0, flap);
            _right.Rotation = new Vector3(0, 0, -flap);
            float left = _life - _t;
            if (left < 1f) Scale = Vector3.One * Mathf.Max(0.01f, left);
            return _t < _life;
        }
    }

    /// <summary>A rabbit (hops in short bursts, nibbles, flees in long bounds) or a deer (grazes,
    /// walks a little, bolts at a gallop). Kept on the ground by a ray each step.</summary>
    private partial class Critter : Node3D
    {
        public readonly bool Deer;
        private readonly RandomNumberGenerator _rng;
        private readonly Node3D _body;
        private Node3D _head;
        private readonly List<Node3D> _legs = new();
        private float _idle, _hop, _flee, _t;
        private Vector3 _dir;
        private float _groundY = float.NaN;

        private float Wary => Deer ? 18f : 7f;
        private float Speed => _flee > 0 ? (Deer ? 11f : 7.5f) : (Deer ? 1.2f : 2.2f);
        private float Despawn => Deer ? 120f : 60f;

        public Critter() { } // (Godot wants one)

        public Critter(bool deer, RandomNumberGenerator rng, bool stag = false)
        {
            Deer = deer;
            _rng = rng;
            _idle = rng.RandfRange(0.5f, 3f);
            _body = new Node3D();
            AddChild(_body);
            if (deer) BuildDeer(stag); else BuildRabbit();
        }

        private void BuildRabbit()
        {
            var fur = Toon(new[] { new Color(0.45f, 0.36f, 0.28f), new Color(0.5f, 0.47f, 0.44f), new Color(0.36f, 0.28f, 0.22f) }[_rng.RandiRange(0, 2)]);
            var white = Toon(new Color(0.92f, 0.9f, 0.86f));
            _body.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 7, Rings = 4 }, MaterialOverride = fur, Position = new Vector3(0, 0.14f, 0.02f), Scale = new Vector3(1f, 0.9f, 1.35f) });
            var head = new Node3D { Position = new Vector3(0, 0.24f, -0.15f) };
            _body.AddChild(head);
            head.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 6, Rings = 4 }, MaterialOverride = fur });
            foreach (float s in new[] { -1f, 1f })
                head.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.16f, 0.05f) }, MaterialOverride = fur, Position = new Vector3(s * 0.035f, 0.11f, 0.02f), Rotation = new Vector3(0.25f, 0, s * 0.15f) });
            _body.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 5, Rings = 3 }, MaterialOverride = white, Position = new Vector3(0, 0.17f, 0.19f) });
        }

        private void BuildDeer(bool stag)
        {
            var coat = Toon(new Color(0.55f, 0.36f, 0.22f));
            var dark = Toon(new Color(0.3f, 0.2f, 0.14f));
            var white = Toon(new Color(0.92f, 0.88f, 0.8f));
            _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.42f, 0.42f, 1.0f) }, MaterialOverride = coat, Position = new Vector3(0, 0.95f, 0) });
            var neck = new Node3D { Position = new Vector3(0, 1.05f, -0.45f), Rotation = new Vector3(0.6f, 0, 0) };
            _body.AddChild(neck);
            neck.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.55f, 0.22f) }, MaterialOverride = coat, Position = new Vector3(0, 0.25f, 0) });
            _head = new Node3D { Position = new Vector3(0, 0.52f, -0.05f), Rotation = new Vector3(-0.6f, 0, 0) };
            neck.AddChild(_head);
            _head.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.18f, 0.34f) }, MaterialOverride = coat, Position = new Vector3(0, 0, -0.08f) });
            _head.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.1f, 0.08f, 0.08f) }, MaterialOverride = dark, Position = new Vector3(0, -0.03f, -0.26f) });
            foreach (float s in new[] { -1f, 1f })
            {
                _head.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(0.1f, 0.16f, 0.03f) }, MaterialOverride = coat, Position = new Vector3(s * 0.11f, 0.13f, 0.04f), Rotation = new Vector3(0, 0, s * -0.5f) });
                if (stag)
                {
                    _head.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.02f, Height = 0.4f, RadialSegments = 4 }, MaterialOverride = dark, Position = new Vector3(s * 0.08f, 0.28f, 0.02f), Rotation = new Vector3(0.2f, 0, s * -0.35f) });
                    _head.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.016f, Height = 0.18f, RadialSegments = 4 }, MaterialOverride = dark, Position = new Vector3(s * 0.15f, 0.36f, -0.06f), Rotation = new Vector3(-0.6f, 0, s * -0.2f) });
                }
            }
            _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.12f, 0.14f, 0.06f) }, MaterialOverride = white, Position = new Vector3(0, 1.08f, 0.52f), Rotation = new Vector3(-0.4f, 0, 0) });
            foreach (var (x, z) in new[] { (-0.14f, -0.38f), (0.14f, -0.38f), (-0.14f, 0.38f), (0.14f, 0.38f) })
            {
                var hip = new Node3D { Position = new Vector3(x, 0.78f, z) };
                _body.AddChild(hip);
                hip.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.78f, 0.09f) }, MaterialOverride = coat, Position = new Vector3(0, -0.39f, 0) });
                hip.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.08f, 0.09f) }, MaterialOverride = dark, Position = new Vector3(0, -0.76f, -0.01f) });
                _legs.Add(hip);
            }
        }

        public bool Step(float dt, Vector3 player, PhysicsDirectSpaceState3D space)
        {
            _t += dt;
            var pos = GlobalPosition;
            var flat = new Vector3(pos.X - player.X, 0, pos.Z - player.Z);
            float dist = flat.Length();
            if (dist > Despawn) return false;

            if (dist < Wary && _flee <= 0f)
            {
                _flee = Deer ? 6f : 3.5f;
                _dir = (flat.LengthSquared() > 0.01f ? flat.Normalized() : Vector3.Forward).Rotated(Vector3.Up, _rng.RandfRange(-0.5f, 0.5f));
            }

            bool moving;
            if (_flee > 0f)
            {
                _flee -= dt;
                moving = true;
            }
            else if (_hop > 0f)
            {
                _hop -= dt;
                moving = true;
            }
            else
            {
                moving = false;
                _idle -= dt;
                if (_idle <= 0f)
                {
                    _idle = _rng.RandfRange(1.5f, Deer ? 6f : 4f);
                    _hop = _rng.RandfRange(0.4f, Deer ? 2.5f : 1.2f);
                    _dir = new Vector3(_rng.RandfRange(-1, 1), 0, _rng.RandfRange(-1, 1)).Normalized();
                }
            }

            if (moving && _dir.LengthSquared() > 0.01f)
            {
                var next = pos + _dir * Speed * dt;
                var from = next + Vector3.Up * 2f;
                var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 5f, GameState.LayerWorld | GameState.LayerProps));
                if (hit.Count > 0 && hit["normal"].AsVector3().Y > 0.7f && Mathf.Abs(hit["position"].AsVector3().Y - pos.Y) < 0.8f
                    && float.IsNaN(GameState.Instance.WaterSurfaceAt(hit["position"].AsVector3() + Vector3.Up * 0.3f)))
                {
                    _groundY = hit["position"].AsVector3().Y;
                    GlobalPosition = new Vector3(next.X, _groundY, next.Z);
                }
                else
                {
                    // A wall, a drop or water: turn aside.
                    _dir = _dir.Rotated(Vector3.Up, _rng.RandfRange(1.2f, 2.4f) * (_rng.Randf() < 0.5f ? -1f : 1f));
                }
                Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(-_dir.X, -_dir.Z), Mathf.Min(1f, dt * 8f)), 0);
            }

            // Animation: rabbits bound, deer stride (or bow their head to graze).
            if (!Deer)
            {
                float hopRate = _flee > 0 ? 9f : 6f;
                _body.Position = moving ? new Vector3(0, Mathf.Abs(Mathf.Sin(_t * hopRate)) * (_flee > 0 ? 0.22f : 0.12f), 0) : Vector3.Zero;
                _body.Rotation = new Vector3(moving ? Mathf.Sin(_t * hopRate * 2f) * 0.15f : Mathf.Sin(_t * 9f) * 0.03f, 0, 0);
            }
            else
            {
                float rate = _flee > 0 ? 11f : 4f, swing = moving ? (_flee > 0 ? 0.7f : 0.35f) : 0f;
                for (int i = 0; i < _legs.Count; i++)
                    _legs[i].Rotation = new Vector3(Mathf.Sin(_t * rate + (i % 2 == 0 ? 0 : Mathf.Pi) + (i >= 2 ? 0.6f : 0)) * swing, 0, 0);
                _body.Position = _flee > 0 ? new Vector3(0, Mathf.Abs(Mathf.Sin(_t * rate)) * 0.12f, 0) : Vector3.Zero;
                if (_head != null)
                {
                    float graze = !moving && Mathf.Sin(_t * 0.6f) > 0.1f ? 1f : 0f;
                    _head.GetParent<Node3D>().Rotation = new Vector3(Mathf.Lerp(_head.GetParent<Node3D>().Rotation.X, graze > 0 ? 1.7f : 0.6f, Mathf.Min(1f, dt * 3f)), 0, 0);
                }
            }
            return true;
        }
    }

    /// <summary>A butterfly: two bright wings flapping, wandering on a lazy loop near where it started.</summary>
    private partial class Butterfly : Node3D
    {
        private readonly Node3D _l, _r;
        private Vector3 _home;
        private float _t;
        private readonly float _speed, _phase;
        private bool _placed;

        public Butterfly() { } // (Godot wants one)

        public Butterfly(RandomNumberGenerator rng)
        {
            _speed = rng.RandfRange(0.6f, 1.2f);
            _phase = rng.RandfRange(0, 10f);
            var colors = new[] { new Color(1f, 0.85f, 0.3f), new Color(0.95f, 0.55f, 0.7f), new Color(0.5f, 0.7f, 1f), new Color(1f, 0.6f, 0.25f), new Color(0.95f, 0.95f, 0.92f) };
            var wing = new StandardMaterial3D { AlbedoColor = colors[rng.RandiRange(0, colors.Length - 1)], CullMode = BaseMaterial3D.CullModeEnum.Disabled, DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon };
            _l = new Node3D();
            _r = new Node3D();
            AddChild(_l);
            AddChild(_r);
            _l.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.09f, 0.08f) }, MaterialOverride = wing, Position = new Vector3(-0.045f, 0, 0), Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0) });
            _r.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.09f, 0.08f) }, MaterialOverride = wing, Position = new Vector3(0.045f, 0, 0), Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0) });
        }

        public bool Step(float dt, Vector3 player, bool day)
        {
            if (!_placed) { _home = GlobalPosition; _placed = true; }
            _t += dt;
            if (!day || GlobalPosition.DistanceTo(player) > 35f) return false;
            float a = (_t + _phase) * _speed;
            GlobalPosition = _home + new Vector3(Mathf.Sin(a) * 2.2f + Mathf.Sin(a * 2.3f) * 0.6f, Mathf.Sin(a * 3.1f) * 0.35f, Mathf.Cos(a * 0.8f) * 2.2f);
            Rotation = new Vector3(0, -a * 0.8f, 0);
            float flap = Mathf.Sin(_t * 26f) * 1.1f;
            _l.Rotation = new Vector3(0, 0, flap);
            _r.Rotation = new Vector3(0, 0, -flap);
            return true;
        }
    }
}
