using System.Collections.Generic;

public sealed class GridNavigationStateSystem
{
    private readonly TerrainGrid terrainGrid;

    // ---------------------------------------------------------------------
    // Destination Reservations
    // ---------------------------------------------------------------------

    private readonly Dictionary<GridCoord, UnitBase> destinationsByCell = new();
    private readonly Dictionary<UnitBase, List<GridCoord>> destinationsByUnit = new();

    // ---------------------------------------------------------------------
    // Current Unit Occupancy
    // ---------------------------------------------------------------------

    private readonly Dictionary<GridCoord, HashSet<UnitBase>> occupantsByCell = new();
    private readonly Dictionary<UnitBase, GridCoord> occupiedCellByUnit = new();

    public int DestinationReservationCount => destinationsByCell.Count;
    public int OccupiedCellCount => occupantsByCell.Count;

    public GridNavigationStateSystem(TerrainGrid terrainGrid)
    {
        this.terrainGrid = terrainGrid;
    }

    // ---------------------------------------------------------------------
    // Destination Reservations
    // ---------------------------------------------------------------------

    public bool TryReserveDestination(GridCoord coord, UnitBase owner)
    {
        if (terrainGrid == null || owner == null)
            return false;

        if (!terrainGrid.IsInside(coord))
            return false;

        // Already reserved.
        if (destinationsByCell.TryGetValue(coord, out UnitBase existingOwner))
        {
            return existingOwner == owner;
        }

        // TerrainGrid still handles:
        // - terrain walkability
        // - hard occupancy
        // - Reserved mirror
        if (!terrainGrid.IsWalkable(coord))
            return false;

        // A final destination may never be a cell currently
        // occupied by another unit.
        if (IsOccupiedByOtherUnit(coord, owner))
            return false;

        destinationsByCell.Add(coord, owner);

        if (!destinationsByUnit.TryGetValue(owner, out List<GridCoord> destinations))
        {
            destinations = new List<GridCoord>();
            destinationsByUnit.Add(owner, destinations);
        }

        destinations.Add(coord);

        // Keep the TerrainGrid mirror for now so PlacementUtil and
        // building placement still see destination reservations.
        terrainGrid.SetReserved(coord, true);

        return true;
    }

    public void ReleaseDestination(GridCoord coord, UnitBase owner)
    {
        if (terrainGrid == null || owner == null)
            return;

        if (!destinationsByCell.TryGetValue(coord, out UnitBase existingOwner))
            return;

        if (existingOwner != owner)
            return;

        destinationsByCell.Remove(coord);

        if (destinationsByUnit.TryGetValue(owner, out List<GridCoord> destinations))
        {
            destinations.Remove(coord);

            if (destinations.Count == 0)
                destinationsByUnit.Remove(owner);
        }

        terrainGrid.SetReserved(coord, false);
    }

    public void ReleaseAllDestinations(UnitBase owner)
    {
        if (terrainGrid == null || owner == null)
            return;

        if (!destinationsByUnit.TryGetValue(owner, out List<GridCoord> destinations))
            return;

        for (int i = 0; i < destinations.Count; i++)
        {
            GridCoord coord = destinations[i];

            destinationsByCell.Remove(coord);
            terrainGrid.SetReserved(coord, false);
        }

        destinationsByUnit.Remove(owner);
    }

    public bool IsDestinationReserved(GridCoord coord)
    {
        return destinationsByCell.ContainsKey(coord);
    }

    public bool IsDestinationReservedBy(GridCoord coord, UnitBase owner)
    {
        if (!destinationsByCell.TryGetValue(coord, out UnitBase existingOwner))
            return false;

        return existingOwner == owner;
    }

    public bool IsDestinationReservedByOther(GridCoord coord, UnitBase owner)
    {
        if (!destinationsByCell.TryGetValue(coord, out UnitBase existingOwner))
            return false;

        return existingOwner != owner;
    }

    // ---------------------------------------------------------------------
    // Current Unit Occupancy
    // ---------------------------------------------------------------------

    public void UpdateUnitOccupancy(UnitBase unit, GridCoord newCell)
    {
        if (terrainGrid == null || unit == null)
            return;

        // If a unit somehow leaves the grid, do not leave stale occupancy.
        if (!terrainGrid.IsInside(newCell))
        {
            ReleaseUnitOccupancy(unit);
            return;
        }

        if (occupiedCellByUnit.TryGetValue(unit, out GridCoord oldCell))
        {
            if (IsSameCoord(oldCell, newCell))
                return;

            RemoveOccupant(oldCell, unit);
        }

        AddOccupant(newCell, unit);
        occupiedCellByUnit[unit] = newCell;
    }

    public void ReleaseUnitOccupancy(UnitBase unit)
    {
        if (unit == null)
            return;

        if (!occupiedCellByUnit.TryGetValue(unit, out GridCoord coord))
            return;

        RemoveOccupant(coord, unit);
        occupiedCellByUnit.Remove(unit);
    }

    public bool HasUnitOccupancy(GridCoord coord)
    {
        return occupantsByCell.TryGetValue(coord, out HashSet<UnitBase> occupants) && occupants.Count > 0;
    }

    public bool IsOccupiedByOtherUnit(GridCoord coord, UnitBase requester)
    {
        if (!occupantsByCell.TryGetValue(coord, out HashSet<UnitBase> occupants))
            return false;

        foreach (UnitBase occupant in occupants)
        {
            if (occupant != null && occupant != requester)
                return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------
    // General Cleanup
    // ---------------------------------------------------------------------

    public void ReleaseAll(UnitBase unit)
    {
        ReleaseAllDestinations(unit);
        ReleaseUnitOccupancy(unit);
    }

    public void Clear()
    {
        if (terrainGrid != null)
        {
            foreach (GridCoord coord in destinationsByCell.Keys)
            {
                terrainGrid.SetReserved(coord, false);
            }
        }

        destinationsByCell.Clear();
        destinationsByUnit.Clear();

        occupantsByCell.Clear();
        occupiedCellByUnit.Clear();
    }

    // ---------------------------------------------------------------------
    // Internal Helpers
    // ---------------------------------------------------------------------

    private void AddOccupant(GridCoord coord, UnitBase unit)
    {
        if (!occupantsByCell.TryGetValue(coord, out HashSet<UnitBase> occupants))
        {
            occupants = new HashSet<UnitBase>();
            occupantsByCell.Add(coord, occupants);
        }

        occupants.Add(unit);
    }

    private void RemoveOccupant(GridCoord coord, UnitBase unit)
    {
        if (!occupantsByCell.TryGetValue(coord, out HashSet<UnitBase> occupants))
            return;

        occupants.Remove(unit);

        if (occupants.Count == 0)
            occupantsByCell.Remove(coord);
    }

    private bool IsSameCoord(GridCoord first, GridCoord second)
    {
        return first.x == second.x && first.z == second.z;
    }

    // ---------------------------------------------------------------------
    // Getter
    // ---------------------------------------------------------------------

    public GridCoord? GetOccupiedCell(UnitBase unit)
    {
        if (unit == null)
            return null;

        if (!occupiedCellByUnit.TryGetValue(unit, out GridCoord coord))
            return null;

        return coord;
    }

    public int GetOccupantCount(GridCoord coord)
    {
        if (!occupantsByCell.TryGetValue(coord, out HashSet<UnitBase> occupants))
            return 0;

        return occupants.Count;
    }
}