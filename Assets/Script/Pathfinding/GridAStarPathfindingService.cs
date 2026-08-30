using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor.PackageManager.Requests;
using UnityEditor.Searcher;
using UnityEngine;
using static GridNavigationStateSystem;

/// <summary>
/// Grid-based A* pathfinding using TerrainGrid walkability,
/// static occupancy, and owner-aware reservations.
/// </summary>
public sealed class GridAStarPathfindingService : MonoBehaviour, IPathfindingService
{
    private sealed class PathNode
    {
        public GridCoord Coord;

        public int GCost;
        public int HCost;

        public PathNode Parent;

        public bool IsClosed;
        public bool IsTouched;

        public int FCost => GCost + HCost;

        public int TraversalSearchId = -1;
        public bool CachedTraversal;

        public int OccupancySearchId = -1;
        public DynamicOccupancyRelation CachedOccupancyRelation;

        public PathNode(GridCoord coord)
        {
            Coord = coord;
            Reset();
        }

        public void Reset()
        {
            GCost = int.MaxValue;
            HCost = 0;

            Parent = null;

            IsClosed = false;
            IsTouched = false;
        }
    }

    private struct PathPriority : IComparable<PathPriority>
    {
        public int FCost;
        public int HCost;

        public PathPriority(int fCost, int hCost)
        {
            FCost = fCost;
            HCost = hCost;
        }

        public int CompareTo(PathPriority other)
        {
            int comparison = FCost.CompareTo(other.FCost);

            if (comparison != 0)
                return comparison;

            return HCost.CompareTo(other.HCost);
        }
    }

    private TerrainGrid terrainGrid;
    private GridNavigationStateSystem navigateState;

    private PathNode[,] nodes;
    private readonly MinPriorityQueue<PathNode, PathPriority> openQueue = new();
    private readonly List<PathNode> touchedNodes = new();

    private const int StraightCost = 10;
    private const int DiagonalCost = 14;

    private int currentSearchId; // caching search 

    [Header("Diagnostic counters")]
    private int traversalCalls;
    private int traversalEvaluations;
    private int traversalCacheHits;
    private int occupancyCalls;
    private int occupancyEvaluations;
    private int occupancyCacheHits;
    private int diagonalChecks;

    [Header("Dynamic Occupancy")]
    [SerializeField, Min(0)] private int sameMovementGroupPenalty = 0;
    [SerializeField, Min(0)] private int otherMovingUnitPenalty = 8;
    [SerializeField, Min(0)] private int stationaryUnitPenalty = 25;

    [Header("Search Budget")]
    [SerializeField, Min(128)] private int minimumExpansionBudget = 1500;
    [SerializeField, Min(512)] private int maximumExpansionBudget = 6000;
    [SerializeField, Min(1)] private int expansionsPerDirectStep = 48;

    [Header("Diagnostics")]
    [SerializeField] private bool logSlowSearches = true;
    [SerializeField, Min(0.1f)] private float slowSearchMilliseconds = 2f;
    [SerializeField, Min(1)] private int slowSearchExpansionThreshold = 1000;

    public void Initialize(TerrainGrid terrainGrid, GridNavigationStateSystem navigateState)
    {
        this.terrainGrid = terrainGrid;
        this.navigateState = navigateState;

        if (terrainGrid == null)
        {
            Debug.LogError("GridAStarPathfindingService cannot initialize because TerrainGrid is missing.");
            return;
        }

        BuildNodes();
    }

    public bool TryFindPath(UnitBase requester, Vector3 start, Vector3 end, List<Vector3> result)
    {
        result.Clear();

        double totalStart = Time.realtimeSinceStartupAsDouble;

        if (terrainGrid == null || nodes == null)
            return false;

        //
        // Reset previous A* search state
        double resetStart = Time.realtimeSinceStartupAsDouble;

        ResetSearch();

        double resetMs = (Time.realtimeSinceStartupAsDouble - resetStart) * 1000.0;

        currentSearchId++; // caching search Id number 

        ResetDiagnosticCounters();

        navigateState?.BeginOccupancyDiagnostics();

        //
        // Search setup
        GridCoord startCoord = terrainGrid.WorldToCell(start);
        GridCoord endCoord = terrainGrid.WorldToCell(end);

        int expansionBudget = CalculateExpansionBudget(startCoord, endCoord);
        int expandedNodes = 0;

        double searchStart = Time.realtimeSinceStartupAsDouble;

        // Validation coordinates
        if (!terrainGrid.IsInside(startCoord) || !terrainGrid.IsInside(endCoord))
        {
            //return false;
            double searchMs = (Time.realtimeSinceStartupAsDouble - searchStart) * 1000.0;

            return FinishSearch(
                requester,
                startCoord,
                endCoord,
                false,
                "OutsideGrid",
                expandedNodes,
                expansionBudget,
                totalStart,
                resetMs,
                searchMs,
                0.0);
        }

        // Validate destination
        if (!CanTraverse(endCoord, startCoord, requester))
        {
            //return false;
            double searchMs = (Time.realtimeSinceStartupAsDouble - searchStart) * 1000.0;

            return FinishSearch(
                requester,
                startCoord,
                endCoord,
                false,
                "EndNotTraversable",
                expandedNodes,
                expansionBudget,
                totalStart,
                resetMs,
                searchMs,
                0.0);
        }

        // Already at destination
        if (IsSameCoord(startCoord, endCoord))
        {
            result.Add(terrainGrid.CellToWorld(endCoord));
            //return true;

            double searchMs = (Time.realtimeSinceStartupAsDouble - searchStart) * 1000.0;

            return FinishSearch(
                requester,
                startCoord,
                endCoord,
                true,
                "AlreadyAtDestination",
                expandedNodes,
                expansionBudget,
                totalStart,
                resetMs,
                searchMs,
                0.0);
        }

        //
        // Initialize start node
        PathNode startNode = nodes[startCoord.x, startCoord.z];

        TouchNode(startNode);

        startNode.GCost = 0;
        startNode.HCost = CalculateHeuristic(startCoord, endCoord);

        EnqueueNode(startNode);

        // -------------------------------------------------------
        // Main A* search
        // -------------------------------------------------------
        while (openQueue.Count > 0)
        {
            PathNode currentNode = openQueue.Dequeue();

            // Old duplicate queue entry (lazy-duplicate logic)
            if (currentNode.IsClosed)
                continue;

            currentNode.IsClosed = true;

            // Goal Reached
            if (IsSameCoord(currentNode.Coord, endCoord))
            {
                //bool success = BuildResultPath(startNode, currentNode, result);
                //return success;

                double searchMs = (Time.realtimeSinceStartupAsDouble - searchStart) * 1000.0;
                double rebuildStart = Time.realtimeSinceStartupAsDouble;

                bool success = BuildResultPath(startNode, currentNode, result);

                double rebuildMs = (Time.realtimeSinceStartupAsDouble - rebuildStart) * 1000.0;

                return FinishSearch(
                    requester,
                    startCoord,
                    endCoord,
                    success,
                    success ? "Success" : "RebuildFailed",
                    expandedNodes,
                    expansionBudget,
                    totalStart,
                    resetMs,
                    searchMs,
                    rebuildMs);
            }

            // Exapnd node tracking
            expandedNodes++;

            // Expansion budget
            if (expandedNodes >= expansionBudget)
            {
                result.Clear();
                //return false;

                double searchMs = (Time.realtimeSinceStartupAsDouble - searchStart) * 1000.0;

                return FinishSearch(
                    requester,
                    startCoord,
                    endCoord,
                    false,
                    "BudgetExceeded",
                    expandedNodes,
                    expansionBudget,
                    totalStart,
                    resetMs,
                    searchMs,
                    0.0);
            }

            ExploreNeighbors(currentNode, startCoord, endCoord, requester);
        }

        //
        // OpenSet exhausted
        result.Clear();

        double exhaustedSearchMs = (Time.realtimeSinceStartupAsDouble - searchStart) * 1000.0;

        return FinishSearch(
            requester,
            startCoord,
            endCoord,
            false,
            "OpenSetExhausted",
            expandedNodes,
            expansionBudget,
            totalStart,
            resetMs,
            exhaustedSearchMs,
            0.0);

        //return false;
    }

    private void ResetDiagnosticCounters()
    {
        traversalCalls = 0;
        traversalEvaluations = 0;
        traversalCacheHits = 0;
        occupancyCalls = 0;
        occupancyEvaluations = 0;
        occupancyCacheHits = 0;
        diagonalChecks = 0;

        // Overflow (int) will probably never happen
        // but this keeps it correct theoritically.
        if (currentSearchId <= 0)
        {
            currentSearchId = 1;
            ClearQueryCaches();
        }
    }

    private void BuildNodes()
    {
        nodes = new PathNode[terrainGrid.Width, terrainGrid.Height];

        for (int z = 0; z < terrainGrid.Height; z++)
        {
            for (int x = 0; x < terrainGrid.Width; x++)
            {
                nodes[x, z] = new PathNode(new GridCoord(x, z));
            }
        }
    }

    private void ExploreNeighbors(PathNode currentNode, GridCoord startCoord, GridCoord endCoord, UnitBase requester)
    {
        for (int zOffset = -1; zOffset <= 1; zOffset++)
        {
            for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                if (xOffset == 0 && zOffset == 0)
                {
                    continue;
                }

                GridCoord neighborCoord = new GridCoord(currentNode.Coord.x + xOffset, currentNode.Coord.z + zOffset);

                if (!terrainGrid.IsInside(neighborCoord))
                {
                    continue;
                }

                bool diagonal = xOffset != 0 && zOffset != 0;

                if (diagonal && !CanMoveDiagonally(currentNode.Coord, xOffset, zOffset, startCoord, requester))
                {
                    continue;
                }

                if (!CanTraverse(neighborCoord, startCoord, requester))
                {
                    continue;
                }

                PathNode neighborNode = nodes[neighborCoord.x, neighborCoord.z];

                TouchNode(neighborNode);

                if (neighborNode.IsClosed)
                    continue;

                int movementCost = diagonal ? DiagonalCost : StraightCost;

                movementCost += GetUnitOccupancyPenalty(neighborCoord, requester);

                int newGCost = currentNode.GCost + movementCost;

                if (newGCost >= neighborNode.GCost)
                {
                    continue;
                }

                neighborNode.GCost = newGCost;
                neighborNode.HCost = CalculateHeuristic(neighborCoord, endCoord);
                neighborNode.Parent = currentNode;

                EnqueueNode(neighborNode);
            }
        }
    }

    private int GetUnitOccupancyPenalty(GridCoord coord, UnitBase requester)
    {
        if (navigateState == null || requester == null)
            return 0;

        occupancyCalls++;

        PathNode node = nodes[coord.x, coord.z];

        DynamicOccupancyRelation relation;

        if (node.OccupancySearchId == currentSearchId)
        {
            occupancyCacheHits++;
            relation = node.CachedOccupancyRelation;
        }
        else
        {
            occupancyEvaluations++;
            relation = navigateState.GetDynamicOccupancyRelation(coord, requester);

            node.OccupancySearchId =  currentSearchId;
            node.CachedOccupancyRelation = relation;  
        }

        //DynamicOccupancyRelation relation = navigateState.GetDynamicOccupancyRelation(coord, requester);

        switch (relation)
        {
            case DynamicOccupancyRelation.SameMovementGroup:
                return sameMovementGroupPenalty;

            case DynamicOccupancyRelation.MovingUnit:
                return otherMovingUnitPenalty;

            case DynamicOccupancyRelation.StationaryUnit:
                return stationaryUnitPenalty;

            case DynamicOccupancyRelation.None:
            default:
                return 0;
        }
    }

    private bool CanMoveDiagonally(GridCoord from, int xOffset, int zOffset, GridCoord startCoord, UnitBase requester)
    {
        GridCoord horizontal = new GridCoord(from.x + xOffset, from.z);
        GridCoord vertical = new GridCoord(from.x, from.z + zOffset);

        return CanTraverse(horizontal, startCoord, requester) && CanTraverse(vertical, startCoord, requester);
    }

    private bool CanTraverse(GridCoord coord, GridCoord startCoord, UnitBase requester)
    {
        traversalCalls++;

        GridCell cell = terrainGrid.GetCell(coord);

        if (cell == null)
            return false;

        //if (!cell.Walkable)
        //{
        //    canTraverse = false;
        //    return false;
        //}

        // unit occupy start cell, thats normal
        if (IsSameCoord(coord, startCoord))
        {
            return true;
        }

        PathNode node = nodes[coord.x, coord.z];

        if (node.TraversalSearchId == currentSearchId)
        {
            traversalCacheHits++;
            return node.CachedTraversal;
        }

        traversalEvaluations++;

        bool canTraverse = true;

        if (!cell.Walkable)
        {
            canTraverse = false;
        }
        else
        {
            float navigationRadius = requester != null ? requester.NavigationRadius : 0f;

            if (!terrainGrid.HasNavigationClearance(coord, navigationRadius)) // Now in O(1)
            {
                canTraverse = false;
            }
            else if (navigateState != null 
                    && navigateState.IsDestinationTraversalBlocked(coord, requester))
            {
                canTraverse = false;
            }
        }

        node.TraversalSearchId = currentSearchId;
        node.CachedTraversal = canTraverse;

        return canTraverse;


        //float navigationRadius = requester != null ? requester.NavigationRadius : 0f;

        //if (!terrainGrid.HasNavigationClearance(coord, navigationRadius))
        //{
        //    return false;
        //}

        //if (navigateState != null && navigateState.IsDestinationTraversalBlocked(coord, requester))
        //    return false;

        //return true;
    }

    private int CalculateHeuristic(GridCoord from, GridCoord to)
    {
        int deltaX = Mathf.Abs(from.x - to.x);
        int deltaZ = Mathf.Abs(from.z - to.z);
        int diagonalSteps = Mathf.Min(deltaX, deltaZ);
        int straightSteps = Mathf.Max(deltaX, deltaZ) - diagonalSteps;

        return diagonalSteps * DiagonalCost + straightSteps * StraightCost;
    }

    private void EnqueueNode(PathNode node)
    {
        PathPriority priority = new PathPriority(node.FCost, node.HCost);
        openQueue.Enqueue(node, priority);
    }

    private bool BuildResultPath(PathNode startNode, PathNode endNode, List<Vector3> result)
    {
        PathNode currentNode = endNode;

        while (currentNode != null && currentNode != startNode)
        {
            //result.Add(terrainGrid.CellToWorld(currentNode.Coord));

            GridCell cell = terrainGrid.GetCell(currentNode.Coord);

            if (cell == null)
            {
                result.Clear();
                return false;
            }

            result.Add(cell.WorldCenter);

            currentNode = currentNode.Parent;
        }

        if (currentNode != startNode)
        {
            result.Clear();
            return false;
        }

        result.Reverse();

        return result.Count > 0;
    }

    private void TouchNode(PathNode node)
    {
        if (node.IsTouched)
            return;

        node.IsTouched = true;
        touchedNodes.Add(node);
    }

    private void ResetSearch()
    {
        openQueue.Clear();

        for (int i = 0; i < touchedNodes.Count; i++)
        {
            touchedNodes[i].Reset();
        }

        touchedNodes.Clear();
    }


    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private bool IsSameCoord(GridCoord first, GridCoord second)
    {
        return first.x == second.x && first.z == second.z;
    }

    private void ClearQueryCaches()
    {
        for (int z = 0; z < terrainGrid.Height; z++)
        {
            for (int x = 0; x < terrainGrid.Width; x++)
            {
                PathNode node = nodes[x, z];
                node.TraversalSearchId = -1;
                node.OccupancySearchId = -1;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Budget
    // ---------------------------------------------------------------------

    private int CalculateExpansionBudget(GridCoord start, GridCoord end)
    {
        int deltaX = Mathf.Abs(start.x - end.x);
        int deltaZ = Mathf.Abs(start.z - end.z);
        int directSteps = Mathf.Max(deltaX, deltaZ);

        long scaledBudget = (long)directSteps * expansionsPerDirectStep;

        return Mathf.Clamp((int)Mathf.Min(scaledBudget, int.MaxValue), minimumExpansionBudget, maximumExpansionBudget);
    }

    private void ReportSearchDiagnostics(
        UnitBase requester,
        GridCoord start,
        GridCoord end,
        bool success,
        string reason,
        int expandedNodes,
        int expansionBudget,
        double resetMs,
        double searchMs,
        double rebuildMs,
        double totalMs,
        int dynCells,
        int dynBuckets,
        int dynTests)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        if (!logSlowSearches)
            return;

        bool interesting = !success || 
            expandedNodes >= slowSearchExpansionThreshold ||
            totalMs >= slowSearchMilliseconds;

        if (!interesting)
            return;

        int dx = Mathf.Abs(start.x - end.x);
        int dz = Mathf.Abs(start.z - end.z);
        int directDistance = Mathf.Max(dx, dz);

        int unitId = requester != null ? requester.UnitId : -1;

        int movementGroup = requester != null ? requester.MovementGroupId : 0;

        Debug.Log(
            "[A*] " +
            "Unit=" + unitId +
            " Group=" + movementGroup +
            " Success=" + success +
            " Reason=" + reason +

            " DirectCells=" + directDistance +
            " Expanded=" + expandedNodes + "/" + expansionBudget +
            " Touched=" + touchedNodes.Count +
            " Traverse=" + traversalEvaluations + "/" + traversalCalls +
            " TraverseCache=" + traversalCacheHits +

            " Occupancy=" + occupancyEvaluations + "/" + occupancyCalls +
            " OccupancyCache=" + occupancyCacheHits +
            " DynCells=" + dynCells +
            " DynBuckets=" + dynBuckets +
            " DynTests=" + dynTests +

            " Diag=" + diagonalChecks +

            " ResetMs=" + resetMs.ToString("F2") +
            " SearchMs=" + searchMs.ToString("F2") +
            " RebuildMs=" + rebuildMs.ToString("F2") +
            " TotalMs=" +  totalMs.ToString("F2"));

#endif
    }

    private bool FinishSearch(
        UnitBase requester,
        GridCoord startCoord,
        GridCoord endCoord,
        bool success,
        string reason,
        int expandedNodes,
        int expansionBudget,
        double totalStart,
        double resetMs,
        double searchMs,
        double rebuildMs)
    {
        int dynQueries = 0;
        int dynCells = 0;
        int dynBuckets = 0;
        int dynTests = 0;

        navigateState?.EndOccupancyDiagnostics( out dynQueries, out dynCells, out dynBuckets, out dynTests);

        double totalMs = (Time.realtimeSinceStartupAsDouble - totalStart) * 1000.0;

        ReportSearchDiagnostics(
            requester,
            startCoord,
            endCoord,
            success,
            reason,
            expandedNodes,
            expansionBudget,
            resetMs,
            searchMs,
            rebuildMs,
            totalMs,
            dynCells,
            dynBuckets,
            dynTests);

        return success;
    }
}