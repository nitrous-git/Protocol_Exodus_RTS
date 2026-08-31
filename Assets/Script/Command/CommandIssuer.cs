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
    private readonly List<CombatUnit> attackCommandUnits = new List<CombatUnit>();
    private readonly Dictionary<CombatUnit, GridCoord> attackAssignments = new Dictionary<CombatUnit, GridCoord>();

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

        // Worker Gather
        if (target.ResourceNode != null && TryIssueGatherCommand(target.ResourceNode))
        {
            return true;
        }

        // Worker Deliver
        if (target.Building != null && TryIssueDeliverCommand(target.Building))
        {
            return true;
        }

        // if (target.Building != null &&
        //     TryIssueRepairCommand(target.Building))
        // {
        //     return true;
        // }

        if (TryIssueAttackCommand(target))
        {
            return true;
        }

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

        float formationMaxNavigationRadius = GetCommandGroupMaxNavigationRadius();

        int movementGroupId = gameContext.AllocateMovementGroupId();
        PrepareMovementGroup(movementGroupId); // mark all units before Astar starts

        FormationMovementGroup formationGroup =
            new FormationMovementGroup(
                movementGroupId, 
                commandableUnits, 
                destinationCenter, 
                formationMaxNavigationRadius, 
                gameContext.TerrainGrid, 
                gameContext.DestinationAllocationSystem.Formation);

        MovementGroup movementGroup =
            new MovementGroup(
                movementGroupId,
                commandableUnits,
                destinationCenter,
                formationMaxNavigationRadius,
                formationGroup);

        bool issuedAnyCommand = false;

        for (int i = 0; i < commandableCount; i++)
        {
            UnitBase unit = commandableUnits[i];

            IControllable controllable = unit as IControllable;
            if (controllable == null)
                continue;

            int slotIndex = formationGroup.GetAssignedSlotIndex(unit);

            if (slotIndex < 0)
            {
                continue;
            }

            CommandContext context = CommandContext.MoveTo(
                destinationCenter, 
                slotIndex, 
                commandableCount, 
                formationMaxNavigationRadius, 
                movementGroupId,
                formationGroup,
                movementGroup);

            controllable.IssueCommand(CommandType.Move, context);

            issuedAnyCommand = true;
        }

        return issuedAnyCommand;
    }

    // ---------------------------------------------------------------------
    // Attack
    // ---------------------------------------------------------------------

    public ITargetable FindAttackTargetFromScreen(Vector2 screenPosition)
    {
        if (!CanIssueCommands() || worldCamera == null)
            return null;

        DefaultCommandTarget target = ResolveDefaultCommandTarget(screenPosition);

        if (target.Unit != null)
            return target.Unit;

        if (target.Building != null)
            return target.Building;

        return null;
    }

    //public bool TryIssueAttackCommand(ITargetable target)
    //{
    //    if (!CanIssueCommands() || target == null)
    //        return false;

    //    CollectCommandableSelectedUnits();

    //    bool issuedAnyCommand = false;

    //    for (int i = 0; i < commandableUnits.Count; i++)
    //    {
    //        CombatUnit combatUnit = commandableUnits[i] as CombatUnit;

    //        if (combatUnit == null)
    //            continue;

    //        if (!combatUnit.CanAttack())
    //            continue;

    //        if (!combatUnit.IsValidAttackTarget(target))
    //            continue;

    //        combatUnit.IssueCommand(CommandType.Attack, CommandContext.AttackTarget(target));

    //        issuedAnyCommand = true;
    //    }

    //    return issuedAnyCommand;
    //}

    public bool TryIssueAttackCommand(ITargetable target)
    {
        if (!CanIssueCommands() || target == null)
        {
            return false;
        }

        CollectCommandableSelectedUnits();

        attackCommandUnits.Clear();
        attackAssignments.Clear();

        // ---------------------------------------------------------
        // Collect valid attackers.
        // ---------------------------------------------------------

        for (int i = 0; i < commandableUnits.Count; i++)
        {
            CombatUnit combatUnit = commandableUnits[i] as CombatUnit;

            if (combatUnit == null)
                continue;

            if (!combatUnit.CanAttack())
                continue;

            if (!combatUnit.IsValidAttackTarget(target))
            {
                continue;
            }

            attackCommandUnits.Add(
                combatUnit);
        }

        if (attackCommandUnits.Count == 0)
            return false;

        // ---------------------------------------------------------
        // The new player command supersedes all old
        // destination claims before deployment is solved.
        //
        // This is important because allocation happens BEFORE
        // SetState()/OnExit() is called on the old state.
        // ---------------------------------------------------------

        for (int i = 0; i < attackCommandUnits.Count; i++)
        {
            gameContext.GridNavigationStateSystem.ReleaseAllDestinations(attackCommandUnits[i]);
        }

        // ---------------------------------------------------------
        // ONE deployment calculation.
        // ---------------------------------------------------------

        gameContext
            .DestinationAllocationSystem
            .Attack
            .AllocateGroup(
                attackCommandUnits,
                target,
                attackAssignments);

        // ---------------------------------------------------------
        // Units that actually move share one A* movement group.
        // ---------------------------------------------------------

        int movementGroupId = 0;

        if (attackAssignments.Count > 0)
        {
            movementGroupId = gameContext.AllocateMovementGroupId();
        }

        //
        // Pre-tag ALL movers before the first A* begins.
        //
        for (int i = 0; i < attackCommandUnits.Count; i++)
        {
            CombatUnit unit = attackCommandUnits[i];

            bool hasAssignment = attackAssignments.ContainsKey(unit);

            unit.PrepareMovementGroup(hasAssignment ? movementGroupId : 0);
        }

        // ---------------------------------------------------------
        // Issue already-resolved Attack commands.
        // ---------------------------------------------------------

        for (int i = 0; i < attackCommandUnits.Count; i++)
        {
            CombatUnit unit = attackCommandUnits[i];

            bool hasAssignment = attackAssignments.TryGetValue(unit, out GridCoord attackCell);

            CommandContext context =
                CommandContext.AttackTarget(
                    target,
                    attackPositionCell: hasAssignment ? attackCell : (GridCoord?)null,
                    movementGroupId: hasAssignment ? movementGroupId : 0,
                    attackDeploymentResolved: true);

            unit.IssueCommand(CommandType.Attack, context);
        }

        return true;
    }



    private bool TryIssueAttackCommand(DefaultCommandTarget target)
    {
        ITargetable attackTarget = null;

        if (target.Unit != null)
        {
            attackTarget = target.Unit;
        }
        else if (target.Building != null)
        {
            attackTarget = target.Building;
        }

        return TryIssueAttackCommand(attackTarget);
    }

    // ---------------------------------------------------------------------
    // Attack-Move
    // ---------------------------------------------------------------------

    public bool TryIssueAttackMoveCommandFromScreen(Vector2 screenPosition)
    {
        if (!TryResolveGroundPositionFromScreen(screenPosition))
        {
            return false;
        }

        return TryIssueAttackMoveCommand(currentGroundPosition);
    }

    public bool TryIssueAttackMoveCommand(Vector3 destinationCenter)
    {
        if (!CanIssueCommands())
            return false;

        CollectCommandableSelectedUnits();

        int commandableCount = commandableUnits.Count;

        if (commandableCount == 0)
            return false;

        float formationMaxNavigationRadius = GetCommandGroupMaxNavigationRadius();

        int movementGroupId = gameContext.AllocateMovementGroupId();
        PrepareMovementGroup(movementGroupId);

        bool issuedAnyCommand = false;

        for (int i = 0; i < commandableCount; i++)
        {
            UnitBase unit = commandableUnits[i];
            IControllable controllable = unit as IControllable;

            if (controllable == null)
                continue;

            CommandContext context = CommandContext.AttackMoveTo(destinationCenter, i, commandableCount, formationMaxNavigationRadius, movementGroupId);

            if (unit is CombatUnit combatUnit && combatUnit.CanAttack())
            {
                controllable.IssueCommand(CommandType.AttackMove, context);
            }
            else
            {
                // Non-combat units still travel with the group.
                controllable.IssueCommand(CommandType.Move, context);
            }

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

    private float GetCommandGroupMaxNavigationRadius()
    {
        float maxRadius = 0f;

        for (int i = 0; i < commandableUnits.Count; i++)
        {
            UnitBase unit = commandableUnits[i];

            if (unit == null)
                continue;

            maxRadius = Mathf.Max(maxRadius, unit.NavigationRadius);
        }

        return maxRadius;
    }

    private void PrepareMovementGroup(int movementGroupId)
    {
        for (int i = 0; i < commandableUnits.Count; i++)
        {
            UnitBase unit = commandableUnits[i];

            if (unit == null)
            {
                continue;
            }

            unit.PrepareMovementGroup(movementGroupId);
        }

    }
}
