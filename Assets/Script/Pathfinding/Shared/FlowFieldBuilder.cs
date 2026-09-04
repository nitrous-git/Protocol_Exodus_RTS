using UnityEngine;

/// <summary>
/// Builds a static clearance-aware FlowField toward one destination.
///
/// The integration field is generated outward from the destination
/// using Dijkstra propagation.
///
/// Dynamic units and reservations are intentionally ignored.
/// </summary>
public static class FlowFieldBuilder
{
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;

    private static readonly GridCoord[] NeighborOffsets =
    {
        new GridCoord(-1,  0),
        new GridCoord( 1,  0),
        new GridCoord( 0, -1),
        new GridCoord( 0,  1),

        new GridCoord(-1, -1),
        new GridCoord(-1,  1),
        new GridCoord( 1, -1),
        new GridCoord( 1,  1)
    };

    public static FlowField Build(
        TerrainGrid grid,
        Vector3 destination,
        float navigationRadius)
    {
        if (grid == null)
        {
            Debug.LogWarning(
                "[FlowField] Cannot build: TerrainGrid missing.");

            return null;
        }

        double startTime = Time.realtimeSinceStartupAsDouble;

        FlowField field =
            new FlowField(
                grid,
                destination,
                navigationRadius);

        BuildTraversability(field);

        double afterTraversability = Time.realtimeSinceStartupAsDouble;

        if (!field.IsInside(
            field.DestinationCell))
        {
            Debug.LogWarning(
                "[FlowField] Destination lies outside the grid.");

            return null;
        }

        if (!field.IsTraversable(
            field.DestinationCell))
        {
            Debug.LogWarning(
                $"[FlowField] Destination cell ({field.DestinationCell.x}, {field.DestinationCell.z}) " +
                $"is not traversable for radius {navigationRadius:F2}.");

            return null;
        }

        int expandedCells = BuildIntegrationField(field); // also build the DirectionField

        double afterIntegration = Time.realtimeSinceStartupAsDouble;

        field.CompleteBuild();

        double endTime = Time.realtimeSinceStartupAsDouble;

        double elapsedMs =
            (Time.realtimeSinceStartupAsDouble -
             startTime) *
            1000.0;

        Debug.Log(
            $"[FlowField] " +
            $"Expanded={expandedCells} " +
            $"TraversabilityMs=" + $"{(afterTraversability - startTime) * 1000.0:F2} " +
            $"IntegrationMs=" + $"{(afterIntegration - afterTraversability) * 1000.0:F2} " +
            $"TotalMs=" + $"{(endTime - startTime) * 1000.0:F2}");

        return field;
    }

    private static void BuildTraversability(FlowField field)
    {
        TerrainGrid grid = field.Grid;

        for (int z = 0; z < field.Height; z++)
        {
            for (int x = 0; x < field.Width; x++)
            {
                GridCoord coord = new GridCoord(x, z);

                bool traversable =
                    grid.IsStaticallyTraversable(
                        coord,
                        field.NavigationRadius);

                field.SetTraversable(coord, traversable);
            }
        }
    }

    private static int BuildIntegrationField(FlowField field)
    {
        MinPriorityQueue<GridCoord, int> openQueue = new MinPriorityQueue<GridCoord, int>();

        bool[] settled = new bool[field.Width * field.Height];

        field.SetIntegrationCost(field.DestinationCell, 0);
        field.SetDirection(field.DestinationCell, Vector3.zero);

        openQueue.Enqueue(field.DestinationCell, 0);

        int expandedCells = 0;

        while (openQueue.Count > 0)
        {
            GridCoord current = openQueue.Dequeue();

            int currentIndex = GetIndex(current, field.Width);

            // Lazy duplicate queue entries
            if (settled[currentIndex])
                continue;

            settled[currentIndex] = true;

            int currentCost = field.GetIntegrationCost(current);

            if (currentCost == FlowField.UnreachableCost)
            {
                continue;
            }

            expandedCells++;

            ExploreNeighbors(field, current, currentCost, openQueue);
        }

        return expandedCells;
    }

    private static void ExploreNeighbors(
        FlowField field,
        GridCoord current,
        int currentCost,
        MinPriorityQueue<GridCoord, int> openQueue)
    {
        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            GridCoord neighbor = Add(current, NeighborOffsets[i]);

            if (!CanTraverse(field, neighbor, current))
            {
                continue;
            }

            int movementCost = GetMovementCost(neighbor, current);

            int candidateCost = currentCost + movementCost;

            int previousCost = field.GetIntegrationCost(neighbor);

            if (candidateCost >= previousCost)
            {
                continue;
            }

            field.SetIntegrationCost(neighbor, candidateCost);

            Vector3 direction = CalculateDirection(field, neighbor, current);

            field.SetDirection(neighbor, direction);

            openQueue.Enqueue(neighbor, candidateCost);
        }
    }

    private static Vector3 CalculateDirection(FlowField field, GridCoord from, GridCoord to)
    {
        Vector3 fromWorld = field.Grid.CellToWorld(from);
        Vector3 toWorld = field.Grid.CellToWorld(to);
        Vector3 direction = toWorld - fromWorld;

        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        return direction.normalized;
    }

    private static bool CanTraverse(FlowField field, GridCoord from, GridCoord to)
    {
        if (!field.IsInside(to))
            return false;

        if (!field.IsTraversable(to))
            return false;

        int deltaX = to.x - from.x;
        int deltaZ = to.z - from.z;

        bool diagonal = deltaX != 0 && deltaZ != 0;

        if (!diagonal)
            return true;

        GridCoord horizontal = new GridCoord(from.x + deltaX, from.z);
        GridCoord vertical = new GridCoord(from.x, from.z + deltaZ);

        return field.IsTraversable(horizontal) && field.IsTraversable(vertical);
    }

    //
    // For now we are not doing a third pass in O(V)
    // We build the DirectionField directly inside
    // Dijkstra expansion (during relaxation step)
    // 
    // We can do a third pass if we need smoothing of 
    // the direction field later. 
    //
    //private static void BuildDirectionField(
    //    FlowField field)
    //{
    //    for (int z = 0;
    //         z < field.Height;
    //         z++)
    //    {
    //        for (int x = 0;
    //             x < field.Width;
    //             x++)
    //        {
    //            GridCoord current =
    //                new GridCoord(x, z);

    //            if (!field.IsReachable(current))
    //            {
    //                field.SetDirection(
    //                    current,
    //                    Vector3.zero);

    //                continue;
    //            }

    //            if (SameCell(
    //                current,
    //                field.DestinationCell))
    //            {
    //                field.SetDirection(
    //                    current,
    //                    Vector3.zero);

    //                continue;
    //            }

    //            Vector3 direction =
    //                CalculateBestDirection(
    //                    field,
    //                    current);

    //            field.SetDirection(
    //                current,
    //                direction);
    //        }
    //    }
    //}



    private static int GetMovementCost(
        GridCoord from,
        GridCoord to)
    {
        bool diagonal =
            from.x != to.x &&
            from.z != to.z;

        return diagonal
            ? DiagonalCost
            : StraightCost;
    }

    private static GridCoord Add(
        GridCoord first,
        GridCoord second)
    {
        return new GridCoord(
            first.x + second.x,
            first.z + second.z);
    }

    private static bool SameCell(
        GridCoord first,
        GridCoord second)
    {
        return
            first.x == second.x &&
            first.z == second.z;
    }

    private static int GetIndex(
        GridCoord coord,
        int width)
    {
        return
            coord.z * width +
            coord.x;
    }
}