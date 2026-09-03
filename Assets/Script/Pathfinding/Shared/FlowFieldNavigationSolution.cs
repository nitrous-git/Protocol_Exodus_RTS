using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// INavigationSolution backed by a grid flow field.
///
/// Each unit samples the shared field from its own world position.
/// </summary>
public sealed class FlowFieldNavigationSolution : INavigationSolution
{
    private const float MinDirectionSqr = 0.0001f;


    private static readonly IReadOnlyList<Vector3> EmptyDebugPath = Array.Empty<Vector3>();
    private readonly FlowField field;

    public bool IsValid => field != null && field.IsBuilt;
    public Vector3 Destination => field != null ? field.Destination : Vector3.zero;
    public IReadOnlyList<Vector3> DebugPath => EmptyDebugPath;
    public FlowField Field => field;

    public FlowFieldNavigationSolution(FlowField field)
    {
        this.field = field;
    }

    public NavigationSample SampleDirection(Vector3 worldPosition, int previousRouteSegmentIndex = -1)
    {
        if (!IsValid || field.Grid == null)
        {
            return NavigationSample.Invalid;
        }

        GridCoord currentCell = field.Grid.WorldToCell(worldPosition);

        if (!field.IsInside(currentCell) || !field.IsReachable(currentCell))
        {
            return NavigationSample.Invalid;
        }

        if (SameCell(currentCell, field.DestinationCell))
        {
            Vector3 destinationPoint =
                field.Grid.CellToWorld(
                    currentCell);

            return new NavigationSample(
                true,
                true,
                Vector3.zero,       // RoutePoint
                destinationPoint,
                destinationPoint,
                0f,  // DistanceFormula
                -1); // RouteSegmentIndex
        }

        Vector3 direction =
            field.GetDirection(
                currentCell);

        direction.y = 0f;

        if (direction.sqrMagnitude <= MinDirectionSqr)
        {
            return NavigationSample.Invalid;
        }

        direction.Normalize();

        Vector3 cellCenter =
            field.Grid.CellToWorld(
                currentCell);

        Vector3 lookAheadPoint =
            cellCenter +
            direction *
            field.Grid.CellSize;

        return new NavigationSample(
            true,
            false,
            direction,
            cellCenter,
            lookAheadPoint,
            0f,
            -1);
    }

    private static bool SameCell(GridCoord first, GridCoord second)
    {
        return first.x == second.x && first.z == second.z;
    }
}