using UnityEngine;

public class GameBuilder : MonoBehaviour
{
    private enum MatchProfile { StandardMatch, DevelopmentSandbox }

    [Header("Core")]
    [SerializeField] private GameLoop gameLoop;
    [SerializeField] private MatchWorld matchWorld;

    [Header("UI")]
    [SerializeField] private MatchUIController matchUI;
    [SerializeField, Min(1)] private int gameLogCapacity = GameLog.DefaultCapacity;
    [SerializeField, Min(0.01f)] private float gameLogDisplayDuration = GameLog.DefaultDisplayDuration;

    [Header("Faction Definitions")]
    [SerializeField] private FactionDefinition FactionTerran_Definition;
    [SerializeField] private FactionDefinition FactionSpecter_Definition;

    [Header("Match Setup")]
    [SerializeField] private MatchProfile matchProfile = MatchProfile.StandardMatch;
    [SerializeField, Min(0)] private int standardStartingMinerals = 550;
    [SerializeField, Min(0)] private int standardStartingSupply = 10;
    [Tooltip("Development Sandbox only. Standard Match starts with workers and builds headquarters during play.")]
    [SerializeField] private int startingCombatCount = 1;
    [SerializeField] private float startingUnitSpacing = 1f;

    [Header("AI Strategy")]
    [SerializeField] private AIStrategySettings aiStrategy = new AIStrategySettings();

    [Header("AI Waves")]
    [SerializeField] private AIWaveSettings aiWaves = new AIWaveSettings();

    [Header("AI Economy")]
    [SerializeField] private AIEconomySettings aiEconomy = new AIEconomySettings();

    private AIFactionController aiFactionController;
    private bool IsStandardMatch => matchProfile == MatchProfile.StandardMatch;

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

        if (matchWorld == null || FactionTerran_Definition == null ||
            matchWorld.FactionSpawnPoints == null || matchWorld.FactionSpawnPoints.Count < 2 ||
            matchWorld.FactionSpawnPoints[0] == null || matchWorld.FactionSpawnPoints[1] == null)
        {
            Debug.LogError("GameBuilder requires a match world, a faction definition, and two faction spawn points.", this);
            return;
        }

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
        GameContext = new GameContext(gameLogCapacity, gameLogDisplayDuration);

        GameContext.SetTerrainGrid(matchWorld.TerrainGrid);

        GridNavigationStateSystem = new GridNavigationStateSystem(matchWorld.TerrainGrid);
        GameContext.SetGridNavigationStateSystem(GridNavigationStateSystem);

        DestinationAllocationSystem = new DestinationAllocationSystem(matchWorld.TerrainGrid, GridNavigationStateSystem);
        GameContext.SetDestinationAllocationSystem(DestinationAllocationSystem);

        matchWorld.PathfindingService?.Initialize(matchWorld.TerrainGrid, GridNavigationStateSystem);
        GameContext.SetPathfindingService(matchWorld.PathfindingService);

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

        aiFactionController = new AIFactionController(aiWaves, aiEconomy,
            IsStandardMatch || aiStrategy.enableStrategyAI ? aiStrategy : null);
        Faction aiFaction01 = BuildFaction(FactionTerran_Definition, FactionColorType.Green, aiFactionController);

        FactionManager.AddFaction(playerFaction);
        FactionManager.AddFaction(aiFaction01);

        PlayerFaction = playerFaction;
        GameContext.SetPlayerFaction(PlayerFaction);

        // --- Initialize ---
        matchWorld.Initialize(GameContext, FactionManager, ResourceNodeRepository, ProjectileManager, PlayerFaction);
        SeedStartingAssets(playerFaction, matchWorld.FactionSpawnPoints[0]);
        SeedStartingAssets(aiFaction01, matchWorld.FactionSpawnPoints[1]);
        matchWorld.TerrainGrid?.RebuildStaticClearanceField(); // Build clearance field once after all static initialization

        matchUI?.Initialize(PlayerFaction, GameContext, matchWorld, gameLoop);

        // --- controller init ---
        playerFactionController?.InitializePlayerControl(
            matchWorld.PlayerInputBindings,
            matchWorld.SelectionManager, 
            matchWorld.CommandIssuer, 
            matchWorld.CameraController,
            matchWorld.TerrainGrid,
            matchWorld.InteractionMarkers,
            matchWorld.BuildingPlacementPreviewPrefab,
            matchWorld.InteractionMarkersRoot,
            gameLoop
        );
    }

    private Faction BuildFaction(FactionDefinition definition, FactionColorType color, IFactionController controller)
    {
        UnitManager unitManager = new UnitManager(GameContext, matchWorld.PathfindingService, matchWorld.UnitsRoot);
        BuildingManager buildingManager = new BuildingManager(GameContext, matchWorld.TerrainGrid, matchWorld.BuildingsRoot); // we can evetually remove TerrainGrid and simply get it from the GameContext
        ResourceManager resourceManager = IsStandardMatch
            ? new ResourceManager(standardStartingMinerals, 0, standardStartingSupply)
            : new ResourceManager(definition.startingMinerals, definition.startingGas, definition.startingMaxSupply);

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

        MatchResultSystem matchResults = new(FactionManager.Factions, PlayerFaction);
        if (!matchResults.IsEnabled)
            Debug.LogWarning("Match results are disabled because the starting faction roster is incomplete.", this);

        gameLoop.Initialize(matchWorld, matchUI, matchResults);
    }

#if UNITY_EDITOR
    [ContextMenu("AI Missions/Launch Test Assault")]
    private void LaunchTestAssault()
    {
        if (Application.isPlaying && aiFactionController?.MissionDirector?.TryLaunchAssault() == false)
            Debug.Log("Test assault unavailable. Check eligible unit count, home defenders, enemy targets, and staging clearance.", this);
    }

    [ContextMenu("AI Missions/Assign Test Base Defense")]
    private void AssignTestBaseDefense()
    {
        if (!Application.isPlaying || aiFactionController?.BaseAnchor is not Vector3 anchor)
            return;

        AIWaveSettings settings = aiFactionController.WaveSettings;
        if (!aiFactionController.MissionDirector.TryDefendArea(anchor, settings.defenseGuardRadius,
                Mathf.Max(1, settings.homeDefenders)))
            Debug.Log("Test defense unavailable. Check eligible units, guard radius, and active defense.", this);
    }

    [ContextMenu("AI Missions/Cancel Active AI Missions")]
    private void CancelActiveAIMissions()
    {
        if (Application.isPlaying)
            aiFactionController?.MissionDirector?.CancelAll();
    }

    [ContextMenu("AI Economy/Reevaluate")]
    private void ReevaluateAIEconomy()
    {
        if (Application.isPlaying)
            aiFactionController?.EconomyManager?.RequestReevaluation();
    }

    [ContextMenu("AI Economy/Log Status")]
    private void LogAIEconomyStatus()
    {
        if (Application.isPlaying && aiFactionController?.EconomyManager is AIEconomyManager economy)
            Debug.Log(economy.GetStatus(), this);
    }

    [ContextMenu("AI Strategy/Log Strategy Status")]
    private void LogAIStrategyStatus()
    {
        if (Application.isPlaying)
            Debug.Log(aiFactionController?.Strategy?.GetStatus() ?? "AI strategy is disabled for this match.", this);
    }
#endif

    private void SeedStartingAssets(Faction faction, Transform spawnPoint)
    {
        Vector3 origin = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
        var aiController = faction.Controller as AIFactionController;
        aiController?.SetBaseAnchor(origin);
        if (IsStandardMatch)
        {
            SpawnStartingWorkers(faction, origin, rotation);
            return;
        }

        int columns = Mathf.CeilToInt(Mathf.Sqrt(startingCombatCount));
        for (int i = 0; i < startingCombatCount; i++)
        {
            UnitDefinition definition = faction.Definition.GetUnitDefinition(i % 5 == 0 ? UnitType.Mech : UnitType.Combat);
            Vector3 offset = new(i % columns * startingUnitSpacing, 0f, i / columns * startingUnitSpacing);
            if (definition != null && definition.Prefab != null)
                faction.UnitManager.SpawnUnit(definition.Prefab, origin + offset, rotation);
        }
    }

    private void SpawnStartingWorkers(Faction faction, Vector3 origin, Quaternion rotation)
    {
        UnitDefinition worker = faction.Definition.GetUnitDefinition(UnitType.Worker);
        if (worker == null || worker.Prefab == null)
        {
            Debug.LogError($"Unable to seed starting workers for {faction.ColorType}. The worker definition is missing.", this);
            return;
        }

        // Keep the spawn area open for either orientation of a future headquarters.
        BuildingDefinition headquarters = faction.Definition.GetBuildingDefinition(BuildingType.CommandCenter);
        Vector2Int footprint = headquarters != null ? headquarters.FootprintSize : Vector2Int.zero;
        int firstRing = Mathf.Max(footprint.x, footprint.y) / 2 + 1;
        int searchRadius = Mathf.Max(24, firstRing);
        int remaining = Mathf.Max(2, aiStrategy.initialWorkerGoal);
        TerrainGrid grid = matchWorld.TerrainGrid;
        GridCoord center = grid.WorldToCell(origin);
        float radius = worker.NavigationRadius;
        for (int ring = firstRing; ring <= searchRadius && remaining > 0; ring++)
        {
            for (int z = -ring; z <= ring && remaining > 0; z++)
            {
                int step = Mathf.Abs(z) == ring ? 1 : Mathf.Max(1, 2 * ring);
                for (int x = -ring; x <= ring && remaining > 0; x += step)
                {
                    GridCoord cell = new(center.x + x, center.z + z);
                    if (!grid.IsStaticallyTraversable(cell, radius))
                        continue;

                    Vector3 position = grid.CellToWorld(cell);
                    if (!GridNavigationStateSystem.IsUnitSpaceClear(position, radius))
                        continue;

                    if (faction.UnitManager.SpawnUnit(worker.Prefab, position, rotation) != null)
                        remaining--;
                }
            }
        }

        if (remaining > 0)
            Debug.LogError($"Unable to find space for {remaining} starting workers for {faction.ColorType}.", this);
    }

}
