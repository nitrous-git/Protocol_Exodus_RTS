using UnityEngine;

public sealed class RingDestinationAllocator
{
    public readonly TerrainGrid terrainGrid;
    private readonly GridReservationSystem reservationSystem;

    public RingDestinationAllocator(TerrainGrid terrainGrid, GridReservationSystem reservationSystem)
    { 
        this.terrainGrid = terrainGrid;
        this.reservationSystem = reservationSystem; 
    }

    /// <summary>
    /// Finds and reserves a destination cell on a ring around a footprint.
    ///
    /// When no preferred cell is provided, the unit's current cell is used.
    /// </summary>
    public GridCoord? TryAllocate(
        UnitBase unit,
        GridCoord footprintOrigin,
        Vector2Int footprintSize,
        int initialDepth = 1,
        int maxExtraDepth = 2,
        GridCoord? preferredCell = null)
    {
        if (unit == null || terrainGrid == null || reservationSystem == null)
        {
            return null;
        }

        GridCoord targetCell = preferredCell ?? terrainGrid.WorldToCell(unit.Position);

        GridCoord? destinationCell =
            PlacementUtil.GetPlacementAroundFootprintScoredWithFallback(
                terrainGrid,
                footprintOrigin,
                footprintSize,
                initialDepth,
                maxExtraDepth,
                targetCell,
                PlacementUtil.PlacementPolicy.Closest,
                openRadius: 1,
                openWeight: 2,
                distanceWeight: 1);

        if (!destinationCell.HasValue)
            return null;

        bool reserved = reservationSystem.TryReserve(destinationCell.Value, unit, GridReservationType.Destination);

        if (!reserved)
            return null;

        return destinationCell;
    }


}
