using Godot;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>A tilled plot (a placed building). E with seeds plants; the crop only grows while the
/// soil is wet: water it (E with Water from a well or the pool) or let the rain do it. One
/// watering keeps the soil wet for WetDays of game time; sleeping through the night counts.
/// E on a ripe crop harvests it into the stockpile, with some seed back. GDD §4.
///
/// Multiplayer: the server owns the plot's state (growth banked so far, since when, wet until)
/// and replicates it as building state (INetState); every machine works out the growth from
/// the shared clock.</summary>
public partial class FarmPlot : StaticBody3D, IInteractable, IServerInteractable, INetState
{
    /// <summary>Game days one watering keeps the soil wet.</summary>
    public const float WetDays = 0.6f;

    [Export] public string CropName { get; set; } = "Wheat";
    [Export] public string SeedItem { get; set; } = "wheat_seeds";
    [Export] public string YieldItem { get; set; } = "wheat";
    [Export] public int YieldCount { get; set; } = 3;
    [Export] public int SeedsBack { get; set; } = 2;
    /// <summary>Wet game days from planting to ripe (1 = a whole day and night).</summary>
    [Export] public float GrowDays { get; set; } = 0.5f;
    [Export] public Color YoungColor { get; set; } = new(0.4f, 0.7f, 0.3f);
    [Export] public Color RipeColor { get; set; } = new(0.95f, 0.78f, 0.3f);
    /// <summary>"stalks" (wheat) or "leaves" (turnips).</summary>
    [Export] public string Look { get; set; } = "stalks";

    public bool Planted { get; private set; }
    /// <summary>Growth (0..1) banked up to _since.</summary>
    private float _banked;
    /// <summary>AbsoluteTime the bank was last updated (planting, watering).</summary>
    private float _since;
    /// <summary>AbsoluteTime the soil dries out.</summary>
    private float _wetUntil = -1f;

    private static float Now => GameState.Instance.AbsoluteTime;
    public bool Wet => Now < _wetUntil;
    public float Growth => Planted
        ? Mathf.Clamp(_banked + Mathf.Max(0f, Mathf.Min(Now, _wetUntil) - _since) / GrowDays, 0f, 1f)
        : 0f;
    public bool Ripe => Planted && Growth >= 1f;

    private Node3D _crop;
    private StandardMaterial3D _mat, _soilMat;
    private float _shownGrowth = -1f;
    private bool _shownWet;
    private static readonly Color DrySoil = new(0.36f, 0.25f, 0.17f);
    private static readonly Color WetSoil = new(0.16f, 0.1f, 0.08f);

    public string InteractPrompt
    {
        get
        {
            var me = GameState.Instance.LocalPlayer;
            if (Ripe) return $"Harvest {CropName}";
            if (!Planted)
            {
                int seeds = Supplies.Count(me, SeedItem);
                return seeds > 0 ? $"Plant {CropName} ({seeds} {ItemDb.NameOf(SeedItem)})" : $"Needs {ItemDb.NameOf(SeedItem)} to plant";
            }
            if (!Wet)
            {
                int water = Supplies.Count(me, "water");
                return water > 0 ? $"Water the {CropName.ToLower()} ({water} Water)" : $"{CropName} is dry: fetch Water from a well";
            }
            return $"{CropName} growing… {Growth * 100f:0}%";
        }
    }

    public override void _Ready()
    {
        _mat = StyleMaterials.Toon(YoungColor, 0.2f);
        if (GetNodeOrNull<MeshInstance3D>("Soil") is { } soil)
        {
            _soilMat = StyleMaterials.Toon(DrySoil, 0.1f);
            soil.MaterialOverride = _soilMat;
        }
        _crop = new Node3D { Name = "Crop" };
        AddChild(_crop);
        var rng = new RandomNumberGenerator { Seed = 11 };
        for (int x = 0; x < 3; x++)
        for (int z = 0; z < 3; z++)
        {
            var pos = new Vector3((x - 1) * 0.55f, 0.12f, (z - 1) * 0.55f);
            Mesh mesh = Look == "stalks"
                ? new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.05f, Height = 0.7f, RadialSegments = 5 }
                : new SphereMesh { Radius = 0.16f, Height = 0.24f, RadialSegments = 6, Rings = 3 };
            var mi = new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = _mat,
                Position = pos + (Look == "stalks" ? new Vector3(0, 0.35f, 0) : new Vector3(0, 0.1f, 0)),
                Rotation = new Vector3(rng.RandfRange(-0.15f, 0.15f), rng.RandfRange(0, Mathf.Tau), rng.RandfRange(-0.15f, 0.15f)),
            };
            _crop.AddChild(mi);
            if (Look == "stalks")
                mi.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(0.14f, 0.26f, 0.12f) }, MaterialOverride = _mat, Position = new Vector3(0, 0.42f, 0) });
            else
                for (int l = 0; l < 3; l++)
                    mi.AddChild(new MeshInstance3D
                    {
                        Mesh = new PrismMesh { Size = new Vector3(0.12f, 0.32f, 0.03f) },
                        MaterialOverride = StyleMaterials.Toon(new Color(0.3f, 0.6f, 0.25f), 0.2f),
                        Position = new Vector3(0, 0.22f, 0),
                        Rotation = new Vector3(0.35f, l * Mathf.Tau / 3f, 0),
                    });
        }
        UpdateVisual();
    }

    public override void _Process(double delta)
    {
        if (Mathf.Abs(Growth - _shownGrowth) > 0.01f || Wet != _shownWet) UpdateVisual();
    }

    private void UpdateVisual()
    {
        _shownGrowth = Growth;
        _shownWet = Wet;
        if (_soilMat != null) _soilMat.AlbedoColor = _shownWet ? WetSoil : DrySoil;
        _crop.Visible = Planted;
        if (!Planted) return;
        float s = Mathf.Lerp(0.2f, 1f, _shownGrowth);
        _crop.Scale = new Vector3(Mathf.Lerp(0.6f, 1f, _shownGrowth), s, Mathf.Lerp(0.6f, 1f, _shownGrowth));
        var c = Ripe ? RipeColor : YoungColor.Lerp(RipeColor, _shownGrowth * 0.4f);
        if (!_shownWet && !Ripe) c = c.Lerp(new Color(0.55f, 0.5f, 0.3f), 0.35f); // thirsty
        _mat.AlbedoColor = c;
    }

    public void Interact(PlayerController player)
    {
        if (Planted && !Ripe && Wet) { Hud.Toast($"The {CropName.ToLower()} isn't ripe yet ({Growth * 100f:0}%)."); return; }
        Net.Instance.RequestInteract(this);
    }

    /// <summary>Server: harvest, plant or water.</summary>
    public void ServerInteract(PlayerController player)
    {
        var gs = GameState.Instance;
        var net = Net.Instance;
        if (Ripe)
        {
            // Plots are at home: the harvest goes straight to the stockpile.
            gs.Inventory.Add(YieldItem, YieldCount);
            if (SeedsBack > 0) gs.Inventory.Add(SeedItem, SeedsBack);
            Planted = false;
            _banked = 0;
            net.ToastTo(player.PeerId, $"+{YieldCount} {ItemDb.NameOf(YieldItem)}" + (SeedsBack > 0 ? $", +{SeedsBack} {ItemDb.NameOf(SeedItem)}" : "") + " (to the stockpile)");
        }
        else if (!Planted)
        {
            if (!Supplies.TryConsume(player, SeedItem)) { net.ToastTo(player.PeerId, $"You have no {ItemDb.NameOf(SeedItem)}"); return; }
            Planted = true;
            _banked = 0;
            _since = Now;
            net.ToastTo(player.PeerId, Wet
                ? $"Planted {CropName.ToLower()}. Ripe in about {GrowDays * 24f:0} hours."
                : $"Planted {CropName.ToLower()}. Water it to make it grow.");
        }
        else if (!Wet)
        {
            if (!Supplies.TryConsume(player, "water")) { net.ToastTo(player.PeerId, "You have no Water. Draw some at a well or the pool."); return; }
            Water();
            net.ToastTo(player.PeerId, $"Watered the {CropName.ToLower()}.");
            return;
        }
        else return;
        Changed();
    }

    /// <summary>Server: wet the soil (watering can, rain). Doesn't broadcast if it barely changed.</summary>
    public void ServerWater()
    {
        if (_wetUntil - Now > WetDays - 0.02f) return;
        Water();
    }

    private void Water()
    {
        if (Planted) _banked = Growth;
        _since = Now;
        _wetUntil = Now + WetDays;
        Changed();
    }

    private void Changed()
    {
        if (_crop != null) UpdateVisual();
        var b = GameState.Instance.FindBuilding(this);
        if (b != null && Net.Instance.IsServer) Net.Instance.BroadcastBuildingState(b);
    }

    public Variant GetState() => new GDict { ["planted"] = Planted, ["banked"] = _banked, ["since"] = _since, ["wet"] = _wetUntil };

    public void ApplyState(Variant state)
    {
        if (state.VariantType == Variant.Type.Dictionary)
        {
            var d = state.AsGodotDictionary();
            Planted = d["planted"].AsBool();
            _banked = (float)d["banked"].AsDouble();
            _since = (float)d["since"].AsDouble();
            _wetUntil = (float)d["wet"].AsDouble();
        }
        else
        {
            // Saves from before watering: the planting time; count it as planted and watered.
            float plantedAt = (float)state.AsDouble();
            Planted = plantedAt >= 0f;
            _banked = 0;
            _since = plantedAt;
            _wetUntil = Planted ? plantedAt + WetDays : -1f;
        }
        if (_crop != null) UpdateVisual();
    }
}
