using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>Billowing cumulus sea below the cliffs: hundreds of soft spheres in one MultiMesh,
/// lit by a stylized shader (toon terminator, silver rim toward the sun, and a warm under-glow
/// near lava falls).</summary>
public partial class CloudSea : Node3D
{
    public const int MaxLavaPoints = 8;

    private const string CloudShader = @"shader_type spatial;
render_mode unshaded, cull_back;
global uniform vec3 sun_dir;
global uniform vec4 sun_color;
global uniform float night;
uniform vec3 lit_color : source_color = vec3(1.0, 0.8, 0.74);
uniform vec3 shade_color : source_color = vec3(0.42, 0.3, 0.5);
uniform vec3 night_color : source_color = vec3(0.1, 0.1, 0.22);
uniform vec3 lava_color : source_color = vec3(1.0, 0.4, 0.13);
uniform vec4 lava_points[8];
uniform int lava_count = 0;
varying vec3 wpos;
varying vec3 wnrm;
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
void fragment() {
    vec3 n = normalize(wnrm);
    vec3 sd = normalize(sun_dir);
    float l = dot(n, sd) * 0.5 + 0.5;
    float band = smoothstep(0.38, 0.7, l);
    vec3 sun_tint = clamp(sun_color.rgb, vec3(0.0), vec3(1.4));
    vec3 col = mix(shade_color, lit_color * mix(vec3(1.0), sun_tint, 0.6), band);
    col += vec3(0.32, 0.26, 0.5) * max(n.y, 0.0) * 0.18;                 // sky bounce on tops
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    col += sun_tint * rim * 0.45;                                        // silver lining
    col = mix(col, night_color + col * 0.15, night);
    float g = 0.0;
    for (int i = 0; i < lava_count; i++) {
        float d = length(wpos - lava_points[i].xyz);
        g += 1.0 - smoothstep(0.0, lava_points[i].w, d);
    }
    g = clamp(g, 0.0, 1.4) * (0.55 + 0.45 * clamp(0.3 - n.y, 0.0, 1.0));
    col += lava_color * g * (1.4 + night * 0.8);
    ALBEDO = col;
}";

    /// <summary>Lava glow sources: xyz = position, w = radius.</summary>
    public readonly List<Vector4> LavaPoints = new();

    private ShaderMaterial _material;

    public void Build(RandomNumberGenerator rng)
    {
        _material = new ShaderMaterial { Shader = new Shader { Code = CloudShader } };
        var transforms = new List<Transform3D>();

        // Near bank hugging the cliff — billows up highest under the blossom tree side.
        for (int i = 0; i < 46; i++)
        {
            float x = rng.RandfRange(-90, 90);
            float rise = Mathf.Clamp(1f - Mathf.Abs(x + 12f) / 45f, 0f, 1f);
            var c = new Vector3(x, rng.RandfRange(-34, -26) + rise * 9f, rng.RandfRange(-40, -70));
            Cluster(rng, c, rng.RandfRange(12, 20) + rise * 6f, transforms);
        }
        // Middle field.
        for (int i = 0; i < 70; i++)
        {
            var c = new Vector3(rng.RandfRange(-300, 300), rng.RandfRange(-42, -28), rng.RandfRange(-70, -360));
            Cluster(rng, c, rng.RandfRange(22, 40), transforms);
        }
        // Far horizon: big, flat banks fading into fog.
        for (int i = 0; i < 60; i++)
        {
            var c = new Vector3(rng.RandfRange(-900, 900), rng.RandfRange(-50, -36), rng.RandfRange(-360, -1100));
            Cluster(rng, c, rng.RandfRange(45, 85), transforms);
        }
        // Flanks to the east and west beyond the side walls.
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 20; i++)
            {
                var c = new Vector3(side * rng.RandfRange(90, 300), rng.RandfRange(-55, -40), rng.RandfRange(-40, 150));
                Cluster(rng, c, rng.RandfRange(15, 30), transforms);
            }

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 14, Rings = 7 },
            InstanceCount = transforms.Count,
        };
        for (int i = 0; i < transforms.Count; i++) mm.SetInstanceTransform(i, transforms[i]);
        AddChild(new MultiMeshInstance3D
        {
            Name = "Clouds",
            Multimesh = mm,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        GD.Print($"[CloudSea] {transforms.Count} cloud puffs");
    }

    /// <summary>One cumulus: a wide flat base of big puffs topped by smaller billows.</summary>
    private static void Cluster(RandomNumberGenerator rng, Vector3 center, float radius, List<Transform3D> output)
    {
        int count = rng.RandiRange(6, 10);
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / count;
            var offset = new Vector3(rng.RandfRange(-1f, 1f) * radius, t * radius * 0.55f, rng.RandfRange(-0.6f, 0.6f) * radius);
            float size = radius * rng.RandfRange(0.7f, 1.15f) * (1f - t * 0.45f) * (1f - Mathf.Abs(offset.X) / radius * 0.3f);
            var basis = Basis.Identity.Scaled(new Vector3(size * 1.15f, size * 0.78f, size));
            output.Add(new Transform3D(basis, center + offset));
        }
    }

    public void ApplyLava()
    {
        var pts = new Vector4[MaxLavaPoints];
        int n = Mathf.Min(LavaPoints.Count, MaxLavaPoints);
        for (int i = 0; i < n; i++) pts[i] = LavaPoints[i];
        _material.SetShaderParameter("lava_points", pts);
        _material.SetShaderParameter("lava_count", n);
    }
}
