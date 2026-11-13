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

    private static readonly Vector3 FirstPersonHandPos = new(0.36f, -0.34f, -0.75f);
    private static readonly Vector3 ThirdPersonHandPos = new(0.42f, 1.15f, -0.3f);
    private static readonly Vector3 RestRotation = new(Mathf.DegToRad(-20), 0, 0);

    private PlayerController _player;
    private Node3D _hand;
    private MeshInstance3D _weaponMesh;
    private string _shownWeapon = null;
    private float _cooldown;
    private Tween _swingTween;

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();
        _hand = new Node3D { Name = "Hand" };
        _weaponMesh = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.On };
        _hand.AddChild(_weaponMesh);
        _player.Body.AddChild(_hand);
        _player.Rig.ViewChanged += OnViewChanged;
    }

    private void OnViewChanged(bool firstPerson)
    {
        Node3D parent = firstPerson ? _player.Rig.Camera : _player.Body;
        if (_hand.GetParent() != parent) _hand.Reparent(parent, false);
        _hand.Position = firstPerson ? FirstPersonHandPos : ThirdPersonHandPos;
        _hand.Rotation = RestRotation;
    }

    public override void _Process(double delta)
    {
        if (_cooldown > 0) _cooldown -= (float)delta;
        var gs = GameState.Instance;
        if (_shownWeapon != gs.Equipped) RebuildWeaponMesh(gs.Equipped);
    }

    private void RebuildWeaponMesh(string itemId)
    {
        _shownWeapon = itemId;
        var def = ItemDb.Get(itemId);
        if (def == null)
        {
            // Fists: a small blocky glove.
            _weaponMesh.Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.09f, 0.12f) };
            _weaponMesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.6f, 0.5f) };
            _weaponMesh.Position = Vector3.Zero;
            return;
        }
        _weaponMesh.Mesh = new BoxMesh { Size = def.WeaponSize };
        _weaponMesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = def.IconColor, Roughness = 0.8f };
        _weaponMesh.Position = new Vector3(0, 0, -def.WeaponSize.Z * 0.45f);
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
        float windup = heavy ? 0.28f : 0.12f;
        _cooldown = baseCooldown * (heavy ? 1.8f : 1f);

        PlaySwing(windup, heavy);
        GetTree().CreateTimer(windup).Timeout += () => DoHit(damage, reach);
    }

    private void PlaySwing(float windup, bool heavy)
    {
        _swingTween?.Kill();
        var raised = new Vector3(Mathf.DegToRad(heavy ? 70 : 45), Mathf.DegToRad(-15), 0);
        var struck = new Vector3(Mathf.DegToRad(-80), Mathf.DegToRad(20), 0);
        _swingTween = CreateTween();
        _swingTween.TweenProperty(_hand, "rotation", raised, windup * 0.8).SetEase(Tween.EaseType.Out);
        _swingTween.TweenProperty(_hand, "rotation", struck, 0.09).SetTrans(Tween.TransitionType.Expo);
        _swingTween.TweenProperty(_hand, "rotation", RestRotation, 0.25).SetTrans(Tween.TransitionType.Sine);
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
