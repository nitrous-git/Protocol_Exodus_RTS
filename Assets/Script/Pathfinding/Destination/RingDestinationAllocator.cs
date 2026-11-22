using System.Collections.Generic;
using UnityEngine;

public sealed class RingDestinationAllocator
{
    public readonly TerrainGrid terrainGrid;
    private readonly GridNavigationStateSystem navigationState;

    public RingDestinationAllocator(TerrainGrid terrainGrid, GridNavigationStateSystem navigationState)
    { 
        this.terrainGrid = terrainGrid;
        this.navigationState = navigationState; 
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
        GridCoord? preferredCell = null,
        ISet<GridCoord> excludedCells = null)
    {
        if (unit == null || terrainGrid == null || navigationState == null)
        {
            return null;
        }

        GridCoord targetCell = preferredCell ?? terrainGrid.WorldToCell(unit.Position);

        GridCoord? FindCandidate(bool checkClearance) =>
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
                distanceWeight: 1,
                additionalValidator: cell =>
                    (excludedCells == null || !excludedCells.Contains(cell)) &&
                    (!checkClearance || navigationState.CanStandAt(cell, unit)));

        GridCoord? destinationCell = FindCandidate(checkClearance: false);
        if (!destinationCell.HasValue)
            return null;

        if (navigationState.TryReserveDestination(destinationCell.Value, unit))
            return destinationCell;

        // Placement alone does not account for unit radii, occupied space, or nearby reservations.
        destinationCell = FindCandidate(checkClearance: true);
        return destinationCell.HasValue && navigationState.TryReserveDestination(destinationCell.Value, unit)
            ? destinationCell
            : null;
    }

    internal bool IsReservationUsable(GridCoord cell, UnitBase unit)
    {
        return navigationState.IsDestinationReservedBy(cell, unit) &&
               terrainGrid.IsStaticallyTraversable(cell, unit.NavigationRadius);
    }

}
