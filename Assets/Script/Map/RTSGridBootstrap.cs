using UnityEngine;

public class RTSGridBootstrap : MonoBehaviour
{
    [Header("Terrain")]
    [SerializeField] private Terrain terrain;

    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 2f;

    [Header("Slope Rules")]
    [SerializeField] private float maxWalkableSlope = 35f;
    [SerializeField] private float maxBuildableSlope = 8f;

    public RTSGrid Grid { get; private set; }

    public Terrain Terrain => terrain;
    public float CellSize => cellSize;

    private void Awake()
    {
        if (terrain == null)
        {
            Debug.LogError("RTSGridBootstrap is missing Terrain reference.");
            return;
        }

        Grid = new RTSGrid(
            terrain,
            cellSize,
            maxWalkableSlope,
            maxBuildableSlope
        );

        Debug.Log($"RTSGrid created: {Grid.Width} x {Grid.Height}, cell size {cellSize}");
    }
}
    
