using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Light (LMB) and heavy (RMB) swings, weapon slots (1-5), and the held-weapon visual.
/// The weapon attaches to the camera in first person and to the body in third person.</summary>
public partial class PlayerCombat : Node
{
    [Export] public float FistDamage { get; set; } = 6f;
    [Export] public float FistReach { get; set; } = 1.5f;
    [Export] public float LightStamina { get; set; } = 10f;
    [Export] public float HeavyStamina { get; set; } = 25f;
    [Export] public float HeavyMultiplier { get; set; } = 2.2f;

    private static readonly Vector3 FirstPersonHandPos = new(0.34f, -0.36f, -0.62f);
    // Weapon visuals point their blade along +Y (KayKit convention), so first person tilts it forward.
    private static readonly Vector3 FirstPersonRest = new(Mathf.DegToRad(-60), Mathf.DegToRad(-8), 0);

    private PlayerController _player;
    private CharacterModel _model;
    private Node3D _hand;
    private Node3D _weapon;
    private string _shownWeapon = null;
    private float _cooldown;
    private Tween _swingTween;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _model = _player.Body.GetNodeOrNull<CharacterModel>("Mesh");
        _hand = new Node3D { Name = "Hand" };
        _player.Body.AddChild(_hand);
        _player.Rig.ViewChanged += OnViewChanged;
    }

    private bool FirstPerson => _player.Rig.FirstPerson;

    private void OnViewChanged(bool firstPerson)
    {
        Node3D parent = firstPerson ? _player.Rig.Camera : (Node3D)_model?.RightHandSlot ?? _player.Body;
        if (_hand.GetParent() != parent) _hand.Reparent(parent, false);
        _swingTween?.Kill();
        if (firstPerson)
        {
            _hand.Position = FirstPersonHandPos;
            _hand.Rotation = FirstPersonRest;
            _hand.Scale = Vector3.One * (_model?.ModelScale ?? 1f);
        }
        else
        {
            _hand.Transform = Transform3D.Identity; // bone slot already carries the model's scale
        }
        _shownWeapon = null; // rebuild so fists show/hide correctly
    }

    public override void _Process(double delta)
    {
        if (_cooldown > 0) _cooldown -= (float)delta;
        var gs = GameState.Instance;
        if (_shownWeapon != gs.Equipped) RebuildWeapon(gs.Equipped);
    }

    private void RebuildWeapon(string itemId)
    {
        _shownWeapon = itemId;
        _weapon?.QueueFree();
        _weapon = null;
        var def = ItemDb.Get(itemId);

        if (def == null)
        {
            if (!FirstPerson) return; // the character model already has hands
            _weapon = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.16f, 0.14f) },
                MaterialOverride = StyleMaterials.Toon(new Color(0.93f, 0.72f, 0.6f)),
            };
        }
        else if (itemId.Contains("sword"))
        {
            _weapon = Models.Instance(Models.Sword, style: false);
            foreach (var n in _weapon.FindChildren("*", "MeshInstance3D", true, false))
                ((MeshInstance3D)n).MaterialOverride = StyleMaterials.Toon(def.IconColor, 0.3f);
        }
        else
        {
            var size = def.WeaponSize; // (width, thickness, length) -> blade along +Y
            _weapon = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size.X, size.Z, size.Y) },
                MaterialOverride = StyleMaterials.Toon(def.IconColor, 0.3f),
                Position = new Vector3(0, size.Z * 0.4f, 0),
            };
        }
        _hand.AddChild(_weapon);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        var gs = GameState.Instance;
        if (gs.UiOpen || Input.MouseMode != Input.MouseModeEnum.Captured) return;

        for (int i = 1; i <= 5; i++)
        {
            if (!e.IsActionPressed($"slot_{i}")) continue;
            var slots = gs.WeaponSlots();
            if (i - 1 < slots.Count)
            {
                gs.Equipped = slots[i - 1];
                Hud.Toast($"Equipped {(gs.Equipped == "" ? "Fists" : ItemDb.NameOf(gs.Equipped))}");
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_player.Build.Active) return;
        if (e.IsActionPressed("attack")) TryAttack(heavy: false);
        else if (e.IsActionPressed("heavy_attack")) TryAttack(heavy: true);
    }

    public void TryAttack(bool heavy)
    {
        if (_cooldown > 0) return;
        if (!_player.TrySpendStamina(heavy ? HeavyStamina : LightStamina))
        {
            Hud.Toast("Too tired to swing!");
            return;
        }

        var def = ItemDb.Get(GameState.Instance.Equipped);
        float damage = (def?.Damage ?? FistDamage) * (heavy ? HeavyMultiplier : 1f);
        float reach = def?.Reach ?? FistReach;
        float baseCooldown = def?.AttackCooldown ?? 0.4f;
        float windup = heavy ? 0.38f : 0.2f;
        _cooldown = baseCooldown * (heavy ? 1.8f : 1f);

        if (FirstPerson) PlaySwing(windup, heavy);
        else _model?.PlayAttack(heavy, def != null);
        GetTree().CreateTimer(windup).Timeout += () => DoHit(damage, reach);
    }

    private void PlaySwing(float windup, bool heavy)
    {
        _swingTween?.Kill();
        var raised = new Vector3(Mathf.DegToRad(heavy ? 25 : 10), Mathf.DegToRad(-25), Mathf.DegToRad(-20));
        var struck = new Vector3(Mathf.DegToRad(-125), Mathf.DegToRad(25), Mathf.DegToRad(15));
        _swingTween = CreateTween();
        _swingTween.TweenProperty(_hand, "rotation", raised, windup * 0.8).SetEase(Tween.EaseType.Out);
        _swingTween.TweenProperty(_hand, "rotation", struck, 0.1).SetTrans(Tween.TransitionType.Expo);
        _swingTween.TweenProperty(_hand, "rotation", FirstPersonRest, 0.28).SetTrans(Tween.TransitionType.Sine);
    }

    private void DoHit(float damage, float reach)
    {
        if (!IsInstanceValid(_player)) return;

        // Sphere in front of the chest, along the camera's aim (works in both views).
        var aim = -_player.Rig.Camera.GlobalBasis.Z;
        var center = _player.GlobalPosition + Vector3.Up * 1.2f + aim * (reach * 0.65f);
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = new SphereShape3D { Radius = 0.45f + reach * 0.3f },
            Transform = new Transform3D(Basis.Identity, center),
            CollisionMask = GameState.LayerProps,
            Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() },
        };

        var hitThisSwing = new HashSet<ulong>();
        foreach (var hit in _player.GetWorld3D().DirectSpaceState.IntersectShape(query, 16))
        {
            var collider = hit["collider"].AsGodotObject();
            if (collider == null || !hitThisSwing.Add(collider.GetInstanceId())) continue;
            var health = Health.FindOn(collider);
            if (health == null || health.IsDead) continue;
            health.TakeDamage(damage, _player);
            Hud.Instance?.FlashHitMarker();
        }
    }
}
