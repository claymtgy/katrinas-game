using System;
using Godot;

namespace KatrinasGame;

/// <summary>Damage component. Add as a child named "Health" to anything that can be hit:
/// the player, enemies, trees, rocks, dummies, buildings.</summary>
[GlobalClass]
public partial class Health : Node
{
    [Export] public float Max { get; set; } = 100f;

    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;
    public float Fraction => Max > 0 ? Current / Max : 0f;

    /// <summary>(amount, source)</summary>
    public event Action<float, Node> Damaged;
    public event Action<Node> Died;

    public override void _Ready() => Current = Max;

    public void TakeDamage(float amount, Node source)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Max(0f, Current - amount);
        Damaged?.Invoke(amount, source);
        if (Current <= 0f) Died?.Invoke(source);
    }

    public void Heal(float amount) => Current = Mathf.Min(Max, Current + amount);

    public void Reset() => Current = Max;

    /// <summary>Finds the Health component on a hit collider (its direct child named "Health").</summary>
    public static Health FindOn(GodotObject obj) => (obj as Node)?.GetNodeOrNull<Health>("Health");
}
