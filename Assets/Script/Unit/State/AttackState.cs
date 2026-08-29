using UnityEngine;

public sealed class AttackState : UnitState<CombatUnit>
{
    private const float ComfortableRangeFraction = 0.95f;

    private readonly ITargetable target;
    private readonly bool deploymentResolved;
    private readonly GridCoord? preallocatedAttackCell;

    private GridCoord? reservedAttackCell;
    private bool pathRequested;

    // ---------------------------------------------------------
    // Automatic / individual combat.
    // Keeps old behavior.
    // ---------------------------------------------------------

    public AttackState(
        ITargetable target)
        : this(
            target,
            false,
            null)
    {
    }

    // ---------------------------------------------------------
    // Explicit player Attack.
    // Deployment was solved before entering state.
    // ---------------------------------------------------------

    public AttackState(
        ITargetable target,
        bool deploymentResolved,
        GridCoord? preallocatedAttackCell)
    {
        this.target =
            target;

        this.deploymentResolved =
            deploymentResolved;

        this.preallocatedAttackCell =
            preallocatedAttackCell;
    }

    protected override void OnEnterTyped(
        CombatUnit unit)
    {
        unit.Motor?.Stop();

        unit.SetCurrentTarget(target);

        unit.View?.PlayAnim(
            "Idle");

        RequestAttackPosition(
            unit);
    }

    protected override void TickTyped(
        CombatUnit unit,
        float deltaTime)
    {
        // -----------------------------------------------------
        // Validate target.
        // -----------------------------------------------------

        if (!unit
                .IsValidAttackTarget(
                    target))
        {
            unit.FinishCombatEngagement();
            return;
        }

        // -----------------------------------------------------
        // Tactical approach.
        // -----------------------------------------------------

        if (pathRequested)
        {
            //
            // Soft combat commitment.
            //
            // Once we have comfortable firing access,
            // the allocated cell has done its job.
            //
            if (IsWithinComfortableRange(
                    unit))
            {
                FinishTacticalApproach(
                    unit);
            }
            else if (unit.Motor != null &&
                     !unit.Motor.HasArrived)
            {
                return;
            }
            else
            {
                //
                // Reached assigned destination.
                //
                // Deployment is final even if target moved.
                // NO retry.
                //
                FinishTacticalApproach(
                    unit);
            }
        }

        // -----------------------------------------------------
        // Combat.
        // -----------------------------------------------------

        if (unit.IsWithinAttackRange(
                target))
        {
            unit.UpdateAttack(
                deltaTime);
        }
    }

    protected override void OnExitTyped(
        CombatUnit unit)
    {
        unit.Motor?.Stop();

        ReleaseAttackPosition(
            unit);

        unit.CancelAttackAnimation();
        unit.ClearCurrentTarget();
    }

    // ---------------------------------------------------------
    // Deployment
    // ---------------------------------------------------------

    private void RequestAttackPosition(
        CombatUnit unit)
    {
        pathRequested = false;

        // -----------------------------------------------------
        // Explicit command:
        // position has ALREADY been decided.
        // -----------------------------------------------------

        if (deploymentResolved)
        {
            if (preallocatedAttackCell.HasValue)
            {
                reservedAttackCell =
                    preallocatedAttackCell.Value;
            }

            //
            // Already useful:
            // don't reposition.
            //
            if (unit.IsWithinAttackRange(
                    target))
            {
                ReleaseAttackPosition(
                    unit);

                unit.PrepareMovementGroup(0);

                return;
            }

            //
            // Deployment was resolved but no position
            // was available.
            //
            // STAGING = stay where you are.
            //
            if (!reservedAttackCell.HasValue)
            {
                unit.PrepareMovementGroup(0);
                return;
            }
        }
        else
        {
            // -------------------------------------------------
            // Automatic combat:
            // retain the old individual allocator.
            // -------------------------------------------------

            if (unit.IsWithinAttackRange(
                    target))
            {
                return;
            }

            if (unit.Motor == null ||
                unit.TerrainGrid == null ||
                unit.DestinationAllocationSystem ==
                    null)
            {
                return;
            }

            reservedAttackCell =
                unit
                    .DestinationAllocationSystem
                    .Attack
                    .TryAllocate(
                        unit,
                        target);

            if (!reservedAttackCell.HasValue)
                return;
        }

        if (unit.Motor == null ||
            unit.TerrainGrid == null)
        {
            ReleaseAttackPosition(
                unit);

            if (deploymentResolved)
                unit.PrepareMovementGroup(0);

            return;
        }

        Vector3 destination =
            unit.TerrainGrid.CellToWorld(
                reservedAttackCell.Value);

        pathRequested =
            unit.Motor.MoveTo(
                destination);

        if (!pathRequested)
        {
            ReleaseAttackPosition(
                unit);

            if (deploymentResolved)
                unit.PrepareMovementGroup(0);

            return;
        }

        unit.View?.PlayAnim(
            "Walk");
    }

    private void FinishTacticalApproach(
        CombatUnit unit)
    {
        unit.Motor?.Stop();

        pathRequested = false;

        //
        // We no longer need to own a tactical destination.
        //
        ReleaseAttackPosition(
            unit);

        //
        // Explicit attack movers now become real
        // stationary combatants for A* occupancy semantics.
        //
        if (deploymentResolved)
        {
            unit.PrepareMovementGroup(0);
        }

        unit.View?.PlayAnim(
            "Idle");
    }

    private bool IsWithinComfortableRange(
        CombatUnit unit)
    {
        if (target == null)
            return false;

        float attackRange =
            unit.GetAttackRange();

        float comfortableRange =
            attackRange *
            ComfortableRangeFraction;

        Vector3 difference =
            target.Position -
            unit.Position;

        difference.y = 0f;

        return difference.sqrMagnitude <=
               comfortableRange *
               comfortableRange;
    }

    private void ReleaseAttackPosition(
        CombatUnit unit)
    {
        if (!reservedAttackCell.HasValue)
            return;

        unit.ReleaseDestination(
            reservedAttackCell.Value);

        reservedAttackCell = null;
    }
}