using UnityEngine;

public sealed class BuildingView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ConstructionVisibilityId = Shader.PropertyToID("_ConstructionVisibility");

    [Header("Selection")]
    [SerializeField] private GameObject selectionIndicator;
    //[SerializeField] private Renderer selectionIndicatorRenderer;

    [Header("Building Visuals")]
    [SerializeField] private Transform visualRoot;

    [Header("Construction")]
    [SerializeField, Range(0f, 1f)] private float underConstructionVisibility = 0.45f;
    [SerializeField, Min(0f)] private float completionTransitionDuration = 0.5f;
    [SerializeField] private AnimationCurve completionTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private BuildingBase owner;

    private Renderer[] buildingRenderers;
    private MaterialPropertyBlock propertyBlock;

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

        CacheComponents();

        SetSelected(false);
        SetOperationalImmediate();
        //RefreshFactionVisuals();
    }

    private void CacheComponents()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        if (visualRoot == null)
        {
            Debug.LogError(name + " BuildingView is missing its visual root.");
            buildingRenderers = System.Array.Empty<Renderer>();
        }
        else
        {
            buildingRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        }

        //if (selectionIndicatorRenderer == null && selectionIndicator != null)
        //{
        //    selectionIndicatorRenderer = selectionIndicator.GetComponentInChildren<Renderer>(true);
        //}
    }

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
        currentConstructionVisibility =
            Mathf.Clamp01(visibility);

        for (int i = 0; i < buildingRenderers.Length; i++)
        {
            Renderer buildingRenderer = buildingRenderers[i];

            if (buildingRenderer == null)
                continue;

            GetPropertyBlock(buildingRenderer);

            propertyBlock.SetFloat(ConstructionVisibilityId, currentConstructionVisibility);

            buildingRenderer.SetPropertyBlock(propertyBlock);
        }
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
    // Faction visuals
    // ---------------------------------------------------------------------

    public void RefreshFactionVisuals()
    {
        if (owner == null || owner.OwnerFaction == null)
            return;

        //Color buildingColor = owner.OwnerFaction.FactionColor;

        for (int i = 0; i < buildingRenderers.Length; i++)
        {
            Renderer buildingRenderer = buildingRenderers[i];

            if (buildingRenderer == null)
                continue;

            GetPropertyBlock(buildingRenderer);
            //propertyBlock.SetColor(BaseColorId, buildingColor);
            buildingRenderer.SetPropertyBlock(propertyBlock);
        }

        //if (selectionIndicatorRenderer != null)
        //{
        //    GetPropertyBlock(selectionIndicatorRenderer);
        //    propertyBlock.SetColor(BaseColorId, owner.OwnerFaction.SelectionRingColor);
        //    selectionIndicatorRenderer.SetPropertyBlock(propertyBlock);
        //}
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