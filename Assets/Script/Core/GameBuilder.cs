using UnityEngine;

public class GameBuilder : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private GameLoop gameLoop;
    [SerializeField] private MatchWorld matchWorld;

    [Header("UI")]
    [SerializeField] private MatchUIController matchUI;

    [Header("Faction Definitions")]
    [SerializeField] private FactionDefinition FactionTerran_Definition;
    [SerializeField] private FactionDefinition FactionSpecter_Definition;

    [Header("Starting Units")]
    //[SerializeField] private UnitBase combatUnitPrefab;
    [SerializeField] private int startingCombatCount = 3;
    [SerializeField] private float startingUnitSpacing = 1f;

    public GameContext GameContext { get; private set; }
    public FactionManager FactionManager { get; private set; }
    public ProjectileManager ProjectileManager { get; private set; }
    public ResourceNodeRepository ResourceNodeRepository { get; private set; }
    public GridNavigationStateSystem GridNavigationStateSystem { get; private set; }
    public DestinationAllocationSystem DestinationAllocationSystem { get; private set; }
    public Faction PlayerFaction { get; private set; }
    
    private void Awake()
    {
        ResolveSceneReferences();

        // because we need to pass it to factions before InitializeSections, we might fix this later. 
        matchWorld.ResolveServices(); 

        BuildMatch();
        //InitializeSections();
        InitializeLoop();
    }

    private void ResolveSceneReferences()
    {
        if (gameLoop == null)
            gameLoop = GetComponentInChildren<GameLoop>();

        if (matchWorld == null)
            matchWorld = GetComponentInChildren<MatchWorld>();

        if (matchUI == null)
            matchUI = GetComponentInChildren<MatchUIController>(true);
    }

    private void BuildMatch()
    {
        GameContext = new GameContext();

        GameContext.SetTerrainGrid(matchWorld.TerrainGrid);

        GridNavigationStateSystem = new GridNavigationStateSystem(matchWorld.TerrainGrid);
        GameContext.SetGridNavigationStateSystem(GridNavigationStateSystem);

        DestinationAllocationSystem = new DestinationAllocationSystem(matchWorld.TerrainGrid, GridNavigationStateSystem);
        GameContext.SetDestinationAllocationSystem(DestinationAllocationSystem);

        matchWorld.PathfindingService?.Initialize(matchWorld.TerrainGrid, GridNavigationStateSystem);

        // Nodes reserve their cells after TerrainGrid (after ResolveServices()) 
        ResourceNodeRepository = new ResourceNodeRepository(GameContext, matchWorld.TerrainGrid); 
        GameContext.SetResourceNodeRepository(ResourceNodeRepository);

        ProjectileManager = new ProjectileManager(matchWorld.ProjectilesRoot);
        GameContext.SetProjectileManager(ProjectileManager);

        // ------ faction setup ------
        // -------------------------------------------------------
        FactionManager = new FactionManager();
        GameContext.SetFactionManager(FactionManager);

        // Controller
        PlayerFactionController playerFactionController = new();

        Faction playerFaction = BuildFaction(FactionTerran_Definition, FactionColorType.Blue, playerFactionController);
        SpawnStartingUnits(playerFaction, matchWorld.FactionSpawnPoints[0]);

        Faction aiFaction01 = BuildFaction(FactionTerran_Definition, FactionColorType.Green, new AIFactionController());
        SpawnStartingUnits(aiFaction01, matchWorld.FactionSpawnPoints[1]);

        Faction aiFaction02 = BuildFaction(FactionTerran_Definition, FactionColorType.Red, new AIFactionController());
        SpawnStartingUnits(aiFaction02, matchWorld.FactionSpawnPoints[2]);

        FactionManager.AddFaction(playerFaction);
        FactionManager.AddFaction(aiFaction01);
        FactionManager.AddFaction(aiFaction02);

        PlayerFaction = playerFaction;
        GameContext.SetPlayerFaction(PlayerFaction);

        // --- panels ---
        matchWorld.Initialize(GameContext, FactionManager, ResourceNodeRepository, ProjectileManager, PlayerFaction);
        matchUI?.Initialize(PlayerFaction, GameContext, matchWorld);

        // --- controller init ---
        playerFactionController?.InitializePlayerControl(
            matchWorld.PlayerInputBindings,
            matchWorld.SelectionManager, 
            matchWorld.CommandIssuer, 
            matchWorld.CameraController,
            matchWorld.TerrainGrid,
            matchWorld.TargetMarkers,
            matchWorld.BuildingPlacementPreviewPrefab,
            matchWorld.InteractionMarkersRoot
        );
    }

    private Faction BuildFaction(FactionDefinition definition, FactionColorType color, IFactionController controller)
    {
        UnitManager unitManager = new UnitManager(GameContext, matchWorld.PathfindingService, matchWorld.UnitsRoot);
        BuildingManager buildingManager = new BuildingManager(GameContext, matchWorld.TerrainGrid, matchWorld.BuildingsRoot); // we can evetually remove TerrainGrid and simply get it from the GameContext
        ResourceManager resourceManager = new ResourceManager(definition.startingMinerals, definition.startingGas, definition.startingMaxSupply);

        return new Faction(
            definition,
            color,
            controller,
            unitManager,
            buildingManager,
            resourceManager,
            GameContext
        );
    }

    //private void InitializeSections()
    //{
    //    matchWorld.Initialize(
    //        GameContext,
    //        FactionManager,
    //        ResourceNodeRepository,
    //        ProjectileManager,
    //        PlayerFaction
    //    );

    //    minimapPanel?.Initialize(GameContext, matchWorld);
    //    selectionPanel?.Initialize(PlayerFaction, GameContext);
    //    commandPanel?.Initialize(PlayerFaction, GameContext);
    //}

    private void InitializeLoop()
    {
        if (gameLoop == null)
        {
            Debug.LogError("GameBuilder cannot initialize because GameLoop is missing.");
            return;
        }

        gameLoop.Initialize(matchWorld, matchUI);
    }

    // Helper methods (should be moved later on)
    private void SpawnStartingUnits(Faction faction, Transform spawnPoint)
    {
        if (faction == null || matchWorld == null)
            return;

        UnitDefinition combatDefinition; // = faction.Definition?.GetUnitDefinition(UnitType.Mech);

        //if (combatDefinition.Prefab == null)
        //{
        //    Debug.LogWarning("GameBuilder has no workerUnitPrefab assigned.");
        //    return;
        //}

        Vector3 origin = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        for (int i = 0; i < startingCombatCount; i++)
        {
            if (i%2 == 0)
            {
                combatDefinition = faction.Definition?.GetUnitDefinition(UnitType.Mech);
            }
            else
            {
                combatDefinition = faction.Definition?.GetUnitDefinition(UnitType.Combat);
            }

            Vector3 spawnPosition = origin + GetStartingUnitOffset(i);

            faction.UnitManager.SpawnUnit(
                combatDefinition.Prefab,
                spawnPosition,
                rotation,
                matchWorld.UnitsRoot
            );
        }
    }

    private Vector3 GetStartingUnitOffset(int index)
    {
        int rowSize = 3;
        int row = index / rowSize;
        int column = index % rowSize;

        float x = (column - 1) * startingUnitSpacing;
        float z = row * startingUnitSpacing;

        return new Vector3(x, 0f, z);
    }

}
