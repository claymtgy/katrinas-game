using Godot;

namespace KatrinasGame;

/// <summary>A tilled plot (a placed building). E with seeds in the stockpile plants; the crop
/// grows over game time (sleeping through the night counts) and E on a ripe crop harvests it into
/// the stockpile, with some seed back. GDD §10.
///
/// Multiplayer: the server owns the planting time and replicates it as building state
/// (INetState); every machine works out the growth from the shared clock.</summary>
public partial class FarmPlot : StaticBody3D, IInteractable, IServerInteractable, INetState
{
    [Export] public string CropName { get; set; } = "Wheat";
    [Export] public string SeedItem { get; set; } = "wheat_seeds";
    [Export] public string YieldItem { get; set; } = "wheat";
    [Export] public int YieldCount { get; set; } = 3;
    [Export] public int SeedsBack { get; set; } = 2;
    /// <summary>Game days from planting to ripe (1 = a whole day and night).</summary>
    [Export] public float GrowDays { get; set; } = 0.5f;
    [Export] public Color YoungColor { get; set; } = new(0.4f, 0.7f, 0.3f);
    [Export] public Color RipeColor { get; set; } = new(0.95f, 0.78f, 0.3f);
    /// <summary>"stalks" (wheat) or "leaves" (turnips).</summary>
    [Export] public string Look { get; set; } = "stalks";

    /// <summary>GameState.AbsoluteTime when planted; negative = empty.</summary>
    public float PlantedAt { get; private set; } = -1f;
    public bool Planted => PlantedAt >= 0f;
    public float Growth => Planted ? Mathf.Clamp((GameState.Instance.AbsoluteTime - PlantedAt) / GrowDays, 0f, 1f) : 0f;
    public bool Ripe => Planted && Growth >= 1f;

    private Node3D _crop;
    private StandardMaterial3D _mat;
    private float _shownGrowth = -1f;

    public string InteractPrompt
    {
        get
        {
            if (Ripe) return $"Harvest {CropName}";
            if (Planted) return $"{CropName} growing… {Growth * 100f:0}%";
            int seeds = GameState.Instance.Inventory.Count(SeedItem);
            return seeds > 0 ? $"Plant {CropName} ({seeds} {ItemDb.NameOf(SeedItem)})" : $"Needs {ItemDb.NameOf(SeedItem)} to plant";
        }
    }

    public override void _Ready()
    {
        _mat = StyleMaterials.Toon(YoungColor, 0.2f);
        _crop = new Node3D { Name = "Crop" };
        AddChild(_crop);
        var rng = new RandomNumberGenerator { Seed = 11 };
        for (int x = 0; x < 3; x++)
        for (int z = 0; z < 3; z++)
        {
            var pos = new Vector3((x - 1) * 0.55f, 0.12f, (z - 1) * 0.55f);
            Mesh mesh = Look == "stalks"
                ? new BoxMesh { Size = new Vector3(0.08f, 0.7f, 0.08f) }
                : new SphereMesh { Radius = 0.16f, Height = 0.24f, RadialSegments = 6, Rings = 3 };
            var mi = new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = _mat,
                Position = pos + (Look == "stalks" ? new Vector3(0, 0.35f, 0) : new Vector3(0, 0.1f, 0)),
                Rotation = new Vector3(rng.RandfRange(-0.15f, 0.15f), rng.RandfRange(0, Mathf.Tau), rng.RandfRange(-0.15f, 0.15f)),
            };
            _crop.AddChild(mi);
            if (Look == "stalks")
                mi.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.2f, 0.14f) }, MaterialOverride = _mat, Position = new Vector3(0, 0.38f, 0) });
            else
                mi.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.02f, 0.1f) }, MaterialOverride = StyleMaterials.Toon(new Color(0.3f, 0.6f, 0.25f), 0.2f), Position = new Vector3(0, 0.2f, 0), Rotation = new Vector3(0, 0, 0.5f) });
        }
        UpdateVisual();
    }

    public override void _Process(double delta)
    {
        if (Mathf.Abs(Growth - _shownGrowth) > 0.01f) UpdateVisual();
    }

    private void UpdateVisual()
    {
        _shownGrowth = Growth;
        _crop.Visible = Planted;
        if (!Planted) return;
        float s = Mathf.Lerp(0.2f, 1f, _shownGrowth);
        _crop.Scale = new Vector3(Mathf.Lerp(0.6f, 1f, _shownGrowth), s, Mathf.Lerp(0.6f, 1f, _shownGrowth));
        _mat.AlbedoColor = Ripe ? RipeColor : YoungColor.Lerp(RipeColor, _shownGrowth * 0.4f);
    }

    public void Interact(PlayerController player)
    {
        if (Planted && !Ripe) { Hud.Toast($"The {CropName.ToLower()} isn't ripe yet ({Growth * 100f:0}%)."); return; }
        Net.Instance.RequestInteract(this);
    }

    /// <summary>Server: plant or harvest.</summary>
    public void ServerInteract(PlayerController player)
    {
        var gs = GameState.Instance;
        var net = Net.Instance;
        if (Ripe)
        {
            gs.Inventory.Add(YieldItem, YieldCount);
            if (SeedsBack > 0) gs.Inventory.Add(SeedItem, SeedsBack);
            PlantedAt = -1f;
            net.ToastTo(player.PeerId, $"+{YieldCount} {ItemDb.NameOf(YieldItem)}" + (SeedsBack > 0 ? $", +{SeedsBack} {ItemDb.NameOf(SeedItem)}" : ""));
        }
        else if (!Planted)
        {
            if (!gs.Inventory.Remove(SeedItem, 1)) { net.ToastTo(player.PeerId, $"No {ItemDb.NameOf(SeedItem)} in the stockpile"); return; }
            PlantedAt = gs.AbsoluteTime;
            net.ToastTo(player.PeerId, $"Planted {CropName.ToLower()}. Ripe in about {GrowDays * 24f:0} hours.");
        }
        else return;
        UpdateVisual();
        var b = gs.FindBuilding(this);
        if (b != null) net.BroadcastBuildingState(b);
    }

    public Variant GetState() => PlantedAt;

    public void ApplyState(Variant state)
    {
        PlantedAt = (float)state.AsDouble();
        if (_crop != null) UpdateVisual();
    }
}
