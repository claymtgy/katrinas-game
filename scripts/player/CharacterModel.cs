using Godot;

namespace KatrinasGame;

/// <summary>Visual body of the player: the animated KayKit Rogue. Picks idle / walk / run /
/// jump from the controller's motion and plays one-shot attack animations on request.
/// Exposes the right-hand bone slot so weapons can be held in third person.</summary>
public partial class CharacterModel : Node3D
{
    [Export] public float TargetHeight { get; set; } = 1.8f;

    public Node3D RightHandSlot { get; private set; }
    public float ModelScale => _model?.Scale.X ?? 1f;

    private PlayerController _player;
    private AnimationPlayer _anim;
    private Node3D _model;
    private float _oneShotTimer;
    private float _stride;
    private string _current = "";

    private static readonly string[] LoopAnims =
        { "Idle", "Unarmed_Idle", "Walking_A", "Running_A", "Jump_Idle", "Blocking", "Lie_Idle" };

    // Rogue comes with props attached to its hands; we show our own weapons instead.
    private static readonly string[] HiddenProps = { "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable" };

    public override void _Ready()
    {
        _player = GetParent().GetParent<PlayerController>(); // Player/Body/Mesh

        _model = Models.Instance(Models.Hero);
        _model.Name = "Rogue";
        _model.RotationDegrees = new Vector3(0, 180, 0); // glTF faces +Z, we face -Z
        AddChild(_model);
        foreach (var prop in HiddenProps)
            if (_model.FindChild(prop, true, false) is Node3D n) n.Visible = false;
        StyleMaterials.ApplyCharacter(_model);

        FitHeight();

        _anim = FindAnimationPlayer(_model);
        if (_anim != null)
        {
            foreach (var name in LoopAnims)
                if (_anim.HasAnimation(name)) _anim.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            Play("Idle", 0);
        }
        else GD.PushWarning("[CharacterModel] No AnimationPlayer in hero model");

        if (_model.FindChild("Skeleton3D", true, false) is Skeleton3D skeleton && skeleton.FindBone("handslot.r") >= 0)
        {
            var attach = new BoneAttachment3D { Name = "RightHandSlot", BoneName = "handslot.r" };
            skeleton.AddChild(attach);
            RightHandSlot = attach;
        }
    }

    private void FitHeight()
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var node in _model.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            if (!mi.IsVisibleInTree()) continue;
            var box = mi.GlobalTransform * mi.GetAabb();
            minY = Mathf.Min(minY, box.Position.Y);
            maxY = Mathf.Max(maxY, box.End.Y);
        }
        float height = maxY - minY;
        if (height > 0.1f && height < 100f)
        {
            float s = TargetHeight / height;
            _model.Scale = Vector3.One * s;
            _model.Position = new Vector3(0, -(minY - GlobalPosition.Y) * s, 0);
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

    private void Play(string name, double blend = 0.2, float speed = 1f)
    {
        if (_anim == null || !_anim.HasAnimation(name)) return;
        if (_current == name && _anim.IsPlaying()) return;
        _current = name;
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
        else if (_player.GuardShown && HasAnimation("Blocking")) Play("Blocking", 0.12);
        else if (!_player.Grounded) Play("Jump_Idle", 0.15);
        else if (speed > 6f) Play("Running_A", 0.2);
        else if (speed > 0.6f) Play("Walking_A", 0.2, Mathf.Clamp(speed / 3.2f, 0.7f, 1.6f));
        else Play(armed ? "Idle" : "Unarmed_Idle", 0.25);
    }
}
