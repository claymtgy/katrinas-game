using System.Collections.Generic;
using System.Linq;
using Godot;
using GArray = Godot.Collections.Array;
using GDict = Godot.Collections.Dictionary;

namespace KatrinasGame;

/// <summary>Autoload singleton: the shared stockpile, time, placed buildings, the player registry,
/// input map and save/load. Only the server (host / dedicated / single player) saves; see Net.</summary>
public partial class GameState : Node
{
    public static GameState Instance { get; private set; }

    public const uint LayerWorld = 1;
    public const uint LayerProps = 2;
    public const uint LayerPlayer = 4;

    public const int SaveVersion = 2;
    private const string LegacySavePath = "user://save.json";

    /// <summary>The shared base stockpile (GDD §16). Server-owned; clients mirror it.</summary>
    public Inventory Inventory { get; } = new();

    /// <summary>0..1, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.</summary>
    public float TimeOfDay { get; set; } = 0.735f;
    public int Day { get; set; } = 1;
    /// <summary>Days since the world began (Day + TimeOfDay): crops grow against this.</summary>
    public float AbsoluteTime => Day + TimeOfDay;

    /// <summary>1 = the starting shack, 2 = the cottage. Server-owned, saved.</summary>
    public int HomeTier { get; private set; } = 1;
    public event System.Action<int> HomeTierChanged;

    /// <summary>True while a menu is open: frees the mouse and blocks movement/attacks.</summary>
    public bool UiOpen { get; private set; }

    /// <summary>This machine's player (null on a dedicated server or before spawning).</summary>
    public PlayerController LocalPlayer { get; set; }
    /// <summary>Every player in the world, by network peer id (1 = host / single player).</summary>
    public readonly Dictionary<int, PlayerController> Players = new();

    /// <summary>Parents set by the world scene.</summary>
    public Node3D BuildingsRoot { get; set; }
    public Node3D PlayersRoot { get; set; }
    /// <summary>Where new players appear and respawn until they pray at a shrine.</summary>
    public Vector3 WorldSpawn { get; set; } = Vector3.Zero;
    public Vector3 ClaimCenter { get; set; } = Vector3.Zero;
    public float ClaimRadius { get; set; } = 30f;

    public readonly List<PlacedBuilding> PlacedBuildings = new();
    private int _nextBuildingId = 1;

    /// <summary>Blossom sanctuaries' light: enemies won't enter or hunt inside (center, radius).</summary>
    public readonly List<(Vector3 Center, float Radius)> SafeZones = new();

    public bool InSafeZone(Vector3 pos) =>
        SafeZones.Any(z => new Vector2(pos.X - z.Center.X, pos.Z - z.Center.Z).Length() < z.Radius);

    /// <summary>Server: saved per-player data by name (position, respawn shrine).</summary>
    public readonly Dictionary<string, GDict> PlayerRecords = new();

    public class PlacedBuilding
    {
        public int NetId;
        public string Id;
        public Vector3 Position;
        public float RotationY;
        public Node3D Node;

        /// <summary>Saved / replicated state of buildings that have some (farm plots).</summary>
        public Variant State => Node is INetState s && GodotObject.IsInstanceValid(Node) ? s.GetState() : default;

        public void ApplyState(Variant state)
        {
            if (Node is INetState s && GodotObject.IsInstanceValid(Node) && state.VariantType != Variant.Type.Nil) s.ApplyState(state);
        }
    }

    public void SetHomeTier(int tier)
    {
        tier = Mathf.Clamp(tier, 1, 2);
        if (tier == HomeTier) return;
        HomeTier = tier;
        ClaimRadius = tier >= 2 ? 42f : 30f;
        HomeTierChanged?.Invoke(tier);
    }

    public static string SavePath => $"user://saves/{Net.Instance?.Config.World ?? "home"}.json";

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        RegisterInputActions();
        ItemDb.Load();
    }

    public PlayerController GetPlayer(int peerId) =>
        Players.TryGetValue(peerId, out var p) && IsInstanceValid(p) ? p : null;

    public void SetUiOpen(bool open)
    {
        UiOpen = open;
        Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    /// <summary>Forget everything from the last session (back at the main menu).</summary>
    public void ResetSession()
    {
        Players.Clear();
        LocalPlayer = null;
        PlacedBuildings.Clear();
        PlayerRecords.Clear();
        SafeZones.Clear();
        Inventory.Clear();
        HomeTier = 1;
        ClaimRadius = 30f;
        HomeTierChanged = null;
        TimeOfDay = 0.735f;
        Day = 1;
        _nextBuildingId = 1;
        UiOpen = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        var net = Net.Instance;
        if (!net.InSession) return;
        if (e.IsActionPressed("save_game"))
        {
            if (net.IsServer) SaveGame();
            else Hud.Toast("Only the host can save. The world saves when everyone sleeps.");
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("load_game"))
        {
            if (net.IsServer) LoadGame();
            else Hud.Toast("Only the host can load the world.");
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed("ui_cancel"))
        {
            if (UiOpen) Hud.Instance?.CloseMenus();
            else Hud.Instance?.OpenGameMenu();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------------------------------------------------------- weapons

    /// <summary>Hotbar for a player: slot 1 is fists, then the weapon in hand and the weapons
    /// in the shared stockpile, by damage.</summary>
    public List<string> WeaponSlots(PlayerController player)
    {
        var ids = Inventory.Items.Keys.ToList();
        if (player != null && player.Equipped != "") ids.Add(player.Equipped);
        var slots = new List<string> { "" };
        slots.AddRange(ids.Distinct()
            .Select(ItemDb.Get)
            .Where(d => d != null && d.IsWeapon)
            .OrderBy(d => d.Damage)
            .Select(d => d.Id));
        return slots;
    }

    // ---------------------------------------------------------------- buildings

    /// <summary>Spawns a building. The server picks the id (netId 0); clients pass the server's id.
    /// Callers on the server must also tell clients (Net does).</summary>
    public PlacedBuilding PlaceBuilding(BuildingDef def, Vector3 position, float rotationY, int netId = 0, Variant state = default)
    {
        if (def?.Scene == null || BuildingsRoot == null) return null;
        if (netId == 0) netId = _nextBuildingId++;
        else _nextBuildingId = Mathf.Max(_nextBuildingId, netId + 1);
        var node = def.Scene.Instantiate<Node3D>();
        node.Name = $"B{netId}";
        BuildingsRoot.AddChild(node);
        node.GlobalPosition = position;
        node.Rotation = new Vector3(0, rotationY, 0);
        var b = new PlacedBuilding { NetId = netId, Id = def.Id, Position = position, RotationY = rotationY, Node = node };
        PlacedBuildings.Add(b);
        b.ApplyState(state);
        return b;
    }

    public PlacedBuilding FindBuilding(int netId) => PlacedBuildings.FirstOrDefault(b => b.NetId == netId);

    public PlacedBuilding FindBuilding(Node node) => PlacedBuildings.FirstOrDefault(b => b.Node == node);

    public void RemoveBuilding(int netId)
    {
        var b = FindBuilding(netId);
        if (b == null) return;
        PlacedBuildings.Remove(b);
        if (IsInstanceValid(b.Node))
        {
            b.Node.GetParent()?.RemoveChild(b.Node);
            b.Node.QueueFree();
        }
    }

    public void ClearBuildings()
    {
        foreach (var b in PlacedBuildings)
            if (IsInstanceValid(b.Node))
            {
                // Detach now so a rebuilt building can reuse the name this frame.
                b.Node.GetParent()?.RemoveChild(b.Node);
                b.Node.QueueFree();
            }
        PlacedBuildings.Clear();
    }

    // ---------------------------------------------------------------- save / load (server only)

    /// <summary>Server: remember a player's position and shrine for when they rejoin.</summary>
    public void RememberPlayer(PlayerController p) =>
        PlayerRecords[p.DisplayName] = new GDict { ["pos"] = p.GlobalPosition, ["spawn"] = p.SpawnPoint };

    public void SaveGame(bool silent = false)
    {
        if (!Net.Instance.IsServer) return;
        foreach (var p in Players.Values) RememberPlayer(p);

        // Weapons in players' hands belong to the stockpile.
        var inv = Inventory.ToDictionary();
        foreach (var p in Players.Values)
            if (p.Equipped != "") inv[p.Equipped] = (inv.TryGetValue(p.Equipped, out var n) ? n.AsInt32() : 0) + 1;

        var buildings = new GArray();
        foreach (var b in PlacedBuildings)
        {
            var entry = new GDict
            {
                ["id"] = b.Id, ["x"] = b.Position.X, ["y"] = b.Position.Y, ["z"] = b.Position.Z, ["rot"] = b.RotationY,
            };
            var state = b.State;
            if (state.VariantType != Variant.Type.Nil) entry["state"] = state;
            buildings.Add(entry);
        }

        var players = new GDict();
        foreach (var kv in PlayerRecords)
        {
            var pos = kv.Value["pos"].AsVector3();
            var spawn = kv.Value["spawn"].AsVector3();
            players[kv.Key] = new GDict
            {
                ["pos"] = new GArray { pos.X, pos.Y, pos.Z },
                ["spawn"] = new GArray { spawn.X, spawn.Y, spawn.Z },
            };
        }

        var data = new GDict
        {
            ["version"] = SaveVersion,
            ["time"] = TimeOfDay,
            ["day"] = Day,
            ["inventory"] = inv,
            ["buildings"] = buildings,
            ["players"] = players,
            ["home_tier"] = HomeTier,
            ["camps"] = Wildlife.Instance?.SaveCamps() ?? new GArray(),
        };

        DirAccess.MakeDirRecursiveAbsolute(SavePath.GetBaseDir());
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null) { Net.Instance.ToastAll("Save failed!"); return; }
        file.StoreString(Json.Stringify(data, "  "));
        GD.Print($"[GameState] Saved {ProjectSettings.GlobalizePath(SavePath)}");
        if (!silent) Net.Instance.ToastAll("Game saved");
    }

    /// <summary>Server: load the world file. resync = push it to connected clients (F9 mid-game).</summary>
    public void LoadGame(bool silent = false, bool resync = true)
    {
        if (!Net.Instance.IsServer) return;
        string path = SavePath;
        if (!FileAccess.FileExists(path) && Net.Instance.Config.World == "home" && FileAccess.FileExists(LegacySavePath))
            path = LegacySavePath;
        if (!FileAccess.FileExists(path)) { if (!silent) Hud.Toast("No save found"); return; }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) { Hud.Toast("Save file is corrupt"); return; }
        var data = parsed.AsGodotDictionary();

        TimeOfDay = data.TryGetValue("time", out var t) ? (float)t.AsDouble() : 0.3f;
        Day = data.TryGetValue("day", out var d) ? d.AsInt32() : 1;

        // Held weapons were saved into the stockpile; empty everyone's hands.
        foreach (var p in Players.Values.Where(p => p.Equipped != "")) Net.Instance.SetEquipped(p, "");
        Inventory.ReplaceAll(data.TryGetValue("inventory", out var invV) ? invV.AsGodotDictionary() : new GDict());

        ClearBuildings();
        _nextBuildingId = 1;
        if (data.TryGetValue("buildings", out var bv))
            foreach (var v in bv.AsGodotArray())
            {
                var b = v.AsGodotDictionary();
                var def = ItemDb.GetBuilding(b["id"].AsString());
                var pos = new Vector3((float)b["x"].AsDouble(), (float)b["y"].AsDouble(), (float)b["z"].AsDouble());
                PlaceBuilding(def, pos, (float)b["rot"].AsDouble(), 0, b.TryGetValue("state", out var st) ? st : default);
            }

        SetHomeTier(data.TryGetValue("home_tier", out var tier) ? tier.AsInt32() : 1);
        Wildlife.Instance?.LoadCamps(data.TryGetValue("camps", out var camps) ? camps.AsGodotArray() : null);

        PlayerRecords.Clear();
        if (data.TryGetValue("players", out var pv))
            foreach (var kv in pv.AsGodotDictionary())
            {
                var rec = kv.Value.AsGodotDictionary();
                PlayerRecords[kv.Key.AsString()] = new GDict { ["pos"] = ToVec(rec["pos"]), ["spawn"] = ToVec(rec["spawn"]) };
            }
        else if (data.TryGetValue("player", out var legacy)) // version 1: one player
            PlayerRecords[Net.Instance.LocalName] = new GDict { ["pos"] = ToVec(legacy), ["spawn"] = WorldSpawn };

        // Put connected players back where the save had them.
        foreach (var p in Players.Values)
            if (PlayerRecords.TryGetValue(p.DisplayName, out var rec))
            {
                p.SpawnPoint = rec["spawn"].AsVector3();
                Net.Instance.ServerTeleport(p, rec["pos"].AsVector3());
            }

        if (resync) Net.Instance.ResyncAll();
        GD.Print($"[GameState] Loaded {ProjectSettings.GlobalizePath(path)}");
        if (!silent) Net.Instance.ToastAll("Game loaded");
    }

    private static Vector3 ToVec(Variant v)
    {
        var a = v.AsGodotArray();
        return new Vector3((float)a[0].AsDouble(), (float)a[1].AsDouble(), (float)a[2].AsDouble());
    }

    // ---------------------------------------------------------------- input map

    private static void RegisterInputActions()
    {
        AddKey("move_forward", Key.W);
        AddKey("move_back", Key.S);
        AddKey("move_left", Key.A);
        AddKey("move_right", Key.D);
        AddKey("jump", Key.Space);
        AddKey("sprint", Key.Shift);
        AddKey("interact", Key.E);
        AddKey("toggle_view", Key.V);
        AddKey("build_mode", Key.B);
        AddKey("rotate_build", Key.R);
        AddKey("help", Key.F1);
        AddKey("save_game", Key.F5);
        AddKey("load_game", Key.F9);
        AddKey("player_list", Key.Tab);
        AddMouse("attack", MouseButton.Left);
        AddKey("heavy_attack", Key.Q);
        AddMouse("heavy_attack", MouseButton.Middle);
        AddMouse("block", MouseButton.Right);
        AddKey("eat", Key.F);
        AddMouse("next_build", MouseButton.WheelDown);
        AddMouse("prev_build", MouseButton.WheelUp);
        AddKey("next_build", Key.Period);
        AddKey("prev_build", Key.Comma);
        for (int i = 1; i <= 5; i++) AddKey($"slot_{i}", Key.Key0 + i);
    }

    private static void EnsureAction(string action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
    }

    private static void AddKey(string action, Key key)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    private static void AddMouse(string action, MouseButton button)
    {
        EnsureAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
