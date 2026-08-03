using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared grid-placement queries.
///
/// This class does not instantiate, move, reserve, or command entities.
/// It only searches for valid grid coordinates.
/// </summary>
public static class PlacementUtil
{
    public enum PlacementPolicy
    {
        Closest,
        MostOpen,
        OpenThenClose
    }

    // ---------------------------------------------------------------------
    // Exact ring queries
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns every free cell on the perimeter of a footprint expanded
    /// by the specified depth.
    ///
    /// depth = 0:
    ///     perimeter cells belonging to the footprint itself.
    ///
    /// depth = 1:
    ///     cells immediately surrounding the footprint.
    ///
    /// The optional validator can apply additional runtime rules,
    /// such as Physics-based unit clearance.
    /// </summary>
    public static List<GridCoord> GetAllFreePlacementsOnRing(
    TerrainGrid grid,
    GridCoord footprintOrigin,
    Vector2Int footprintSize,
    int depth,
    Func<GridCoord, bool> additionalValidator = null)
    {
        List<GridCoord> placements = new();

        if (grid == null)
            return placements;

        if (footprintSize.x <= 0 || footprintSize.y <= 0)
            return placements;

        depth = Mathf.Max(0, depth);

        int minX = footprintOrigin.x - depth;
        int minZ = footprintOrigin.z - depth;

        int maxX = footprintOrigin.x + (footprintSize.x - 1) + depth;
        int maxZ = footprintOrigin.z + (footprintSize.y - 1) + depth;

        // Bottom and top edges.
        for (int x = minX; x <= maxX; x++)
        {
            AddIfAvailable(grid, new GridCoord(x, minZ), placements, additionalValidator);
        }

        if (maxZ != minZ)
        {
            for (int x = minX; x <= maxX; x++)
            {
                AddIfAvailable(grid, new GridCoord(x, maxZ), placements, additionalValidator);
            }
        }

        // Left and right edges, excluding corners already added above.
        for (int z = minZ + 1; z <= maxZ - 1; z++)
        {
            AddIfAvailable(grid, new GridCoord(minX, z), placements, additionalValidator);
        }

        if (maxX != minX)
        {
            for (int z = minZ + 1; z <= maxZ - 1; z++)
            {
                AddIfAvailable(grid, new GridCoord(maxX, z), placements, additionalValidator);
            }
        }

        return placements;
    }

    // ---------------------------------------------------------------------
    // Simple placement
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns the free placement on the requested ring that is closest
    /// to the preferred cell.
    ///
    /// When preferredCell is null, the footprint center is used.
    /// </summary>
    public static GridCoord? GetPlacementAroundFootprint(
    TerrainGrid grid,
    GridCoord footprintOrigin,
    Vector2Int footprintSize,
    int depth,
    GridCoord? preferredCell = null,
    Func<GridCoord, bool> additionalValidator = null)
    {
        return GetPlacementAroundFootprintScored(
            grid,
            footprintOrigin,
            footprintSize,
            depth,
            preferredCell,
            PlacementPolicy.Closest,
            openRadius: 0,
            openWeight: 0,
            distanceWeight: 1,
            additionalValidator);
    }

    // ---------------------------------------------------------------------
    // Scored placement
    // ---------------------------------------------------------------------

    /// <summary>
    /// Selects the best placement on one exact ring.
    /// </summary>
    public static GridCoord? GetPlacementAroundFootprintScored(
    TerrainGrid grid,
    GridCoord footprintOrigin,
    Vector2Int footprintSize,
    int depth,
    GridCoord? preferredCell,
    PlacementPolicy policy,
    int openRadius,
    int openWeight,
    int distanceWeight,
    Func<GridCoord, bool> additionalValidator = null)
    {
        List<GridCoord> candidates = GetAllFreePlacementsOnRing(grid, footprintOrigin, footprintSize, depth, additionalValidator);

        GridCoord target = preferredCell ?? GetFootprintCenterCell(footprintOrigin, footprintSize);

        return SelectBestCandidate(
            grid,
            candidates,
            target,
            policy,
            openRadius,
            openWeight,
            distanceWeight);
    }

    /// <summary>
    /// Searches several rings, from initialDepth through
    /// initialDepth + maxExtraDepth.
    ///
    /// All valid candidates are scored together.
    /// </summary>
    public static GridCoord? GetPlacementAroundFootprintScoredWithFallback(
    TerrainGrid grid,
    GridCoord footprintOrigin,
    Vector2Int footprintSize,
    int initialDepth,
    int maxExtraDepth,
    GridCoord? preferredCell,
    PlacementPolicy policy,
    int openRadius,
    int openWeight,
    int distanceWeight,
    Func<GridCoord, bool> additionalValidator = null)
    {
        if (grid == null)
            return null;

        if (footprintSize.x <= 0 || footprintSize.y <= 0)
            return null;

        initialDepth = Mathf.Max(0, initialDepth);
        maxExtraDepth = Mathf.Max(0, maxExtraDepth);

        List<GridCoord> candidates = new();

        int finalDepth = initialDepth + maxExtraDepth;

        for (int depth = initialDepth; depth <= finalDepth; depth++)
        {
            List<GridCoord> ring = GetAllFreePlacementsOnRing(grid, footprintOrigin, footprintSize, depth, additionalValidator);
            candidates.AddRange(ring);
        }

        GridCoord target = preferredCell ?? GetFootprintCenterCell(footprintOrigin, footprintSize);

        return SelectBestCandidate(
            grid,
            candidates,
            target,
            policy,
            openRadius,
            openWeight,
            distanceWeight);
    }

    // ---------------------------------------------------------------------
    // Footprint geometry
    // ---------------------------------------------------------------------

    public static GridCoord GetFootprintCenterCell(GridCoord footprintOrigin, Vector2Int footprintSize)
    {
        int centerX = footprintOrigin.x + (footprintSize.x - 1) / 2;
        int centerZ = footprintOrigin.z + (footprintSize.y - 1) / 2;
        return new GridCoord(centerX, centerZ);
    }

    /// <summary>
    /// Returns a preferred cell outside one side of the footprint.
    ///
    /// direction must represent a cardinal grid direction:
    /// (1,0), (-1,0), (0,1), or (0,-1).
    ///
    /// distance = 1 represents the immediately adjacent row or column.
    /// </summary>
    public static GridCoord GetFootprintSideCenter(GridCoord footprintOrigin, Vector2Int footprintSize, Vector2Int direction, int distance = 1)
    {
        distance = Mathf.Max(1, distance);

        GridCoord center = GetFootprintCenterCell(footprintOrigin, footprintSize);

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            if (direction.x >= 0)
            {
                return new GridCoord(footprintOrigin.x + footprintSize.x - 1 + distance, center.z);
            }

            return new GridCoord(footprintOrigin.x - distance, center.z);
        }

        if (direction.y >= 0)
        {
            return new GridCoord(center.x, footprintOrigin.z + footprintSize.y - 1 + distance);
        }

        return new GridCoord(center.x, footprintOrigin.z - distance);
    }

    // ---------------------------------------------------------------------
    // Candidate scoring
    // ---------------------------------------------------------------------

    private static GridCoord? SelectBestCandidate(
    TerrainGrid grid,
    IReadOnlyList<GridCoord> candidates,
    GridCoord target,
    PlacementPolicy policy,
    int openRadius,
    int openWeight,
    int distanceWeight)
    {
        if (grid == null || candidates == null || candidates.Count == 0)
            return null;

        openRadius = Mathf.Max(0, openRadius);
        openWeight = Mathf.Max(0, openWeight);
        distanceWeight = Mathf.Max(0, distanceWeight);

        bool hasBest = false;
        GridCoord best = default;

        int bestScore = int.MinValue;
        int bestOpenness = int.MinValue;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            GridCoord candidate = candidates[i];

            int openness = CountFreeCellsInSquare(grid, candidate, openRadius);

            int distance = ManhattanDistance(candidate, target);

            int score = policy switch
            {
                PlacementPolicy.Closest => -distance,

                PlacementPolicy.MostOpen => openness,

                PlacementPolicy.OpenThenClose => openWeight * openness - distanceWeight * distance,

                _ => -distance
            };

            bool isBetter =
                !hasBest ||
                score > bestScore ||
                score == bestScore && openness > bestOpenness ||
                score == bestScore &&
                openness == bestOpenness &&
                distance < bestDistance ||
                score == bestScore &&
                openness == bestOpenness &&
                distance == bestDistance &&
                IsDeterministicallyBefore(candidate, best);

            if (!isBetter)
                continue;

            hasBest = true;
            best = candidate;
            bestScore = score;
            bestOpenness = openness;
            bestDistance = distance;
        }

        return hasBest ? best : null;
    }

    private static int CountFreeCellsInSquare(TerrainGrid grid, GridCoord center, int radius)
    {
        int freeCount = 0;
        int minX = center.x - radius;
        int maxX = center.x + radius;
        int minZ = center.z - radius;
        int maxZ = center.z + radius;

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                GridCoord coord = new GridCoord(x, z);

                if (grid.IsWalkable(coord))
                    freeCount++;
            }
        }

        return freeCount;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static void AddIfAvailable(
    TerrainGrid grid,
    GridCoord coord,
    List<GridCoord> placements,
    Func<GridCoord, bool> additionalValidator)
    {
        if (!grid.IsWalkable(coord))
            return;

        if (additionalValidator != null && !additionalValidator(coord))
        {
            return;
        }

        placements.Add(coord);
    }

    private static int ManhattanDistance(GridCoord first, GridCoord second)
    {
        return Mathf.Abs(first.x - second.x) + Mathf.Abs(first.z - second.z);
    }

    private static bool IsDeterministicallyBefore(GridCoord candidate, GridCoord currentBest)
    {
        if (candidate.z != currentBest.z)
            return candidate.z < currentBest.z;

        return candidate.x < currentBest.x;
    }
}