using UnityEngine;

/// <summary>
/// Visual representation of the building footprint currently
/// being considered for placement.
///
/// This object has no gameplay authority. TerrainGrid and
/// BuildingManager remain responsible for validation.
/// </summary>
public sealed class BuildingPlacementPreview : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Transform visual;
    [SerializeField] private Renderer previewRenderer;

    [Header("Materials")]
    [SerializeField] private Material validMaterial;
    [SerializeField] private Material invalidMaterial;

    [Header("Dimensions")]
    [SerializeField] [Min(0.01f)] private float previewHeight = 0.025f;
    [SerializeField] [Min(0f)] private float groundOffset = 0.05f;

    public void Configure(Vector2Int footprintSize, float cellSize)
    {
        if (visual == null)
            return;

        float width = footprintSize.x * cellSize;
        float depth = footprintSize.y * cellSize;

        visual.localScale = new Vector3(width, previewHeight, depth);
        visual.localPosition = new Vector3(0f, previewHeight * 0.5f + groundOffset, 0f);
    }

    public void Show(Vector3 worldPosition, bool isValid)
    {
        transform.position = worldPosition;
        transform.rotation = Quaternion.identity;

        if (previewRenderer != null)
        {
            previewRenderer.sharedMaterial = isValid ? validMaterial : invalidMaterial;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }
}