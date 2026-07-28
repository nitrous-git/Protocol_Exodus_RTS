using UnityEngine;

public class FootprintTestDebugger : MonoBehaviour
{
    [SerializeField] private TerrainGridSystem gridBootstrap;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask terrainLayerMask = ~0;

    [Header("Footprint")]
    [SerializeField] private Vector2Int footprintSize = new Vector2Int(3, 3);
    [SerializeField] private float maxHeightDifference = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool drawFootprint = true;
    [SerializeField] private float yOffset = 0.2f;

    private bool hasPreview;
    private GridCoord previewTopLeft;
    private bool previewValid;

    private void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    private void Update()
    {
        UpdatePreviewFromMouse();
    }

    private void UpdatePreviewFromMouse()
    {
        hasPreview = false;

        if (gridBootstrap == null || gridBootstrap.Grid == null)
            return;

        if (mainCamera == null)
            return;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, terrainLayerMask))
            return;

        TerrainGrid grid = gridBootstrap.Grid;

        GridCoord centerCell = grid.WorldToCell(hit.point);
        previewTopLeft = GetTopLeftFromCenter(centerCell, footprintSize);

        previewValid = CanPlaceFootprint(
            grid,
            previewTopLeft,
            footprintSize,
            maxHeightDifference
        );

        hasPreview = true;
    }

    private GridCoord GetTopLeftFromCenter(GridCoord center, Vector2Int size)
    {
        int startX = center.x - size.x / 2;
        int startZ = center.z - size.y / 2;

        return new GridCoord(startX, startZ);
    }

    private bool CanPlaceFootprint(
        TerrainGrid grid,
        GridCoord topLeft,
        Vector2Int size,
        float maxHeightDiff)
    {
        float minHeight = float.MaxValue;
        float maxHeight = float.MinValue;

        for (int z = 0; z < size.y; z++)
        {
            for (int x = 0; x < size.x; x++)
            {
                GridCoord coord = new GridCoord(topLeft.x + x, topLeft.z + z);

                if (!grid.TryGetCell(coord, out GridCell cell))
                    return false;

                if (!cell.IsFreeForBuilding())
                    return false;

                minHeight = Mathf.Min(minHeight, cell.Height);
                maxHeight = Mathf.Max(maxHeight, cell.Height);
            }
        }

        float heightDifference = maxHeight - minHeight;

        return heightDifference <= maxHeightDiff;
    }

    private void OnDrawGizmos()
    {
        if (!drawFootprint)
            return;

        if (!hasPreview)
            return;

        if (gridBootstrap == null || gridBootstrap.Grid == null)
            return;

        TerrainGrid grid = gridBootstrap.Grid;

        Gizmos.color = previewValid
            ? new Color(0f, 1f, 0.2f, 0.9f)
            : new Color(1f, 0f, 0.2f, 0.9f);

        for (int z = 0; z < footprintSize.y; z++)
        {
            for (int x = 0; x < footprintSize.x; x++)
            {
                GridCoord coord = new GridCoord(previewTopLeft.x + x, previewTopLeft.z + z);

                if (!grid.TryGetCell(coord, out GridCell cell))
                    continue;

                Vector3 center = cell.WorldCenter + Vector3.up * yOffset;
                Vector3 size = new Vector3(grid.CellSize, 0.05f, grid.CellSize);

                Gizmos.DrawWireCube(center, size);
            }
        }
    }
}
