using Unity.Profiling;
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
    // Debug marker
    private static readonly ProfilerMarker BuildMarker = new ProfilerMarker("FlowField.Build");
    private static readonly ProfilerMarker AllocationMarker = new ProfilerMarker("FlowField.Allocation");
    private static readonly ProfilerMarker TraversabilityMarker = new ProfilerMarker("FlowField.Traversability");
    private static readonly ProfilerMarker IntegrationMarker = new ProfilerMarker("FlowField.Integration");
    private static readonly ProfilerMarker GoalSeedMarker = new ProfilerMarker("FlowField.Integration.GoalSeed");
    private static readonly ProfilerMarker DijkstraMarker = new ProfilerMarker("FlowField.Integration.Dijkstra");
    private static readonly ProfilerMarker DirectionMarker = new ProfilerMarker("FlowField.Direction");

    // Declaration
    private const int StraightCost = 10;
    private const int DiagonalCost = 14;

    private const float ComfortMarginCells = 1.0f;
    private const int MaxClearancePenalty = 20;

    private const float DiagonalDirection = 0.70710678f;

    private static readonly GridCoord[] NeighborOffsets =
    {
        // Cardinal
        new GridCoord(-1,  0),
        new GridCoord( 1,  0),
        new GridCoord( 0, -1),
        new GridCoord( 0,  1),

        // Diagonal (so that, diag = i>= 4)
        new GridCoord(-1, -1),
        new GridCoord(-1,  1),
        new GridCoord( 1, -1),
        new GridCoord( 1,  1)
    };

    // Matching pre-normalized directions
    private static readonly Vector3[] NeighborDirections =
    {
        new Vector3(-1f, 0f,  0f),
        new Vector3( 1f, 0f,  0f),
        new Vector3( 0f, 0f, -1f),
        new Vector3( 0f, 0f,  1f),

        new Vector3(-DiagonalDirection, 0f, -DiagonalDirection),
        new Vector3(-DiagonalDirection, 0f,  DiagonalDirection),
        new Vector3( DiagonalDirection, 0f, -DiagonalDirection),
        new Vector3( DiagonalDirection, 0f,  DiagonalDirection)
    };

    // Diagnostic 
    static int enqueued = 0;
    static int dequeued = 0;
    static int duplicateDequeues = 0;
    static int peakOpen = 0;

    public static FlowField Build(
        TerrainGrid grid,
        Vector3 destination,
        float navigationRadius,
        Vector2 goalHalfExtents)
    {
        using (BuildMarker.Auto())
        {
            if (grid == null)
            {
                Debug.LogWarning("[FlowField] Cannot build: TerrainGrid missing.");
                return null;
            }

            double startTime = Time.realtimeSinceStartupAsDouble;

            FlowField field;
            //FlowField field =
            //    new FlowField(
            //        grid,
            //        destination,
            //        navigationRadius,
            //        goalHalfExtents);

            using (AllocationMarker.Auto()) 
            {
                field = new FlowField(
                            grid,
                            destination,
                            navigationRadius,
                            goalHalfExtents);
            }

            double afterAllocation = Time.realtimeSinceStartupAsDouble;

            using (TraversabilityMarker.Auto())
            {
                BuildTraversability(field);
            }
            
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

            int expandedCells;

            using (IntegrationMarker.Auto())
            {
                expandedCells = BuildIntegrationField(field); 
            }

            double afterIntegration = Time.realtimeSinceStartupAsDouble;

            using (DirectionMarker.Auto())
            {
                BuildDirectionField(field);
            }

            double afterDirection = Time.realtimeSinceStartupAsDouble;

            field.CompleteBuild();

            double totalMs = (Time.realtimeSinceStartupAsDouble - startTime);

            Debug.Log(
                $"[FlowField] " +
                $"Grid={field.Width}x{field.Height} " +
                $"Cells={field.Width * field.Height} " +
                $"Goals={field.GoalCellCount} " +
                $"Expanded={expandedCells} " +
                $"Enqueued={enqueued} " +
                $"Dequeued={dequeued} " +
                $"Duplicates={duplicateDequeues} " +
                $"PeakOpen={peakOpen} " +
                $"AllocationMs={(afterAllocation - startTime) * 1000.0:F2} " +
                $"TraversabilityMs={(afterTraversability - afterAllocation) * 1000.0:F2} " +
                $"IntegrationMs={(afterIntegration - afterTraversability) * 1000.0:F2} " +
                $"DirectionMs={(afterDirection - afterIntegration) * 1000.0:F2} " +
                $"TotalMs={totalMs * 1000.0:F2}");

            return field;
        }
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

        int goalCellCount;

        using (GoalSeedMarker.Auto())
        {
            goalCellCount = SeedGoalRegion(field, openQueue);
        }

        // Queue diagnostics
        enqueued = openQueue.Count;
        dequeued = 0;
        duplicateDequeues = 0;
        peakOpen = openQueue.Count;


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

        using (DijkstraMarker.Auto())
        {
            while (openQueue.Count > 0)
            {
                GridCoord current = openQueue.Dequeue();

                dequeued++;

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
                {
                    duplicateDequeues++;
                    continue;
                }
        
                settled[currentIndex] = true;

                int currentCost = field.GetIntegrationCostAt(currentIndex);

                if (currentCost == FlowField.UnreachableCost)
                {
                    continue;
                }

                expandedCells++;

                int added = ExploreNeighbors(field, current, currentCost, openQueue);

                enqueued += added;

                if (openQueue.Count > peakOpen)
                {
                    peakOpen = openQueue.Count;
                }
            }
        }

        return expandedCells;
    }

    private static int ExploreNeighbors(
        FlowField field,
        GridCoord current,
        int currentCost,
        MinPriorityQueue<GridCoord, int> openQueue)
    {
        int enqueued = 0;

        int width = field.Width;
        int height = field.Height;
        //int currentIndex = current.z * width + current.x;

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            GridCoord offset = NeighborOffsets[i];

            int neighborX = current.x + offset.x;
            int neighborZ = current.z + offset.z;


            // --------------------------------------------
            // Bounds
            // --------------------------------------------

            if (neighborX < 0 || neighborZ < 0 ||
                neighborX >= width || neighborZ >= height)
            {
                continue;
            }

            int neighborIndex = neighborZ * width + neighborX;

            // --------------------------------------------
            // Static traversability
            // --------------------------------------------

            if (!field.IsTraversableAt(
                    neighborIndex))
            {
                continue;
            }

            // --------------------------------------------
            // Prevent diagonal corner cutting
            // --------------------------------------------

            bool diagonal = i >= 4;

            if (diagonal)
            {
                int horizontalIndex = current.z * width + neighborX;
                int verticalIndex = neighborZ * width + current.x;

                if (!field.IsTraversableAt(horizontalIndex) ||
                    !field.IsTraversableAt(verticalIndex))
                {
                    continue;
                }
            }

            // --------------------------------------------
            // Integration relaxation
            // --------------------------------------------

            int movementCost = diagonal ? DiagonalCost : StraightCost;

            int candidateCost =
                currentCost +
                movementCost +
                field.GetClearancePenaltyAt(
                    neighborIndex);

            int previousCost = field.GetIntegrationCostAt(neighborIndex);

            if (candidateCost >= previousCost)
            {
                continue;
            }

            field.SetIntegrationCostAt(neighborIndex, candidateCost);

            openQueue.Enqueue(new GridCoord(neighborX, neighborZ), candidateCost);

            enqueued++;
        }

        return enqueued;
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
        int width = field.Width;
        int height = field.Height;

        for (int z = 0; z < height; z++)
        {
            int rowStart = z * width;

            for (int x = 0; x < width; x++)
            {
                int index = rowStart + x;

                if (!field.IsReachableAt(index))
                {
                    field.SetNormalizedDirectionAt(index, Vector3.zero);
                    continue;
                }

                if (field.IsGoalCellAt(index))
                {
                    field.SetNormalizedDirectionAt(index, Vector3.zero);
                    continue;
                }

                Vector3 direction = CalculateBestDirection(field, x, z);

                field.SetNormalizedDirectionAt(index, direction);
            }
        }
    }

    private static Vector3 CalculateBestDirection(FlowField field, int currentX, int currentZ)
    {
        int width = field.Width;
        int height = field.Height;

        int bestTotalCost = int.MaxValue;
        int bestNeighborCost = int.MaxValue;
        int bestNeighborIndex = -1;

        for (int i = 0; i < NeighborOffsets.Length; i++)
        {
            GridCoord offset = NeighborOffsets[i];

            int neighborX = currentX + offset.x;
            int neighborZ = currentZ + offset.z;

            // --------------------------------------------
            // Bounds
            // --------------------------------------------

            if (neighborX < 0 || neighborZ < 0 ||
                neighborX >= width || neighborZ >= height)
            {
                continue;
            }

            int neighborIndex = neighborZ * width + neighborX;

            // --------------------------------------------
            // Traversability
            // --------------------------------------------

            if (!field.IsTraversableAt(neighborIndex))
            {
                continue;
            }

            // --------------------------------------------
            // Prevent diagonal corner cutting
            // --------------------------------------------

            bool diagonal = i >= 4;

            if (diagonal)
            {
                int horizontalIndex = currentZ * width + neighborX;
                int verticalIndex = neighborZ * width + currentX;

                if (!field.IsTraversableAt(horizontalIndex) ||
                    !field.IsTraversableAt(verticalIndex))
                {
                    continue;
                }
            }

            // --------------------------------------------
            // Reachability / cost
            // --------------------------------------------

            int neighborCost = field.GetIntegrationCostAt(neighborIndex);

            if (neighborCost == FlowField.UnreachableCost)
            {
                continue;
            }

            int stepCost = diagonal ? DiagonalCost : StraightCost;

            int totalCost = neighborCost + stepCost;

            // Tie-breaking
            if (totalCost > bestTotalCost)
            {
                continue;
            }

            if (totalCost == bestTotalCost &&
                neighborCost >= bestNeighborCost)
            {
                continue;
            }

            bestTotalCost = totalCost;
            bestNeighborCost = neighborCost;
            bestNeighborIndex = i;
        }

        if (bestNeighborIndex < 0)
        {
            return Vector3.zero;
        }

        return NeighborDirections[bestNeighborIndex];
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