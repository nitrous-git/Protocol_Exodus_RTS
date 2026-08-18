using UnityEngine;

public class MoveState : UnitState<UnitBase>
{
    private Vector3 formationCenter;
    public int formationSlotIndex;
    public int formationUnitCount;

    private GridCoord? reservedDestinationCell;
    private bool pathRequested;

    public MoveState(Vector3 formationCenter, int formationSlotIndex, int formationUnitCount)
    {
        this.formationCenter = formationCenter;
        this.formationSlotIndex = formationSlotIndex;
        this.formationUnitCount = formationUnitCount;
    }

    protected override void OnEnterTyped(UnitBase unit)
    {
        if (unit.Motor == null ||
            unit.TerrainGrid == null ||
            unit.DestinationAllocationSystem == null)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());

            return;
        }

        GridCoord centerCell = unit.TerrainGrid.WorldToCell(formationCenter);

        reservedDestinationCell = unit.DestinationAllocationSystem.Formation.TryAllocate(unit, centerCell, formationSlotIndex, formationUnitCount);

        if (!reservedDestinationCell.HasValue)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        Vector3 destination = unit.TerrainGrid.CellToWorld(reservedDestinationCell.Value);

        pathRequested = unit.Motor.MoveTo(destination);

        if (!pathRequested)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
        }

        unit.View?.PlayAnim("Walk");
    }

    protected override void TickTyped(UnitBase unit)
    {
        if (!pathRequested)
            return;

        if (unit.Motor == null || unit.Motor.HasArrived)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
        }
    }

    protected override void OnExitTyped(UnitBase unit)
    {
        if (!reservedDestinationCell.HasValue)
            return;

        unit.ReleaseDestination(reservedDestinationCell.Value);
        reservedDestinationCell = null;
    }
}
