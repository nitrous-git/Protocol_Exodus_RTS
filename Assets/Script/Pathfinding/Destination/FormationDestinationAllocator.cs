using UnityEngine;

public sealed class FormationDestinationAllocator
{
    private readonly TerrainGrid terrainGrid;
    private readonly GridNavigationStateSystem navigationState;

    private int spacing = 2;

    public FormationDestinationAllocator(TerrainGrid terrainGrid, GridNavigationStateSystem navigationState)
    {
        this.terrainGrid = terrainGrid;
        this.navigationState = navigationState;
    }

    public GridCoord? TryAllocate(
        UnitBase unit,
        GridCoord formationCenter,
        int slotIndex,
        int unitCount,
        float formationMaxNavigationRadius,
        int maxFallbackDepth = 3)
    {
        if (unit == null ||
            terrainGrid == null ||
            navigationState == null ||
            unitCount <= 0 ||
            slotIndex < 0 ||
            slotIndex >= unitCount)
        {
            return null;
        }

        int spacing = CalculateSpacing(formationMaxNavigationRadius);

        GridCoord preferredCell = GetPreferredSlot(formationCenter, slotIndex, unitCount, spacing);

        if (navigationState.TryReserveDestination(preferredCell, unit))
        {
            return preferredCell;
        }

        for (int depth = 1; depth <= maxFallbackDepth; depth++)
        {
            GridCoord? fallback = TryAllocateAround(unit, preferredCell, depth, spacing);

            if (fallback.HasValue)
                return fallback;
        }

        return null;
    }

    private GridCoord GetPreferredSlot(
        GridCoord formationCenter,
        int slotIndex,
        int unitCount,
        int spacing)
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


    public GridCoord GetPreferredSlot(
        GridCoord formationCenter,
        int slotIndex,
        int unitCount,
        float formationMaxNavigationRadius)
    {
        int spacing = CalculateSpacing(formationMaxNavigationRadius);
        return GetPreferredSlot(formationCenter, slotIndex, unitCount, spacing);
    }


    private GridCoord? TryAllocateAround(
        UnitBase unit,
        GridCoord center,
        int depth, 
        int spacing)
    {
        for (int z = -depth; z <= depth; z++)
        {
            for (int x = -depth; x <= depth; x++)
            {
                bool isEdge = Mathf.Abs(x) == depth || Mathf.Abs(z) == depth;

                if (!isEdge)
                    continue;

                GridCoord candidate = new GridCoord(center.x + x * spacing, center.z + z * spacing);

                if (navigationState.TryReserveDestination(candidate, unit))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // Helpers 
    // ---------------------------------------------------------------------

    private int CalculateSpacing(float formationMaxNavigationRadius)
    {
        float requiredWorldSpacing = formationMaxNavigationRadius * 2f;
        int requiredCellSpacing = Mathf.CeilToInt((requiredWorldSpacing) / terrainGrid.CellSize);

        return Mathf.Max(2, requiredCellSpacing);
    }
}