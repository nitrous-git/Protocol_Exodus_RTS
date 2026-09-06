using System.Drawing;
using UnityEngine;

public class TerrainGrid
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float CellSize { get; private set; }
    public GridCell[,] cells { get; private set; }

    private Terrain terrain;
    private TerrainData terrainData;
    private Vector3 terrainOrigin;
    private Vector3 terrainSize;

    private float maxWalkableSlope;
    private float maxBuildableSlope;

    private StaticClearanceField staticClearanceField;

    public TerrainGrid(Terrain terrain, float cellSize, float maxWalkableSlope, float maxBuildableSlope)
    {
        if (terrain == null)
        {
            Debug.LogError("TerrainGrid cannot be created because Terrain is null.");
            return;
        }

        this.terrain = terrain;
        this.terrainData = terrain.terrainData;
        this.terrainOrigin = terrain.transform.position;
        this.terrainSize = terrainData.size;

        this.CellSize = cellSize;
        this.maxWalkableSlope = maxWalkableSlope;
        this.maxBuildableSlope = maxBuildableSlope;

        Width = Mathf.FloorToInt(terrainSize.x / cellSize);
        Height = Mathf.FloorToInt(terrainSize.z / cellSize);

        cells = new GridCell[Width, Height];

        BuildCells();

        staticClearanceField = new StaticClearanceField(this);
        //staticClearanceField.RebuildStaticClearanceField();
    }

    private void BuildCells()
    {
        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                GridCoord coord = new GridCoord(x, z);
                Vector3 worldCenter = CellToWorld(coord);

                float height = terrain.SampleHeight(worldCenter) + terrainOrigin.y;
                worldCenter.y = height;

                float normalizedX = (worldCenter.x - terrainOrigin.x) / terrainSize.x;
                float normalizedZ = (worldCenter.z - terrainOrigin.z) / terrainSize.z;

                float slope = terrainData.GetSteepness(normalizedX, normalizedZ);

                bool walkable = slope <= maxWalkableSlope;
                bool buildable = slope <= maxBuildableSlope;

                cells[x, z] = new GridCell
                {
                    Coord = coord,
                    WorldCenter = worldCenter,
                    Height = worldCenter.y,
                    Slope = slope,

                    Walkable = walkable,
                    Buildable = buildable,

                    Occupied = false,
                    Reserved = false,

                    OccupyingUnitId = -1,
                    OccupyingBuildingId = -1,
                    OccupyingResourceNodeId = -1
                };
            }
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    public Vector3 CellToWorld(GridCoord coord)
    {

        //
        // Runtime fast path:
        // grid geometry was already sampled during BuildCells().
        //
        if (cells != null && IsInside(coord))
        {
            GridCell cell = cells[coord.x, coord.z];

            if (cell != null)
            {
                return cell.WorldCenter;
            }
        }

        //
        // Grid construction / fallback.
        //
        float worldX = terrainOrigin.x + (coord.x + 0.5f) * CellSize;
        float worldZ = terrainOrigin.z + (coord.z + 0.5f) * CellSize;

        Vector3 worldPosition = new Vector3(worldX, 0f, worldZ);
        float terrainHeight = terrain.SampleHeight(worldPosition) + terrainOrigin.y;
        worldPosition.y = terrainHeight;

        return worldPosition;
    }

    public GridCoord WorldToCell(Vector3 worldPosition)
    {
        int x = Mathf.FloorToInt((worldPosition.x - terrainOrigin.x) / CellSize);
        int z = Mathf.FloorToInt((worldPosition.z - terrainOrigin.z) / CellSize);

        return new GridCoord(x, z);
    }

    public bool IsInside(GridCoord coord)
    {
        return coord.x >= 0 && coord.z >= 0 && coord.x < Width && coord.z < Height;
    }

    public GridCell GetCell(GridCoord coord)
    {
        if (!IsInside(coord))
            return null;

        return cells[coord.x, coord.z];
    }

    public bool IsWalkable(GridCoord coord)
    {
        GridCell cell = GetCell(coord);

        return cell != null && cell.IsFreeForMovement();
    }

    public bool IsBuildable(GridCoord coord)
    {
        GridCell cell = GetCell(coord);

        return cell != null && cell.IsFreeForBuilding();
    }

    public void SetOccupied(GridCoord coord, bool occupied, int buildingId = -1, int unitId = -1, int resourceNodeId = -1)
    {
        GridCell cell = GetCell(coord);

        if (cell == null)
            return;

        bool wasStatic = staticClearanceField.IsStaticNavigationBlocked(cell);

        cell.Occupied = occupied;
        cell.OccupyingBuildingId = occupied ? buildingId : -1;
        cell.OccupyingUnitId = occupied ? unitId : -1;
        cell.OccupyingResourceNodeId = occupied ? resourceNodeId : -1;

        bool isStatic = staticClearanceField.IsStaticNavigationBlocked(cell);

        // Did the change affected static geometry
        if (wasStatic != isStatic)
        {
            staticClearanceField.SetDirty(true);
        }
    }

    public void SetReserved(GridCoord coord, bool reserved)
    {
        GridCell cell = GetCell(coord);

        if (cell == null)
            return;

        cell.Reserved = reserved;
    }

    // ---------------------------------------------------------------------
    // Placement 
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns whether every cell in a rectangular footprint
    /// is currently available for building placement.
    /// </summary>
    public bool CanPlaceFootprint(GridCoord origin, Vector2Int footprintSize)
    {
        if (footprintSize.x <= 0 || footprintSize.y <= 0)
        {
            return false;
        }

        for (int z = 0; z < footprintSize.y; z++)
        {
            for (int x = 0; x < footprintSize.x; x++)
            {
                GridCoord coord = new GridCoord(origin.x + x, origin.z + z);

                GridCell cell = GetCell(coord);

                if (cell == null)
                    return false;

                if (!cell.IsFreeForBuilding())
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Marks every cell in a building footprint as occupied.
    /// Call only after validating the footprint.
    /// </summary>
    public void SetFootprintOccupied(GridCoord origin, Vector2Int footprintSize, int buildingId)
    {
        for (int z = 0; z < footprintSize.y; z++)
        {
            for (int x = 0; x < footprintSize.x; x++)
            {
                GridCoord coord = new GridCoord(origin.x + x, origin.z + z);

                GridCell cell = GetCell(coord);

                if (cell == null)
                    continue;

                cell.Occupied = true;
                cell.OccupyingBuildingId = buildingId;
                cell.OccupyingUnitId = -1;
                cell.OccupyingResourceNodeId = -1;
            }
        }

        staticClearanceField.SetDirty(true);
    }

    /// <summary>
    /// Clears footprint cells currently occupied by the specified building.
    /// </summary>
    public void ClearFootprintOccupied(GridCoord origin, Vector2Int footprintSize, int buildingId)
    {
        for (int z = 0; z < footprintSize.y; z++)
        {
            for (int x = 0; x < footprintSize.x; x++)
            {
                GridCoord coord = new GridCoord(origin.x + x, origin.z + z);

                GridCell cell = GetCell(coord);

                if (cell == null)
                    continue;

                if (cell.OccupyingBuildingId != buildingId)
                    continue;

                cell.Occupied = false;
                cell.OccupyingBuildingId = -1;
                cell.OccupyingUnitId = -1;
                cell.OccupyingResourceNodeId = -1;
            }
        }

        staticClearanceField.SetDirty(true);
    }

    /// <summary>
    /// Returns the center of the rectangular footprint in world space.
    ///
    /// The same calculation works for odd, even, square, and
    /// rectangular footprints.
    /// </summary>
    public Vector3 GetFootprintWorldCenter(GridCoord footprintOrigin, Vector2Int footprintSize)
    {
        GridCoord finalCell = new GridCoord(footprintOrigin.x + footprintSize.x - 1, footprintOrigin.z + footprintSize.y - 1);

        Vector3 firstCellWorld = CellToWorld(footprintOrigin);
        Vector3 finalCellWorld = CellToWorld(finalCell);

        return (firstCellWorld + finalCellWorld) * 0.5f;
    }

    // ---------------------------------------------------------------------
    // Grid Clearance 
    // ---------------------------------------------------------------------

    public bool HasNavigationClearance(GridCoord centerCoord, float radius)
    {
        return staticClearanceField.HasNavigationClearance(centerCoord, radius);
    }

    public void RebuildStaticClearanceField()
    {
        staticClearanceField?.RebuildStaticClearanceField();
    }

    public bool IsStaticallyTraversable(GridCoord coord, float navigationRadius)
    {
        if (!IsInside(coord))
            return false;

        GridCell cell = GetCell(coord);

        if (cell == null || !cell.Walkable)
        {
            return false;
        }

        return HasNavigationClearance(coord, navigationRadius);
    }

    public bool IsStaticTransitionTraversable(GridCoord from, GridCoord to, float navigationRadius)
    {
        int deltaX = to.x - from.x;
        int deltaZ = to.z - from.z;

        if (deltaX == 0 && deltaZ == 0)
        {
            return true;
        }

        if (Mathf.Abs(deltaX) > 1 || Mathf.Abs(deltaZ) > 1)
        {
            return false;
        }

        if (!IsStaticallyTraversable(to, navigationRadius))
        {
            return false;
        }

        bool diagonal = deltaX != 0 && deltaZ != 0;

        if (!diagonal)
            return true;

        GridCoord horizontal = new GridCoord(from.x + deltaX, from.z);
        GridCoord vertical = new GridCoord(from.x, from.z + deltaZ);

        return IsStaticallyTraversable(horizontal, navigationRadius) 
            && IsStaticallyTraversable(vertical, navigationRadius);
    }
}