using UnityEngine;

public sealed class BuildingView : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private GameObject selectionIndicator;

    [Header("Building Visuals")]
    [SerializeField] private Transform visualRoot;

    [Header("Construction")]
    [SerializeField, Range(0f, 1f)] private float underConstructionVisibility = 0.45f;
    [SerializeField, Min(0f)] private float completionTransitionDuration = 0.5f;
    [SerializeField] private AnimationCurve completionTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Building Visuals")]
    [SerializeField] private Renderer[] buildingRenderers;
    [SerializeField] private UnitFactionTextureVariant[] factionTextureVariants;

    private BuildingBase owner;

    private MeshRenderer selectionIndicatorRenderer;

    private MaterialPropertyBlock propertyBlock;
    private static readonly int BaseMapID = Shader.PropertyToID("_BaseMap");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ConstructionVisibilityID = Shader.PropertyToID("_ConstructionVisibility");

    private float currentConstructionVisibility = 1f;
    private float transitionStartVisibility;
    private float transitionElapsed;

    private bool isTransitioningToOperational;

    // ---------------------------------------------------------------------
    // Initialization
    // ---------------------------------------------------------------------

    public void Initialize(BuildingBase owner)
    {
        this.owner = owner;

        propertyBlock = new();
        selectionIndicatorRenderer = selectionIndicator.GetComponent<MeshRenderer>();

        SetSelected(false);
        RefreshFactionVisuals();
        //SetOperationalImmediate();
    }

    //private void CacheComponents()
    //{
    //    if (propertyBlock == null)
    //    {
    //        propertyBlock = new MaterialPropertyBlock();
    //    }

    //    if (visualRoot == null)
    //    {
    //        Debug.LogError(name + " BuildingView is missing its visual root.");
    //        buildingRenderers = System.Array.Empty<Renderer>();
    //    }
    //    else
    //    {
    //        buildingRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
    //    }
    //}

    // ---------------------------------------------------------------------
    // Centralized tick
    // ---------------------------------------------------------------------

    public void Tick(float deltaTime)
    {
        if (!isTransitioningToOperational)
            return;

        TickOperationalTransition(deltaTime);
    }

    private void TickOperationalTransition(float deltaTime)
    {
        if (completionTransitionDuration <= 0f)
        {
            SetOperationalImmediate();
            return;
        }

        transitionElapsed += Mathf.Max(0f, deltaTime);

        float normalizedTime = Mathf.Clamp01(transitionElapsed / completionTransitionDuration);

        float curvedTime = completionTransitionCurve != null ? completionTransitionCurve.Evaluate(normalizedTime) : Mathf.SmoothStep(0f, 1f, normalizedTime);

        curvedTime = Mathf.Clamp01(curvedTime);

        float visibility = Mathf.Lerp(transitionStartVisibility, 1f, curvedTime);

        ApplyConstructionVisibility(visibility);

        if (normalizedTime >= 1f)
        {
            isTransitioningToOperational = false;
            ApplyConstructionVisibility(1f);
        }
    }

    // ---------------------------------------------------------------------
    // Construction visuals
    // ---------------------------------------------------------------------

    /// <summary>
    /// Immediately displays the building in its under-construction state.
    /// </summary>
    public void ShowUnderConstruction()
    {
        isTransitioningToOperational = false;
        transitionElapsed = 0f;

        ApplyConstructionVisibility(underConstructionVisibility);
    }

    /// <summary>
    /// Begins the short visual transition from the current construction
    /// visibility to the fully operational appearance.
    ///
    /// BuildingBase can already be in the InOperation gameplay state while
    /// this purely visual transition finishes.
    /// </summary>
    public void TransitionToOperational()
    {
        if (currentConstructionVisibility >= 1f || completionTransitionDuration <= 0f)
        {
            SetOperationalImmediate();
            return;
        }

        transitionStartVisibility = currentConstructionVisibility;

        transitionElapsed = 0f;
        isTransitioningToOperational = true;
    }

    /// <summary>
    /// Immediately displays the fully operational appearance.
    /// Useful during initialization, loading, or object reuse.
    /// </summary>
    public void SetOperationalImmediate()
    {
        isTransitioningToOperational = false;
        transitionElapsed = 0f;

        ApplyConstructionVisibility(1f);
    }

    private void ApplyConstructionVisibility(float visibility)
    {
        currentConstructionVisibility = Mathf.Clamp01(visibility);

        for (int i = 0; i < buildingRenderers.Length; i++)
        {
            Renderer buildingRenderer = buildingRenderers[i];

            if (buildingRenderer == null)
                continue;

            GetPropertyBlock(buildingRenderer);

            propertyBlock.SetFloat(ConstructionVisibilityID, currentConstructionVisibility);

            buildingRenderer.SetPropertyBlock(propertyBlock);
        }
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
        for (int i = 0; i < buildingRenderers.Length; i++)
        {
            Renderer targetRenderer = buildingRenderers[i];

            propertyBlock.Clear();
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture(BaseMapID, texture);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }

        // Set Selection Ring
        selectionIndicatorRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorID, owner.OwnerFaction.SelectionRingColor);
        selectionIndicatorRenderer.SetPropertyBlock(propertyBlock);
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
    // Selection
    // ---------------------------------------------------------------------

    public void SetSelected(bool selected)
    {
        if (selectionIndicator != null)
            selectionIndicator.SetActive(selected);
    }

    // ---------------------------------------------------------------------
    // Material property block
    // ---------------------------------------------------------------------

    private void GetPropertyBlock(Renderer targetRenderer)
    {
        propertyBlock.Clear();
        targetRenderer.GetPropertyBlock(propertyBlock);
    }
}