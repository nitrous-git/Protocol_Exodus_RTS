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

        //Debug.Log($"ResourceNodeRepository initialized with {resourceNodeList.Count} resource nodes.");
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
                UnregisterNode(node);
                continue;
            }

            if (!node.IsDepleted)
                continue;

            RemoveNode(node);
        }
    }

    internal void NotifyDepleted(ResourceNode node)
    {
        Faction playerFaction = gameContext.PlayerFaction;
        IReadOnlyList<UnitBase> playerUnits = playerFaction?.UnitManager?.UnitList;
        if (playerUnits == null)
            return;

        // Check assignments only on depletion, including when another faction takes the last resources.
        for (int i = 0; i < playerUnits.Count; i++)
        {
            if (playerUnits[i] is WorkerUnit worker && worker != null && worker.IsAlive &&
                worker.CurrentCommand is CommandType.Gather or CommandType.Deliver &&
                worker.ResourceComponent?.AssignedNode == node)
            {
                string resource = node.ResourceType == ResourceType.Gas ? "Gas" : "Mineral";
                gameContext.GameLog.Log($"{resource} node depleted.", playerFaction);
                return;
            }
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

        if (node.IsInitialized)
            return false;

        if (terrainGrid == null || gameContext == null)
            return false;

        Vector2Int size = node.Footprint;
        GridCoord centerCell = terrainGrid.WorldToCell(node.Position);

        GridCoord originCell = new GridCoord(centerCell.x - size.x / 2, centerCell.z - size.y / 2);

        if (!terrainGrid.CanPlaceFootprint(originCell, size))
        {
            Debug.LogError(
                $"{node.name}: Invalid footprint {size} at {originCell}"
            );
            return false;
        }

        Vector3 previousPosition = node.Position;
        node.transform.position = terrainGrid.GetFootprintWorldCenter(originCell, size);
        if (!node.Initialize(this, gameContext, originCell, size))
        {
            node.transform.position = previousPosition;
            return false;
        }

        if (!terrainGrid.SetFootprintOccupied(originCell, size, true, resourceNodeId: node.ResourceNodeId))
        {
            node.NotifyRemoved();
            node.transform.position = previousPosition;
            return false;
        }

        resourceNodeList.Add(node);
        gameContext.RegisterResourceNode(node);

        return true;
    }

    /// <summary>
    /// Removes a node from repositories and releases its occupied footprint.
    ///
    /// Does not destroy the GameObject.
    /// </summary>
    public void UnregisterNode(ResourceNode node)
    {
        if (ReferenceEquals(node, null))
            return;

        bool wasRegistered = resourceNodeList.Remove(node);

        if (!wasRegistered)
            return;

        gameContext?.UnregisterResourceNode(node);

        terrainGrid.SetFootprintOccupied(node.OriginCell, node.Footprint, false,
            resourceNodeId: node.ResourceNodeId);
        if (node != null)
            node.NotifyRemoved();
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

        Object.Destroy(node.gameObject);
    }



}
