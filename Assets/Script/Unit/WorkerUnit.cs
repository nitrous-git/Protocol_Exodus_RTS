using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(WorkerResourceComponent))]
public sealed class WorkerUnit : UnitBase
{
    private WorkerResourceComponent resourceComponent;

    public WorkerResourceComponent ResourceComponent => resourceComponent;
    //public TerrainGrid TerrainGrid => ownerFaction?.BuildingManager?.TerrainGrid;
    public ResourceNodeRepository ResourceNodeRepository => gameContext?.ResourceNodeRepository;

    protected override void CacheComponents()
    {
        base.CacheComponents();
        resourceComponent = GetComponent<WorkerResourceComponent>();
    }

    public override void IssueCommand(CommandType commandType, CommandContext context)
    {
        if (!CanReceiveCommands)
            return;

        CurrentCommand = commandType;
        currentContext = context;

        switch (commandType)
        {
            case CommandType.Move:
                SetState(new MoveState(context.WorldPosition));
                break;

            case CommandType.Gather:
                SetState(new WorkerGatherState(context.ResourceNode));
                break;

            case CommandType.Deliver:
                SetState(new WorkerDeliverState(context.Target as BuildingBase));
                break;

            case CommandType.Idle:
            default:
                SetState(new IdleState());
                break;
        }
    }


    /// <summary>
    /// Finds the closest operational headquarters owned by this worker faction.
    /// HeadquartersComponent identifies a valid resource drop-off.
    /// </summary>
    public BuildingBase FindClosestResourceDropOff()
    {
        BuildingManager buildingManager = ownerFaction?.BuildingManager;

        if (buildingManager == null)
            return null;

        IReadOnlyList<BuildingBase> buildings = buildingManager.BuildingList;

        BuildingBase closestBuilding = null;
        float closestDistanceSquared = float.PositiveInfinity;

        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingBase building = buildings[i];

            if (building == null)
                continue;

            if (!building.IsInitialized || !building.IsAlive || !building.IsOperational)
            {
                continue;
            }

            if (building.Headquarters == null)
                continue;

            float distanceSquared = (building.Position - Position).sqrMagnitude;

            if (distanceSquared >= closestDistanceSquared)
                continue;

            closestDistanceSquared = distanceSquared;
            closestBuilding = building;
        }

        return closestBuilding;
    }

    /// <summary>
    /// Finds another available resource node matching the worker
    /// currently assigned resource type.
    /// </summary>
    public ResourceNode FindReplacementResourceNode()
    {
        if (resourceComponent == null || !resourceComponent.HasAssignedResourceType)
        {
            return null;
        }

        return ResourceNodeRepository?.FindClosestAvailableNode(resourceComponent.AssignedResourceType, Position);
    }

    // ---------------------------------------------------------------------
    // Reservation
    // ---------------------------------------------------------------------

    public GridCoord? ReserveInteractionCell(ResourceNode resourceNode)
    {
        if (resourceNode == null)
            return null;

        return ReserveInteractionCell(resourceNode.OccupiedCell, Vector2Int.one);
    }

    public GridCoord? ReserveInteractionCell(BuildingBase building)
    {
        if (building == null || building.Definition == null)
            return null;

        return ReserveInteractionCell(building.FootprintOrigin, building.Definition.FootprintSize);
    }

    /// <summary>
    /// Finds and reserves a free interaction cell around a footprint.
    /// </summary>
    private GridCoord? ReserveInteractionCell(GridCoord footprintOrigin, Vector2Int footprintSize)
    {
        TerrainGrid terrainGrid = TerrainGrid;
        GridReservationSystem reservationSystem = gameContext?.GridReservationSystem;

        if (terrainGrid == null || reservationSystem == null)
            return null;

        GridCoord preferredCell = terrainGrid.WorldToCell(Position);

        GridCoord? interactionCell =
            PlacementUtil.GetPlacementAroundFootprintScoredWithFallback(
                terrainGrid,
                footprintOrigin,
                footprintSize,
                initialDepth: 1,
                maxExtraDepth: 2,
                preferredCell,
                PlacementUtil.PlacementPolicy.Closest,
                openRadius: 1,
                openWeight: 2,
                distanceWeight: 1);

        if (!interactionCell.HasValue)
            return null;

        bool reserved = reservationSystem.TryReserve(interactionCell.Value, this, GridReservationType.Destination);

        if (!reserved)
            return null;

        Debug.Log($"{name} reserved interaction cell ({interactionCell.Value.x}, {interactionCell.Value.z})");

        return interactionCell;
    }

    public void ReleaseInteractionCell(GridCoord interactionCell)
    {
        gameContext?.GridReservationSystem?.Release(interactionCell, this, GridReservationType.Destination);
    }

    //public Vector3? GetInteractionPosition(ResourceNode resourceNode)
    //{
    //    if (resourceNode == null)
    //        return null;

    //    return GetInteractionPosition(resourceNode.OccupiedCell, Vector2Int.one);
    //}

    //public Vector3? GetInteractionPosition(BuildingBase building)
    //{
    //    if (building == null || building.Definition == null)
    //    {
    //        return null;
    //    }

    //    return GetInteractionPosition(building.FootprintOrigin, building.Definition.FootprintSize);
    //}

    ///// <summary>
    ///// Finds a free cell outside an occupied footprint.
    /////
    ///// The first searched ring is directly adjacent to the footprint.
    ///// Additional rings provide a fallback when the adjacent cells
    ///// are unavailable.
    ///// </summary>
    //private Vector3? GetInteractionPosition(GridCoord footprintOrigin, Vector2Int footprintSize)
    //{
    //    TerrainGrid terrainGrid = TerrainGrid;

    //    if (terrainGrid == null)
    //        return null;

    //    GridCoord preferredCell = terrainGrid.WorldToCell(Position);

    //    Debug.Log(
    //    $"INTERACTION QUERY | " +
    //    $"origin=({footprintOrigin.x},{footprintOrigin.z}) | " +
    //    $"size=({footprintSize.x},{footprintSize.y}) | " +
    //    $"preferred=({preferredCell.x},{preferredCell.z})");

    //    GridCoord? interactionCell =
    //        PlacementUtil.GetPlacementAroundFootprintScoredWithFallback(
    //            terrainGrid,
    //            footprintOrigin,
    //            footprintSize,
    //            initialDepth: 1,
    //            maxExtraDepth: 2,
    //            preferredCell,
    //            PlacementUtil.PlacementPolicy.Closest,
    //            openRadius: 1,
    //            openWeight: 2,
    //            distanceWeight: 1);

    //    if (!interactionCell.HasValue)
    //        return null;

    //    if (interactionCell.HasValue)
    //    {
    //        Debug.Log(
    //            $"INTERACTION RESULT | " +
    //            $"({interactionCell.Value.x},{interactionCell.Value.z})");
    //    }

    //    return terrainGrid.CellToWorld(interactionCell.Value);
    //}
}