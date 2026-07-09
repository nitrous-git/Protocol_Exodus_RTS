using UnityEngine;

public class GameBuilder : MonoBehaviour
{
    [Header("Scene Services")]
    [SerializeField] private MonoBehaviour pathfindingServiceComponent;
    [SerializeField] private UnitManager unitManager;
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private CommandIssuer commandIssuer;

    public IPathfindingService PathfindingService { get; private set; }
    public UnitManager UnitManager { get { return unitManager; } }
    public SelectionManager SelectionManager { get { return selectionManager; } }
    public CommandIssuer CommandIssuer { get { return commandIssuer; } }


    private void Awake()
    {




        PathfindingService = pathfindingServiceComponent as IPathfindingService;

        if (PathfindingService == null)
            Debug.LogError("Pathfinding service is missing or invalid.");

        ResolveSceneReferences();
    }

    private void ResolveSceneReferences()
    {
        if (unitManager == null)
            unitManager = GetComponentInChildren<UnitManager>();

        if (selectionManager == null)
            selectionManager = GetComponentInChildren<SelectionManager>();

        if (commandIssuer == null)
            commandIssuer = GetComponentInChildren<CommandIssuer>();
    }






}
