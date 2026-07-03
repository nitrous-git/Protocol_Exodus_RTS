using UnityEngine;

public class RTSGridDebugRenderer : MonoBehaviour
{
    [SerializeField] private RTSGridBootstrap gridBootstrap;

    [Header("Debug Draw")]
    [SerializeField] private bool drawGrid = true;
    [SerializeField] private bool drawOnlyNearCamera = true;
    [SerializeField] private float drawRadius = 80f;
    [SerializeField] private float yOffset = 0.08f;

    [Header("Cell Colors")]
    [SerializeField] private Color buildableColor = new Color(0f, 1f, 0f, 0.35f);
    [SerializeField] private Color walkableColor = new Color(1f, 1f, 0f, 0.35f);
    [SerializeField] private Color blockedColor = new Color(1f, 0f, 0f, 0.35f);
    [SerializeField] private Color occupiedColor = new Color(0f, 0.25f, 1f, 0.45f);
    [SerializeField] private Color reservedColor = new Color(0f, 1f, 1f, 0.45f);

    private void OnDrawGizmos()
    {
        if (!drawGrid)
            return;

        if (gridBootstrap == null)
            return;

        RTSGrid grid = gridBootstrap.Grid;

        if (grid == null)
            return;

        Vector3 cameraPosition = Vector3.zero;
        bool hasCamera = Camera.current != null;

        if (hasCamera)
        {
            cameraPosition = Camera.current.transform.position;
        }

        for (int z = 0; z < grid.Height; z++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                GridCoord coord = new GridCoord(x, z);
                RTSGridCell cell = grid.GetCell(coord);

                if (drawOnlyNearCamera && hasCamera)
                {
                    float distance = Vector3.Distance(cameraPosition, cell.WorldCenter);

                    if (distance > drawRadius)
                        continue;
                }

                Gizmos.color = GetCellColor(cell);

                Vector3 center = cell.WorldCenter + Vector3.up * yOffset;
                Vector3 size = new Vector3(grid.CellSize, 0.01f, grid.CellSize);

                Gizmos.DrawWireCube(center, size);
            }
        }
    }

    private Color GetCellColor(RTSGridCell cell)
    {
        if (cell.Occupied)
            return occupiedColor;

        if (cell.Reserved)
            return reservedColor;

        if (cell.Buildable)
            return buildableColor;

        if (cell.Walkable)
            return walkableColor;

        return blockedColor;
    }
}
