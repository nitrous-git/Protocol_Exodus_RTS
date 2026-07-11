using UnityEngine;

// owner of world-level update, equivalent of GamePanel
public sealed class MatchWorld : MonoBehaviour
{
    [Header("Services")]
    [SerializeField] private MonoBehaviour pathfindingServiceComponent;

    [Header("Input")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private CommandIssuer commandIssuer;

    [Header("Camera")]
    [SerializeField] private CameraController cameraController;

    [Header("Runtime Roots")]
    [SerializeField] private Transform unitsRoot;
    [SerializeField] private Transform buildingsRoot;
    [SerializeField] private Transform projectilesRoot;

    [Header("Spawn Points")]
    [SerializeField] private Transform playerSpawnPoint;

    public IPathfindingService PathfindingService { get; private set; }

    public Transform UnitsRoot => unitsRoot;
    public Transform BuildingsRoot => buildingsRoot;
    public Transform ProjectilesRoot => projectilesRoot;
    public Transform PlayerSpawnPoint => playerSpawnPoint;

    public FactionManager FactionManager { get; private set; }
    public ResourceNodeRepository ResourceNodeRepository { get; private set; }

    public CameraController CameraController => cameraController;

    public void ResolveServices()
    {
        PathfindingService = pathfindingServiceComponent as IPathfindingService;

        if (PathfindingService == null)
            Debug.LogError("MatchWorld is missing a valid IPathfindingService component.");
    }

    public void Initialize(
        GameContext gameContext,
        FactionManager factionManager,
        ResourceNodeRepository resourceNodeRepository,
        Faction playerFaction)
    {
        FactionManager = factionManager;
        ResourceNodeRepository = resourceNodeRepository;

        PathfindingService = pathfindingServiceComponent as IPathfindingService;

        if (PathfindingService == null)
            Debug.LogError("MatchWorld is missing a valid IPathfindingService component.");

        selectionManager?.Initialize(gameContext);
        commandIssuer?.Initialize(gameContext, playerFaction);
        cameraController?.Initialize(gameContext);
    }

    public void TickInput(float deltaTime)
    {
        selectionManager?.TickInput(deltaTime);
        commandIssuer?.TickInput(deltaTime);
    }

    public void TickSimulation(float deltaTime)
    {
        //ResourceNodeRepository?.Tick(deltaTime);
        FactionManager?.Tick(deltaTime);

        // Later:
        // FogOfWarController?.Tick(deltaTime);
        // ProjectileRepository?.Tick(deltaTime);
        // VisibilitySystem?.Tick(deltaTime);
    }

    public void TickLate(float deltaTime)
    {
        cameraController?.TickLate(deltaTime);
    }
}