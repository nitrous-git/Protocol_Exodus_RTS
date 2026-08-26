using System.Collections.Generic;
using UnityEngine;

// owner of world-level update, equivalent of GamePanel
public sealed class MatchWorld : MonoBehaviour
{
    [Header("Services")]
    [SerializeField] private MonoBehaviour pathfindingServiceComponent;
    [SerializeField] private TerrainGridSystem terrainGridSystem;

    [Header("Input")]
    [SerializeField] private PlayerInputBindings playerInputBindings = new();
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private CommandIssuer commandIssuer;

    [Header("Camera")]
    [SerializeField] private CameraController cameraController;

    [Header("Runtime Roots")]
    [SerializeField] private Transform unitsRoot;
    [SerializeField] private Transform buildingsRoot;
    [SerializeField] private Transform resourceNodesRoot;
    [SerializeField] private Transform projectilesRoot;

    [Header("Spawn Points")]
    [SerializeField] private List<Transform> factionSpawnPoints = new();

    [Header("Interaction Presentation")]
    [SerializeField] private List<TargetMarker> targetMarkers;
    [SerializeField] private Transform interactionMarkersRoot;

    [Header("Building Placement")]
    [SerializeField] private BuildingPlacementPreview buildingPlacementPreviewPrefab;

    private UnitDepenetrationSystem unitDepenetrationSystem;
    private CrowdAvoidanceSystem crowdAvoidanceSystem;

    public Transform UnitsRoot => unitsRoot;
    public Transform BuildingsRoot => buildingsRoot;
    public Transform ResourceNodesRoot => resourceNodesRoot;
    public Transform ProjectilesRoot => projectilesRoot;
    public IReadOnlyList<Transform> FactionSpawnPoints => factionSpawnPoints;
    public SelectionManager SelectionManager => selectionManager;
    public CommandIssuer CommandIssuer => commandIssuer;
    public CameraController CameraController => cameraController;
    public PlayerInputBindings PlayerInputBindings => playerInputBindings;

    public Faction PlayerFaction { get; private set; }
    public FactionManager FactionManager { get; private set; }
    public ResourceNodeRepository ResourceNodeRepository { get; private set; }
    public ProjectileManager ProjectileManager { get; private set; }
    public IPathfindingService PathfindingService { get; private set; }

    public List<TargetMarker> TargetMarkers => targetMarkers;
    public Transform InteractionMarkersRoot => interactionMarkersRoot;

    public TerrainGrid TerrainGrid => terrainGridSystem != null ? terrainGridSystem.Grid : null;

    public BuildingPlacementPreview BuildingPlacementPreviewPrefab => buildingPlacementPreviewPrefab;

    public void ResolveServices()
    {
        PathfindingService = pathfindingServiceComponent as IPathfindingService;

        if (PathfindingService == null)
            Debug.LogError("MatchWorld is missing a valid IPathfindingService component.");

        terrainGridSystem.Initialize();
    }

    public void Initialize(
        GameContext gameContext,
        FactionManager factionManager,
        ResourceNodeRepository resourceNodeRepository,
        ProjectileManager projectileManager,
        Faction playerFaction)
    {
        FactionManager = factionManager;
        ResourceNodeRepository = resourceNodeRepository;
        ProjectileManager = projectileManager;
        PlayerFaction = playerFaction;

        unitDepenetrationSystem = new UnitDepenetrationSystem(gameContext.AllUnits);
        crowdAvoidanceSystem = new CrowdAvoidanceSystem(gameContext);

        PathfindingService = pathfindingServiceComponent as IPathfindingService;

        if (PathfindingService == null)
            Debug.LogError("MatchWorld is missing a valid IPathfindingService component.");

        selectionManager?.Initialize(gameContext);
        commandIssuer?.Initialize(gameContext, playerFaction);
        cameraController?.Initialize();

        ResourceNodeRepository?.Initialize(resourceNodesRoot);
    }

    public void TickInput(float deltaTime)
    {
        PlayerFaction?.TickInput(deltaTime);
    }

    public void TickSimulation(float deltaTime)
    {
        FactionManager?.Tick(deltaTime);

        crowdAvoidanceSystem?.Tick(deltaTime);
        unitDepenetrationSystem?.Tick();

        ResourceNodeRepository?.Tick(deltaTime);
        ProjectileManager?.Tick(deltaTime);

        // Later:
        // FogOfWarController?.Tick(deltaTime);
        // VisibilitySystem?.Tick(deltaTime);
    }

    public void TickLate(float deltaTime)
    {
        FactionManager?.TickLate(deltaTime);
        cameraController?.TickLate(deltaTime);
    }
}