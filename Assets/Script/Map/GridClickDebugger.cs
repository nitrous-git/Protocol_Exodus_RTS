using UnityEngine;

public class GridClickDebugger : MonoBehaviour
{
    [SerializeField] private TerrainGridSystem gridBootstrap;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private LayerMask terrainLayerMask = ~0;

    [Header("Debug")]
    [SerializeField] private bool debugClick = true;
    [SerializeField] private GameObject markerPrefab;

    private GameObject currentMarker;

    private void Awake()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (!debugClick)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            TryDebugClick();
        }
    }

    private void TryDebugClick()
    {
        if (gridBootstrap == null || gridBootstrap.Grid == null)
        {
            Debug.LogWarning("Grid not ready.");
            return;
        }

        if (mainCamera == null)
        {
            Debug.LogWarning("Missing camera.");
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, terrainLayerMask))
            return;

        TerrainGrid grid = gridBootstrap.Grid;

        GridCoord coord = grid.WorldToCell(hit.point);

        if (!grid.TryGetCell(coord, out GridCell cell))
        {
            Debug.Log($"Clicked outside grid at world position {hit.point}");
            return;
        }

        Debug.Log(
            $"Clicked cell {coord} | " +
            $"World: {cell.WorldCenter} | " +
            $"Height: {cell.Height:F2} | " +
            $"Slope: {cell.Slope:F2} | " +
            $"Walkable: {cell.Walkable} | " +
            $"Buildable: {cell.Buildable} | " +
            $"Occupied: {cell.Occupied} | " +
            $"Reserved: {cell.Reserved}"
        );

        MoveMarker(cell.WorldCenter);
    }

    private void MoveMarker(Vector3 position)
    {
        if (markerPrefab == null)
            return;

        if (currentMarker == null)
        {
            currentMarker = Instantiate(markerPrefab);
        }

        currentMarker.transform.position = position + Vector3.up * 0.2f;
    }
}
