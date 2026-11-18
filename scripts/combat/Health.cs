using System;
using Godot;

namespace KatrinasGame;

/// <summary>Damage component. Add as a child named "Health" to anything that can be hit:
/// the player, enemies, trees, rocks, dummies, buildings.
///
/// Networked: only the server changes health (TakeDamage / Heal / Reset). Each change is
/// mirrored to clients, which fire the same Damaged / Died / Revived events, so visuals
/// (shakes, damage numbers, falling trees) run everywhere and gameplay results (drops,
/// respawns) run on the server only (guard them with Net.Instance.IsServer).
/// The owner's path must be identical on every machine (give it a stable name).</summary>
[GlobalClass]
public partial class Health : Node
{
    public const string Group = "net_health";

    public enum ChangeKind { Damage, Heal, Revive }

    [Export] public float Max { get; set; } = 100f;

    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;
    public float Fraction => Max > 0 ? Current / Max : 0f;

    /// <summary>(amount, source)</summary>
    public event Action<float, Node> Damaged;
    public event Action<Node> Died;
    /// <summary>Back to full after being dead (respawned resource, rebuilt dummy, respawned player).</summary>
    public event Action Revived;

    public override void _Ready()
    {
        Current = Max;
        AddToGroup(Group);
    }

    private static bool Authoritative => Net.Instance == null || Net.Instance.IsServer;

    public void TakeDamage(float amount, Node source)
    {
        if (!Authoritative || IsDead || amount <= 0f) return;
        Current = Mathf.Max(0f, Current - amount);
        Net.Instance?.BroadcastHealth(this, ChangeKind.Damage, amount, source);
        Damaged?.Invoke(amount, source);
        if (Current <= 0f) Died?.Invoke(source);
    }

    public void Heal(float amount)
    {
        if (!Authoritative || IsDead) return;
        Current = Mathf.Min(Max, Current + amount);
        Net.Instance?.BroadcastHealth(this, ChangeKind.Heal, amount, null);
    }

    /// <summary>Change the maximum (food buffs). Every machine calls it; the server also trims
    /// current health that no longer fits and tells the clients.</summary>
    public void SetMax(float max)
    {
        if (Mathf.IsEqualApprox(max, Max)) return;
        Max = max;
        if (Authoritative && Current > Max)
        {
            Current = Max;
            Net.Instance?.BroadcastHealth(this, ChangeKind.Heal, 0f, null);
        }
    }

    public void Reset()
    {
        if (!Authoritative) return;
        bool wasDead = IsDead;
        Current = Max;
        Net.Instance?.BroadcastHealth(this, wasDead ? ChangeKind.Revive : ChangeKind.Heal, 0f, null);
        if (wasDead) Revived?.Invoke();
    }

    /// <summary>Client: mirror a change made on the server.</summary>
    public void ApplyRemote(float current, float amount, ChangeKind kind, Node source)
    {
        bool wasDead = IsDead;
        Current = current;
        switch (kind)
        {
            case ChangeKind.Damage:
                Damaged?.Invoke(amount, source);
                if (!wasDead && IsDead) Died?.Invoke(source);
                break;
            case ChangeKind.Revive:
                if (wasDead) Revived?.Invoke();
                break;
        }
    }

    /// <summary>Client: jump straight to a value when joining (dead things play their death once).</summary>
    public void ApplySnapshot(float current)
    {
        bool wasDead = IsDead;
        Current = current;
        if (!wasDead && IsDead) Died?.Invoke(null);
        else if (wasDead && !IsDead) Revived?.Invoke();
    }

    /// <summary>Finds the Health component on a hit collider (its direct child named "Health").</summary>
    public static Health FindOn(GodotObject obj) => (obj as Node)?.GetNodeOrNull<Health>("Health");
}
