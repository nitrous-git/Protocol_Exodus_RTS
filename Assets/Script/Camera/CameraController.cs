using UnityEngine;

[DisallowMultipleComponent]
public sealed class CameraController : MonoBehaviour
{
    [Header("Map Bounds")]
    [SerializeField] private Terrain terrain;

    [Tooltip("Distance kept between the CameraRoot and the Terrain border.")]
    [SerializeField, Min(0f)] private float borderBuffer = 25f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float movementSpeed = 30f;

    private Vector2 movementInput;

    private float minimumX;
    private float maximumX;
    private float minimumZ;
    private float maximumZ;

    private bool hasValidMovementBounds;
    private bool isInitialized;

    public Terrain Terrain => terrain;
    public float MovementSpeed => movementSpeed;
    public float BorderBuffer => borderBuffer;

    public void Initialize()
    {
        if (terrain == null)
            terrain = Terrain.activeTerrain;

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("CameraController cannot initialize because no valid Terrain is assigned.", this);
            return;
        }

        if (!RecalculateMovementBounds())
            return;

        isInitialized = true;
        movementInput = Vector2.zero;

        // Ensure the initial scene position is also inside the map.
        SetGroundPosition(transform.position);
    }


    public void TickLate(float deltaTime)
    {
        if (!isInitialized || !isActiveAndEnabled)
            return;

        Move(movementInput, deltaTime);
    }

    /// <summary>
    /// Moves the CameraRoot in world-space XZ coordinates.
    /// The Y position is never modified.
    /// </summary>
    public void Move(Vector2 direction, float deltaTime)
    {
        if (!hasValidMovementBounds)
            return;

        if (deltaTime <= 0f || direction.sqrMagnitude <= 0f)
            return;

        direction = Vector2.ClampMagnitude(direction, 1f);
        Vector3 displacement = new Vector3(direction.x, 0f, direction.y);
        displacement *= movementSpeed * deltaTime;

        SetGroundPosition(transform.position + displacement);
    }

    /// <summary>
    /// Places the CameraRoot at an XZ world position while preserving its
    /// existing height and enforcing the Terrain movement bounds.
    ///
    /// The minimap can use this method later.
    /// </summary>
    public void SetGroundPosition(Vector3 worldPosition)
    {
        if (!hasValidMovementBounds)
            return;

        Vector3 currentPosition = transform.position;

        worldPosition.x = Mathf.Clamp(worldPosition.x, minimumX, maximumX);
        worldPosition.y = currentPosition.y;
        worldPosition.z = Mathf.Clamp(worldPosition.z, minimumZ, maximumZ);

        transform.position = worldPosition;
    }


    public bool RecalculateMovementBounds()
    {
        if (terrain == null || terrain.terrainData == null)
        {
            hasValidMovementBounds = false;
            return false;
        }

        Vector3 terrainOrigin = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;

        float terrainMinimumX = terrainOrigin.x;
        float terrainMaximumX = terrainOrigin.x + terrainSize.x;
        float terrainMinimumZ = terrainOrigin.z;
        float terrainMaximumZ = terrainOrigin.z + terrainSize.z;

        ResolveInsetBounds(
            terrainMinimumX,
            terrainMaximumX,
            borderBuffer,
            out minimumX,
            out maximumX);

        ResolveInsetBounds(
            terrainMinimumZ,
            terrainMaximumZ,
            borderBuffer,
            out minimumZ,
            out maximumZ);

        hasValidMovementBounds = true;
        return true;
    }

   
    private static void ResolveInsetBounds(
        float terrainMinimum,
        float terrainMaximum,
        float inset,
        out float allowedMinimum,
        out float allowedMaximum)
    {
        allowedMinimum = terrainMinimum + inset;
        allowedMaximum = terrainMaximum - inset;

        // Prevent invalid clamping when the buffer is larger than
        // half of the Terrain dimension.
        if (allowedMinimum <= allowedMaximum)
            return;

        float center = (terrainMinimum + terrainMaximum) * 0.5f;

        allowedMinimum = center;
        allowedMaximum = center;
    }

    private void OnDisable()
    {
        ClearMovementInput();
    }

    private void OnValidate()
    {
        movementSpeed = Mathf.Max(0f, movementSpeed);
        borderBuffer = Mathf.Max(0f, borderBuffer);
    }

    /// <summary>
    /// Supplies world-space XZ camera movement intent.
    ///
    /// The input source is owned by the player controller. CameraController
    /// only stores and executes the requested movement.
    /// </summary>
    public void SetMovementInput(Vector2 input)
    {
        if (!isInitialized || !isActiveAndEnabled)
        {
            movementInput = Vector2.zero;
            return;
        }

        movementInput = Vector2.ClampMagnitude(input, 1f);
    }

    public void ClearMovementInput()
    {
        movementInput = Vector2.zero;
    }


    // Gizmos 
    private void OnDrawGizmosSelected()
    {
        Terrain targetTerrain = terrain;

        if (targetTerrain == null || targetTerrain.terrainData == null)
            return;

        Vector3 terrainOrigin = targetTerrain.transform.position;
        Vector3 terrainSize = targetTerrain.terrainData.size;

        ResolveInsetBounds(
            terrainOrigin.x,
            terrainOrigin.x + terrainSize.x,
            borderBuffer,
            out float gizmoMinimumX,
            out float gizmoMaximumX);

        ResolveInsetBounds(
            terrainOrigin.z,
            terrainOrigin.z + terrainSize.z,
            borderBuffer,
            out float gizmoMinimumZ,
            out float gizmoMaximumZ);

        Vector3 center = new Vector3(
            (gizmoMinimumX + gizmoMaximumX) * 0.5f,
            transform.position.y,
            (gizmoMinimumZ + gizmoMaximumZ) * 0.5f);

        Vector3 size = new Vector3(
            gizmoMaximumX - gizmoMinimumX,
            0.1f,
            gizmoMaximumZ - gizmoMinimumZ);

        Gizmos.DrawWireCube(center, size);
    }
}