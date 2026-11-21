using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Something left out in the country to search once (a spilled wagon, a hermit's chest,
/// a strongbox on a ruined tower). Whoever searches it first takes everything; after that it's
/// empty for the whole world (the journal fact "searched:&lt;node name&gt;", shared and saved).</summary>
public partial class LootCache : StaticBody3D, IInteractable, IServerInteractable
{
    [Export] public string CacheName { get; set; } = "crate";

    /// <summary>What's inside (item id → count). Set where the world is built, the same on every machine.</summary>
    public readonly Dictionary<string, int> Loot = new();

    public string Fact => $"searched:{Name}";
    public bool Searched => Journal.Instance?.Facts.Contains(Fact) ?? false;

    public string InteractPrompt => Searched ? $"The {CacheName} is empty" : $"Search the {CacheName}";

    public void Interact(PlayerController player) => Net.Instance.RequestInteract(this);

    public void ServerInteract(PlayerController player)
    {
        if (Searched)
        {
            Net.Instance.ToastTo(player.PeerId, $"Someone has already emptied the {CacheName}.");
            return;
        }
        var got = new List<string>();
        foreach (var (id, n) in Loot)
        {
            int fits = Supplies.Give(player, id, n);
            if (fits > 0) got.Add($"+{fits} {ItemDb.Get(id)?.DisplayName ?? id}");
        }
        Journal.Fact(Fact);
        Net.Instance.BroadcastJournal("");
        Sfx.At("rustle", GlobalPosition + Vector3.Up, 0f, 0.1f);
        Net.Instance.ToastTo(player.PeerId, got.Count > 0 ? $"You search the {CacheName}: {string.Join(", ", got)}" : $"The {CacheName} holds nothing you can carry.");
    }

    public int Total => Loot.Values.Sum();
}
