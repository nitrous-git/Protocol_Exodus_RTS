using UnityEngine;

public sealed class FormationDestinationAllocator
{
    private readonly TerrainGrid terrainGrid;
    private readonly GridReservationSystem reservationSystem;

    private int spacing = 3;

    public FormationDestinationAllocator(TerrainGrid terrainGrid, GridReservationSystem reservationSystem)
    {
        this.terrainGrid = terrainGrid;
        this.reservationSystem = reservationSystem;
    }

    public GridCoord? TryAllocate(
        UnitBase unit,
        GridCoord formationCenter,
        int slotIndex,
        int unitCount,
        int maxFallbackDepth = 3)
    {
        if (unit == null ||
            terrainGrid == null ||
            reservationSystem == null ||
            unitCount <= 0 ||
            slotIndex < 0 ||
            slotIndex >= unitCount)
        {
            return null;
        }

        GridCoord preferredCell = GetPreferredSlot(formationCenter, slotIndex, unitCount);

        if (reservationSystem.TryReserve(preferredCell, unit, GridReservationType.Destination))
        {
            return preferredCell;
        }

        for (int depth = 1; depth <= maxFallbackDepth; depth++)
        {
            GridCoord? fallback = TryAllocateAround(unit, preferredCell, depth);

            if (fallback.HasValue)
                return fallback;
        }

        return null;
    }

    private GridCoord GetPreferredSlot(
        GridCoord formationCenter,
        int slotIndex,
        int unitCount)
    {
        int columns = Mathf.CeilToInt(Mathf.Sqrt(unitCount));
        int rows = Mathf.CeilToInt((float)unitCount / columns);

        int row = slotIndex / columns;
        int indexInRow = slotIndex % columns;

        int unitsInRow = Mathf.Min(columns, unitCount - row * columns);

        int startX = formationCenter.x - ((unitsInRow - 1) * spacing) / 2;
        int startZ = formationCenter.z - ((rows - 1) * spacing) / 2;

        return new GridCoord(startX + indexInRow * spacing, startZ + row * spacing);
    }

    private GridCoord? TryAllocateAround(
        UnitBase unit,
        GridCoord center,
        int depth)
    {
        for (int z = -depth; z <= depth; z++)
        {
            for (int x = -depth; x <= depth; x++)
            {
                bool isEdge = Mathf.Abs(x) == depth || Mathf.Abs(z) == depth;

                if (!isEdge)
                    continue;

                GridCoord candidate = new GridCoord(center.x + x * spacing, center.z + z * spacing);

                if (reservationSystem.TryReserve(candidate, unit, GridReservationType.Destination))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}