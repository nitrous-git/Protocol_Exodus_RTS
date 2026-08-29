using UnityEngine;

public sealed class AttackState :
    UnitState<CombatUnit>
{
    //
    // Formation-style soft tolerance:
    //
    // the assigned attack cell defines the intended
    // radial firing layer, but we do not insist that the
    // unit touch the exact cell center.
    //
    private const float AssignedDepthToleranceCells = 0.50f;

    private readonly ITargetable target;

    private readonly bool deploymentResolved;

    private readonly GridCoord? preallocatedAttackCell;

    private GridCoord? reservedAttackCell;

    private bool pathRequested;

    // =====================================================================
    // Automatic / individual combat
    //
    // Keeps the old simple allocator behavior.
    // =====================================================================

    public AttackState(
        ITargetable target)
        : this(
            target,
            false,
            null)
    {
    }

    // =====================================================================
    // Explicit player Attack
    //
    // Combat Deployment was already solved once by CommandIssuer.
    // =====================================================================

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

    // =====================================================================
    // State
    // =====================================================================

    protected override void OnEnterTyped(
        CombatUnit unit)
    {
        unit.Motor?.Stop();

        unit.SetCurrentTarget(
            target);

        //unit.View?.PlayAnim("Idle");

        RequestAttackPosition(unit);
    }

    protected override void TickTyped(
        CombatUnit unit,
        float deltaTime)
    {
        // -------------------------------------------------------------
        // Target validation
        // -------------------------------------------------------------

        if (!unit.IsValidAttackTarget(
                target))
        {
            unit.FinishCombatEngagement();
            return;
        }

        // -------------------------------------------------------------
        // Tactical approach
        // -------------------------------------------------------------

        if (pathRequested)
        {
            if (deploymentResolved)
            {
                //
                // Explicit Combat Deployment:
                //
                // Do NOT stop everybody at one universal
                // "95% weapon range" shell.
                //
                // Stop when THIS unit reaches approximately
                // the radial depth of ITS assigned attack cell.
                //
                if (HasReachedAssignedRadialDepth(
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
                    // Exact assigned cell reached before our
                    // radial tolerance triggered.
                    //
                    FinishTacticalApproach(
                        unit);
                }
            }
            else
            {
                //
                // Old individual / automatic attack semantics:
                // simply finish the tactical path.
                //
                if (unit.Motor != null &&
                    !unit.Motor.HasArrived)
                {
                    return;
                }

                FinishTacticalApproach(
                    unit);
            }
        }

        // -------------------------------------------------------------
        // Combat
        // -------------------------------------------------------------

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

    // =====================================================================
    // Deployment
    // =====================================================================

    private void RequestAttackPosition(
        CombatUnit unit)
    {
        pathRequested = false;

        // -------------------------------------------------------------
        // Explicit player Attack
        // -------------------------------------------------------------

        if (deploymentResolved)
        {
            if (preallocatedAttackCell
                .HasValue)
            {
                reservedAttackCell =
                    preallocatedAttackCell
                        .Value;
            }

            //
            // Already in firing range:
            // do not redeploy.
            //
            if (unit.IsWithinAttackRange(
                    target))
            {
                ReleaseAttackPosition(
                    unit);

                unit.PrepareMovementGroup(
                    0);

                return;
            }

            //
            // Deployment was solved, but this unit did not
            // obtain a firing position.
            //
            // No retry.
            // No polling.
            // Stay where it currently is.
            //
            if (!reservedAttackCell
                .HasValue)
            {
                unit.PrepareMovementGroup(
                    0);

                return;
            }
        }

        // -------------------------------------------------------------
        // Automatic / individual combat
        // -------------------------------------------------------------

        else
        {
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

            if (!reservedAttackCell
                .HasValue)
            {
                return;
            }
        }

        // -------------------------------------------------------------
        // Path to tactical position
        // -------------------------------------------------------------

        if (unit.Motor == null ||
            unit.TerrainGrid == null)
        {
            ReleaseAttackPosition(
                unit);

            if (deploymentResolved)
            {
                unit.PrepareMovementGroup(
                    0);
            }

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
            {
                unit.PrepareMovementGroup(
                    0);
            }

            return;
        }

        unit.View?.PlayAnim(
            "Walk");
    }

    // =====================================================================
    // Explicit deployment arrival
    // =====================================================================

    private bool HasReachedAssignedRadialDepth(
        CombatUnit unit)
    {
        if (!deploymentResolved ||
            !reservedAttackCell.HasValue ||
            target == null ||
            unit.TerrainGrid == null)
        {
            return false;
        }

        Vector3 assignedPosition =
            unit.TerrainGrid.CellToWorld(
                reservedAttackCell.Value);

        float assignedDistance =
            FlatDistance(
                assignedPosition,
                target.Position);

        float currentDistance =
            FlatDistance(
                unit.Position,
                target.Position);

        float tolerance =
            unit.TerrainGrid.CellSize *
            AssignedDepthToleranceCells;

        //
        // The cell is a tactical guide, not a sacred endpoint.
        //
        // We only need:
        //
        // 1. to actually be in weapon range
        // 2. to have reached approximately our own assigned
        //    radial firing layer
        //
        // Example:
        //
        // front assignment ~= 70%
        //     keeps moving through 95%, 90%, 80%...
        //     stops around its 70% shell
        //
        // rear assignment ~= 95%
        //     stops much earlier
        //
        return unit.IsWithinAttackRange(
                   target)
               &&
               currentDistance <=
                   assignedDistance +
                   tolerance;
    }

    private void FinishTacticalApproach(
        CombatUnit unit)
    {
        //
        // Important:
        // Stop where we actually are.
        //
        // UnitMotor.Stop() clears the path and velocity;
        // we do not snap to the attack cell center.
        //
        unit.Motor?.Stop();

        pathRequested = false;

        //
        // Once engagement begins, the temporary attack
        // destination no longer needs to block anyone else.
        //
        ReleaseAttackPosition(
            unit);

        //
        // Explicit Attack movers are no longer moving members
        // of the command group.
        //
        // They now behave as stationary combat occupancy.
        //
        if (deploymentResolved)
        {
            unit.PrepareMovementGroup(
                0);
        }

        unit.View?.PlayAnim(
            "Idle");
    }

    // =====================================================================
    // Reservation
    // =====================================================================

    private void ReleaseAttackPosition(
        CombatUnit unit)
    {
        if (!reservedAttackCell
            .HasValue)
        {
            return;
        }

        unit.ReleaseDestination(
            reservedAttackCell.Value);

        reservedAttackCell =
            null;
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static float FlatDistance(
        Vector3 first,
        Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;

        return Vector3.Distance(
            first,
            second);
    }
}