using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>An animated KayKit character for NPCs and humanoid enemies: shows only the listed
/// props, fits a height, optionally tints, and plays loops / one-shots by name.</summary>
public partial class HumanoidModel : Node3D
{
    private static readonly string[] Loops =
        { "Idle", "Unarmed_Idle", "Walking_A", "Running_A", "Blocking", "Sit_Floor_Idle", "Sit_Chair_Idle", "2H_Melee_Idle", "Lie_Idle" };

    private Node3D _model;
    private AnimationPlayer _anim;
    private string _current = "";
    private float _oneShot;
    private StandardMaterial3D _overlay;
    private Color _tint;

    /// <param name="hideProps">Mesh nodes to hide (weapons, helmets...).</param>
    /// <param name="tint">Overlay colour (alpha = strength), or transparent for none.</param>
    public static HumanoidModel Create(string path, float height, string[] hideProps, Color tint)
    {
        var h = new HumanoidModel { Name = "Model" };
        h._model = Models.Instance(path);
        h._model.RotationDegrees = new Vector3(0, 180, 0); // glTF faces +Z, we face -Z
        h.AddChild(h._model);
        foreach (var prop in hideProps)
            if (h._model.FindChild(prop, true, false) is Node3D n) n.Visible = false;
        StyleMaterials.ApplyCharacter(h._model);
        h._tint = tint;
        if (tint.A > 0)
        {
            h._overlay = new StandardMaterial3D
            {
                AlbedoColor = tint,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };
            foreach (var mi in h._model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
                mi.MaterialOverlay = h._overlay;
        }
        h._anim = h._model.FindChildren("*", "AnimationPlayer", true, false).FirstOrDefault() as AnimationPlayer;
        if (h._anim != null)
            foreach (var name in Loops)
                if (h._anim.HasAnimation(name)) h._anim.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
        h._height = height;
        return h;
    }

    private float _height;

    public override void _Ready()
    {
        Models.FitHeight(_model, _height);
        // Feet on the ground.
        float minY = float.MaxValue;
        foreach (var mi in _model.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
            if (mi.IsVisibleInTree()) minY = Mathf.Min(minY, (mi.GlobalTransform * mi.GetAabb()).Position.Y);
        if (minY < float.MaxValue) _model.Position = new Vector3(0, GlobalPosition.Y - minY, 0);
        Loop("Idle", 0);
    }

    public override void _Process(double delta)
    {
        if (_oneShot > 0) _oneShot -= (float)delta;
    }

    public bool Busy => _oneShot > 0;

    /// <summary>Play a looping animation unless a one-shot is still running.</summary>
    public void Loop(string name, double blend = 0.2, float speed = 1f)
    {
        if (_anim == null || _oneShot > 0 || !_anim.HasAnimation(name)) return;
        if (_current == name && _anim.IsPlaying()) { _anim.SpeedScale = speed; return; }
        _current = name;
        _anim.Play(name, blend);
        _anim.SpeedScale = speed;
    }

    /// <summary>Play an animation once and hold it until it ends (hold = keep the last frame).</summary>
    public void OneShot(string name, float speed = 1f, bool hold = false)
    {
        if (_anim == null || !_anim.HasAnimation(name)) return;
        _current = name;
        _anim.Play(name, 0.08);
        _anim.SpeedScale = speed;
        _oneShot = hold ? float.MaxValue : (float)_anim.GetAnimation(name).Length / speed;
    }

    public void Flash(Color color)
    {
        if (_overlay == null) return;
        var t = CreateTween();
        t.TweenProperty(_overlay, "albedo_color", color, 0.05);
        t.TweenProperty(_overlay, "albedo_color", _tint, 0.3);
    }
}
