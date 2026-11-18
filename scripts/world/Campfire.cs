using Godot;

namespace KatrinasGame;

/// <summary>Flickering light and a cooking station: rest here to heal, or cook (bread, roast
/// turnips) from the shared stockpile.</summary>
public partial class Campfire : CraftingStation, IServerInteractable
{
    public Campfire()
    {
        StationId = "campfire";
        StationName = "Campfire";
    }

    [Export] public float HealAmount { get; set; } = 40f;

    private OmniLight3D _light;
    private Node3D _flame;
    private float _t;

    public override string InteractPrompt => "Rest or cook at the fire";

    public override void _Ready()
    {
        _light = GetNodeOrNull<OmniLight3D>("Light");
        _flame = GetNodeOrNull<Node3D>("Flame");
        _t = (float)GD.RandRange(0, 100);
        Sfx.Loop(this, "amb_fire", new Vector3(0, 0.4f, 0), -4f, 16f, 2f);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float flicker = 0.85f + 0.15f * Mathf.Sin(_t * 13f) * Mathf.Sin(_t * 7.3f + 1.1f);
        if (_light != null) _light.LightEnergy = 2.2f * flicker;
        if (_flame != null) _flame.Scale = new Vector3(1, 0.85f + 0.3f * flicker, 1);
    }

    /// <summary>Local player: ask the server to rest here.</summary>
    public void Rest() => Net.Instance.RequestInteract(this);

    public void ServerInteract(PlayerController player)
    {
        Net.Instance.ServerRestore(player, HealAmount);
        Net.Instance.ToastTo(player.PeerId, "You rest by the fire and feel restored.");
    }
}
