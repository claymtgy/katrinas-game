using System;
using System.Collections.Generic;
using System.Linq;

namespace KatrinasGame;

/// <summary>Simple item-id -> count bag. Plain C# so it can be reused for chests, traders, workers later.</summary>
public class Inventory
{
    private readonly Dictionary<string, int> _items = new();

    /// <summary>Fires whenever contents change.</summary>
    public event Action Changed;
    /// <summary>Fires on Add with (itemId, amount) — used for pickup toasts.</summary>
    public event Action<string, int> ItemAdded;

    public IReadOnlyDictionary<string, int> Items => _items;

    public int Count(string id) => _items.TryGetValue(id, out var c) ? c : 0;

    public void Add(string id, int amount = 1)
    {
        if (amount <= 0 || string.IsNullOrEmpty(id)) return;
        _items[id] = Count(id) + amount;
        ItemAdded?.Invoke(id, amount);
        Changed?.Invoke();
    }

    public bool Remove(string id, int amount = 1)
    {
        if (Count(id) < amount) return false;
        _items[id] -= amount;
        if (_items[id] <= 0) _items.Remove(id);
        Changed?.Invoke();
        return true;
    }

    public bool Has(IReadOnlyDictionary<string, int> cost) => cost.All(kv => Count(kv.Key) >= kv.Value);

    public bool TryConsume(IReadOnlyDictionary<string, int> cost)
    {
        if (!Has(cost)) return false;
        foreach (var kv in cost) _items[kv.Key] -= kv.Value;
        foreach (var key in _items.Where(kv => kv.Value <= 0).Select(kv => kv.Key).ToList()) _items.Remove(key);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        Changed?.Invoke();
    }
}
