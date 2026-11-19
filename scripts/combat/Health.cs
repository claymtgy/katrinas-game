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
    /// <summary>Flat damage soaked off every hit (bandits' mail), never below <see cref="ArmorFloor"/>
    /// of the hit. Players' armour works differently (Gear), so theirs stays 0.</summary>
    [Export] public float Armor { get; set; } = 0f;
    public const float ArmorFloor = 0.2f;

    /// <summary>What a hit of <paramref name="amount"/> does after armour.</summary>
    public float AfterArmor(float amount) => Armor <= 0f ? amount : Mathf.Max(amount - Armor, amount * ArmorFloor);

    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;
    public float Fraction => Max > 0 ? Current / Max : 0f;

    /// <summary>(amount, source)</summary>
    public event Action<float, Node> Damaged;
    public event Action<Node> Died;
    /// <summary>Back to full after being dead (respawned resource, rebuilt dummy, respawned player).</summary>
    public event Action Revived;

    /// <summary>Server: when true for a hit's source, the hit does nothing (a downed player).</summary>
    public Func<Node, bool> Absorb;
    /// <summary>Server: asked before a killing blow; true keeps this alive at 1 health instead (a
    /// player going down, GDD §16).</summary>
    public Func<Node, bool> BeforeLethal;
    /// <summary>Server: what a hit from a source is multiplied by before armour (War Cry, Mark:
    /// Net.ServerDamageBonus).</summary>
    public static Func<Health, Node, float> Bonus;

    public override void _Ready()
    {
        Current = Max;
        AddToGroup(Group);
    }

    private static bool Authoritative => Net.Instance == null || Net.Instance.IsServer;

    public void TakeDamage(float amount, Node source)
    {
        TakeDamage(amount, source, false);
    }

    /// <summary>A hit; <paramref name="ignoreArmor"/> for blows that go straight through (the
    /// Archer's Piercing talent).</summary>
    public void TakeDamage(float amount, Node source, bool ignoreArmor)
    {
        if (!Authoritative || IsDead || amount <= 0f) return;
        if (Absorb != null && Absorb(source)) return;
        if (Bonus != null && source != null) amount *= Bonus(this, source);
        if (!ignoreArmor) amount = AfterArmor(amount);
        if (Current - amount <= 0f && BeforeLethal != null && BeforeLethal(source))
        {
            // Kept at 1: down, not out.
            float dealt = Mathf.Max(0f, Current - 1f);
            Current = Mathf.Min(Current, 1f);
            Net.Instance?.BroadcastHealth(this, ChangeKind.Damage, dealt, source);
            Damaged?.Invoke(dealt, source);
            return;
        }
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

    /// <summary>Every machine, at spawn: set the maximum and fill up, without telling anyone.</summary>
    public void InitMax(float max)
    {
        Max = max;
        Current = max;
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
