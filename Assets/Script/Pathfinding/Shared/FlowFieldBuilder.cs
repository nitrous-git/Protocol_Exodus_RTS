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

    private const float ComfortMarginCells = 1.0f;
    private const int MaxClearancePenalty = 20;

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
        float navigationRadius,
        Vector2 goalHalfExtents)
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
                navigationRadius,
                goalHalfExtents);

        BuildTraversability(field);

        double afterTraversability = Time.realtimeSinceStartupAsDouble;

        if (!field.IsInside(field.DestinationCell))
        {
            Debug.LogWarning("[FlowField] Destination lies outside the grid.");
            return null;
        }

        if (!field.IsTraversable(field.DestinationCell))
        {
            Debug.LogWarning(
                $"[FlowField] Destination cell ({field.DestinationCell.x}, {field.DestinationCell.z}) " +
                $"is not traversable for radius {navigationRadius:F2}.");

            return null;
        }

        int expandedCells = BuildIntegrationField(field); // also build the DirectionField

        double afterIntegration = Time.realtimeSinceStartupAsDouble;

        BuildDirectionField(field);

        field.CompleteBuild();

        double afterDirectionTime = Time.realtimeSinceStartupAsDouble;

        double elapsedMs =
            (Time.realtimeSinceStartupAsDouble -
             startTime) *
            1000.0;

        Debug.Log(
            $"[FlowField] " +
            $"Expanded={expandedCells} " +
            $"TraversabilityMs=" + $"{(afterTraversability - startTime) * 1000.0:F2} " +
            $"IntegrationMs=" + $"{(afterIntegration - afterTraversability) * 1000.0:F2} " +
            $"DirectionMs=" + $"{(afterDirectionTime - afterIntegration) * 1000.0:F2}" +
            $"TotalMs=" + $"{elapsedMs * 1000.0:F2}");

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

                if (!traversable)
                {
                    field.SetClearancePenalty(coord, 0);

                    continue;
                }

                GridCell cell = grid.GetCell(coord);

                int clearancePenalty =
                    CalculateClearancePenalty(
                        field,
                        cell);

                field.SetClearancePenalty(coord, clearancePenalty);
            }
        }
    }

    private static int BuildIntegrationField(FlowField field)
    {
        MinPriorityQueue<GridCoord, int> openQueue = new MinPriorityQueue<GridCoord, int>();

        bool[] settled = new bool[field.Width * field.Height];

        //field.SetIntegrationCost(field.DestinationCell, 0);
        //field.SetDirection(field.DestinationCell, Vector3.zero);

        //openQueue.Enqueue(field.DestinationCell, 0);

        int goalCellCount = SeedGoalRegion(field, openQueue);

        if (goalCellCount == 0)
        {
            Debug.LogWarning("[FlowField] No valid goal cells.");
            return 0;
        }

        field.GoalCellCount = goalCellCount;

        int expandedCells = 0;

        // ---------------------------------------------------
        // Main Dijkstra Loop
        // ---------------------------------------------------

        while (openQueue.Count > 0)
        {
            GridCoord current = openQueue.Dequeue();

            if (!field.IsInside(current))
            {
                Debug.LogError(
                    $"[FlowField] Queue contained invalid cell " +
                    $"({current.x},{current.z}).");

                continue;
            }

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

            if (!CanTraverse(field, current, neighbor))
            {
                continue;
            }

            int movementCost = GetMovementCost(neighbor, current);

            int clearancePenalty = field.GetClearancePenalty(neighbor);

            int candidateCost = currentCost + movementCost + clearancePenalty;

            int previousCost = field.GetIntegrationCost(neighbor);

            if (candidateCost >= previousCost)
            {
                continue;
            }

            field.SetIntegrationCost(neighbor, candidateCost);

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
        if (!field.IsInside(from) || !field.IsInside(to))
            return false;

        if (!field.IsTraversable(from) || !field.IsTraversable(to))
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

    private static void BuildDirectionField(FlowField field)
    {
        for (int z = 0; z < field.Height; z++)
        {
            for (int x = 0; x < field.Width; x++)
            {
                GridCoord current = new GridCoord(x, z);

                if (!field.IsReachable(
                    current))
                {
                    field.SetDirection(
                        current,
                        Vector3.zero);

                    continue;
                }

                if (field.IsGoalCell(current))
                {
                    field.SetDirection(
                        current,
                        Vector3.zero);

                    continue;
                }

                Vector3 direction = CalculateBestDirection(field, current);

                field.SetDirection(current, direction);
            }
        }
    }

    private static Vector3 CalculateBestDirection(FlowField field, GridCoord current)
    {
        int bestTotalCost = int.MaxValue;
        int bestNeighborCost = int.MaxValue;

        GridCoord bestNeighbor = current;

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            GridCoord neighbor =
                Add(
                    current,
                    NeighborOffsets[i]);

            if (!CanTraverse(
                field,
                current,
                neighbor))
            {
                continue;
            }

            if (!field.IsReachable(
                neighbor))
            {
                continue;
            }

            int neighborCost =
                field.GetIntegrationCost(
                    neighbor);

            int stepCost =
                GetMovementCost(
                    current,
                    neighbor);

            int totalCost =
                neighborCost +
                stepCost;

            if (totalCost >
                bestTotalCost)
            {
                continue;
            }

            if (totalCost ==
                    bestTotalCost &&
                neighborCost >=
                    bestNeighborCost)
            {
                continue;
            }

            bestTotalCost = totalCost;
            bestNeighborCost = neighborCost;
            bestNeighbor = neighbor;
        }

        if (SameCell(
            current,
            bestNeighbor))
        {
            return Vector3.zero;
        }

        return CalculateDirection(
            field,
            current,
            bestNeighbor);
    }

    // ----------------------------------------------------
    // Penalty
    // ----------------------------------------------------
    private static int CalculateClearancePenalty(
    FlowField field,
    GridCell cell)
    {
        if (cell == null)
            return 0;

        float spareClearance =
            cell.StaticClearanceRadius -
            field.NavigationRadius;

        float comfortMargin =
            field.Grid.CellSize *
            ComfortMarginCells;

        if (comfortMargin <=
            Mathf.Epsilon)
        {
            return 0;
        }

        float normalizedDiscomfort =
            1f -
            Mathf.Clamp01(
                spareClearance /
                comfortMargin);

        return Mathf.RoundToInt(
            normalizedDiscomfort *
            MaxClearancePenalty);
    }

    // ----------------------------------------------------
    // Goal region
    // ----------------------------------------------------

    private static int SeedGoalRegion(
        FlowField field,
        MinPriorityQueue<GridCoord, int> openQueue)
    {
        TerrainGrid grid =
            field.Grid;

        GridCoord centerCell =
            field.DestinationCell;

        Vector3 centerWorld =
            grid.CellToWorld(
                centerCell);

        Vector2 halfExtents =
            field.GoalHalfExtents;

        int cellRadiusX =
            Mathf.CeilToInt(
                halfExtents.x /
                grid.CellSize);

        int cellRadiusZ =
            Mathf.CeilToInt(
                halfExtents.y /
                grid.CellSize);

        int goalCellCount = 0;

        for (int z =
                 centerCell.z - cellRadiusZ;
             z <=
                 centerCell.z + cellRadiusZ;
             z++)
        {
            for (int x =
                     centerCell.x - cellRadiusX;
                 x <=
                     centerCell.x + cellRadiusX;
                 x++)
            {
                GridCoord coord =
                    new GridCoord(x, z);

                if (!field.IsInside(coord))
                    continue;

                if (!field.IsTraversable(coord))
                    continue;

                Vector3 cellWorld =
                    grid.CellToWorld(coord);

                Vector3 difference =
                    cellWorld -
                    centerWorld;

                difference.y = 0f;

                if (Mathf.Abs(difference.x) >
                        halfExtents.x ||
                    Mathf.Abs(difference.z) >
                        halfExtents.y)
                {
                    continue;
                }

                field.SetGoalCell(
                    coord,
                    true);

                field.SetIntegrationCost(
                    coord,
                    0);

                field.SetDirection(
                    coord,
                    Vector3.zero);

                openQueue.Enqueue(
                    coord,
                    0);

                goalCellCount++;
            }
        }

        return goalCellCount;
    }


    // ----------------------------------------------------
    // Helpers
    // ----------------------------------------------------

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