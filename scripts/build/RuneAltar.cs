using Godot;

namespace KatrinasGame;

/// <summary>A carved standing stone on a plinth with a glowing rune: enchant armour here
/// (GDD §6, enchantments). It's a crafting station; the Hud shows the enchanting menu for it.</summary>
public partial class RuneAltar : CraftingStation
{
    public override string InteractPrompt => "Enchant at the Rune Altar";

    private MeshInstance3D _rune;
    private OmniLight3D _light;
    private float _t;

    public override void _Ready()
    {
        var stone = StyleMaterials.Toon(new Color(0.55f, 0.56f, 0.6f), 0.25f);
        var dark = StyleMaterials.Toon(new Color(0.38f, 0.39f, 0.44f), 0.2f);
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.4f, 1.8f, 1.0f) }, Position = new Vector3(0, 0.9f, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.5f, 0.35f, 1.1f) }, MaterialOverride = dark, Position = new Vector3(0, 0.175f, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.2f, 0.2f, 0.85f) }, MaterialOverride = stone, Position = new Vector3(0, 0.45f, 0) });
        // The standing stone, tapering and a little crooked.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.24f, BottomRadius = 0.36f, Height = 1.35f, RadialSegments = 5, Rings = 1 },
            MaterialOverride = stone, Position = new Vector3(0, 1.2f, 0), Rotation = new Vector3(0.04f, 0.3f, -0.05f),
        });
        // The rune: a glowing mark on the stone's face, which pulses.
        var glow = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.9f, 1f), EmissionEnabled = true, Emission = new Color(0.4f, 0.85f, 1f),
            EmissionEnergyMultiplier = 2.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _rune = new MeshInstance3D { Position = new Vector3(0, 1.25f, 0.3f), MaterialOverride = glow };
        var bar = new BoxMesh { Size = new Vector3(0.05f, 0.5f, 0.03f) };
        _rune.AddChild(new MeshInstance3D { Mesh = bar, MaterialOverride = glow });
        _rune.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.22f, 0.03f) }, MaterialOverride = glow, Position = new Vector3(0.07f, 0.12f, 0), Rotation = new Vector3(0, 0, -0.8f) });
        _rune.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.22f, 0.03f) }, MaterialOverride = glow, Position = new Vector3(-0.07f, -0.06f, 0), Rotation = new Vector3(0, 0, -0.8f) });
        AddChild(_rune);
        _light = new OmniLight3D { Position = new Vector3(0, 1.3f, 0.6f), LightColor = new Color(0.45f, 0.85f, 1f), LightEnergy = 0.8f, OmniRange = 3.5f };
        AddChild(_light);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float pulse = 0.75f + 0.25f * Mathf.Sin(_t * 2.2f);
        if (_light != null) _light.LightEnergy = 0.8f * pulse;
        if (_rune != null) _rune.Scale = Vector3.One * (0.96f + 0.04f * pulse);
    }
}
