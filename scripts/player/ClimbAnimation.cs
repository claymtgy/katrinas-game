using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The KayKit packs have no climbing clip, so this builds two from the skeleton at run time:
/// "Climb" (hand over hand, opposite foot up: one cycle a second) and "Climb_Hang" (holding on,
/// breathing). Limbs are aimed in the model's own space (up, forward into the wall, out to the side)
/// rather than as rotations of the rest pose, so it works on any KayKit hero rig.</summary>
public static class ClimbAnimation
{
    public const string Climb = "Climb", Hang = "Climb_Hang";

    private static readonly string[] Bones =
    {
        "hips", "spine", "chest", "head",
        "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "upperarm.r", "lowerarm.r", "wrist.r", "hand.r",
        "upperleg.l", "lowerleg.l", "foot.l", "toes.l", "upperleg.r", "lowerleg.r", "foot.r", "toes.r",
    };

    /// <summary>Adds the clips to the player's library if they aren't there yet.</summary>
    public static void Ensure(AnimationPlayer anim, Node model)
    {
        if (anim == null || anim.HasAnimation(Climb) || !anim.HasAnimation("Idle")) return;
        if (model.FindChild("Skeleton3D", true, false) is not Skeleton3D sk) return;
        var rig = Rig.Read(sk, anim.GetAnimation("Idle"));
        if (rig == null) { GD.PushWarning("[ClimbAnimation] Unexpected rig: no climbing clip"); return; }
        var lib = anim.GetAnimationLibrary(anim.FindAnimationLibrary(anim.GetAnimation("Idle")));
        if (lib == null) return;

        // Climb: phase s runs -1..1..-1; left hand high (and right foot high) at s = 1.
        lib.AddAnimation(Climb, rig.Build(1.0f, 16, t => Mathf.Sin(Mathf.Tau * t), 1f));
        // Hang: a fixed grip, with a slight sway as you breathe.
        lib.AddAnimation(Hang, rig.Build(2.4f, 8, t => 0.35f + 0.08f * Mathf.Sin(Mathf.Tau * t), 0.25f));
    }

    private sealed class Rig
    {
        private Skeleton3D _sk;
        private string _prefix;
        private Vector3 _up, _fwd, _left;
        private readonly Dictionary<string, int> _idx = new();

        public static Rig Read(Skeleton3D sk, Animation reference)
        {
            var rig = new Rig { _sk = sk };
            foreach (var b in Bones)
            {
                int i = sk.FindBone(b);
                if (i < 0) return null;
                rig._idx[b] = i;
            }
            // Track paths look like "Rig/Skeleton3D:upperarm.l": borrow the prefix from the idle clip.
            for (int t = 0; t < reference.GetTrackCount(); t++)
            {
                string path = reference.TrackGetPath(t).ToString();
                if (path.EndsWith(":upperarm.l")) { rig._prefix = path[..^"upperarm.l".Length]; break; }
            }
            if (rig._prefix == null) return null;
            rig._up = Vector3.Up;
            rig._left = (rig.Rest("upperarm.l").Origin - rig.Rest("upperarm.r").Origin) with { Y = 0 };
            rig._left = rig._left.Normalized();
            rig._fwd = (rig.Rest("toes.l").Origin - rig.Rest("foot.l").Origin) with { Y = 0 };
            if (rig._fwd.LengthSquared() < 1e-6f) rig._fwd = rig._left.Cross(Vector3.Up);
            rig._fwd = rig._fwd.Normalized();
            return rig;
        }

        private Transform3D Rest(string bone) => _sk.GetBoneGlobalRest(_idx[bone]);

        /// <summary>Which bone's head a limb bone points at (its length runs from here to there).</summary>
        private static string Tip(string bone) => bone switch
        {
            "upperarm.l" => "lowerarm.l", "lowerarm.l" => "wrist.l",
            "upperarm.r" => "lowerarm.r", "lowerarm.r" => "wrist.r",
            "upperleg.l" => "lowerleg.l", "lowerleg.l" => "foot.l",
            "upperleg.r" => "lowerleg.r", "lowerleg.r" => "foot.r",
            _ => null,
        };

        /// <summary>Where each limb points (model space) at phase s: reach up, toes into the wall.</summary>
        private Dictionary<string, Vector3> Aims(float s)
        {
            var aims = new Dictionary<string, Vector3>();
            foreach (int side in new[] { 1, -1 })
            {
                string sfx = side > 0 ? ".l" : ".r";
                var out_ = _left * side;
                float reach = side * s;                 // 1 = this hand at the top of its reach
                float knee = Mathf.Clamp(-side * s, -1f, 1f) * 0.5f + 0.5f; // 1 = this knee drawn up (opposite hand high)
                aims["upperarm" + sfx] = (_up * (0.45f + 0.35f * reach) + out_ * 0.85f + _fwd * 0.25f).Normalized(); // wide: hands clear of the big head
                aims["lowerarm" + sfx] = (_up * (0.75f + 0.3f * reach) + out_ * (0.3f - 0.1f * reach) + _fwd * 0.35f).Normalized();
                aims["upperleg" + sfx] = (-_up * (1f - 0.6f * knee) + _fwd * (0.2f + 0.75f * knee) + out_ * 0.14f).Normalized();
                aims["lowerleg" + sfx] = (-_up + _fwd * (0.12f - 0.1f * knee) + out_ * 0.04f).Normalized();
            }
            return aims;
        }

        public Animation Build(float length, int keys, System.Func<float, float> phase, float bob)
        {
            var a = new Animation { Length = length, LoopMode = Animation.LoopModeEnum.Linear };
            var tracks = new Dictionary<string, int>();
            foreach (var b in Bones)
            {
                int t = a.AddTrack(Animation.TrackType.Rotation3D);
                a.TrackSetPath(t, _prefix + b);
                tracks[b] = t;
            }
            int hipsPos = a.AddTrack(Animation.TrackType.Position3D);
            a.TrackSetPath(hipsPos, _prefix + "hips");
            var hipsRest = _sk.GetBoneRest(_idx["hips"]).Origin;

            for (int k = 0; k <= keys; k++)
            {
                float time = length * k / keys;
                float s = phase(k / (float)keys);
                var aims = Aims(s);
                // Forward kinematics in model space: aimed bones swing from their rest direction onto
                // the aim; the rest follow their parent. Then back to parent-relative rotations.
                var global = new Dictionary<string, Basis>();
                foreach (var b in Bones)
                {
                    int i = _idx[b];
                    int parent = _sk.GetBoneParent(i);
                    string pName = parent >= 0 ? _sk.GetBoneName(parent) : null;
                    var pRest = parent >= 0 ? _sk.GetBoneGlobalRest(parent).Basis : Basis.Identity;
                    var pNow = pName != null && global.TryGetValue(pName, out var g) ? g : pRest;
                    var restLocal = _sk.GetBoneRest(i).Basis;
                    Basis now = pNow * restLocal;
                    if (b == "chest") now = new Basis(_left, Mathf.DegToRad(8f)) * now; // lean into the rock
                    if (b == "head") now = new Basis(_left, Mathf.DegToRad(-14f)) * now; // look up the face
                    if (Tip(b) is { } tip && aims.TryGetValue(b, out var aim))
                    {
                        var restDir = (Rest(tip).Origin - Rest(b).Origin).Normalized();
                        var curDir = (pNow * pRest.Inverse() * restDir).Normalized();
                        now = new Basis(Arc(curDir, aim)) * now;
                    }
                    global[b] = now.Orthonormalized();
                    var local = (pNow.Inverse() * global[b]).Orthonormalized();
                    a.RotationTrackInsertKey(tracks[b], time, local.GetRotationQuaternion());
                }
                // A little heave with each pull, and a sway toward the reaching hand.
                float sway = bob * 0.04f * s, heave = bob * 0.03f * Mathf.Abs(s);
                a.PositionTrackInsertKey(hipsPos, time, hipsRest + Local(_left * sway + _up * heave));
            }
            return a;
        }

        /// <summary>A model-space offset in the hips' parent space (the root bone is usually unrotated).</summary>
        private Vector3 Local(Vector3 offset)
        {
            int parent = _sk.GetBoneParent(_idx["hips"]);
            var basis = parent >= 0 ? _sk.GetBoneGlobalRest(parent).Basis : Basis.Identity;
            return basis.Inverse() * offset;
        }

        private static Quaternion Arc(Vector3 from, Vector3 to)
        {
            float d = Mathf.Clamp(from.Dot(to), -1f, 1f);
            if (d > 0.9999f) return Quaternion.Identity;
            var axis = from.Cross(to);
            if (axis.LengthSquared() < 1e-8f) axis = from.Cross(Vector3.Right).LengthSquared() > 1e-4f ? from.Cross(Vector3.Right) : from.Cross(Vector3.Forward);
            return new Quaternion(axis.Normalized(), Mathf.Acos(d));
        }
    }
}
