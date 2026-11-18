using System.Collections.Generic;
using Godot;

namespace KatrinasGame;

/// <summary>The volcano on the north-west horizon (art pass, round 2): a faceted, ridged cone
/// built in code, with gullies running down its flanks, a shoulder peak, a crater breached
/// toward the home cliffs, and lava pouring out of the breach in glowing ribbons. Scenery only
/// (no collider); the Ember Wastes region will put real ground under it.</summary>
public partial class HomeCliffs
{
    private static readonly Color AshTop = new(0.25f, 0.18f, 0.2f);
    private static readonly Color Scorch = new(0.32f, 0.1f, 0.06f);

    private void BuildVolcano()
    {
        var root = new Node3D { Name = "Volcano", Position = new Vector3(-380, -70, -950) };
        AddChild(root);
        var rng = new RandomNumberGenerator { Seed = 4711 };
        // Direction from the volcano toward home, as atan2(x, z): the breach and the lava face us.
        float home = Mathf.Atan2(380f, 950f);

        var main = new Cone
        {
            Height = 380, BaseRadius = 340, RimRadius = 40, Sectors = 34, Rings = 11,
            BreachAngle = home, BreachDepth = 34, CraterDepth = 30, Ridges = 0.12f,
        };
        root.AddChild(main.Build(rng));
        // A lower, older shoulder peak to break the symmetric silhouette.
        var shoulder = new Cone
        {
            Height = 210, BaseRadius = 210, RimRadius = 26, Sectors = 22, Rings = 7,
            BreachAngle = home + 1.3f, BreachDepth = 12, CraterDepth = 10, Ridges = 0.14f, Seed = 9,
        };
        var sh = shoulder.Build(rng);
        sh.Position = new Vector3(-190, -10, 70);
        root.AddChild(sh);

        // Lava pool in the crater.
        var lavaFlat = new StandardMaterial3D
        {
            AlbedoColor = Magma, EmissionEnabled = true, Emission = Magma, EmissionEnergyMultiplier = 6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 31, BottomRadius = 31, Height = 2, RadialSegments = 12 },
            MaterialOverride = lavaFlat, Position = new Vector3(0, main.Height - main.CraterDepth + 3, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // Lava rivers spilling out of the breach and braiding down the flank.
        // Fog would wash the far-off cone into a flat pastel shape, so it's drawn unfogged and the
        // haze is painted into its colours instead (Cone.Haze).
        var lava = new ShaderMaterial { Shader = new Shader { Code = LavaShader.Replace("render_mode unshaded,", "render_mode unshaded, fog_disabled,") } };
        lava.SetShaderParameter("columns", 3f);
        lava.SetShaderParameter("speed", 0.18f);
        lava.SetShaderParameter("intensity", 3.5f);
        float[] starts = { 0f, -0.06f, 0.07f, 0.02f };
        float[] drifts = { 0.05f, -0.4f, 0.45f, 0.2f };   // radians of wander over the whole run: rivers fan out
        float[] lengths = { 0.85f, 0.6f, 0.7f, 0.45f };
        for (int i = 0; i < starts.Length; i++)
            root.AddChild(main.Ribbon(home + starts[i], lengths[i], 7f + i % 2 * 3f, drifts[i], lava, rng));

        root.AddChild(new OmniLight3D { Position = new Vector3(0, main.Height + 20, 0), LightColor = Magma, LightEnergy = 8f, OmniRange = 400f });
        var breachDir = new Vector3(Mathf.Sin(home), 0, Mathf.Cos(home));
        root.AddChild(new OmniLight3D { Position = breachDir * 150 + new Vector3(0, 220, 0), LightColor = Magma, LightEnergy = 4f, OmniRange = 220f });
        // Smoke column and a far-off rumble.
        Particles(40, 20f, new Vector3(20, 5, 20), root.Position + new Vector3(0, 390, 0),
            new Color(0.25f, 0.2f, 0.25f, 0.5f), 40f, 90f, new Vector3(3, 8, 0));
        Sfx.Loop(root, "amb_lava", new Vector3(0, 300, 0), 6f, 1400f, 140f);
    }

    /// <summary>A procedural volcanic cone: rings from base to rim, sectors around, a concave
    /// profile (gentle foot, steep summit), radial ridges and gullies, a crater with a breach.
    /// Flat-shaded, coloured per face (obsidian below, ash near the top, scorched at the breach).</summary>
    private sealed class Cone
    {
        public float Height, BaseRadius, RimRadius, BreachAngle, BreachDepth, CraterDepth, Ridges;
        /// <summary>Distance haze painted into the colours (the cone is drawn without fog).</summary>
        public Color Haze = new(0.42f, 0.24f, 0.3f);
        public float HazeAmount = 0.3f;
        public int Sectors, Rings, Seed = 1;

        /// <summary>Surface point at angle a (atan2(x,z)) and u: 1 at the foot, 0 at the rim.</summary>
        public Vector3 Surface(float a, float u)
        {
            float ridge = 1f + Ridges * (Mathf.Sin(a * 5f + Seed) * 0.6f + Mathf.Sin(a * 11f + Seed * 2.3f) * 0.4f) * Mathf.Clamp(u * 1.6f, 0.15f, 1f);
            float r = (RimRadius + (BaseRadius - RimRadius) * u) * ridge;
            float y = Height * Mathf.Pow(1f - u, 1.8f) - Breach(a) * Mathf.Clamp(1f - u * 5f, 0f, 1f);
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
        }

        private float Breach(float a)
        {
            float d = Mathf.Wrap(a - BreachAngle, -Mathf.Pi, Mathf.Pi);
            return BreachDepth * Mathf.Exp(-(d / 0.3f) * (d / 0.3f));
        }

        public MeshInstance3D Build(RandomNumberGenerator rng)
        {
            // Grid of jittered points: rings 0..Rings (foot..rim), then one inner crater ring.
            var pts = new Vector3[Rings + 2, Sectors];
            for (int j = 0; j <= Rings; j++)
            {
                float u = 1f - (float)j / Rings;
                for (int k = 0; k < Sectors; k++)
                {
                    float a = (k + (j % 2) * 0.5f) / Sectors * Mathf.Tau + rng.RandfRange(-0.09f, 0.09f);
                    var p = Surface(a, u);
                    if (j > 0 && j < Rings)
                    {
                        p.Y += rng.RandfRange(-1f, 1f) * Height * 0.02f;
                        p *= new Vector3(1, 1, 1) + new Vector3(1, 0, 1) * rng.RandfRange(-0.05f, 0.05f);
                    }
                    pts[j, k] = p;
                }
            }
            for (int k = 0; k < Sectors; k++)
            {
                var rim = pts[Rings, k];
                pts[Rings + 1, k] = new Vector3(rim.X * 0.72f, Height - CraterDepth, rim.Z * 0.72f);
            }

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            void Tri(Vector3 a, Vector3 b, Vector3 c, float u)
            {
                var n = (b - a).Cross(c - a).Normalized();
                if (n.Y < 0) { (b, c) = (c, b); n = -n; }
                // Ash on the upper slopes, lighter on faces turned up, scorched near the breach.
                var centre = (a + b + c) / 3f;
                float top = Mathf.Clamp((centre.Y / Height - 0.45f) / 0.5f, 0f, 1f);
                var col = Obsidian.Lerp(AshTop, top * 0.8f);
                col = col.Lerp(col.Lightened(0.25f), Mathf.Clamp((n.Y - 0.55f) * 2f, 0f, 1f));
                float ca = Mathf.Atan2(centre.X, centre.Z);
                float near = Mathf.Exp(-Mathf.Pow(Mathf.Wrap(ca - BreachAngle, -Mathf.Pi, Mathf.Pi) / 0.25f, 2)) * top;
                col = col.Lerp(Scorch, near * 0.7f);
                // Haze thickest at the foot (it sits in the cloud sea), thinning toward the summit.
                col = col.Lerp(Haze, HazeAmount * (1.4f - centre.Y / Height));
                foreach (var v in new[] { a, b, c })
                {
                    st.SetColor(col);
                    st.SetNormal(n);
                    st.AddVertex(v);
                }
            }
            for (int j = 0; j <= Rings; j++)
            {
                float u = j < Rings ? 1f - (j + 0.5f) / Rings : -1f; // crater wall faces inward
                for (int k = 0; k < Sectors; k++)
                {
                    int k1 = (k + 1) % Sectors;
                    // Rows are offset by half a sector on odd rings: stitch accordingly.
                    bool odd = j % 2 == 1;
                    var a0 = pts[j, k]; var a1 = pts[j, k1];
                    var b0 = pts[j + 1, k]; var b1 = pts[j + 1, k1];
                    if (!odd) { Tri(a0, a1, b0, u); Tri(a1, b1, b0, u); }
                    else { Tri(a0, a1, b1, u); Tri(a0, b1, b0, u); }
                }
            }
            var mesh = st.Commit();
            var mat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
                SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
                Roughness = 1f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                DisableFog = true,
                RimEnabled = true, Rim = 0.5f, RimTint = 0.8f,
            };
            return new MeshInstance3D { Name = "Cone", Mesh = mesh, MaterialOverride = mat };
        }

        /// <summary>A strip of lava from the rim (at angle a) down a fraction of the flank,
        /// wandering a little and widening as it goes. UV.y runs 0 at the top to 1 at the toe.</summary>
        public MeshInstance3D Ribbon(float a, float length, float width, float drift, Material mat, RandomNumberGenerator rng)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            int steps = 26;
            var left = new List<Vector3>();
            var right = new List<Vector3>();
            float angle = a;
            for (int i = 0; i <= steps; i++)
            {
                float u = 0.01f + length * i / steps;
                if (i > 0) angle += drift / steps * (0.5f + 1.5f * i / steps) + rng.RandfRange(-0.02f, 0.02f);
                var p = Surface(angle, u);
                var down = (Surface(angle, u + 0.02f) - p).Normalized();
                var side = new Vector3(Mathf.Cos(angle), 0, -Mathf.Sin(angle));
                var normal = side.Cross(down).Normalized();
                if (normal.Dot(new Vector3(p.X, 0, p.Z)) < 0) normal = -normal;
                float w = width * (1f + u * 4f);
                var c = p + normal * (3f + u * 6f);
                left.Add(c - side * w * 0.5f);
                right.Add(c + side * w * 0.5f);
            }
            for (int i = 0; i < steps; i++)
            {
                float v0 = (float)i / steps, v1 = (float)(i + 1) / steps;
                void V(Vector3 p, float x, float y) { st.SetUV(new Vector2(x, y)); st.AddVertex(p); }
                V(left[i], 0, v0); V(right[i], 1, v0); V(left[i + 1], 0, v1);
                V(right[i], 1, v0); V(right[i + 1], 1, v1); V(left[i + 1], 0, v1);
            }
            return new MeshInstance3D
            {
                Name = "LavaRiver", Mesh = st.Commit(), MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
        }
    }
}
