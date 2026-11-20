using Godot;

namespace KatrinasGame;

/// <summary>Navigation mesh for the enemies (server only: the AI runs there). Baked at runtime
/// from the static colliders of the generated world (terrain, rocks, trees, the shack, Ashby,
/// the bandit camp, placed buildings), and baked again a moment after a building goes up or
/// comes down. Enemy.Steer asks for paths; until the first bake finishes they walk straight.</summary>
public partial class WorldNav : NavigationRegion3D
{
    public const string SourceGroup = "nav_source";

    /// <summary>The home region (the plateau, the vale and Ashby).</summary>
    public static WorldNav Instance { get; private set; }
    /// <summary>Every region (home, and the forest and mine to the west).</summary>
    public static readonly System.Collections.Generic.List<WorldNav> All = new();

    /// <summary>The plateau, the vale and Ashby (enemies never leave Wildlife's world box).</summary>
    public static readonly Aabb HomeBox = new(new Vector3(Wildlife.WorldMinX - 4, -3, Wildlife.WorldMinZ - 4),
        new Vector3(Wildlife.WorldMaxX - Wildlife.WorldMinX + 8, 14, Wildlife.WorldMaxZ - Wildlife.WorldMinZ + 8));
    /// <summary>The Forest Road and the Hollow Mine (HomeCliffs.Mine).</summary>
    public static readonly Aabb WestBox = new(new Vector3(HomeCliffs.MineBox.Position.X - 2, -3, HomeCliffs.ForestBounds.Position.Y - 8),
        new Vector3(HomeCliffs.ForestBounds.End.X - HomeCliffs.MineBox.Position.X + 6, 9.5f, HomeCliffs.MineBox.End.Y - HomeCliffs.ForestBounds.Position.Y + 10));

    /// <summary>What this region bakes.</summary>
    public Aabb BakeBox { get; set; } = HomeBox;

    /// <summary>The baked region this point is in (null before its first bake, or outside them all).</summary>
    public static WorldNav For(Vector3 pos)
    {
        foreach (var n in All)
            if (n.Ready && IsInstanceValid(n) && n.BakeBox.Grow(-1f).HasPoint(new Vector3(pos.X, n.BakeBox.Position.Y + 1f, pos.Z))) return n;
        return null;
    }

    /// <summary>Buildings changed: every region bakes again soon.</summary>
    public static void RebakeAll()
    {
        foreach (var n in All) if (IsInstanceValid(n)) n.RequestRebake();
    }
    /// <summary>A navmesh is in use (the first bake finished).</summary>
    public bool Ready { get; private set; }
    public int Bakes { get; private set; }

    private float _rebakeIn = -1f;
    private bool _pending;

    public Rid Map => GetWorld3D().NavigationMap;

    public override void _EnterTree()
    {
        if (BakeBox == HomeBox) Instance = this;
        if (!All.Contains(this)) All.Add(this);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        All.Remove(this);
    }

    public override void _Ready()
    {
        NavigationMesh = new NavigationMesh
        {
            AgentRadius = 0.45f,
            AgentHeight = 1.6f,
            AgentMaxClimb = 0.45f,
            AgentMaxSlope = 40f,
            CellSize = 0.25f,
            CellHeight = 0.25f,
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometryCollisionMask = GameState.LayerWorld | GameState.LayerProps,
            GeometrySourceGeometryMode = NavigationMesh.SourceGeometryMode.GroupsWithChildren,
            GeometrySourceGroupName = SourceGroup,
            FilterBakingAabb = BakeBox,
        };
        BakeFinished += OnBakeFinished;
        CallDeferred(MethodName.Rebake);

        var gs = GameState.Instance;
        gs.HomeTierChanged += OnHomeTier;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && GameState.Instance != null) GameState.Instance.HomeTierChanged -= OnHomeTier;
    }

    private void OnHomeTier(int tier) => RequestRebake();

    /// <summary>Bake again soon (buildings changed). Several changes in a row make one bake.</summary>
    public void RequestRebake() => _rebakeIn = 1.5f;

    private void Rebake()
    {
        if (IsBaking()) { _pending = true; return; }
        BakeNavigationMesh(true);
    }

    private void OnBakeFinished()
    {
        Bakes++;
        Ready = true;
        GD.Print($"[WorldNav] {Name} navmesh baked ({NavigationMesh.GetPolygonCount()} polygons)");
        if (_pending) { _pending = false; Rebake(); }
    }

    public override void _Process(double delta)
    {
        if (_rebakeIn < 0) return;
        _rebakeIn -= (float)delta;
        if (_rebakeIn < 0) Rebake();
    }

    /// <summary>A walking path (empty before the first bake).</summary>
    public Vector3[] Path(Vector3 from, Vector3 to) =>
        Ready ? NavigationServer3D.MapGetPath(Map, from, to, true) : System.Array.Empty<Vector3>();
}
