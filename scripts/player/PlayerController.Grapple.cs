using Godot;

namespace KatrinasGame;

/// <summary>The grapple hook (GDD §11, step 2): with one in your pack, aim at a marked iron anchor
/// ring (HomeCliffs.Anchors) within reach and press Use to throw the hook and be reeled to it.
/// A rim ring hauls you up over the edge; a ring on a climbing face leaves you hanging on the rock.
/// Jump lets go mid-zip (you can open the glider straight away). Shoves from goats and the
/// Queen's wings knock you off a face, a zip or a glider. All local movement, like the glider;
/// other screens see the rope from the anchor index sent with the movement mode.</summary>
public partial class PlayerController
{
    public const float GrappleRange = 38f, GrappleSpeed = 19f, GrappleCost = 12f;
    /// <summary>How close to the crosshair an anchor must be (cosine of the angle).</summary>
    private const float GrappleAim = 0.985f;
    /// <summary>The hook's flight before the line goes taut.</summary>
    private const float HookFlight = 0.18f;

    public bool HasGrapple => Pack.Count("grapple_hook") > 0;
    public bool Grappling => Mode == MoveMode.Grapple;
    /// <summary>The anchor being reeled to (-1 none).</summary>
    public int GrappleAnchor { get; private set; } = -1;

    /// <summary>What goes over the network as the movement mode: the mode, and the anchor above it.</summary>
    public int NetMode => (int)Mode | ((Grappling ? GrappleAnchor + 1 : 0) << 3);

    private float _grappleTime, _grappleStuck, _shoveTime;
    private Vector3 _lastGrapplePos;
    private MeshInstance3D _rope, _hook;

    /// <summary>Local player: the anchor ring under the crosshair that the hook can reach (-1 none).</summary>
    public int FindAnchor()
    {
        if (!HasGrapple || Swimming || Grappling || Downed || Sleeping || Health.IsDead) return -1;
        var cam = Rig.Camera;
        var from = cam.GlobalPosition;
        var fwd = -cam.GlobalBasis.Z;
        var chest = GlobalPosition + Vector3.Up * 1.3f;
        var space = GetWorld3D().DirectSpaceState;
        int best = -1;
        float bestDot = GrappleAim;
        for (int i = 0; i < HomeCliffs.Anchors.Length; i++)
        {
            var ring = HomeCliffs.Anchors[i].Ring;
            float d = chest.DistanceTo(ring);
            if (d > GrappleRange || d < 2.5f) continue;
            float dot = (ring - from).Normalized().Dot(fwd);
            if (dot <= bestDot) continue;
            // A clear line to it (the rock it's bolted into doesn't count).
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(chest, ring, GameState.LayerWorld,
                new Godot.Collections.Array<Rid> { GetRid() }));
            if (hit.Count > 0 && hit["position"].AsVector3().DistanceTo(ring) > 1.2f) continue;
            best = i;
            bestDot = dot;
        }
        return best;
    }

    /// <summary>Local player: throw the hook at an anchor and start reeling in.</summary>
    public bool StartGrapple(int anchor)
    {
        if (anchor < 0 || anchor >= HomeCliffs.Anchors.Length || !HasGrapple || Swimming || Downed || Sleeping || Health.IsDead) return false;
        if (!TrySpendStamina(GrappleCost)) { Hud.Toast("Too tired to throw the hook."); return false; }
        if (Combat.GuardUp) Combat.DropGuard();
        GrappleAnchor = anchor;
        _grappleTime = 0f;
        _grappleStuck = 0f;
        _lastGrapplePos = GlobalPosition;
        _dodgeTime = 0;
        _dashTime = 0;
        _mantleTime = 0;
        SetMode(MoveMode.Grapple);
        Sfx.At("dodge", GlobalPosition + Vector3.Up * 1.4f, 0f, 0.2f);
        Journal.Fact("grappled");
        return true;
    }

    /// <summary>Local player on the line: reeled toward the ring; over the rim or onto the face at the end.</summary>
    private Vector3 GrappleStep(float dt, bool blocked)
    {
        _grappleTime += dt;
        _airVelocityY = 0;
        _regenDelay = 0.6f;
        if (GrappleAnchor < 0 || GrappleAnchor >= HomeCliffs.Anchors.Length) { SetMode(MoveMode.Normal); return Velocity; }
        var a = HomeCliffs.Anchors[GrappleAnchor];
        var hang = a.Hang;

        // Let go: a little hop, and the glider is yours to open.
        if (!blocked && _grappleTime > HookFlight && Input.IsActionJustPressed("jump"))
        {
            EndGrapple();
            _airTime = 1f;
            return Velocity * 0.6f + Vector3.Up * 4f;
        }
        // The hook is still flying: you hang where you were.
        if (_grappleTime < HookFlight) return Velocity * 0.5f;

        var to = hang - GlobalPosition;
        float dist = to.Length();
        // Stuck on something, or it's taken too long: the hook slips.
        _grappleStuck = GlobalPosition.DistanceTo(_lastGrapplePos) < GrappleSpeed * 0.15f * dt ? _grappleStuck + dt : 0f;
        if (_grappleTime > HookFlight + 0.2f && _grappleStuck > 0.3f)
        {
            EndGrapple();
            Hud.Toast("The line snagged.");
            return Vector3.Zero;
        }
        _lastGrapplePos = GlobalPosition;
        if (dist < 0.7f || _grappleTime > 5f)
        {
            EndGrapple();
            if (a.OntoFace && TryGrab(-a.Out))
            {
                SetMode(MoveMode.Climb);
                return Vector3.Zero;
            }
            // Up and over the rim.
            _mantleTime = 0.5f;
            _mantleDir = -a.Out;
            return Vector3.Up * 7.5f - a.Out * 2.5f;
        }
        float speed = GrappleSpeed * Mathf.Clamp(dist / 3f, 0.45f, 1f);
        return to / dist * speed;
    }

    private void EndGrapple()
    {
        SetMode(MoveMode.Normal);
        _regrabIn = 0.3f;
    }

    /// <summary>Local player: knocked flying (a goat's butt, the Queen's wings). Lets go of rock,
    /// line and glider; you keep the momentum for a moment.</summary>
    public void Shove(Vector3 velocity)
    {
        if (Health.IsDead || Downed) return;
        if (Mode is MoveMode.Climb or MoveMode.Grapple or MoveMode.Glide) SetMode(MoveMode.Normal);
        _regrabIn = 0.8f;
        _mantleTime = 0;
        _shoveTime = 0.45f;
        Velocity = velocity;
        _airVelocityY = Mathf.Min(_airVelocityY, 0f);
        Gamepad.Rumble(0.7f, 0.4f, 0.3f);
    }

    /// <summary>Everyone: the line from the climber's hand to the ring, while grappling.</summary>
    private void UpdateRope()
    {
        if (Net.Instance.IsDedicated) return;
        bool on = Grappling && GrappleAnchor >= 0 && GrappleAnchor < HomeCliffs.Anchors.Length;
        if (!on)
        {
            if (_rope != null) { _rope.Visible = false; _hook.Visible = false; }
            return;
        }
        if (_rope == null)
        {
            var line = StyleMaterials.Toon(new Color(0.55f, 0.42f, 0.28f), 0.2f);
            _rope = new MeshInstance3D { Name = "GrappleRope", Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 1f, RadialSegments = 5 }, MaterialOverride = line, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _hook = new MeshInstance3D { Name = "GrappleHook", Mesh = new TorusMesh { InnerRadius = 0.08f, OuterRadius = 0.16f, Rings = 8, RingSegments = 6 }, MaterialOverride = StyleMaterials.Toon(new Color(0.5f, 0.52f, 0.56f), 0.5f), TopLevel = true };
            AddChild(_rope);
            AddChild(_hook);
        }
        var ring = HomeCliffs.Anchors[GrappleAnchor].Ring;
        var hand = GlobalPosition + Vector3.Up * 1.45f;
        // The hook flies out first.
        float flight = IsLocal ? Mathf.Clamp(_grappleTime / HookFlight, 0f, 1f) : 1f;
        var end = hand.Lerp(ring, flight);
        var d = end - hand;
        float len = Mathf.Max(d.Length(), 0.05f);
        var y = d / len;
        var x = Mathf.Abs(y.Dot(Vector3.Up)) > 0.95f ? y.Cross(Vector3.Right).Normalized() : y.Cross(Vector3.Up).Normalized();
        var z = x.Cross(y);
        _rope.GlobalTransform = new Transform3D(new Basis(x, y * len, z), (hand + end) / 2f);
        _hook.GlobalPosition = end;
        _rope.Visible = true;
        _hook.Visible = true;
    }
}
