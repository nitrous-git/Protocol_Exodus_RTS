using UnityEngine;

public class GameBuilder : MonoBehaviour
{
    [Header("Faction Definitions")]
    [SerializeField] private FactionDefinition playerFactionDefinition;

    [Header("Scene Services")]
    [SerializeField] private MonoBehaviour pathfindingServiceComponent;
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private CommandIssuer commandIssuer;

    [Header("Starting Units")]
    [SerializeField] private UnitBase combatUnitPrefab;
    [SerializeField] private Transform playerSpawnPoint;
    [SerializeField] private Transform spawnedUnitsRoot;
    [SerializeField] private int startingWorkerCount = 3;
    [SerializeField] private float startingUnitSpacing = 2f;

    public GameContext GameContext { get; private set; }
    public FactionManager FactionManager { get; private set; }
    public Faction PlayerFaction { get; private set; }
    public IPathfindingService PathfindingService { get; private set; }

    private void Awake()
    {
        ResolveSceneReferences();
        ResolveServices();
        BuildMatch();
    }

    private void Update()
    {
        FactionManager?.Tick();
    }

    private void ResolveSceneReferences()
    {
        if (selectionManager == null)
            selectionManager = GetComponentInChildren<SelectionManager>();

        if (commandIssuer == null)
            commandIssuer = GetComponentInChildren<CommandIssuer>();
    }

    private void ResolveServices()
    {
        PathfindingService = pathfindingServiceComponent as IPathfindingService;

        if (PathfindingService == null)
            Debug.LogError("GameBuilder is missing a valid IPathfindingService component.");
    }

    private void BuildMatch()
    {
        GameContext = new GameContext();

        FactionManager = new FactionManager();
        GameContext.SetFactionManager(FactionManager);

        PlayerFaction = BuildPlayerFaction();
        GameContext.SetPlayerFaction(PlayerFaction);

        FactionManager.AddFaction(PlayerFaction);

        if (selectionManager != null)
            selectionManager.Initialize(GameContext);

        if (commandIssuer != null)
            commandIssuer.Initialize(GameContext, PlayerFaction);

        SpawnStartingPlayerUnits();
    }

    private Faction BuildPlayerFaction()
    {
        ResourceManager resourceManager = new ResourceManager();
        UnitManager unitManager = new UnitManager(GameContext, PathfindingService);
        IFactionController controller = new PlayerFactionController();

        return new Faction(
            playerFactionDefinition,
            controller,
            unitManager,
            resourceManager,
            GameContext
        );
    }

    private void SpawnStartingPlayerUnits()
    {
        if (PlayerFaction == null)
            return;

        if (combatUnitPrefab == null)
        {
            Debug.LogWarning("GameBuilder has no workerUnitPrefab assigned.");
            return;
        }

        Vector3 origin = playerSpawnPoint != null ? playerSpawnPoint.position : Vector3.zero;
        Quaternion rotation = playerSpawnPoint != null ? playerSpawnPoint.rotation : Quaternion.identity;

        for (int i = 0; i < startingWorkerCount; i++)
        {
            Vector3 offset = GetStartingUnitOffset(i+1);
            Vector3 spawnPosition = origin + offset;

            PlayerFaction.UnitManager.SpawnUnit(
                combatUnitPrefab,
                spawnPosition,
                rotation,
                spawnedUnitsRoot
            );
        }
    }

    private Vector3 GetStartingUnitOffset(int index)
    {
        if (index == 0)
            return Vector3.zero;

        int rowSize = 3;
        int row = index / rowSize;
        int column = index % rowSize;

        float x = (column - 1) * startingUnitSpacing;
        float z = row * startingUnitSpacing;

        return new Vector3(x, 0f, z);
    }

}
