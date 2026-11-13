using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Paths to the game's model files and helpers to instance them styled.</summary>
public static class Models
{
    public const string Dir = "res://assets/models/";
    public const string Shack = Dir + "shack.glb";
    public const string CherryTree = Dir + "cherry_tree.glb";
    public const string Hero = "res://assets/thirdparty/kaykit/Rogue.glb";
    public const string Sword = "res://assets/thirdparty/kaykit/sword_1handed.gltf";

    public static readonly string[] Pines = { Dir + "pine_01.glb", Dir + "pine_02.glb", Dir + "pine_03.glb" };
    public static readonly string[] Boulders =
        { Dir + "rock_boulder_01.glb", Dir + "rock_boulder_02.glb", Dir + "rock_boulder_03.glb", Dir + "rock_chunk_01.glb" };
    public static readonly string[] Slabs = { Dir + "rock_slab_01.glb", Dir + "rock_slab_02.glb" };
    public static readonly string[] Columns = { Dir + "rock_column_01.glb", Dir + "rock_column_02.glb", Dir + "rock_column_03.glb" };
    /// <summary>Jagged obsidian blades / crowns. Origin at the base, 1 m footprint, 1 m tall.</summary>
    public static readonly string[] Shards = { Dir + "rock_shard_01.glb", Dir + "rock_shard_02.glb" };
    public static readonly string[] Crags = { Dir + "rock_crag_01.glb", Dir + "rock_crag_02.glb", Dir + "rock_crag_03.glb" };
    public static readonly string[] AllRocks = Boulders.Concat(Slabs).Concat(Columns).ToArray();

    private static readonly Dictionary<string, PackedScene> Scenes = new();
    private static readonly Dictionary<string, Vector3[]> HullPoints = new();

    public static PackedScene Load(string path)
    {
        if (!Scenes.TryGetValue(path, out var scene))
        {
            scene = GD.Load<PackedScene>(path);
            if (scene == null) GD.PushError($"[Models] Missing model {path} (run the editor import once)");
            Scenes[path] = scene;
        }
        return scene;
    }

    /// <summary>Instance a model with stylized materials applied.</summary>
    public static Node3D Instance(string path, bool style = true)
    {
        var node = Load(path)?.Instantiate<Node3D>() ?? new Node3D { Name = "MissingModel" };
        if (style) StyleMaterials.Apply(node);
        return node;
    }

    /// <summary>Static rock with a convex collider matching its (scaled) mesh.</summary>
    public static StaticBody3D RockBody(string path, Vector3 size, uint layer = GameState.LayerWorld)
    {
        var body = new StaticBody3D { CollisionLayer = layer, CollisionMask = 0 };
        var model = Instance(path);
        model.Scale = size;
        body.AddChild(model);
        var points = Hull(path, model);
        if (points.Length > 3)
        {
            body.AddChild(new CollisionShape3D
            {
                Shape = new ConvexPolygonShape3D { Points = points.Select(p => p * size).ToArray() },
            });
        }
        return body;
    }

    private static Vector3[] Hull(string path, Node3D model)
    {
        if (HullPoints.TryGetValue(path, out var pts)) return pts;
        var mi = FindMesh(model);
        pts = mi?.Mesh?.GetFaces().Distinct().Select(v => mi.Transform * v).ToArray() ?? System.Array.Empty<Vector3>();
        HullPoints[path] = pts;
        return pts;
    }

    /// <summary>Uniformly scale a model (already in the tree) so its mesh height equals height.</summary>
    public static void FitHeight(Node3D model, float height)
    {
        model.Scale = Vector3.One;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var n in model.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)n;
            var box = mi.GlobalTransform * mi.GetAabb();
            minY = Mathf.Min(minY, box.Position.Y);
            maxY = Mathf.Max(maxY, box.End.Y);
        }
        float h = maxY - minY;
        if (h > 0.01f) model.Scale = Vector3.One * (height / h);
    }

    public static string Nature(string name) => $"res://assets/thirdparty/quaternius/nature/glTF/{name}.gltf";
    public static string Village(string name) => $"res://assets/thirdparty/quaternius/village/glTF/{name}.gltf";

    public static MeshInstance3D FindMesh(Node n)
    {
        if (n is MeshInstance3D mi) return mi;
        foreach (var c in n.GetChildren())
        {
            var found = FindMesh(c);
            if (found != null) return found;
        }
        return null;
    }
}
