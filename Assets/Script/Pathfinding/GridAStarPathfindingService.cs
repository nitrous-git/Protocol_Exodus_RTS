using System;
using System.Collections.Generic;
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

    [Header("Dynamic Occupancy")]
    [SerializeField, Min(0)] private int sameMovementGroupPenalty = 0;
    [SerializeField, Min(0)] private int otherMovingUnitPenalty = 8;
    [SerializeField, Min(0)] private int stationaryUnitPenalty = 25;

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

        if (terrainGrid == null || nodes == null)
            return false;

        ResetSearch();

        GridCoord startCoord = terrainGrid.WorldToCell(start);
        GridCoord endCoord = terrainGrid.WorldToCell(end);

        if (!terrainGrid.IsInside(startCoord) || !terrainGrid.IsInside(endCoord))
        {
            return false;
        }

        if (!CanTraverse(endCoord, startCoord, requester))
        {
            return false;
        }

        if (IsSameCoord(startCoord, endCoord))
        {
            result.Add(terrainGrid.CellToWorld(endCoord));
            return true;
        }

        PathNode startNode = nodes[startCoord.x, startCoord.z];

        TouchNode(startNode);

        startNode.GCost = 0;
        startNode.HCost = CalculateHeuristic(startCoord, endCoord);

        EnqueueNode(startNode);

        // Main A* loop
        while (openQueue.Count > 0)
        {
            PathNode currentNode = openQueue.Dequeue();

            // Old duplicate queue entry (lazy-duplicate logic)
            if (currentNode.IsClosed)
                continue;

            currentNode.IsClosed = true;

            if (IsSameCoord(currentNode.Coord, endCoord))
            {
                return BuildResultPath(startNode, currentNode, result);
            }

            ExploreNeighbors(currentNode, startCoord, endCoord, requester);
        }

        return false;
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
        if (navigateState == null)
            return 0;

        DynamicOccupancyRelation relation = navigateState.GetDynamicOccupancyRelation(coord, requester);

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
        GridCell cell = terrainGrid.GetCell(coord);

        if (cell == null)
            return false;

        if (!cell.Walkable)
            return false;

        if (IsSameCoord(coord, startCoord))
        {
            return true;
        }

        float navigationRadius = requester != null ? requester.NavigationRadius : 0f;

        if (!terrainGrid.HasNavigationClearance(coord, navigationRadius))
        {
            return false;
        }

        if (navigateState != null && navigateState.IsDestinationReservedByOther(coord, requester))
            return false;

        return true;
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
            result.Add(terrainGrid.CellToWorld(currentNode.Coord));
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

    private bool IsSameCoord(GridCoord first, GridCoord second)
    {
        return first.x == second.x && first.z == second.z;
    }
}