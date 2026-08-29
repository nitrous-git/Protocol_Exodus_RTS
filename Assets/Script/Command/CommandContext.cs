using UnityEngine;

// weird struct implementation, compare to Java again later
public struct CommandContext
{
    public Vector3 WorldPosition;
    public Vector2Int GridCell;
    public int MovementGroupId;
    public int FormationSlotIndex;
    public int FormationUnitCount;
    public float FormationMaxNavigationRadius;

    public GridCoord AttackPositionCell;
    public bool HasAttackPositionCell;
    public bool AttackDeploymentResolved;

    public ITargetable Target;
    public ResourceNode ResourceNode;
    public FormationMovementGroup FormationGroup;

    public bool HasWorldPosition;
    public bool HasGridCell;

    public bool HasTarget => Target != null;
    public bool HasResourceNode => ResourceNode != null;

    public static CommandContext None()
    {
        return new CommandContext();
    }

    public static CommandContext MoveTo(
        Vector3 worldPosition, 
        int formationSlotIndex = 0, 
        int formationUnitCount = 1, 
        float formationMaxNavigationRadius = 1f,
        int movementGroupId = 0,
        FormationMovementGroup formationGroup = null)
    {
        return new CommandContext
        {
            WorldPosition = worldPosition,
            HasWorldPosition = true,

            MovementGroupId = movementGroupId,
            FormationSlotIndex = formationSlotIndex,
            FormationUnitCount = formationUnitCount,
            FormationMaxNavigationRadius = formationMaxNavigationRadius,
            FormationGroup = formationGroup
        };
    }

    public static CommandContext AttackMoveTo(
        Vector3 worldPosition, 
        int formationSlotIndex = 0, 
        int formationUnitCount = 1, 
        float formationMaxNavigationRadius = 1f, 
        int movementGroupId = 0)
    {
        return new CommandContext
        {
            WorldPosition = worldPosition,
            HasWorldPosition = true,

            MovementGroupId = movementGroupId,
            FormationSlotIndex = formationSlotIndex,
            FormationUnitCount = formationUnitCount,
            FormationMaxNavigationRadius = formationMaxNavigationRadius
        };
    }


    public static CommandContext AttackTarget(
        ITargetable target,
        GridCoord? attackPositionCell = null,
        int movementGroupId = 0,
        bool attackDeploymentResolved = false)
    {
        CommandContext context =
            new CommandContext
            {
                Target = target,
                MovementGroupId = movementGroupId,
                AttackDeploymentResolved = attackDeploymentResolved
            };

        if (attackPositionCell.HasValue)
        {
            context.AttackPositionCell = attackPositionCell.Value;
            context.HasAttackPositionCell = true;
        }

        return context;
    }

    public static CommandContext Gather(ResourceNode resourceNode)
    {
        return new CommandContext
        {
            ResourceNode = resourceNode
        };
    }

    public static CommandContext DeliverTo(BuildingBase building)
    {
        return new CommandContext
        {
            Target = building
        };
    }

    public static CommandContext Cell(Vector2Int gridCell)
    {
        return new CommandContext
        {
            GridCell = gridCell,
            HasGridCell = true
        };
    }
}
