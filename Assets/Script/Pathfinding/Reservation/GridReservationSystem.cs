using System.Collections.Generic;

public sealed class GridReservationSystem
{
    private sealed class GridReservation
    {
        public UnitBase Owner { get; }
        private readonly HashSet<GridReservationType> types = new();

        public bool HasAnyType => types.Count > 0;

        public GridReservation(UnitBase owner, GridReservationType type)
        {
            Owner = owner;
            types.Add(type);
        }

        public void AddType(GridReservationType type)
        {
            types.Add(type);
        }

        public void RemoveType(GridReservationType type)
        {
            types.Remove(type);
        }

        public bool HasType(GridReservationType type)
        {
            return types.Contains(type);
        }
    }

    private readonly TerrainGrid terrainGrid;

    private readonly Dictionary<GridCoord, GridReservation> reservationsByCell = new();
    private readonly Dictionary<UnitBase, List<GridCoord>> reservationsByUnit = new();

    public int ReservationCount => reservationsByCell.Count;

    public GridReservationSystem(TerrainGrid terrainGrid)
    {
        this.terrainGrid = terrainGrid;
    }

    /// <summary>
    /// Attempts to reserve a cell for a unit.
    ///
    /// A cell may only have one reservation owner.
    /// </summary>
    public bool TryReserve(GridCoord coord, UnitBase owner, GridReservationType type)
    {
        if (terrainGrid == null || owner == null)
            return false;

        // The cell is already managed by the reservation system.
        if (reservationsByCell.TryGetValue(coord, out GridReservation existingReservation))
        {
            if (existingReservation.Owner != owner)
                return false;

            existingReservation.AddType(type);
            return true;
        }

        // TerrainGrid handles:
        // - outside grid
        // - non-walkable
        // - occupied
        // - already reserved
        if (!terrainGrid.IsWalkable(coord))
            return false;

        GridReservation reservation = new(owner, type);

        reservationsByCell.Add(coord, reservation);

        if (!reservationsByUnit.TryGetValue(owner, out List<GridCoord> unitReservations))
        {
            unitReservations = new List<GridCoord>();
            reservationsByUnit.Add(owner, unitReservations);
        }

        unitReservations.Add(coord);

        terrainGrid.SetReserved(coord, true);

        return true;
    }

    /// <summary>
    /// Releases a reservation if it belongs to the specified unit.
    /// </summary>
    public void Release(GridCoord coord, UnitBase owner, GridReservationType type)
    {
        if (terrainGrid == null || owner == null)
            return;

        if (!reservationsByCell.TryGetValue(coord, out GridReservation reservation))
        {
            return;
        }

        if (reservation.Owner != owner)
            return;

        reservation.RemoveType(type);

        if (reservation.HasAnyType)
            return;

        reservationsByCell.Remove(coord);

        if (reservationsByUnit.TryGetValue(owner, out List<GridCoord> unitReservations))
        {
            unitReservations.Remove(coord);

            if (unitReservations.Count == 0)
            {
                reservationsByUnit.Remove(owner);
            }
        }

        terrainGrid.SetReserved(coord, false);
    }

    /// <summary>
    /// Releases every reservation owned by a unit.
    /// </summary>
    public void ReleaseAll(UnitBase owner)
    {
        if (terrainGrid == null || owner == null)
            return;

        if (!reservationsByUnit.TryGetValue(owner, out List<GridCoord> unitReservations))
        {
            return;
        }

        for (int i = 0; i < unitReservations.Count; i++)
        {
            GridCoord coord = unitReservations[i];

            reservationsByCell.Remove(coord);
            terrainGrid.SetReserved(coord, false);
        }

        reservationsByUnit.Remove(owner);
    }

    public bool IsReserved(GridCoord coord)
    {
        return reservationsByCell.ContainsKey(coord);
    }

    public bool IsReservedBy(GridCoord coord, UnitBase owner)
    {        
        if (!reservationsByCell.TryGetValue(coord, out GridReservation reservation))
        {
            return false;
        }

        return reservation.Owner == owner;
    }

    public bool IsReservedByOther(GridCoord coord, UnitBase owner)
    {
        if (reservationsByCell.TryGetValue(coord, out GridReservation reservation))
        {
            return reservation.Owner != owner;
        }

        // A cell could theoretically have been reserved directly
        // through TerrainGrid. In that case we do not know the owner,
        // so it must be considered unavailable.
        GridCell cell = terrainGrid?.GetCell(coord);

        return cell != null && cell.Reserved;
    }

    /// <summary>
    /// Clears the entire reservation table.
    /// Intended for match cleanup.
    /// </summary>
    public void Clear()
    {
        if (terrainGrid != null)
        {
            foreach (GridCoord coord in reservationsByCell.Keys)
            {
                terrainGrid.SetReserved(coord, false);
            }
        }

        reservationsByCell.Clear();
        reservationsByUnit.Clear();
    }
}