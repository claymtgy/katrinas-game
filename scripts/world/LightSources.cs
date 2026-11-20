using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Where it's light enough to keep cave crawlers off (GDD §11, Hollow Mine): placed
/// torches, campfires and the hearth, the daylight shaft over the Mine Blossom, and any player
/// wearing a Lantern. Every machine can ask (it only reads positions and worn gear), but the
/// crawlers' brains run on the server.</summary>
public static class LightSources
{
    /// <summary>How far a torch's light keeps crawlers off.</summary>
    public const float TorchRadius = 6.5f;
    /// <summary>A lantern's circle is small: crawlers wait just past it and lunge in.</summary>
    public const float LanternRadius = 3.5f;
    /// <summary>The perk on the Lantern (ItemDef.Perk).</summary>
    public const string PerkLight = "light";

    private static readonly List<(Node3D Node, float Radius)> Fixed = new();

    /// <summary>A light that stays put (a torch, the mine shaft).</summary>
    public static void Register(Node3D node, float radius)
    {
        Unregister(node);
        Fixed.Add((node, radius));
    }

    public static void Unregister(Node3D node) => Fixed.RemoveAll(l => l.Node == node);

    public static bool Wears(PlayerController p) => p != null && Gear.HasPerk(p, PerkLight);

    /// <summary>Every light right now: its centre and radius.</summary>
    public static IEnumerable<(Vector3 Center, float Radius)> All()
    {
        for (int i = Fixed.Count - 1; i >= 0; i--)
        {
            var (n, r) = Fixed[i];
            if (!GodotObject.IsInstanceValid(n) || !n.IsInsideTree()) { Fixed.RemoveAt(i); continue; }
            yield return (n.GlobalPosition, r);
        }
        foreach (var f in Campfire.All)
            if (GodotObject.IsInstanceValid(f) && f.IsInsideTree()) yield return (f.GlobalPosition, Campfire.WolfWardRadius);
        var gs = GameState.Instance;
        if (gs == null) yield break;
        foreach (var p in gs.Players.Values)
            if (GodotObject.IsInstanceValid(p) && p.IsInsideTree() && !p.Health.IsDead && Wears(p))
                yield return (p.GlobalPosition, LanternRadius);
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>True when some light covers this point (plus <paramref name="margin"/> metres).</summary>
    public static bool Lit(Vector3 pos, float margin = 0f)
    {
        foreach (var (c, r) in All())
            if (Flat(pos, c) < r + margin && Mathf.Abs(pos.Y - c.Y) < 4f) return true;
        return false;
    }

    /// <summary>Which way to go to get out of the light (zero when outside it and its margin).
    /// Stronger the deeper in you are.</summary>
    public static Vector3 Push(Vector3 pos, float margin)
    {
        var push = Vector3.Zero;
        foreach (var (c, r) in All())
        {
            if (Mathf.Abs(pos.Y - c.Y) > 4f) continue;
            var off = new Vector3(pos.X - c.X, 0, pos.Z - c.Z);
            float d = off.Length();
            float edge = r + margin;
            if (d >= edge) continue;
            var dir = d > 0.01f ? off / d : Vector3.Right;
            push += dir * (1f + 2f * (1f - d / edge));
        }
        return push;
    }
}
