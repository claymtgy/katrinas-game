using Godot;

namespace KatrinasGame;

/// <summary>The Blast Forge (GDD §11, step 3): a tall brick furnace that burns the Cinder Foothills'
/// coal hot enough to turn iron into steel, and forges plate, steel weapons and the best bows.
/// Built from code so the scene stays a bare station: a tapering brick stack on a stone plinth, a
/// glowing mouth, an iron hood, a coal heap and an anvil block beside it. The fire breathes.</summary>
public partial class BlastForgeStation : CraftingStation
{
    private OmniLight3D _glow;
    private StandardMaterial3D _mouth;
    private float _time;

    public override void _Ready()
    {
        var brick = StyleMaterials.Toon(new Color(0.48f, 0.24f, 0.18f), 0.25f);
        var brickDark = StyleMaterials.Toon(new Color(0.32f, 0.16f, 0.13f), 0.2f);
        var stone = StyleMaterials.Toon(new Color(0.36f, 0.35f, 0.38f), 0.2f);
        var iron = StyleMaterials.Toon(new Color(0.3f, 0.31f, 0.34f), 0.5f);
        var coal = StyleMaterials.Toon(new Color(0.1f, 0.09f, 0.1f), 0.4f);
        _mouth = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.55f, 0.2f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.12f),
            EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        void Mesh(Mesh m, Material mat, Vector3 pos, Vector3 rot = default) =>
            AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot });

        // Plinth, then the brick stack in three tapering courses with dark bands between.
        Mesh(new BoxMesh { Size = new Vector3(1.9f, 0.4f, 1.9f) }, stone, new Vector3(0, 0.2f, 0));
        float y = 0.4f;
        float[] widths = { 1.6f, 1.35f, 1.05f };
        float[] heights = { 0.9f, 0.8f, 0.8f };
        for (int i = 0; i < 3; i++)
        {
            Mesh(new BoxMesh { Size = new Vector3(widths[i], heights[i], widths[i]) }, brick, new Vector3(0, y + heights[i] / 2f, 0));
            y += heights[i];
            Mesh(new BoxMesh { Size = new Vector3(widths[i] + 0.06f, 0.08f, widths[i] + 0.06f) }, brickDark, new Vector3(0, y, 0));
        }
        // Iron hood and a short flue.
        Mesh(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.5f, Height = 0.35f, RadialSegments = 8 }, iron, new Vector3(0, y + 0.17f, 0));
        Mesh(new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.22f, Height = 0.6f, RadialSegments = 8 }, iron, new Vector3(0, y + 0.65f, 0));
        // The glowing mouth (front, +Z) under an iron lintel.
        Mesh(new BoxMesh { Size = new Vector3(0.7f, 0.5f, 0.06f) }, _mouth, new Vector3(0, 0.75f, 0.79f));
        Mesh(new BoxMesh { Size = new Vector3(0.9f, 0.1f, 0.12f) }, iron, new Vector3(0, 1.05f, 0.8f));
        // Tuyere pipe on the side, and a coal heap and a quench trough by the plinth.
        Mesh(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 0.8f, RadialSegments = 6 }, iron, new Vector3(0.95f, 0.85f, 0), new Vector3(0, 0, Mathf.Pi / 2f));
        for (int k = 0; k < 7; k++)
        {
            float a = k * 2.3f;
            Mesh(new BoxMesh { Size = Vector3.One * (0.16f + 0.04f * (k % 3)) }, coal,
                new Vector3(-0.8f + Mathf.Cos(a) * 0.2f, 0.08f + 0.06f * (k % 2), 1.05f + Mathf.Sin(a) * 0.15f), new Vector3(a, a * 0.5f, 0));
        }
        Mesh(new BoxMesh { Size = new Vector3(0.7f, 0.3f, 0.35f) }, stone, new Vector3(0.75f, 0.15f, 1.05f));

        _glow = new OmniLight3D { Position = new Vector3(0, 0.8f, 1.3f), LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.6f, OmniRange = 5f };
        AddChild(_glow);
        if (!Net.Instance.IsDedicated) Sfx.Loop(this, "amb_fire", new Vector3(0, 0.8f, 0.8f), -6f, 12f, 2f);
    }

    public override void _Process(double delta)
    {
        if (_glow == null) return;
        _time += (float)delta;
        float f = 0.8f + 0.2f * Mathf.Sin(_time * 2.1f) + 0.1f * Mathf.Sin(_time * 5.3f);
        _glow.LightEnergy = 1.6f * f;
        _mouth.EmissionEnergyMultiplier = 4f * f;
    }
}
