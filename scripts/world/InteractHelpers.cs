using Godot;

namespace KatrinasGame;

// Small interactables. Each Godot script class needs its own file, so these live in separate
// files below; this file only holds shared helpers.
public static class InteractHelpers
{
    /// <summary>Advance the clock to the next dawn (0.27), bumping the day if we pass midnight.</summary>
    public static void SkipToMorning()
    {
        var gs = GameState.Instance;
        if (gs.TimeOfDay > 0.27f) gs.Day++;
        gs.TimeOfDay = 0.27f;
    }
}
