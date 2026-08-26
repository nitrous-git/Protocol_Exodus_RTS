using System.Collections.Generic;
using UnityEngine;

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

    private float maxOccupiedNavigationRadius;

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

        if (!CanStandAt(coord, owner))
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

        maxOccupiedNavigationRadius = Mathf.Max(maxOccupiedNavigationRadius, unit.NavigationRadius);

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

        float releasedRadius = unit.NavigationRadius;

        RemoveOccupant(coord, unit);
        occupiedCellByUnit.Remove(unit);

        if (releasedRadius >= maxOccupiedNavigationRadius - 0.001f) 
        {
            RecalculateMaxOccupiedNavigationRadius();
        }
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
    // Radius Based Clearance
    // ---------------------------------------------------------------------

    public bool CanStandAt(GridCoord coord, UnitBase unit)
    {
        if (terrainGrid == null || unit == null)
            return false;

        if (!terrainGrid.IsInside(coord))
            return false;

        float radius = unit.NavigationRadius;

        if (!terrainGrid.HasNavigationClearance(coord, radius))
            return false;

        GridCell candidateCell = terrainGrid.GetCell(coord);

        if (candidateCell == null)
            return false;

        Vector3 center = candidateCell.WorldCenter;
        
        if (OverlapsOccupiedUnit(center, radius, unit))
            return false;

        if (OverlapsReservedDestination(center, radius, unit))
            return false;

        return true;
    }

    //private bool HasHardClearance(GridCoord centerCoord, Vector3 center, float radius)
    //{
    //    GridCell centerCell = terrainGrid.GetCell(centerCoord);

    //    if (centerCell == null)
    //        return false;

    //    // Important:
    //    // Reserved is NOT checked here.
    //    //
    //    // Reserved mobile destinations are handled separately through
    //    // destinationsByCell. Here we only care about hard geometry.
    //    if (!centerCell.Walkable || centerCell.Occupied)
    //        return false;

    //    if (radius <= 0f)
    //        return true;

    //    float cellSize = terrainGrid.CellSize;
    //    float halfCellSize = cellSize * 0.5f;

    //    // ------------------------------------------------------------
    //    // Grid boundary clearance
    //    // ------------------------------------------------------------

    //    GridCell firstCell = terrainGrid.GetCell(new GridCoord(0, 0));
    //    GridCell lastCell = terrainGrid.GetCell( new GridCoord(terrainGrid.Width - 1, terrainGrid.Height - 1));

    //    if (firstCell == null || lastCell == null)
    //        return false;

    //    float minX = firstCell.WorldCenter.x - halfCellSize;
    //    float maxX = lastCell.WorldCenter.x + halfCellSize;
    //    float minZ = firstCell.WorldCenter.z - halfCellSize;
    //    float maxZ = lastCell.WorldCenter.z + halfCellSize;

    //    if (center.x - radius < minX || center.x + radius > maxX ||
    //        center.z - radius < minZ || center.z + radius > maxZ)
    //    {
    //        return false;
    //    }

    //    // ------------------------------------------------------------
    //    // Nearby hard cells
    //    // ------------------------------------------------------------

    //    int searchDepth = Mathf.CeilToInt((radius + halfCellSize) / cellSize);

    //    for (int z = -searchDepth; z <= searchDepth; z++)
    //    {
    //        for (int x = -searchDepth; x <= searchDepth; x++)
    //        {
    //            GridCoord testCoord = new GridCoord(centerCoord.x + x, centerCoord.z + z);

    //            if (!terrainGrid.IsInside(testCoord))
    //                continue;

    //            GridCell cell = terrainGrid.GetCell(testCoord);
    //            if (cell == null)
    //                continue;

    //            // Free hard-space cell.
    //            // Ignore Reserved here intentionally.
    //            if (cell.Walkable && !cell.Occupied)
    //                continue;

    //            if (CircleOverlapsCell(center, radius, cell.WorldCenter, halfCellSize))
    //            {
    //                return false;
    //            }
    //        }
    //    }

    //    return true;
    //}

    //private bool CircleOverlapsCell(Vector3 circleCenter, float radius, Vector3 cellCenter, float halfCellSize)
    //{
    //    float deltaX = Mathf.Max(Mathf.Abs(circleCenter.x - cellCenter.x) - halfCellSize, 0f);
    //    float deltaZ = Mathf.Max(Mathf.Abs(circleCenter.z - cellCenter.z) - halfCellSize, 0f);
    //    float distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
    //    return distanceSquared < radius * radius;
    //}

    private bool OverlapsOccupiedUnit(Vector3 center, float radius, UnitBase requester)
    {
        if (occupantsByCell.Count == 0)
            return false;

        GridCoord centerCoord = terrainGrid.WorldToCell(center);

        int searchDepth = Mathf.CeilToInt((radius + maxOccupiedNavigationRadius) / terrainGrid.CellSize) + 1;

        for (int z = -searchDepth; z <= searchDepth; z++)
        {
            for (int x = -searchDepth; x <= searchDepth; x++)
            {
                GridCoord coord = new GridCoord(centerCoord.x + x, centerCoord.z + z);

                if (!terrainGrid.IsInside(coord))
                    continue;

                if (!occupantsByCell.TryGetValue(coord, out HashSet<UnitBase> occupants))
                {
                    continue;
                }

                foreach (UnitBase other in occupants)
                {
                    if (other == null || other == requester)
                    {
                        continue;
                    }

                    float requiredDistance = radius + other.NavigationRadius;

                    if (XZDistanceSquared(center, other.Position) < requiredDistance * requiredDistance)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    public bool WouldOverlapOccupiedUnit(GridCoord coord, UnitBase requester)
    {
        if (terrainGrid == null || requester == null || !terrainGrid.IsInside(coord))
        {
            return false;
        }

        GridCell candidateCell = terrainGrid.GetCell(coord);

        if (candidateCell == null)
            return false;

        return OverlapsOccupiedUnit(candidateCell.WorldCenter, requester.NavigationRadius, requester);
    }

    private bool OverlapsReservedDestination(Vector3 center, float radius, UnitBase requester)
    {
        foreach (KeyValuePair<GridCoord, UnitBase> pair in destinationsByCell)
        {
            UnitBase other = pair.Value;

            if (other == null || other == requester)
                continue;

            GridCell reservedCell = terrainGrid.GetCell(pair.Key);
            if (reservedCell == null)
                continue;

            float requiredDistance = radius + other.NavigationRadius;

            if (XZDistanceSquared(center, reservedCell.WorldCenter) < requiredDistance * requiredDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void RecalculateMaxOccupiedNavigationRadius()
    {
        maxOccupiedNavigationRadius = 0f;

        foreach (UnitBase unit in occupiedCellByUnit.Keys)
        {
            if (unit == null)
                continue;

            maxOccupiedNavigationRadius = Mathf.Max(maxOccupiedNavigationRadius, unit.NavigationRadius);
        }
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

        maxOccupiedNavigationRadius = 0f;

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

    private float XZDistanceSquared(Vector3 first, Vector3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return deltaX * deltaX + deltaZ * deltaZ;
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