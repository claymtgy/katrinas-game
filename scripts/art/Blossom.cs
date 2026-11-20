using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The blossom tree's canopy, as in the concept art: a dense cloud of small five-petal
/// florets over the big soft clumps, in a few shades of pink, so it reads as flowers up close
/// and as glowing clumps far off. The florets are one MultiMesh laid over the model's blossom
/// mesh (sampled once, shared by every blossom tree).</summary>
public static class Blossom
{
    public const int FloretCount = 3200;

    private const string FloretShader = @"shader_type spatial;
render_mode cull_disabled, diffuse_lambert_wrap, specular_disabled, depth_prepass_alpha;
global uniform float night;
uniform vec3 core_color : source_color = vec3(0.98, 0.78, 0.66);
void fragment() {
    vec2 d = UV - 0.5;
    float a = atan(d.y, d.x);
    float r = length(d) * 2.0;
    // Five round petals with notched tips.
    float petal = 0.72 + 0.28 * cos(5.0 * a);
    petal -= 0.08 * pow(max(cos(5.0 * a), 0.0), 18.0);
    if (r > petal) discard;
    vec3 c = COLOR.rgb;
    c = mix(core_color, c, smoothstep(0.06, 0.2, r));
    c *= 0.86 + 0.18 * r;
    ALBEDO = c * 0.55;
    BACKLIGHT = vec3(1.0, 0.6, 0.75) * 0.8;
    ROUGHNESS = 1.0;
    EMISSION = c * c * (0.3 + 0.1 * (1.0 - r)) * (1.0 + night * 0.4);
}";

    private static readonly Dictionary<Mesh, MultiMesh> Cache = new();
    private static ShaderMaterial _mat;

    private static readonly Color[] Pinks =
    {
        new(1f, 0.66f, 0.8f), new(0.96f, 0.52f, 0.72f), new(1f, 0.76f, 0.86f), new(0.88f, 0.4f, 0.62f), new(0.98f, 0.62f, 0.76f),
    };

    /// <summary>Lay florets over every blossom mesh under <paramref name="tree"/>.</summary>
    public static void Dress(Node3D tree)
    {
        foreach (var node in tree.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)node;
            if (mi.Mesh == null || mi.Mesh.GetSurfaceCount() == 0) continue;
            var name = mi.Mesh.SurfaceGetMaterial(0)?.ResourceName ?? "";
            if (!name.StartsWith("Blossom")) continue;
            var mm = Florets(mi.Mesh);
            if (mm == null) continue;
            _mat ??= new ShaderMaterial { Shader = new Shader { Code = FloretShader } };
            mi.AddChild(new MultiMeshInstance3D
            {
                Name = "Florets", Multimesh = mm, MaterialOverride = _mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            mi.AddChild(Petals(mi.Mesh.GetAabb()));
        }
    }

    private static StandardMaterial3D _petalMat;

    /// <summary>Big petals drifting off the canopy on the breeze, tumbling as they fall (they read
    /// as speed and air when they cross close to the camera).</summary>
    private static GpuParticles3D Petals(Aabb canopy)
    {
        if (_petalMat == null)
        {
            var tex = new GradientTexture2D
            {
                Width = 32, Height = 32, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
                Gradient = new Gradient { Offsets = new[] { 0f, 0.7f, 1f }, Colors = new[] { Colors.White, Colors.White, new Color(1, 1, 1, 0) } },
            };
            _petalMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
                AlphaScissorThreshold = 0.5f,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                VertexColorUseAsAlbedo = true,
                AlbedoTexture = tex,
            };
        }
        var c = canopy.GetCenter();
        return new GpuParticles3D
        {
            Name = "Petals",
            Amount = 70,
            Lifetime = 11f,
            Preprocess = 11f,
            Position = c,
            // A petal, not a disc: long and narrow.
            DrawPass1 = new QuadMesh { Size = new Vector2(0.14f, 0.24f), Material = _petalMat },
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
                EmissionBoxExtents = canopy.Size * 0.42f,
                Direction = new Vector3(1f, -0.3f, 0.4f),
                Spread = 35f,
                InitialVelocityMin = 0.4f,
                InitialVelocityMax = 1.1f,
                Gravity = new Vector3(0.35f, -0.32f, 0.15f),
                AngleMin = -180f,
                AngleMax = 180f,
                AngularVelocityMin = -160f,
                AngularVelocityMax = 160f,
                ScaleMin = 0.8f,
                ScaleMax = 1.6f,
                TurbulenceEnabled = true,
                TurbulenceNoiseStrength = 1.2f,
                TurbulenceNoiseScale = 4f,
                Color = new Color(1f, 0.7f, 0.82f),
                HueVariationMin = -0.03f,
                HueVariationMax = 0.03f,
            },
            VisibilityAabb = new Aabb(-canopy.Size * 2f - new Vector3(0, 10, 0), canopy.Size * 4f + new Vector3(30, 20, 30)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static MultiMesh Florets(Mesh source)
    {
        if (Cache.TryGetValue(source, out var cached)) return cached;
        var arrays = source.SurfaceGetArrays(0);
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int tris = indices.Length > 0 ? indices.Length / 3 : verts.Length / 3;
        if (tris == 0) return null;
        int Idx(int i) => indices.Length > 0 ? indices[i] : i;

        // Area-weighted, so florets spread evenly over the clumps.
        var cumulative = new float[tris];
        float total = 0f;
        for (int t = 0; t < tris; t++)
        {
            var a = verts[Idx(t * 3)]; var b = verts[Idx(t * 3 + 1)]; var c = verts[Idx(t * 3 + 2)];
            total += (b - a).Cross(c - a).Length() * 0.5f;
            cumulative[t] = total;
        }
        var rng = new RandomNumberGenerator { Seed = 4242 };
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = FloretCount,
        };
        for (int i = 0; i < FloretCount; i++)
        {
            float pick = rng.Randf() * total;
            int lo = 0, hi = tris - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (cumulative[mid] < pick) lo = mid + 1; else hi = mid; }
            int i0 = Idx(lo * 3), i1 = Idx(lo * 3 + 1), i2 = Idx(lo * 3 + 2);
            float u = rng.Randf(), v = rng.Randf();
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            var p = verts[i0] + (verts[i1] - verts[i0]) * u + (verts[i2] - verts[i0]) * v;
            var n = normals.Length > 0 ? (normals[i0] + normals[i1] + normals[i2]).Normalized() : Vector3.Up;
            if (n.LengthSquared() < 0.5f) n = Vector3.Up;
            // Face outward (a quad's front is +Z), spun at random, tipped a little off the surface.
            var tilt = (n + new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(-0.2f, 0.4f), rng.RandfRange(-0.4f, 0.4f))).Normalized();
            var basis = Basis.LookingAt(-tilt, Mathf.Abs(tilt.Y) > 0.95f ? Vector3.Right : Vector3.Up);
            basis = basis * new Basis(Vector3.Back, rng.RandfRange(0, Mathf.Tau));
            float size = rng.RandfRange(0.35f, 0.62f);
            mm.SetInstanceTransform(i, new Transform3D(basis.Scaled(Vector3.One * size), p + n * rng.RandfRange(0.02f, 0.12f)));
            mm.SetInstanceColor(i, Pinks[rng.RandiRange(0, Pinks.Length - 1)]);
        }
        Cache[source] = mm;
        return mm;
    }
}
