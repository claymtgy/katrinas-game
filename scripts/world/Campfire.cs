using Godot;

namespace KatrinasGame;

/// <summary>Flickering light and a cooking station: rest here to heal, or cook (bread, roast
/// turnips) from the shared stockpile. Wolves won't come within <see cref="WolfWardRadius"/> of a
/// fire (or the hearth, which is one), so on a fresh character a fire is how you get through the night.</summary>
public partial class Campfire : CraftingStation, IServerInteractable
{
    public Campfire()
    {
        StationId = "campfire";
        StationName = "Campfire";
    }

    [Export] public float HealAmount { get; set; } = 40f;

    /// <summary>Wolves keep this far from any fire (GDD §9).</summary>
    public const float WolfWardRadius = 7f;
    private static readonly System.Collections.Generic.List<Campfire> Fires = new();

    public override void _EnterTree() { if (!Fires.Contains(this)) Fires.Add(this); }
    public override void _ExitTree() => Fires.Remove(this);

    /// <summary>The fire whose ward covers this point (flat distance), or null.</summary>
    public static Campfire WardAt(Vector3 pos, float extra = 0f)
    {
        foreach (var f in Fires)
        {
            if (!IsInstanceValid(f) || !f.IsInsideTree()) continue;
            var d = f.GlobalPosition - pos;
            if (new Vector2(d.X, d.Z).Length() < WolfWardRadius + extra) return f;
        }
        return null;
    }

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
