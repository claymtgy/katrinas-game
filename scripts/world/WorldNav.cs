using Godot;

namespace KatrinasGame;

/// <summary>Navigation mesh for the enemies (server only: the AI runs there). Baked at runtime
/// from the static colliders of the generated world (terrain, rocks, trees, the shack, Ashby,
/// the bandit camp, placed buildings), and baked again a moment after a building goes up or
/// comes down. Enemy.Steer asks for paths; until the first bake finishes they walk straight.</summary>
public partial class WorldNav : NavigationRegion3D
{
    public const string SourceGroup = "nav_source";

    public static WorldNav Instance { get; private set; }
    /// <summary>A navmesh is in use (the first bake finished).</summary>
    public bool Ready { get; private set; }
    public int Bakes { get; private set; }

    private float _rebakeIn = -1f;
    private bool _pending;

    public Rid Map => GetWorld3D().NavigationMap;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
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
            // The plateau, the vale and Ashby (enemies never leave Wildlife's world box).
            FilterBakingAabb = new Aabb(new Vector3(Wildlife.WorldMinX - 4, -3, Wildlife.WorldMinZ - 4),
                new Vector3(Wildlife.WorldMaxX - Wildlife.WorldMinX + 8, 14, Wildlife.WorldMaxZ - Wildlife.WorldMinZ + 8)),
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
        GD.Print($"[WorldNav] Navmesh baked ({NavigationMesh.GetPolygonCount()} polygons)");
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
