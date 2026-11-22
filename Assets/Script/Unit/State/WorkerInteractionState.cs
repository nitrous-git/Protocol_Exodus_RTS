using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns a worker's current interaction reservation and retries for one logistics order.
/// </summary>
public abstract class WorkerInteractionState : UnitState<WorkerUnit>
{
    private const float ProgressCheckInterval = 0.5f;
    private const float StallTimeout = 3f;

    private GridCoord? interactionCell;
    private Vector3 interactionPosition;
    private HashSet<GridCoord> rejectedCells;
    private string destination;
    private float retryDelay;
    private int retryCount;

    private float progressElapsed;
    private float stalledTime;
    private int lastWaypoint;
    private float bestWaypointDistance;

    protected bool HasApproach => interactionCell.HasValue;
    internal bool IsWaitingToRetry => retryDelay > 0f;

    protected abstract void FindApproach(WorkerUnit unit);

    protected void StartApproach(WorkerUnit unit, GridCoord origin, Vector2Int footprint,
        HashSet<GridCoord> rejected, Component target)
    {
        rejectedCells = rejected;
        if (unit.LogLogisticsRecovery)
            destination = $"{target.name} at {origin}";

        if (unit.Motor == null || unit.TerrainGrid == null || unit.DestinationAllocationSystem == null)
        {
            WaitForRetry(unit, "navigation unavailable");
            return;
        }

        interactionCell = unit.DestinationAllocationSystem.Ring.TryAllocate(unit, origin, footprint, excludedCells: rejectedCells);
        if (!interactionCell.HasValue)
        {
            // Revisit previously failed cells once there are no untried options on this footprint.
            rejectedCells.Clear();
            WaitForRetry(unit, "interaction allocation blocked");
            return;
        }

        interactionPosition = unit.TerrainGrid.CellToWorld(interactionCell.Value);
        if (!unit.Motor.MoveTo(interactionPosition))
        {
            WaitForRetry(unit, "pathfinding failed");
            return;
        }

        progressElapsed = stalledTime = 0f;
        lastWaypoint = -1;
        bestWaypointDistance = float.PositiveInfinity;
    }

    protected bool TickApproach(WorkerUnit unit, float deltaTime)
    {
        if (!interactionCell.HasValue)
        {
            retryDelay -= deltaTime;
            if (retryDelay <= 0f)
                FindApproach(unit);

            return false;
        }

        UnitMotor motor = unit.Motor;
        if (motor == null)
        {
            WaitForRetry(unit, "navigation unavailable", rejectCell: false);
            return false;
        }

        // HasArrived is also true after Stop or a cleared path. Interact only at the reserved cell.
        if (motor.HasArrived)
        {
            if (!IsReservationUsable(unit))
                WaitForRetry(unit, "interaction cell blocked or reservation lost");
            else if (!motor.IsAtDestination(interactionPosition))
                WaitForRetry(unit, "movement ended outside the interaction cell");
            else
            {
                if (retryCount > 0)
                {
                    retryCount = 0;
                    rejectedCells.Clear();
                }

                return true;
            }

            return false;
        }

        progressElapsed += deltaTime;
        if (progressElapsed < ProgressCheckInterval)
            return false;

        if (!IsReservationUsable(unit))
        {
            WaitForRetry(unit, "interaction cell blocked or reservation lost");
            return false;
        }

        if (motor.TryGetPathProgress(out int waypoint, out float distance) &&
            (waypoint > lastWaypoint || bestWaypointDistance - distance >= unit.TerrainGrid.CellSize * 0.15f))
        {
            lastWaypoint = waypoint;
            bestWaypointDistance = distance;
            stalledTime = 0f;
        }
        else
        {
            stalledTime += progressElapsed;
            if (stalledTime >= StallTimeout)
                WaitForRetry(unit, "movement stalled");
        }

        progressElapsed = 0f;
        return false;
    }

    protected void WaitForRetry(WorkerUnit unit, string reason, bool rejectCell = true)
    {
        retryCount++;
        if (unit.LogLogisticsRecovery)
        {
            string cell = interactionCell.HasValue ? $", cell {interactionCell.Value}" : string.Empty;
            Debug.Log($"Worker {unit.UnitId} ({unit.OwnerFaction?.Name}) {unit.CurrentCommand}, " +
                $"cargo {unit.ResourceComponent.CargoAmount}, destination {destination ?? "none"}{cell}, " +
                $"{reason}, retry {retryCount}.", unit);
        }

        if (rejectCell && interactionCell.HasValue)
            rejectedCells.Add(interactionCell.Value);

        ReleaseApproach(unit);
        unit.ResourceComponent.ResetActionCycle();
        // Stagger retries so a group blocked together does not replan on the same tick.
        retryDelay = Mathf.Min(3f, 0.75f + (retryCount - 1) * 0.5f) + unit.UnitId % 11 * 0.035f;
    }

    protected override void OnExitTyped(WorkerUnit unit) => ReleaseApproach(unit);

    private bool IsReservationUsable(WorkerUnit unit) =>
        unit.DestinationAllocationSystem.Ring.IsReservationUsable(interactionCell.Value, unit);

    private void ReleaseApproach(WorkerUnit unit)
    {
        unit.Motor?.Stop();
        if (!interactionCell.HasValue)
            return;

        unit.ReleaseDestination(interactionCell.Value);
        interactionCell = null;
    }
}
