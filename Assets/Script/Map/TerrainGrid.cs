using UnityEngine;

public class TerrainGrid
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float CellSize { get; private set; }

    private Terrain terrain;
    private TerrainData terrainData;
    private Vector3 terrainOrigin;
    private Vector3 terrainSize;

    private GridCell[,] cells;

    private float maxWalkableSlope;
    private float maxBuildableSlope;

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
                    OccupyingBuildingId = -1
                };
            }
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    public Vector3 CellToWorld(GridCoord coord)
    {
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

    public void SetOccupied(GridCoord coord, bool occupied, int buildingId = -1, int unitId = -1)
    {
        GridCell cell = GetCell(coord);

        if (cell == null)
            return;

        cell.Occupied = occupied;
        cell.OccupyingBuildingId = occupied ? buildingId : -1;
        cell.OccupyingUnitId = occupied ? unitId : -1;
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
            }
        }
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
            }
        }
    }



}