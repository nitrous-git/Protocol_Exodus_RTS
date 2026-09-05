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
    public float NavigationRadius => field != null ? field.NavigationRadius : 0f;
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

        if (field.IsGoalCell(currentCell))
        {
            Vector3 goalPoint = field.Grid.CellToWorld(currentCell);

            return new NavigationSample(
                true,
                false,
                Vector3.zero,       // RoutePoint
                goalPoint,
                goalPoint,
                0f,  // DistanceFormula
                -1); // RouteSegmentIndex
        }

        //Vector3 direction =
        //    field.GetDirection(
        //        currentCell);

        //direction.y = 0f;

        //if (direction.sqrMagnitude <= MinDirectionSqr)
        //{
        //    return NavigationSample.Invalid;
        //}

        //direction.Normalize();

        Vector3 direction = SampleSmoothedDirection(worldPosition, currentCell);

        direction.y = 0f;

        if (direction.sqrMagnitude <= MinDirectionSqr)
        {
            // Safe fallback to the discrete direction.
            direction = field.GetDirection( currentCell);
            direction.y = 0f;

            if (direction.sqrMagnitude <= MinDirectionSqr)
            {
                return NavigationSample.Invalid;
            }
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

    // ---------------------------------------------------------------------
    // Smoothing Path
    // ---------------------------------------------------------------------

    private Vector3 SampleSmoothedDirection(Vector3 worldPosition, GridCoord currentCell)
    {
        Vector3 cellCenter = field.Grid.CellToWorld(currentCell);
        float cellSize = field.Grid.CellSize;
        float localX = (worldPosition.x - cellCenter.x) / cellSize;
        float localZ = (worldPosition.z - cellCenter.z) / cellSize;

        int x0;
        int x1;
        float tx;

        if (localX >= 0f)
        {
            x0 = currentCell.x;
            x1 = currentCell.x + 1;
            tx = localX;
        }
        else
        {
            x0 = currentCell.x - 1;
            x1 = currentCell.x;
            tx = 1f + localX;
        }

        int z0;
        int z1;
        float tz;

        if (localZ >= 0f)
        {
            z0 = currentCell.z;
            z1 = currentCell.z + 1;
            tz = localZ;
        }
        else
        {
            z0 = currentCell.z - 1;
            z1 = currentCell.z;
            tz = 1f + localZ;
        }

        tx = Mathf.Clamp01(tx);
        tz = Mathf.Clamp01(tz);

        GridCoord c00 = new GridCoord(x0, z0);
        GridCoord c10 = new GridCoord(x1, z0);
        GridCoord c01 = new GridCoord(x0, z1);
        GridCoord c11 = new GridCoord(x1, z1);

        float w00 = (1f - tx) * (1f - tz);
        float w10 = tx * (1f - tz);
        float w01 = (1f - tx) * tz;
        float w11 = tx * tz;

        Vector3 blended = Vector3.zero;
        float totalWeight = 0f;

        AddDirectionSample(currentCell, c00, w00, ref blended, ref totalWeight);
        AddDirectionSample(currentCell, c10, w10, ref blended, ref totalWeight);
        AddDirectionSample(currentCell, c01, w01, ref blended, ref totalWeight);
        AddDirectionSample(currentCell, c11, w11, ref blended, ref totalWeight);

        if (totalWeight <= Mathf.Epsilon)
        {
            return Vector3.zero;
        }

        blended /= totalWeight;
        blended.y = 0f;

        return blended;
    }

    private void AddDirectionSample(GridCoord currentCell, GridCoord sampleCell, float weight, ref Vector3 blended, ref float totalWeight)
    {
        if (weight <= 0f)
            return;

        if (!CanBlendFrom(currentCell, sampleCell))
        {
            return;
        }

        //
        // Goal cells have zero direction.
        //
        // Do not blend their zero vector into an
        // outside cell and artificially slow it down.
        //
        if (field.IsGoalCell(sampleCell))
        {
            return;
        }

        Vector3 direction = field.GetDirection(sampleCell);
        direction.y = 0f;

        if (direction.sqrMagnitude <= MinDirectionSqr)
        {
            return;
        }

        direction.Normalize();

        blended += direction * weight;
        totalWeight += weight;
    }

    private bool CanBlendFrom(GridCoord current, GridCoord sample)
    {
        if (!field.IsInside(sample) || !field.IsReachable(sample))
        {
            return false;
        }

        int deltaX = sample.x - current.x;
        int deltaZ = sample.z - current.z;

        if (Mathf.Abs(deltaX) > 1 || Mathf.Abs(deltaZ) > 1)
        {
            return false;
        }

        bool diagonal = deltaX != 0 && deltaZ != 0;

        if (!diagonal)
            return true;

        GridCoord horizontal = new GridCoord(current.x + deltaX, current.z);
        GridCoord vertical = new GridCoord(current.x, current.z + deltaZ);

        return field.IsTraversable(horizontal) && field.IsTraversable(vertical);
    }

    // ---------------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------------

    private static bool SameCell(GridCoord first, GridCoord second)
    {
        return first.x == second.x && first.z == second.z;
    }
}