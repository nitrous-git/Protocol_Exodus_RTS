using UnityEngine;

public class UnitView : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private GameObject selectionRing;
    [SerializeField] private bool projectSelectionRingToGround = true;
    [SerializeField] private float selectionRingHeightOffset = 0.03f;
    [SerializeField] private float selectionRingRotationSpeed = 20f;
    [SerializeField] private Terrain terrain;

    [Header("Animation")]
    //[SerializeField] private Animator animator;

    private UnitBase owner;

    [Header("Components")]
    private MeshRenderer cylinderRenderer;
    private MeshRenderer selectionRingRenderer;

    private MaterialPropertyBlock propertyBlock;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    public void Initialize(UnitBase owner)
    {
        this.owner = owner;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        propertyBlock = new();
        if (cylinderRenderer == null)
        {
            cylinderRenderer = this.transform.GetChild(0).GetComponent<MeshRenderer>();
            selectionRingRenderer = selectionRing.GetComponent<MeshRenderer>();
        }

        SetSelected(false);
        RefreshFactionVisuals();
        //PlayIdle();
    }

    public void TickLate()
    {
        if (owner == null)
            return;

        if (selectionRing == null)
            return;

        if (!selectionRing.activeSelf)
            return;

        if (projectSelectionRingToGround)
            UpdateSelectionRingGroundProjection();
    }

    public void SetSelected(bool selected)
    {
        if (selectionRing != null)
        {
            selectionRing.SetActive(selected);

            if (selected && projectSelectionRingToGround)
                UpdateSelectionRingGroundProjection();
        }
    }

    private void UpdateSelectionRingGroundProjection()
    {
        Vector3 unitPosition = owner.Position;

        Vector3 ringPosition = new Vector3(
            unitPosition.x,
            unitPosition.y,
            unitPosition.z
        );

        Vector3 groundNormal = Vector3.up;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        if (terrain != null && terrain.terrainData != null)
        {
            ringPosition.y = terrain.SampleHeight(ringPosition)
                + terrain.transform.position.y
                + selectionRingHeightOffset;

            groundNormal = GetTerrainNormal(ringPosition);
        }
        else
        {
            ringPosition.y += selectionRingHeightOffset;
        }

        selectionRing.transform.position = ringPosition;

        Quaternion targetRotation = Quaternion.FromToRotation(Vector3.up, groundNormal);

        selectionRing.transform.rotation = Quaternion.Slerp(
            selectionRing.transform.rotation,
            targetRotation,
            selectionRingRotationSpeed * Time.deltaTime
        );
    }

    private Vector3 GetTerrainNormal(Vector3 worldPosition)
    {
        if (terrain == null || terrain.terrainData == null)
            return Vector3.up;

        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPosition = terrain.transform.position;

        float normalizedX = (worldPosition.x - terrainPosition.x) / terrainData.size.x;
        float normalizedZ = (worldPosition.z - terrainPosition.z) / terrainData.size.z;

        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedZ = Mathf.Clamp01(normalizedZ);

        return terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
    }

    //public void PlayIdle()
    //{
    //    if (animator != null)
    //        animator.Play("Idle");
    //}

    //public void PlayMove()
    //{
    //    if (animator != null)
    //        animator.Play("Move");
    //}

    public void RefreshFactionVisuals()
    {
        if (owner == null)
            return;

        cylinderRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorID, owner.OwnerFaction.FactionColor);
        cylinderRenderer.SetPropertyBlock(propertyBlock);

        selectionRingRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorID, owner.OwnerFaction.SelectionRingColor);
        selectionRingRenderer.SetPropertyBlock(propertyBlock);
    }
}