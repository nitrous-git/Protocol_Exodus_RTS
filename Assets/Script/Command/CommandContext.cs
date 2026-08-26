using UnityEngine;

// weird struct implementation, compare to Java again later
public struct CommandContext
{
    public Vector3 WorldPosition;
    public Vector2Int GridCell;
    public int FormationSlotIndex;
    public int FormationUnitCount;
    public float FormationMaxNavigationRadius;

    public ITargetable Target;
    public ResourceNode ResourceNode;

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
        float formationMaxNavigationRadius = 1f)
    {
        return new CommandContext
        {
            WorldPosition = worldPosition,
            HasWorldPosition = true,

            FormationSlotIndex = formationSlotIndex,
            FormationUnitCount = formationUnitCount,
            FormationMaxNavigationRadius = formationMaxNavigationRadius
        };
    }

    public static CommandContext AttackMoveTo(
        Vector3 worldPosition, 
        int formationSlotIndex = 0, 
        int formationUnitCount = 1, 
        float formationMaxNavigationRadius = 1f)
    {
        return new CommandContext
        {
            WorldPosition = worldPosition,
            HasWorldPosition = true,

            FormationSlotIndex = formationSlotIndex,
            FormationUnitCount = formationUnitCount,
            FormationMaxNavigationRadius = formationMaxNavigationRadius
        };
    }


    public static CommandContext AttackTarget(ITargetable target)
    {
        return new CommandContext
        {
            Target = target
        };
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
