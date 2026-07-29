using UnityEngine;

public class TerrainGridSystem : MonoBehaviour
{
    [Header("Terrain")]
    [SerializeField] private Terrain terrain;

    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 2f;

    [Header("Slope Rules")]
    [SerializeField] private float maxWalkableSlope = 35f;
    [SerializeField] private float maxBuildableSlope = 8f;

    public TerrainGrid Grid { get; private set; }

    public Terrain Terrain => terrain;
    public float CellSize => cellSize;

    //private void Awake()
    //{
    //    if (terrain == null)
    //    {
    //        Debug.LogError("TerrainGridSystem is missing Terrain reference.");
    //        return;
    //    }

    //    Grid = new TerrainGrid(
    //        terrain,
    //        cellSize,
    //        maxWalkableSlope,
    //        maxBuildableSlope
    //    );

    //    Debug.Log($"RTSGrid created: {Grid.Width} x {Grid.Height}, cell size {cellSize}");
    //}

    public void Initialize()
    {
        if (Grid != null)
            return;

        Grid = new TerrainGrid(terrain, cellSize, maxWalkableSlope, maxBuildableSlope);
    }
}
    
