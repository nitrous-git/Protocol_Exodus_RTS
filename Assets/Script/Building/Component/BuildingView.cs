using UnityEngine;
using UnityEngine.Rendering;

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
    private ParticleSystem[] selectionParticles;
    private Color[] selectionParticleColors;
    private ParticleSystem selectionEffect;

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
        CacheSelectionIndicator();

        SetSelected(false);
        RefreshFactionVisuals();
        //SetOperationalImmediate();
    }

    private void CacheSelectionIndicator()
    {
        if (selectionIndicator == null)
            return;

        selectionIndicatorRenderer = selectionIndicator.GetComponent<MeshRenderer>();
        selectionParticles = selectionIndicator.GetComponentsInChildren<ParticleSystem>(true);
        if (selectionParticles.Length == 0)
            return;

        // The marker's root particle system starts and stops the whole effect.
        selectionEffect = selectionParticles[0];
        selectionParticleColors = new Color[selectionParticles.Length];
        for (int i = 0; i < selectionParticles.Length; i++)
            selectionParticleColors[i] = selectionParticles[i].main.startColor.color;
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

        ApplyConstructionVisibility(visibility, false);

        if (normalizedTime >= 1f)
        {
            isTransitioningToOperational = false;
            ApplyConstructionVisibility(1f, true);
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

        ApplyConstructionVisibility(underConstructionVisibility, false);
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

        ApplyConstructionVisibility(1f, true);
    }

    private void ApplyConstructionVisibility(float visibility, bool setOpaque)
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

            if (setOpaque)
            {
                Material mat = buildingRenderer.material;
                SetMaterialOpaque(mat);
            }
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

        if (texture != null)
        {
            for (int i = 0; i < buildingRenderers.Length; i++)
            {
                Renderer targetRenderer = buildingRenderers[i];
                GetPropertyBlock(targetRenderer);
                propertyBlock.SetTexture(BaseMapID, texture);
                targetRenderer.SetPropertyBlock(propertyBlock);
            }
        }

        ApplySelectionColor(owner.OwnerFaction.SelectionRingColor);
    }

    private void ApplySelectionColor(Color factionColor)
    {
        if (selectionIndicatorRenderer != null)
        {
            GetPropertyBlock(selectionIndicatorRenderer);
            propertyBlock.SetColor(BaseColorID, factionColor);
            selectionIndicatorRenderer.SetPropertyBlock(propertyBlock);
        }

        if (selectionEffect == null)
            return;

        Color.RGBToHSV(factionColor, out float hue, out float saturation, out _);
        for (int i = 0; i < selectionParticles.Length; i++)
        {
            Color authoredColor = selectionParticleColors[i];
            Color.RGBToHSV(authoredColor, out _, out float authoredSaturation, out float brightness);
            Color tint = Color.HSVToRGB(hue, Mathf.Min(saturation, authoredSaturation), brightness);
            tint.a = authoredColor.a;
            var main = selectionParticles[i].main;
            main.startColor = tint;
        }

        // Live particles keep the start color they had when emitted.
        if (selectionIndicator.activeInHierarchy)
        {
            selectionEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            selectionEffect.Play(true);
        }
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
        if (selectionIndicator == null || selectionIndicator.activeSelf == selected)
            return;

        if (!selected && selectionEffect != null)
            selectionEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        selectionIndicator.SetActive(selected);

        if (selected && selectionEffect != null)
            selectionEffect.Play(true);
    }

    // ---------------------------------------------------------------------
    // Material property block
    // ---------------------------------------------------------------------

    private void GetPropertyBlock(Renderer targetRenderer)
    {
        propertyBlock.Clear();
        targetRenderer.GetPropertyBlock(propertyBlock);
    }

    // ---------------------------------------------------------------------
    // Material Surface type methods
    // ---------------------------------------------------------------------

    public void SetMaterialOpaque(Material mat)
    {
        if (mat == null)
            return;

        // set surface type to opaque
        mat.SetFloat("_Surface", 0f);

        // Opaque blending
        mat.SetInt("_SrcBlend", (int)BlendMode.One);
        mat.SetInt("_DstBlend", (int)BlendMode.Zero);

        if (mat.HasProperty("_SrcBlendAlpha"))
            mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);

        if (mat.HasProperty("_DstBlendAlpha"))
            mat.SetInt("_DstBlendAlpha", (int)BlendMode.Zero);

        // Enable writing to depth buffer
        mat.SetInt("_ZWrite", 1);

        // URP surface keyword
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

        // Transparency-related keywords
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");

        // We do not use alpha clipping
        mat.DisableKeyword("_ALPHATEST_ON");

        // Opaque render queue
        mat.renderQueue = (int)RenderQueue.Geometry;
    }

}
