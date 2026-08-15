using System.Collections.Generic;
using UnityEngine;

internal readonly struct DefaultCommandTarget
{
    public readonly ResourceNode ResourceNode;
    public readonly BuildingBase Building;
    public readonly UnitBase Unit;

    public DefaultCommandTarget(ResourceNode resourceNode, BuildingBase building, UnitBase unit)
    {
        ResourceNode = resourceNode;
        Building = building;
        Unit = unit;
    }
}

public class CommandIssuer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;

    [Header("Context Commands")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private LayerMask resourceNodeMask = ~0;
    [SerializeField] private LayerMask contextCommandMask = ~0;

    private List<UnitBase> commandableUnits = new List<UnitBase>();

    private GameContext gameContext;
    private Faction issuingFaction;

    private Vector3 currentGroundPosition = Vector3.zero;
    private Vector3 currentGroundNormal = Vector3.up;

    public Vector3 CurrentGroundPosition => currentGroundPosition;
    public Vector3 CurrentGroundNormal => currentGroundNormal;

    public void Initialize(GameContext gameContext, Faction issuingFaction)
    {
        this.gameContext = gameContext;
        this.issuingFaction = issuingFaction;
    }

    private void Awake()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;
    }

    // ---------------------------------------------------------------------
    // Default contextual command
    // ---------------------------------------------------------------------

    public bool TryIssueDefaultCommandFromScreen(Vector2 screenPosition)
    {
        if (!CanIssueCommands() || worldCamera == null)
            return false;

        DefaultCommandTarget target = ResolveDefaultCommandTarget(screenPosition);

        // Worker -> ResourceNode
        if (target.ResourceNode != null && TryIssueGatherCommand(target.ResourceNode))
        {
            return true;
        }

        // Worker carrying cargo -> own CommandCenter
        if (target.Building != null && TryIssueDeliverCommand(target.Building))
        {
            return true;
        }

        // Later:
        //
        // if (target.Building != null &&
        //     TryIssueRepairCommand(target.Building))
        // {
        //     return true;
        // }
        //
        // if (TryIssueAttackCommand(target))
        // {
        //     return true;
        // }

        // Nothing contextual applied.
        return TryIssueMoveCommandFromScreen(screenPosition);
    }

    private DefaultCommandTarget ResolveDefaultCommandTarget(Vector2 screenPosition)
    {
        if (worldCamera == null)
            return default;

        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, contextCommandMask, QueryTriggerInteraction.Collide);

        float closestDistance = float.PositiveInfinity;

        DefaultCommandTarget closestTarget = default;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            ResourceNode resourceNode = hit.collider.GetComponentInParent<ResourceNode>();
            BuildingBase building = hit.collider.GetComponentInParent<BuildingBase>();
            UnitBase unit = hit.collider.GetComponentInParent<UnitBase>();

            if (resourceNode == null && building == null && unit == null)
            {
                continue;
            }

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestTarget = new DefaultCommandTarget(resourceNode, building, unit);
        }

        return closestTarget;
    }

    // ---------------------------------------------------------------------
    // Gather
    // ---------------------------------------------------------------------

    public bool TryIssueGatherCommand(ResourceNode resourceNode)
    {
        if (!CanIssueCommands())
            return false;

        if (resourceNode == null || !resourceNode.IsInitialized || resourceNode.IsDepleted)
        {
            return false;
        }

        CollectCommandableSelectedUnits();

        bool issuedAnyCommand = false;

        for (int i = 0; i < commandableUnits.Count; i++)
        {
            if (commandableUnits[i] is not WorkerUnit worker)
                continue;

            worker.IssueCommand(CommandType.Gather, CommandContext.Gather(resourceNode));
            issuedAnyCommand = true;
        }

        return issuedAnyCommand;
    }

    private ResourceNode FindResourceNodeFromScreen(Vector2 screenPosition)
    {
        if (!CanIssueCommands() || worldCamera == null)
            return null;

        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, resourceNodeMask, QueryTriggerInteraction.Collide);

        ResourceNode closestNode = null;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            ResourceNode resourceNode = hit.collider.GetComponentInParent<ResourceNode>();

            if (resourceNode == null || !resourceNode.IsInitialized || resourceNode.IsDepleted)
            {
                continue;
            }

            if (hit.distance >= closestDistance)
                continue;

            closestDistance = hit.distance;
            closestNode = resourceNode;
        }

        return closestNode;
    }

    // ---------------------------------------------------------------------
    // Deliver
    // ---------------------------------------------------------------------

    public bool TryIssueDeliverCommand(BuildingBase building)
    {
        if (!CanIssueCommands())
            return false;

        if (!IsValidResourceDropOff(building))
            return false;

        CollectCommandableSelectedUnits();

        bool issuedAnyCommand = false;

        for (int i = 0; i < commandableUnits.Count; i++)
        {
            if (commandableUnits[i] is not WorkerUnit worker)
                continue;

            WorkerResourceComponent resourceComponent = worker.ResourceComponent;

            if (resourceComponent == null || !resourceComponent.HasCargo)
            {
                continue;
            }

            worker.IssueCommand(CommandType.Deliver, CommandContext.DeliverTo(building));

            issuedAnyCommand = true;
        }

        return issuedAnyCommand;
    }

    // ---------------------------------------------------------------------
    // Move
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolves a screen position to a point on commandable ground.
    /// </summary>
    public bool TryResolveGroundPositionFromScreen(Vector2 screenPosition)
    {
        currentGroundPosition = Vector3.zero;
        currentGroundNormal = Vector3.up;

        if (!CanIssueCommands() || worldCamera == null)
            return false;

        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;

        if (!Physics.Raycast(ray, out hit, 10000f, groundMask))
        {
            return false;
        }

        currentGroundPosition = hit.point;
        currentGroundNormal = hit.normal;

        return true;
    }

    public bool TryIssueMoveCommandFromScreen(Vector2 screenPosition)
    {
        if (!TryResolveGroundPositionFromScreen(screenPosition))
        {
            return false;
        }

        return TryIssueMoveCommand(currentGroundPosition);
    }

    public bool TryIssueMoveCommand(Vector3 destinationCenter)
    {
        if (!CanIssueCommands())
            return false;

        CollectCommandableSelectedUnits();

        int commandableCount = commandableUnits.Count;

        if (commandableCount == 0)
            return false;

        bool issuedAnyCommand = false;

        for (int i = 0; i < commandableCount; i++)
        {
            UnitBase unit = commandableUnits[i];

            IControllable controllable = unit as IControllable;
            if (controllable == null)
                continue;

            //Vector3 destination = destinationCenter + GetSimpleDestinationOffset(i, commandableCount);
            //controllable.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination));

            CommandContext context = CommandContext.MoveTo(destinationCenter, i, commandableCount);
            controllable.IssueCommand(CommandType.Move, context);

            issuedAnyCommand = true;
        }

        return issuedAnyCommand;
    }

    // ---------------------------------------------------------------------
    // Immediate commands
    // ---------------------------------------------------------------------
    public bool TryIssueHoldPositionCommand()
    {
        if (!CanIssueCommands())
            return false;

        CollectCommandableSelectedUnits();

        if (commandableUnits.Count == 0)
            return false;

        bool issuedAnyCommand = false;
        CommandContext context = CommandContext.None();

        for (int i = 0; i < commandableUnits.Count; i++)
        {
            UnitBase unit = commandableUnits[i];

            if (unit is not IControllable controllable)
                continue;

            controllable.IssueCommand(CommandType.HoldPosition, context);

            issuedAnyCommand = true;
        }

        return issuedAnyCommand;
    }

    // ---------------------------------------------------------------------
    // Selection resolution
    // ---------------------------------------------------------------------

    private void CollectCommandableSelectedUnits()
    {
        commandableUnits.Clear();

        if (gameContext == null || issuingFaction == null)
            return;

        IReadOnlyList<UnitBase> selectedUnits = gameContext.SelectedUnits;

        for (int i = 0; i < selectedUnits.Count; i++)
        {
            UnitBase unit = selectedUnits[i];

            if (!issuingFaction.CanIssueCommandsTo(unit))
                continue;

            commandableUnits.Add(unit);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    //private Vector3 GetSimpleDestinationOffset(int index, int count)
    //{
    //    if (count <= 1)
    //        return Vector3.zero;

    //    float angle = index * 137.5f * Mathf.Deg2Rad;
    //    float radius = Mathf.Sqrt(index + 1) * groupDestinationSpacing;

    //    return new Vector3(Mathf.Cos(angle) * radius,0f, Mathf.Sin(angle) * radius);
    //}

    private bool CanIssueCommands()
    {
        return isActiveAndEnabled && gameContext != null && issuingFaction != null;
    }

    private bool IsValidResourceDropOff(BuildingBase building)
    {
        return building != null &&
               building.IsInitialized &&
               building.IsAlive &&
               building.IsOperational &&
               building.OwnerFaction == issuingFaction &&
               building.Headquarters != null;
    }

}
