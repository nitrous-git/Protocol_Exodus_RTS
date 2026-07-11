using UnityEngine;

public class GameBuilder : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private GameLoop gameLoop;
    [SerializeField] private MatchWorld matchWorld;

    [Header("UI Sections")]
    [SerializeField] private MinimapPanelController minimapPanel;
    [SerializeField] private SelectionPanelController selectionPanel;
    [SerializeField] private CommandPanelController commandPanel;

    [Header("Faction Definitions")]
    [SerializeField] private FactionDefinition playerFactionDefinition;

    [Header("Starting Units")]
    [SerializeField] private UnitBase combatUnitPrefab;
    [SerializeField] private int startingWorkerCount = 3;
    [SerializeField] private float startingUnitSpacing = 2f;

    public GameContext GameContext { get; private set; }
    public FactionManager FactionManager { get; private set; }
    public ResourceNodeRepository ResourceNodeRepository { get; private set; }
    public Faction PlayerFaction { get; private set; }

    private void Awake()
    {
        ResolveSceneReferences();

        matchWorld.ResolveServices();

        BuildMatch();
        InitializeSections();
        SpawnStartingPlayerUnits();
        InitializeLoop();
    }

    private void ResolveSceneReferences()
    {
        if (gameLoop == null)
            gameLoop = GetComponentInChildren<GameLoop>();

        if (matchWorld == null)
            matchWorld = GetComponentInChildren<MatchWorld>();

        if (minimapPanel == null)
            minimapPanel = GetComponentInChildren<MinimapPanelController>();

        if (selectionPanel == null)
            selectionPanel = GetComponentInChildren<SelectionPanelController>();

        if (commandPanel == null)
            commandPanel = GetComponentInChildren<CommandPanelController>();
    }

    private void BuildMatch()
    {
        GameContext = new GameContext();

        ResourceNodeRepository = new ResourceNodeRepository(GameContext);

        FactionManager = new FactionManager();
        GameContext.SetFactionManager(FactionManager);

        PlayerFaction = BuildPlayerFaction();
        GameContext.SetPlayerFaction(PlayerFaction);

        FactionManager.AddFaction(PlayerFaction);
    }

    private Faction BuildPlayerFaction()
    {
        ResourceManager resourceManager = new ResourceManager();

        UnitManager unitManager = new UnitManager(
            GameContext,
            matchWorld.PathfindingService
        );

        IFactionController controller = new PlayerFactionController();

        return new Faction(
            playerFactionDefinition,
            controller,
            unitManager,
            resourceManager,
            GameContext
        );
    }

    private void InitializeSections()
    {
        matchWorld.Initialize(
            GameContext,
            FactionManager,
            ResourceNodeRepository,
            PlayerFaction
        );

        minimapPanel?.Initialize(GameContext, matchWorld);
        selectionPanel?.Initialize(PlayerFaction, GameContext);
        commandPanel?.Initialize(PlayerFaction, GameContext);
    }

    private void InitializeLoop()
    {
        if (gameLoop == null)
        {
            Debug.LogError("GameBuilder cannot initialize because GameLoop is missing.");
            return;
        }

        gameLoop.Initialize(
            matchWorld,
            minimapPanel,
            selectionPanel,
            commandPanel
        );
    }

    private void SpawnStartingPlayerUnits()
    {
        if (PlayerFaction == null || matchWorld == null)
            return;

        if (combatUnitPrefab == null)
        {
            Debug.LogWarning("GameBuilder has no workerUnitPrefab assigned.");
            return;
        }

        Vector3 origin = matchWorld.PlayerSpawnPoint != null
            ? matchWorld.PlayerSpawnPoint.position
            : Vector3.zero;

        Quaternion rotation = matchWorld.PlayerSpawnPoint != null
            ? matchWorld.PlayerSpawnPoint.rotation
            : Quaternion.identity;

        for (int i = 0; i < startingWorkerCount; i++)
        {
            Vector3 spawnPosition = origin + GetStartingUnitOffset(i + 1);

            PlayerFaction.UnitManager.SpawnUnit(
                combatUnitPrefab,
                spawnPosition,
                rotation,
                matchWorld.UnitsRoot
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
