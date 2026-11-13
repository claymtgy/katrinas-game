using Godot;

namespace KatrinasGame;

/// <summary>Flickering light; resting restores health and stamina.</summary>
public partial class Campfire : StaticBody3D, IInteractable
{
    [Export] public float HealAmount { get; set; } = 40f;

    private OmniLight3D _light;
    private Node3D _flame;
    private float _t;

    public string InteractPrompt => "Rest by the fire";

    public override void _Ready()
    {
        _light = GetNodeOrNull<OmniLight3D>("Light");
        _flame = GetNodeOrNull<Node3D>("Flame");
        _t = (float)GD.RandRange(0, 100);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float flicker = 0.85f + 0.15f * Mathf.Sin(_t * 13f) * Mathf.Sin(_t * 7.3f + 1.1f);
        if (_light != null) _light.LightEnergy = 2.2f * flicker;
        if (_flame != null) _flame.Scale = new Vector3(1, 0.85f + 0.3f * flicker, 1);
    }

    public void Interact(PlayerController player)
    {
        player.Health.Heal(HealAmount);
        player.RestoreStamina();
        Hud.Toast("You rest by the fire and feel restored.");
    }
}
