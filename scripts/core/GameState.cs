using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KatrinasGame;

/// <summary>Autoload singleton: inventory, time, equipment, placed buildings, input map, save/load.</summary>
public partial class GameState : Node
{
    public static GameState Instance { get; private set; }

    public const uint LayerWorld = 1;
    public const uint LayerProps = 2;
    public const uint LayerPlayer = 4;

    public const string SavePath = "user://save.json";

    public Inventory Inventory { get; } = new();

    /// <summary>0..1, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.</summary>
    public float TimeOfDay { get; set; } = 0.735f;
    public int Day { get; set; } = 1;

    /// <summary>Equipped weapon item id; "" = bare fists.</summary>
    public string Equipped { get; set; } = "";

    /// <summary>True while a menu is open: frees the mouse and blocks movement/attacks.</summary>
    public bool UiOpen { get; private set; }

    public PlayerController Player { get; set; }
    /// <summary>Parent node for placed buildings (set by the world scene).</summary>
    public Node3D BuildingsRoot { get; set; }
    public Vector3 SpawnPoint { get; set; } = Vector3.Zero;
    public Vector3 ClaimCenter { get; set; } = Vector3.Zero;
    public float ClaimRadius { get; set; } = 30f;

    public readonly List<PlacedBuilding> PlacedBuildings = new();

    public class PlacedBuilding
    {
        public string Id;
        public Vector3 Position;
        public float RotationY;
        public Node3D Node;
    }

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        RegisterInputActions();
        ItemDb.Load();
    }

    public override void _Ready() => DevAutotest.MaybeStart(this);

    public void SetUiOpen(bool open)
    {
        UiOpen = open;
        Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("save_game")) { SaveGame(); GetViewport().SetInputAsHandled(); }
        else if (e.IsActionPressed("load_game")) { LoadGame(); GetViewport().SetInputAsHandled(); }
        else if (e.IsActionPressed("ui_cancel"))
        {
            if (UiOpen) Hud.Instance?.CloseMenus();
            else Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    // ---------------------------------------------------------------- weapons

    /// <summary>Hotbar: slot 1 is always fists, then owned weapons by damage.</summary>
    public List<string> WeaponSlots()
    {
        var slots = new List<string> { "" };
        slots.AddRange(Inventory.Items.Keys
            .Select(ItemDb.Get)
            .Where(d => d != null && d.IsWeapon)
            .OrderBy(d => d.Damage)
            .Select(d => d.Id));
        return slots;
    }

    // ---------------------------------------------------------------- buildings

    public Node3D PlaceBuilding(BuildingDef def, Vector3 position, float rotationY)
    {
        if (def?.Scene == null || BuildingsRoot == null) return null;
        var node = def.Scene.Instantiate<Node3D>();
        BuildingsRoot.AddChild(node);
        node.GlobalPosition = position;
        node.Rotation = new Vector3(0, rotationY, 0);
        PlacedBuildings.Add(new PlacedBuilding { Id = def.Id, Position = position, RotationY = rotationY, Node = node });
        return node;
    }

    private void ClearBuildings()
    {
        foreach (var b in PlacedBuildings)
            if (IsInstanceValid(b.Node)) b.Node.QueueFree();
        PlacedBuildings.Clear();
    }

    // ---------------------------------------------------------------- save / load

    public void SaveGame()
    {
        var inv = new Godot.Collections.Dictionary();
        foreach (var kv in Inventory.Items) inv[kv.Key] = kv.Value;

        var buildings = new Godot.Collections.Array();
        foreach (var b in PlacedBuildings)
            buildings.Add(new Godot.Collections.Dictionary
            {
                ["id"] = b.Id, ["x"] = b.Position.X, ["y"] = b.Position.Y, ["z"] = b.Position.Z, ["rot"] = b.RotationY,
            });

        var data = new Godot.Collections.Dictionary
        {
            ["version"] = 1,
            ["time"] = TimeOfDay,
            ["day"] = Day,
            ["equipped"] = Equipped,
            ["inventory"] = inv,
            ["buildings"] = buildings,
        };
        if (Player != null)
        {
            var p = Player.GlobalPosition;
            data["player"] = new Godot.Collections.Array { p.X, p.Y, p.Z };
        }

        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file == null) { Hud.Toast("Save failed!"); return; }
        file.StoreString(Json.Stringify(data, "  "));
        Hud.Toast("Game saved");
    }

    public void LoadGame()
    {
        if (!FileAccess.FileExists(SavePath)) { Hud.Toast("No save found"); return; }
        using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) { Hud.Toast("Save file is corrupt"); return; }
        var data = parsed.AsGodotDictionary();

        TimeOfDay = data.TryGetValue("time", out var t) ? (float)t.AsDouble() : 0.3f;
        Day = data.TryGetValue("day", out var d) ? d.AsInt32() : 1;

        Inventory.Clear();
        if (data.TryGetValue("inventory", out var invV))
            foreach (var kv in invV.AsGodotDictionary())
                Inventory.Add(kv.Key.AsString(), kv.Value.AsInt32());

        Equipped = data.TryGetValue("equipped", out var eq) ? eq.AsString() : "";
        if (!WeaponSlots().Contains(Equipped)) Equipped = "";

        ClearBuildings();
        if (data.TryGetValue("buildings", out var bv))
            foreach (var v in bv.AsGodotArray())
            {
                var b = v.AsGodotDictionary();
                var def = ItemDb.GetBuilding(b["id"].AsString());
                var pos = new Vector3((float)b["x"].AsDouble(), (float)b["y"].AsDouble(), (float)b["z"].AsDouble());
                PlaceBuilding(def, pos, (float)b["rot"].AsDouble());
            }

        if (Player != null && data.TryGetValue("player", out var pv))
        {
            var a = pv.AsGodotArray();
            Player.Teleport(new Vector3((float)a[0].AsDouble(), (float)a[1].AsDouble(), (float)a[2].AsDouble()));
        }
        Hud.Toast("Game loaded");
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
        AddMouse("attack", MouseButton.Left);
        AddMouse("heavy_attack", MouseButton.Right);
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
