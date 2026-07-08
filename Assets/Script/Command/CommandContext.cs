using UnityEngine;

// weird implementation, compare to Java again later
public struct CommandContext
{
    public Vector3 WorldPosition;
    public Vector2Int GridCell;
    public ITargetable Target;

    public bool HasWorldPosition;
    public bool HasGridCell;
    public bool HasTarget => Target != null;

    public static CommandContext None()
    {
        return new CommandContext();
    }

    public static CommandContext MoveTo(Vector3 worldPosition)
    {
        return new CommandContext
        {
            WorldPosition = worldPosition,
            HasWorldPosition = true
        };
    }

    public static CommandContext AttackTarget(ITargetable target)
    {
        return new CommandContext
        {
            Target = target
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
