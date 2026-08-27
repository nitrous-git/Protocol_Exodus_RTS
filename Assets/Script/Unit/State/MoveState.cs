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

    protected override void OnEnterTyped(UnitBase unit)
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
            Debug.Log("Could not allocate destination and move");
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        unit.View?.PlayAnim("Walk");
    }

    protected override void TickTyped(UnitBase unit)
    {
        //
        // Ordinary MoveState without formation semantics.
        //
        if (formationGroup == null)
        {
            Debug.Log(unit.name + " ordinary MovaState  ticking");
            if (!pathRequested || unit.Motor == null || unit.Motor.HasArrived)
            {
                unit.IssueCommand(CommandType.Idle, CommandContext.None());
            }

            return;
        }

        //
        // FIRST:
        // allow this unit to lock its current slot
        // before the group considers final reassignment.
        //
        TryCommitFormationSlot(unit);

        //
        // THEN:
        // evaluate assembly/final reassignment.
        //
        formationGroup.Tick();

        //
        // A committed unit stays stopped but remains in
        // MoveState until the one-time final pass occurs.
        //
        // This keeps its MovementGroupId and reservation alive,
        // so it acts as a real locked formation member.
        //
        if (formationGroup.IsCommitted(unit))
        {
            if (formationGroup.FinalAssignmentDone)
            {
                unit.IssueCommand(CommandType.Idle, CommandContext.None());
            }

            return;
        }

        //
        // Still traveling toward its assigned slot.
        //
        if (pathRequested)
            return;

        //
        // If a path disappeared/finished, attempt commitment
        // once more. This also handles the generic motor reaching
        // the exact slot before this state got another tick.
        //
        if (TryCommitFormationSlot(unit))
        {
            if (formationGroup.FinalAssignmentDone)
            {
                unit.IssueCommand(CommandType.Idle, CommandContext.None());
            }

            return;
        }

        //
        // Final reassignment has already happened and this unit
        // no longer has useful movement.
        //
        if (formationGroup.FinalAssignmentDone)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
        }
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
        if (formationGroup == null || formationGroup.MovementGroupId != movementGroupId)
        {
            return;
        }

        if (formationGroup.IsCommitted(unit))
            return;

        ReleaseReservedDestination(unit);
    }

    public bool ReassignFormationSlot(
        UnitBase unit,
        int movementGroupId,
        int newSlotIndex)
    {
        if (formationGroup == null || formationGroup.MovementGroupId != movementGroupId)
        {
            return false;
        }

        if (formationGroup.IsCommitted(unit))
            return false;

        formationSlotIndex = newSlotIndex;

        return AllocateDestinationAndMove(unit);
    }

    // ---------------------------------------------------------------------
    // Destination
    // ---------------------------------------------------------------------

    private bool AllocateDestinationAndMove(UnitBase unit)
    {
        GridCoord centerCell =unit.TerrainGrid.WorldToCell(formationCenter);

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

        Vector3 destination = unit.TerrainGrid.CellToWorld(reservedDestinationCell.Value);

        pathRequested = unit.Motor.MoveTo(destination);

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

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private bool TryCommitFormationSlot(UnitBase unit)
    {
        if (formationGroup == null)
            return false;

        if (formationGroup.IsCommitted(unit))
            return true;

        if (!reservedDestinationCell.HasValue)
            return false;

        if (unit.TerrainGrid == null)
            return false;

        Vector3 destination = unit.TerrainGrid.CellToWorld(reservedDestinationCell.Value);
        Vector3 difference = destination - unit.Position;
        difference.y = 0f;

        float tolerance = formationGroup.ArrivalTolerance;

        if (difference.sqrMagnitude > tolerance * tolerance)
        {
            return false;
        }

        if (!formationGroup.TryCommit(unit, formationSlotIndex))
        {
            return false;
        }

        //
        // Do NOT snap to the slot center.
        // Stop exactly where we are.
        //
        unit.Motor?.Stop();

        pathRequested = false;

        return true;
    }
}
