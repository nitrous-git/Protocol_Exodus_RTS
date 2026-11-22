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

    private readonly CombatUnitAISettings aiSettings;
    private readonly bool canPursue;
    private GridCoord? rejectedAttackCell;
    private Vector3 plannedTargetPosition;
    private Vector3 lastProgressPosition;
    private float repathDelay;
    private float timeWithoutProgress;
    private int failedApproaches;

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

    internal AttackState(ITargetable target, CombatUnitAISettings settings, bool canPursue)
        : this(target)
    {
        aiSettings = settings;
        this.canPursue = canPursue;
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

        if (aiSettings == null)
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

        if (aiSettings != null)
        {
            TickAICombat(unit, deltaTime);
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
                    StopTacticalApproach(
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
                    StopTacticalApproach(
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

                StopTacticalApproach(
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

    private void TickAICombat(CombatUnit unit, float deltaTime)
    {
        repathDelay -= deltaTime;
        if (unit.IsWithinAttackRange(target))
        {
            if (pathRequested)
                StopTacticalApproach(unit);

            failedApproaches = 0;
            rejectedAttackCell = null;
            timeWithoutProgress = 0f;
            unit.UpdateAttack(deltaTime);
            return;
        }

        if (!canPursue)
        {
            unit.FinishCombatEngagement();
            return;
        }

        if (pathRequested)
        {
            timeWithoutProgress += deltaTime;
            if (FlatDistance(lastProgressPosition, unit.Position) >= CombatUnitAISettings.ProgressDistance)
            {
                lastProgressPosition = unit.Position;
                timeWithoutProgress = 0f;
            }

            bool stalled = unit.Motor.HasArrived || timeWithoutProgress >= CombatUnitAISettings.StalledTravelTimeout;
            bool destinationBlocked = !unit.TerrainGrid.HasNavigationClearance(reservedAttackCell.Value, unit.NavigationRadius);
            if (stalled || destinationBlocked)
            {
                failedApproaches++;
                rejectedAttackCell = reservedAttackCell;
            }
            else if (repathDelay > 0f || !ShouldRepath(unit))
            {
                return;
            }

            // A replacement path can start this tick.
            StopTacticalApproach(unit);
        }

        if (failedApproaches >= 3)
        {
            unit.FinishCombatEngagement();
            return;
        }

        if (repathDelay <= 0f)
        {
            unit.CancelAttackAnimation();
            repathDelay = Mathf.Max(0.05f, aiSettings.repathInterval);
            plannedTargetPosition = target.Position;
            lastProgressPosition = unit.Position;
            RequestAttackPosition(unit);
            if (!pathRequested)
                failedApproaches++;
        }
    }

    private bool ShouldRepath(CombatUnit unit)
    {
        Vector3 targetPosition = target.Position;
        Vector3 destination = unit.TerrainGrid.CellToWorld(reservedAttackCell.Value);
        return FlatDistance(plannedTargetPosition, targetPosition) >= aiSettings.repathDistance &&
            FlatDistance(destination, targetPosition) > unit.GetAttackRange();
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
                        target, rejectedAttackCell);

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
            if (aiSettings != null)
                rejectedAttackCell = reservedAttackCell;
            ReleaseAttackPosition(
                unit);

            if (deploymentResolved)
            {
                unit.PrepareMovementGroup(
                    0);
            }
        }
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

    private void StopTacticalApproach(CombatUnit unit)
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
