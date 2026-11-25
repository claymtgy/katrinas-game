using Godot;

namespace KatrinasGame;

/// <summary>The Ember Pass gate in the Cinder Foothills (GDD §11, step 3): iron bars in a stone
/// arch, locked. The Bandit Warlord carries the key. Unlocked once for the whole world (the journal
/// fact "ember_pass_opened", shared and saved), the bars sink into the ground; beyond, the pass is
/// choked with fresh lava for now (the Ember Wastes come later).</summary>
public partial class EmberGate : StaticBody3D, IInteractable, IServerInteractable
{
    public const string OpenedFact = "ember_pass_opened";
    public const string KeyId = "ember_pass_key";

    public static bool Opened => Journal.Instance?.Facts.Contains(OpenedFact) ?? false;

    private Node3D _bars;
    private CollisionShape3D _block;
    private bool _shownOpen;

    /// <summary>Make the bars (called by the world builder once it's in the tree).</summary>
    public void Build()
    {
        var iron = StyleMaterials.Toon(new Color(0.18f, 0.17f, 0.18f), 0.45f);
        var rust = StyleMaterials.Toon(new Color(0.45f, 0.2f, 0.12f), 0.3f);
        _bars = new Node3D { Name = "Bars" };
        AddChild(_bars);
        for (int i = 0; i < 7; i++)
            _bars.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.07f, Height = 5f, RadialSegments = 6 },
                MaterialOverride = i % 3 == 1 ? rust : iron, Position = new Vector3(-1.65f + i * 0.55f, 2.5f, 0),
            });
        foreach (float y in new[] { 1.2f, 3.6f })
            _bars.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3.8f, 0.14f, 0.12f) }, MaterialOverride = iron, Position = new Vector3(0, y, 0) });
        // The lock: a heavy iron box with an ember-red keyhole.
        _bars.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.6f, 0.25f) }, MaterialOverride = iron, Position = new Vector3(0, 2.4f, 0.12f) });
        _bars.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.2f, 0.02f) }, MaterialOverride = EnemyMesh.Glow(new Color(1f, 0.4f, 0.1f), 2.5f), Position = new Vector3(0, 2.38f, 0.26f) });
        _block = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.9f, 5f, 0.4f) }, Position = new Vector3(0, 2.5f, 0) };
        AddChild(_block);
        ShowOpen(Opened, instant: true);
    }

    public string InteractPrompt => Opened
        ? "The Ember Pass: choked with fresh lava beyond (not yet)"
        : (GameState.Instance.LocalPlayer is { } p && Supplies.Count(p, KeyId) > 0 ? "Unlock the Ember Pass gate" : "The Ember Pass gate is locked (the Warlord has the key)");

    public void Interact(PlayerController player)
    {
        if (Opened) { Hud.Toast("Past the gate the pass is a river of fresh lava. There's no way on… yet."); return; }
        Net.Instance.RequestInteract(this);
    }

    public void ServerInteract(PlayerController player)
    {
        var net = Net.Instance;
        if (Opened) return;
        if (!Supplies.TryConsume(player, KeyId))
        {
            net.ToastTo(player.PeerId, "Locked. A heavy iron lock, the keyhole warm to the touch. The Bandit Warlord carries the key.");
            return;
        }
        Journal.Fact(OpenedFact);
        net.BroadcastJournal("");
        net.ToastAll($"{player.DisplayName} unlocked the Ember Pass. The bars grind down into the rock… beyond, the pass is choked with fresh lava. (The Ember Wastes come in a later update.)");
    }

    public override void _Process(double delta)
    {
        if (_bars != null && Opened != _shownOpen) ShowOpen(Opened, instant: false);
    }

    private void ShowOpen(bool open, bool instant)
    {
        _shownOpen = open;
        _block.Disabled = open;
        float y = open ? -4.8f : 0f;
        if (instant) _bars.Position = new Vector3(0, y, 0);
        else
        {
            CreateTween().TweenProperty(_bars, "position:y", y, 2.5).SetTrans(Tween.TransitionType.Sine);
            Sfx.At("build", GlobalPosition + Vector3.Up * 2f, 4f, 0.05f, 40f);
        }
    }
}
