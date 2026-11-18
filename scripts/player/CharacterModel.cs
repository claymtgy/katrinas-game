using Godot;

namespace KatrinasGame;

/// <summary>Visual body of the player: an animated KayKit Rogue or Knight, painted and sized by
/// the player's <see cref="Appearance"/> (rebuilt live when it changes). Picks idle / walk / run /
/// jump from the controller's motion and plays one-shot attack animations on request.
/// Exposes the right-hand bone slot so weapons can be held in third person.</summary>
public partial class CharacterModel : Node3D
{
    [Export] public float TargetHeight { get; set; } = 1.8f;

    public Node3D RightHandSlot { get; private set; }
    /// <summary>The left hand (the archer's bow); null if the rig has no slot there.</summary>
    public Node3D LeftHandSlot { get; private set; }
    public float ModelScale => _model?.Scale.X ?? 1f;

    private PlayerController _player;
    private AnimationPlayer _anim;
    private Node3D _model;
    private float _oneShotTimer;
    private float _stride;
    private string _current = "";

    private static readonly string[] LoopAnims =
        { "Idle", "Unarmed_Idle", "Walking_A", "Running_A", "Jump_Idle", "Blocking", "Lie_Idle" };

    /// <summary>The look the body was last built from.</summary>
    public Appearance Look { get; private set; }

    /// <summary>Raised after the body is rebuilt (new look): the hand slot is a new node.</summary>
    public event System.Action Rebuilt;

    public override void _Ready()
    {
        _player = GetParent().GetParent<PlayerController>(); // Player/Body/Mesh
        SetLook(_player.Look);
    }

    /// <summary>(Re)build the body for a look: model, hidden parts, paint, height, animations,
    /// hand slot. Whatever hangs from the old hand slot (the held weapon) is kept.</summary>
    public void SetLook(Appearance look)
    {
        look ??= new Appearance();
        Look = look.Clone();
        var held = new System.Collections.Generic.List<Node>();
        if (RightHandSlot != null)
            foreach (var child in RightHandSlot.GetChildren())
            {
                child.Reparent(this, false);
                held.Add(child);
            }
        if (_model != null)
        {
            RemoveChild(_model);
            _model.QueueFree();
        }
        RightHandSlot = null;
        LeftHandSlot = null;

        _model = BuildBody(Look);
        AddChild(_model);
        FitHeight(_model, TargetHeight * Look.Height, GlobalPosition.Y);

        _anim = FindAnimationPlayer(_model);
        _current = "";
        _oneShotTimer = 0;
        if (_anim != null)
        {
            foreach (var name in LoopAnims)
                if (_anim.HasAnimation(name)) _anim.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            ClimbAnimation.Ensure(_anim, _model);
            Play("Idle", 0);
        }
        else GD.PushWarning("[CharacterModel] No AnimationPlayer in hero model");

        if (_model.FindChild("Skeleton3D", true, false) is Skeleton3D skeleton && skeleton.FindBone("handslot.r") >= 0)
        {
            var attach = new BoneAttachment3D { Name = "RightHandSlot", BoneName = "handslot.r" };
            skeleton.AddChild(attach);
            RightHandSlot = attach;
            foreach (var child in held) child.Reparent(attach, false);
            if (skeleton.FindBone("handslot.l") >= 0)
            {
                LeftHandSlot = new BoneAttachment3D { Name = "LeftHandSlot", BoneName = "handslot.l" };
                skeleton.AddChild(LeftHandSlot);
            }
        }
        DressGear(_model, Look);
        if (_fade > 0) SetFade(_fade);
        Rebuilt?.Invoke();
    }

    /// <summary>Worn gear the model has no part for (Gear.Dress): a cap on the Rogue's head, and a
    /// cloak's colour on the cape.</summary>
    public static void DressGear(Node3D model, Appearance look)
    {
        if (look.CapeColor != "" && Color.HtmlIsValid(look.CapeColor))
            foreach (var node in model.FindChildren("*_Cape", "MeshInstance3D", true, false))
            {
                var cape = (MeshInstance3D)node;
                var cloth = StyleMaterials.Toon(Color.FromHtml(look.CapeColor), 0.2f);
                cloth.NextPass = StyleMaterials.Outline;
                for (int i = 0; i < cape.Mesh.GetSurfaceCount(); i++) cape.SetSurfaceOverrideMaterial(i, cloth);
            }
        if (look.HeadGear == "" || !Color.HtmlIsValid(look.HeadGear)) return;
        if (model.FindChild("Skeleton3D", true, false) is not Skeleton3D sk || sk.FindBone("head") < 0) return;
        if (model.FindChild("*_Head", true, false) is not MeshInstance3D headMesh) return;
        // Fit a dome over the top of the head, in the head bone's space (so it follows the head).
        var box = headMesh.GetAabb();
        float r = Mathf.Max(box.Size.X, box.Size.Z) * 0.56f;
        var top = new Vector3(box.GetCenter().X, box.End.Y - box.Size.Y * 0.36f, box.GetCenter().Z);
        var bone = sk.GetBoneGlobalRest(sk.FindBone("head"));
        var attach = new BoneAttachment3D { Name = "HeadGear", BoneName = "head" };
        sk.AddChild(attach);
        var color = Color.FromHtml(look.HeadGear);
        var mat = StyleMaterials.Toon(color, 0.35f);
        mat.NextPass = StyleMaterials.Outline;
        var cap = new Node3D { Transform = new Transform3D(bone.Basis.Inverse(), bone.AffineInverse() * top) };
        attach.AddChild(cap);
        cap.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r, IsHemisphere = true, RadialSegments = 16, Rings = 6 }, MaterialOverride = mat });
        cap.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 1.06f, BottomRadius = r * 1.1f, Height = r * 0.14f, RadialSegments = 16 },
            MaterialOverride = StyleMaterials.Toon(color.Darkened(0.25f), 0.2f), Position = new Vector3(0, r * 0.03f, 0),
        });
    }

    private float _fade;

    /// <summary>See-through amount (0 solid … 1 invisible), e.g. the Rogue's Vanish. Survives rebuilds.</summary>
    public void SetFade(float fade)
    {
        _fade = fade;
        if (_model == null) return;
        foreach (var node in _model.FindChildren("*", "GeometryInstance3D", true, false))
            ((GeometryInstance3D)node).Transparency = fade;
    }

    /// <summary>A styled, painted body for a look (also used by the character creator's preview).
    /// Faces -Z; not yet scaled (see <see cref="FitHeight"/>).</summary>
    public static Node3D BuildBody(Appearance look)
    {
        var model = Models.Instance(look.ModelPath);
        model.Name = look.Body == Appearance.Knight ? "Knight" : "Rogue";
        model.RotationDegrees = new Vector3(0, 180, 0); // glTF faces +Z, we face -Z
        foreach (var part in look.HiddenParts())
            if (model.FindChild(part, true, false) is Node3D n) n.Visible = false;
        StyleMaterials.ApplyCharacter(model, look);
        return model;
    }

    /// <summary>Scale a body so head-to-toe (not counting helmet or cape) is <paramref name="height"/>
    /// metres, feet at the parent's origin. Call once it's in the tree.</summary>
    public static void FitHeight(Node3D model, float height, float groundY)
    {
        model.Scale = Vector3.One;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var node in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            string n = mi.Name;
            if (!(n.EndsWith("_Body") || n.EndsWith("_Head") || n.Contains("_Leg"))) continue;
            var box = mi.GlobalTransform * mi.GetAabb();
            minY = Mathf.Min(minY, box.Position.Y);
            maxY = Mathf.Max(maxY, box.End.Y);
        }
        float h = maxY - minY;
        if (h > 0.1f && h < 100f)
        {
            float s = height / h;
            model.Scale = Vector3.One * s;
            model.Position = new Vector3(0, -(minY - groundY) * s, 0);
        }
    }

    private static AnimationPlayer FindAnimationPlayer(Node n)
    {
        if (n is AnimationPlayer ap) return ap;
        foreach (var c in n.GetChildren())
            if (FindAnimationPlayer(c) is { } found) return found;
        return null;
    }

    /// <summary>A soft footfall every stride while walking or running on the ground.</summary>
    private void Footsteps(float speed, float dt)
    {
        if (!_player.Grounded || speed < 0.8f || _player.Sleeping) { _stride = 0.5f; return; }
        _stride += speed * dt;
        float strideLength = speed > 6f ? 2.1f : 1.35f;
        if (_stride < strideLength) return;
        _stride -= strideLength;
        Sfx.At("step", _player.GlobalPosition, (speed > 6f ? -10f : -14f) + (_player.IsLocal ? 0f : -2f), 0.18f, 20f);
    }

    public bool HasAnimation(string name) => _anim?.HasAnimation(name) ?? false;
    /// <summary>The clip playing now ("" if none).</summary>
    public string CurrentAnimation => _anim?.CurrentAnimation ?? "";

    private void Play(string name, double blend = 0.2, float speed = 1f)
    {
        if (_anim == null || !_anim.HasAnimation(name)) return;
        string key = speed < 0 ? name + "<" : name; // a clip played backwards counts as a different one
        if (_current == key && _anim.IsPlaying()) return;
        _current = key;
        _anim.Play(name, blend, speed);
    }

    /// <summary>Plays a one-shot (attack, interact...) and holds it until done. Returns its length.</summary>
    public float PlayOneShot(string name, float speed = 1f)
    {
        if (_anim == null || !_anim.HasAnimation(name)) return 0f;
        _current = "";
        Play(name, 0.08, speed);
        _oneShotTimer = (float)_anim.GetAnimation(name).Length / speed;
        return _oneShotTimer;
    }

    public void PlayAttack(bool heavy, bool armed)
    {
        string name = armed
            ? (heavy ? "1H_Melee_Attack_Chop" : (GD.Randf() < 0.5f ? "1H_Melee_Attack_Slice_Diagonal" : "1H_Melee_Attack_Slice_Horizontal"))
            : (heavy ? "Unarmed_Melee_Attack_Kick" : (GD.Randf() < 0.5f ? "Unarmed_Melee_Attack_Punch_A" : "Unarmed_Melee_Attack_Punch_B"));
        PlayOneShot(name, heavy ? 1.0f : 1.35f);
    }

    public override void _Process(double delta)
    {
        if (_anim == null || _player == null) return;
        if (_oneShotTimer > 0)
        {
            _oneShotTimer -= (float)delta;
            return;
        }

        var v = _player.Velocity;
        float speed = new Vector2(v.X, v.Z).Length();
        Footsteps(speed, (float)delta);
        bool armed = _player.Equipped != "";
        if (_player.Sleeping && HasAnimation("Lie_Idle")) Play("Lie_Idle", 0.4);
        else if (_player.Climbing)
        {
            // Hand over hand while moving on the face (backwards on the way down), holding on otherwise.
            float climb = v.Length();
            if (climb > 0.4f) Play(ClimbAnimation.Climb, 0.15, Mathf.Clamp(climb / 2.4f, 0.7f, 1.4f) * (v.Y < -0.3f ? -1f : 1f));
            else Play(ClimbAnimation.Hang, 0.25);
        }
        else if (_player.Swimming)
        {
            // No swim clip in the kit: a slow stride reads as kicking under the water, an idle as treading it.
            float stroke = v.Length();
            if (stroke > 0.5f) Play("Walking_A", 0.25, Mathf.Clamp(stroke / 3f, 0.6f, 1.3f));
            else Play("Unarmed_Idle", 0.3);
        }
        else if (_player.GuardShown && HasAnimation("Blocking")) Play("Blocking", 0.12);
        else if (!_player.Grounded) Play("Jump_Idle", 0.15);
        else if (speed > 6f) Play("Running_A", 0.2);
        else if (speed > 0.6f) Play("Walking_A", 0.2, Mathf.Clamp(speed / 3.2f, 0.7f, 1.6f));
        else Play(armed ? "Idle" : "Unarmed_Idle", 0.25);
    }
}
