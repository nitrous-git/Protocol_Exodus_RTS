using UnityEngine;

public class RTSGrid
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float CellSize { get; private set; }

    private Terrain terrain;
    private TerrainData terrainData;
    private Vector3 terrainOrigin;
    private Vector3 terrainSize;

    private RTSGridCell[,] cells;

    private float maxWalkableSlope;
    private float maxBuildableSlope;

    public RTSGrid(Terrain terrain, float cellSize, float maxWalkableSlope, float maxBuildableSlope)
    {
        if (terrain == null)
        {
            Debug.LogError("RTSGrid cannot be created because Terrain is null.");
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

        cells = new RTSGridCell[Width, Height];

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

                cells[x, z] = new RTSGridCell
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
        return coord.x >= 0 &&
               coord.z >= 0 &&
               coord.x < Width &&
               coord.z < Height;
    }

    public RTSGridCell GetCell(GridCoord coord)
    {
        return cells[coord.x, coord.z];
    }

    public bool TryGetCell(GridCoord coord, out RTSGridCell cell)
    {
        if (!IsInside(coord))
        {
            cell = default;
            return false;
        }

        cell = cells[coord.x, coord.z];
        return true;
    }

    public bool IsWalkable(GridCoord coord)
    {
        if (!TryGetCell(coord, out RTSGridCell cell))
            return false;

        return cell.IsFreeForMovement();
    }

    public bool IsBuildable(GridCoord coord)
    {
        if (!TryGetCell(coord, out RTSGridCell cell))
            return false;

        return cell.IsFreeForBuilding();
    }

    public void SetOccupied(GridCoord coord, bool occupied, int buildingId = -1, int unitId = -1)
    {
        if (!IsInside(coord))
            return;

        RTSGridCell cell = cells[coord.x, coord.z];

        cell.Occupied = occupied;

        cell.OccupyingBuildingId = occupied ? buildingId : -1;
        cell.OccupyingUnitId = occupied ? unitId : -1;

        cells[coord.x, coord.z] = cell;
    }

    public void SetReserved(GridCoord coord, bool reserved)
    {
        if (!IsInside(coord))
            return;

        RTSGridCell cell = cells[coord.x, coord.z];
        cell.Reserved = reserved;

        cells[coord.x, coord.z] = cell;
    }
}