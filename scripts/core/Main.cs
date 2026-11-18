using Godot;

namespace KatrinasGame;

/// <summary>Root of the game scene. Once the world is built it hands over to Net, which loads the
/// save (server) or asks to join (client) and spawns players under "Players".</summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        GameState.Instance.PlayersRoot = GetNode<Node3D>("Players");
        if (Net.Instance.IsDedicated) GetNode("Hud").QueueFree(); // no screen, no UI
        Net.Instance.CallDeferred(Net.MethodName.OnWorldReady);
    }
}
