using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Match-wide repository for all resource nodes.
///
/// Owns scene-node discovery, registration, grid occupancy,
/// runtime lookup, and depleted-node cleanup.
/// </summary>
public sealed class ResourceNodeRepository
{
    private readonly List<ResourceNode> resourceNodeList = new();

    private readonly GameContext gameContext;
    private readonly TerrainGrid terrainGrid;

    private Transform resourceNodesRoot;

    public IReadOnlyList<ResourceNode> ResourceNodeList => resourceNodeList;

    public bool IsInitialized { get; private set; }

    public ResourceNodeRepository(GameContext gameContext, TerrainGrid terrainGrid)
    {
        this.gameContext = gameContext;
        this.terrainGrid = terrainGrid;
    }

    /// <summary>
    /// Finds and registers the ResourceNode components manually placed
    /// beneath the Resources scene root.
    /// </summary>
    public void Initialize(Transform resourceNodesRoot)
    {
        if (IsInitialized)
            return;

        if (resourceNodesRoot == null)
        {
            Debug.LogError("ResourceNodeRepository cannot initialize because the Resources root is missing.");
            return;
        }

        if (terrainGrid == null)
        {
            Debug.LogError("ResourceNodeRepository cannot initialize because TerrainGrid is missing.");
            return;
        }

        this.resourceNodesRoot = resourceNodesRoot;

        ResourceNode[] sceneNodes = resourceNodesRoot.GetComponentsInChildren<ResourceNode>(true);

        for (int i = 0; i < sceneNodes.Length; i++)
        {
            ResourceNode node = sceneNodes[i];

            if (node == null)
                continue;

            if (!node.gameObject.activeInHierarchy)
                continue;

            TryRegisterNode(node);
        }

        IsInitialized = true;

        Debug.Log($"ResourceNodeRepository initialized with {resourceNodeList.Count} resource nodes.");
    }

    /// <summary>
    /// Handles repository-level resource lifecycle.
    ///
    /// Resource nodes currently have no time-based simulation of their own,
    /// so this pass removes null and depleted nodes.
    /// </summary>
    public void Tick(float deltaTime)
    {
        for (int i = resourceNodeList.Count - 1; i >= 0; i--)
        {
            ResourceNode node = resourceNodeList[i];

            if (node == null)
            {
                resourceNodeList.RemoveAt(i);
                continue;
            }

            if (!node.IsDepleted)
                continue;

            RemoveNode(node);
        }
    }

    /// <summary>
    /// Registers a scene-authored or runtime-created resource node.
    ///
    /// Returns false if the node is invalid, outside the grid,
    /// or overlaps another occupied/reserved grid cell.
    /// </summary>
    public bool TryRegisterNode(ResourceNode node)
    {
        if (node == null)
            return false;

        if (resourceNodeList.Contains(node))
            return false;

        if (terrainGrid == null)
            return false;

        GridCoord occupiedCell = terrainGrid.WorldToCell(node.Position);
        GridCell gridCell = terrainGrid.GetCell(occupiedCell);

        if (gridCell == null)
        {
            Debug.LogError($"{node.name} is outside the TerrainGrid and cannot be registered.");

            return false;
        }

        if (gridCell.Occupied || gridCell.Reserved)
        {
            Debug.LogError($"{node.name} cannot occupy grid cell ({occupiedCell.x}, {occupiedCell.z}) because it is unavailable.");

            return false;
        }

        node.Initialize(this, occupiedCell);

        if (!node.IsInitialized)
            return false;

        resourceNodeList.Add(node);
        terrainGrid.SetOccupied(occupiedCell, true, node.ResourceNodeId);
        gameContext?.RegisterResourceNode(node);

        return true;
    }

    /// <summary>
    /// Removes a node from repositories and releases its occupied grid cell.
    ///
    /// Does not destroy the GameObject.
    /// </summary>
    public void UnregisterNode(ResourceNode node)
    {
        if (node == null)
            return;

        bool wasRegistered = resourceNodeList.Remove(node);

        if (!wasRegistered)
            return;

        gameContext?.UnregisterResourceNode(node);

        if (terrainGrid != null && node.IsInitialized)
        {
            terrainGrid.SetOccupied(node.OccupiedCell, false, node.ResourceNodeId);
        }
    }

    public bool Contains(ResourceNode node)
    {
        return node != null && resourceNodeList.Contains(node);
    }

    /// <summary>
    /// Returns the closest non-depleted node of the requested type.
    ///
    /// This will be useful for WorkerUnit and AI behavior.
    /// </summary>
    public ResourceNode FindClosestAvailableNode(ResourceType resourceType, Vector3 worldPosition)
    {
        ResourceNode closestNode = null;
        float closestDistanceSquared = float.PositiveInfinity;

        for (int i = 0; i < resourceNodeList.Count; i++)
        {
            ResourceNode node = resourceNodeList[i];

            if (node == null)
                continue;

            if (!node.IsInitialized || node.IsDepleted)
                continue;

            if (node.ResourceType != resourceType)
                continue;

            float distanceSquared =
                (node.Position - worldPosition).sqrMagnitude;

            if (distanceSquared >= closestDistanceSquared)
                continue;

            closestDistanceSquared = distanceSquared;
            closestNode = node;
        }

        return closestNode;
    }

    private void RemoveNode(ResourceNode node)
    {
        if (node == null)
            return;

        if (!resourceNodeList.Contains(node))
            return;

        Debug.Log($"{node.name} depleted and was removed from the match.");

        UnregisterNode(node);

        node.NotifyRemoved();

        Object.Destroy(node.gameObject);
    }
}