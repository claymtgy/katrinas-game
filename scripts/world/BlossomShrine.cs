using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Sacred cherry-blossom sanctuary: sets your respawn point and heals you.
/// Respawn points are personal. Enemies won't enter its light.
///
/// Fast travel (GDD §10): praying at a sanctuary attunes it for the whole world (the journal fact
/// "prayed:&lt;node name&gt;", so co-op players share it and it's saved). The home blossom starts
/// attuned. Praying at one opens the list of the other attuned sanctuaries; picking one sends you
/// there (Net.RequestTravel). Travel is personal, and not while you're in a fight.</summary>
public partial class BlossomShrine : StaticBody3D, IInteractable, IServerInteractable
{
    [Export] public string ShrineName { get; set; } = "Cliffside Blossom";
    /// <summary>Enemies won't come within this many meters of the tree.</summary>
    [Export] public float SafeRadius { get; set; } = 9f;
    /// <summary>Attuned from the start (the home blossom).</summary>
    [Export] public bool StartsAttuned { get; set; }

    /// <summary>Can't travel within this many seconds of being hurt.</summary>
    public const double CombatLockout = 8.0;

    /// <summary>Every sanctuary in the world, in the order they were built.</summary>
    public static readonly List<BlossomShrine> All = new();

    public override void _Ready()
    {
        GameState.Instance.SafeZones.Add((GlobalPosition, SafeRadius));
        All.Add(this);
    }

    public override void _ExitTree() => All.Remove(this);

    public static BlossomShrine Find(string nodeName) => All.FirstOrDefault(s => IsInstanceValid(s) && s.Name == nodeName);

    /// <summary>Someone has prayed here (or it's home). Every machine knows (the journal is shared).</summary>
    public bool Attuned => StartsAttuned || (Journal.Instance?.Facts.Contains($"prayed:{Name}") ?? false);

    /// <summary>Where you stand when you respawn or arrive here.</summary>
    public Vector3 ArrivalPoint => GlobalPosition - GlobalBasis.Z * 3f + Vector3.Up * 0.2f;

    /// <summary>The other attuned sanctuaries you could travel to from here.</summary>
    public IEnumerable<BlossomShrine> Destinations => All.Where(s => s != this && IsInstanceValid(s) && s.Attuned);

    public string InteractPrompt => $"Pray at the {ShrineName}";

    public void Interact(PlayerController player)
    {
        Net.Instance.RequestInteract(this);
        if (Destinations.Any()) Hud.Instance?.OpenTravel(this);
    }

    public void ServerInteract(PlayerController player)
    {
        bool first = !Attuned;
        player.SpawnPoint = ArrivalPoint;
        Net.Instance.ServerRestore(player);
        Journal.Fact($"prayed:{Name}");
        if (first) Net.Instance.BroadcastJournal("");
        Net.Instance.ToastTo(player.PeerId, first
            ? $"The {ShrineName} blooms. Respawn point set, wounds healed. You can now travel here from any sanctuary."
            : $"The {ShrineName} glows warmly. Respawn point set, wounds healed.");
    }

    /// <summary>Server: send a player from this sanctuary to another. Returns why not, or "".</summary>
    public string ServerTravel(PlayerController player, BlossomShrine to)
    {
        if (to == null || to == this) return "There's no such sanctuary.";
        if (!to.Attuned) return $"Nobody has prayed at the {to.ShrineName} yet.";
        if (player.Health.IsDead || player.Sleeping) return "You can't travel now.";
        if (Net.Now - player.LastHurtAt < CombatLockout) return "Too shaken to travel. Get clear of the fight first.";
        player.SpawnPoint = to.ArrivalPoint;
        Net.Instance.ServerRestore(player);
        Net.Instance.ServerTeleport(player, to.ArrivalPoint);
        Journal.Fact("travelled");
        Net.Instance.ToastTo(player.PeerId, $"Petals swirl around you… you arrive at the {to.ShrineName}.");
        return "";
    }
}
