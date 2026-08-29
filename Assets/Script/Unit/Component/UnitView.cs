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
    [SerializeField] private Animator animator;

    [Header("Components")]
    private MeshRenderer selectionRingRenderer;

    [Header("Faction Visuals")]
    [SerializeField] private Renderer[] factionRenderers;
    [SerializeField] private UnitFactionTextureVariant[] factionTextureVariants;

    private string currentAnimState;
    private string baseAnimState;
    private string oneShotAnimState;
    private bool playingOneShot;

    private UnitBase owner;
    private MaterialPropertyBlock propertyBlock;
    private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    public void Initialize(UnitBase owner)
    {
        this.owner = owner;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        propertyBlock = new();

        selectionRingRenderer = selectionRing.GetComponent<MeshRenderer>();

        SetSelected(false);
        RefreshFactionVisuals();

        if (animator != null)
        {
            animator.applyRootMotion = false;
            currentAnimState = null;
            baseAnimState = null;
            oneShotAnimState = null;
            playingOneShot = false;
        }
    }

    public void TickLate()
    {
        if (owner == null)
            return;

        UpdateOneShotAnimation();

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
        Vector3 ringPosition = new Vector3(unitPosition.x, unitPosition.y, unitPosition.z);
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

    // ---------------------------------------------------------------------
    // Faction Visuals - Materials
    // ---------------------------------------------------------------------

    public void RefreshFactionVisuals()
    {
        if (owner == null || owner.OwnerFaction == null)
            return;

        FactionColorType colorType = owner.OwnerFaction.ColorType;
        Texture texture = GetFactionTexture(colorType);

        // Set Textures
        for (int i = 0; i < factionRenderers.Length; i++)
        {
            Renderer targetRenderer = factionRenderers[i];

            propertyBlock.Clear();
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(BaseMapID, texture);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        // Set Selection Ring
        selectionRingRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorID, owner.OwnerFaction.SelectionRingColor);
        selectionRingRenderer.SetPropertyBlock(propertyBlock);
    }

    private Texture GetFactionTexture(FactionColorType colorType)
    {
        for (int i = 0; i < factionTextureVariants.Length; i++)
        {
            if (factionTextureVariants[i].colorType == colorType)
                return factionTextureVariants[i].baseMap;
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // Animations
    // ---------------------------------------------------------------------

    public void PlayAnim(string newAnimState, bool restart = false)
    {
        if (animator == null)
            return;

        baseAnimState = newAnimState;
        playingOneShot = false;

        if (!restart && currentAnimState == newAnimState)
            return;

        if (restart)
        {
            animator.Play(newAnimState, 0, 0f);
        }
        else
        {
            animator.Play(newAnimState);
        }

        currentAnimState = newAnimState;
    }

    public void PlayOneShotAnim(string newAnimState)
    {
        if (animator == null)
            return;

        playingOneShot = true;
        oneShotAnimState = newAnimState;

        animator.Play(newAnimState, 0, 0f);

        currentAnimState = newAnimState;
    }

    private void UpdateOneShotAnimation()
    {
        if (animator == null || !playingOneShot)
            return;

        AnimatorStateInfo stateInfo =
            animator.GetCurrentAnimatorStateInfo(0);

        if (!stateInfo.IsName(oneShotAnimState))
            return;

        if (stateInfo.normalizedTime < 1f)
            return;

        playingOneShot = false;
        oneShotAnimState = null;
        currentAnimState = null;

        PlayAnim(baseAnimState);
    }

    public void ResetAnimState()
    {
        baseAnimState = null;
        currentAnimState = null;
    }
}