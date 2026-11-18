using System;
using System.Collections.Generic;
using System.Linq;

namespace KatrinasGame;

/// <summary>Simple item-id -> count bag. Plain C# so it can be reused for chests, traders, workers later.
/// GameState.Inventory is the shared base stockpile: the server owns it, clients hold a mirror.</summary>
public class Inventory
{
    private readonly Dictionary<string, int> _items = new();

    /// <summary>Fires whenever contents change.</summary>
    public event Action Changed;
    /// <summary>Fires on Add with (itemId, amount) — used for pickup toasts.</summary>
    public event Action<string, int> ItemAdded;

    public IReadOnlyDictionary<string, int> Items => _items;

    /// <summary>Bumped on every change (cheap "did it change?" checks for UI).</summary>
    public int Version { get; private set; }

    /// <summary>Items that take up room in a pack (coins weigh nothing).</summary>
    public int Load => _items.Where(kv => kv.Key != "coin").Sum(kv => kv.Value);

    public int Count(string id) => _items.TryGetValue(id, out var c) ? c : 0;

    public void Add(string id, int amount = 1)
    {
        if (amount <= 0 || string.IsNullOrEmpty(id)) return;
        _items[id] = Count(id) + amount;
        ItemAdded?.Invoke(id, amount);
        Notify();
    }

    public bool Remove(string id, int amount = 1)
    {
        if (Count(id) < amount) return false;
        _items[id] -= amount;
        if (_items[id] <= 0) _items.Remove(id);
        Notify();
        return true;
    }

    public bool Has(IReadOnlyDictionary<string, int> cost) => cost.All(kv => Count(kv.Key) >= kv.Value);

    public bool TryConsume(IReadOnlyDictionary<string, int> cost)
    {
        if (!Has(cost)) return false;
        foreach (var kv in cost) _items[kv.Key] -= kv.Value;
        foreach (var key in _items.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList()) _items.Remove(key);
        Notify();
        return true;
    }

    /// <summary>Replace everything (clients mirroring the server). Doesn't fire ItemAdded.</summary>
    public void ReplaceAll(Godot.Collections.Dictionary items)
    {
        _items.Clear();
        foreach (var kv in items)
        {
            int n = kv.Value.AsInt32();
            if (n > 0) _items[kv.Key.AsString()] = n;
        }
        Notify();
    }

    public Godot.Collections.Dictionary ToDictionary()
    {
        var d = new Godot.Collections.Dictionary();
        foreach (var kv in _items) d[kv.Key] = kv.Value;
        return d;
    }

    public void Clear()
    {
        _items.Clear();
        Notify();
    }

    private void Notify()
    {
        Version++;
        Changed?.Invoke();
    }
}
