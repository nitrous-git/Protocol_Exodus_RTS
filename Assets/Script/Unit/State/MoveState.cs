using UnityEngine;

public class MoveState : UnitState<UnitBase>
{
    private Vector3 formationCenter;
    public int formationSlotIndex;
    public int formationUnitCount;

    private GridCoord? reservedDestinationCell;
    private bool pathRequested;
    private float formationMaxNavigationRadius;

    private readonly FormationMovementGroup formationGroup;

    public MoveState(
        Vector3 formationCenter, 
        int formationSlotIndex, 
        int formationUnitCount, 
        float formationMaxNavigationRadius,
        FormationMovementGroup formationGroup)
    {
        this.formationCenter = formationCenter;
        this.formationSlotIndex = formationSlotIndex;
        this.formationUnitCount = formationUnitCount;
        this.formationMaxNavigationRadius = formationMaxNavigationRadius;
        this.formationGroup = formationGroup;
    }

    protected override void OnEnterTyped(
         UnitBase unit)
    {
        if (unit.Motor == null ||
            unit.TerrainGrid == null ||
            unit.DestinationAllocationSystem == null)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        if (!AllocateDestinationAndMove(unit))
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());

            return;
        }

        unit.View?.PlayAnim("Walk");
    }

    protected override void TickTyped(UnitBase unit)
    {
        //
        // All units share the same object.
        // FormationMovementGroup internally evaluates once/frame.
        //
        formationGroup?.Tick();

        if (!pathRequested)
        {
            if (formationGroup == null || formationGroup.FinalAssignmentDone)
            {
                unit.IssueCommand(CommandType.Idle, CommandContext.None());
            }

            return;
        }

        if (unit.Motor == null || !unit.Motor.HasArrived)
        {
            return;
        }

        //
        // Important:
        //
        // A unit that reaches its INITIAL slot early must remain
        // part of the movement group until the final reassignment
        // has occurred.
        //
        if (formationGroup != null && !formationGroup.FinalAssignmentDone)
        {
            return;
        }

        unit.IssueCommand(CommandType.Idle, CommandContext.None());
    }

    protected override void OnExitTyped(UnitBase unit)
    {
        ReleaseReservedDestination(unit);
    }

    // ---------------------------------------------------------------------
    // Final reassignment
    // ---------------------------------------------------------------------

    public void ReleaseForFormationReassignment(
        UnitBase unit,
        int movementGroupId)
    {
        if (formationGroup == null ||
            formationGroup.MovementGroupId !=
            movementGroupId)
        {
            return;
        }

        ReleaseReservedDestination(unit);
    }

    public bool ReassignFormationSlot(
        UnitBase unit,
        int movementGroupId,
        int newSlotIndex)
    {
        if (formationGroup == null ||
            formationGroup.MovementGroupId !=
            movementGroupId)
        {
            return false;
        }

        formationSlotIndex = newSlotIndex;

        return AllocateDestinationAndMove(unit);
    }

    // ---------------------------------------------------------------------
    // Destination
    // ---------------------------------------------------------------------

    private bool AllocateDestinationAndMove(
        UnitBase unit)
    {
        GridCoord centerCell =
            unit.TerrainGrid.WorldToCell(
                formationCenter);

        reservedDestinationCell =
            unit.DestinationAllocationSystem
                .Formation
                .TryAllocate(
                    unit,
                    centerCell,
                    formationSlotIndex,
                    formationUnitCount,
                    formationMaxNavigationRadius);

        if (!reservedDestinationCell.HasValue)
        {
            pathRequested = false;
            return false;
        }

        Vector3 destination =
            unit.TerrainGrid.CellToWorld(
                reservedDestinationCell.Value);

        pathRequested =
            unit.Motor.MoveTo(
                destination);

        if (!pathRequested)
        {
            ReleaseReservedDestination(unit);
            return false;
        }

        return true;
    }

    private void ReleaseReservedDestination(UnitBase unit)
    {
        if (!reservedDestinationCell.HasValue)
            return;

        unit.ReleaseDestination(reservedDestinationCell.Value);

        reservedDestinationCell = null;
    }
}
