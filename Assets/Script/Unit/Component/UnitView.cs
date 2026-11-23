using UnityEngine;

public class UnitView : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private GameObject selectionRing;
    [SerializeField] private ParticleSystem selectionRingCylinder;
    [SerializeField] private ParticleSystem selectionRingIndicator;
    [SerializeField] private bool projectSelectionRingToGround = true;
    [SerializeField] private float selectionRingHeightOffset = 0.03f;
    [SerializeField] private float selectionRingRotationSpeed = 20f;
    [SerializeField] private Terrain terrain;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    [Header("Components")]
    private MeshRenderer selectionRingRenderer;
    private Color authoredSelectionCylinderColor;
    private Color authoredSelectionIndicatorColor;

    [Header("Faction Visuals")]
    [SerializeField] private Renderer[] factionRenderers;
    [SerializeField] private UnitFactionTextureVariant[] factionTextureVariants;

    [Header("Marker")]
    [SerializeField] private Transform attackTargetMarkerAnchor;
    [SerializeField] private GameObject attackTargetMarkerPrefab;
    [SerializeField, Min(0f)] private float attackTargetMarkerDuration = 1.8f;

    private GameObject activeAttackTargetMarker;

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

        if (projectSelectionRingToGround && terrain == null)
            terrain = Terrain.activeTerrain;

        propertyBlock = new();

        selectionRingRenderer = selectionRing != null ? selectionRing.GetComponent<MeshRenderer>() : null;
        if (selectionRingCylinder != null)
            authoredSelectionCylinderColor = selectionRingCylinder.main.startColor.color;
        if (selectionRingIndicator != null)
            authoredSelectionIndicatorColor = selectionRingIndicator.main.startColor.color;

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
        CheckFlameAttackAnimation();

        if (selectionRing == null)
            return;

        if (!selectionRing.activeSelf)
            return;

        if (projectSelectionRingToGround)
            UpdateSelectionRingGroundProjection();
    }

    public void SetSelected(bool selected)
    {
        if (selectionRing == null)
            return;

        bool wasSelected = selectionRing.activeSelf;
        selectionRing.SetActive(selected);
        if (!selected)
            return;

        if (projectSelectionRingToGround)
            UpdateSelectionRingGroundProjection();

        if (!wasSelected)
        {
            ShowSelectionParticle(selectionRingCylinder);
            ShowSelectionParticle(selectionRingIndicator);
        }
    }

    private static void ShowSelectionParticle(ParticleSystem particles)
    {
        // Simulate the initial burst and leave it paused so the marker lasts until deselection.
        if (particles != null)
            particles.Simulate(0.05f, withChildren: false);
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

        if (texture != null)
        {
            for (int i = 0; i < factionRenderers.Length; i++)
            {
                Renderer targetRenderer = factionRenderers[i];

                propertyBlock.Clear();
                targetRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetTexture(BaseMapID, texture);
                targetRenderer.SetPropertyBlock(propertyBlock);
            }
        }

        Color ringColor = owner.OwnerFaction.SelectionRingColor;
        if (selectionRingRenderer != null)
        {
            selectionRingRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorID, ringColor);
            selectionRingRenderer.SetPropertyBlock(propertyBlock);
        }

        SetSelectionParticleColor(selectionRingCylinder, authoredSelectionCylinderColor, ringColor, colorType);
        SetSelectionParticleColor(selectionRingIndicator, authoredSelectionIndicatorColor, ringColor, colorType);
    }

    private static void SetSelectionParticleColor(
        ParticleSystem particles, Color authoredColor, Color factionColor, FactionColorType colorType)
    {
        if (particles == null)
            return;

        Color.RGBToHSV(factionColor, out float hue, out float saturation, out _);
        Color.RGBToHSV(authoredColor, out float authoredHue, out float authoredSaturation, out float brightness);

        // Map palette blue around the marker's authored cyan while keeping other faction hues.
        if (colorType == FactionColorType.Blue)
            hue = Mathf.Repeat(authoredHue + hue - 2f / 3f, 1f);

        Color tint = Color.HSVToRGB(hue, Mathf.Min(saturation, authoredSaturation), brightness);
        tint.a = authoredColor.a;
        var main = particles.main;
        main.startColor = tint;

        // The burst's existing particle keeps its original start color.
        if (particles.gameObject.activeInHierarchy)
            ShowSelectionParticle(particles);
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

    internal void SyncLocomotion(UnitMotor motor)
    {
        if (animator == null || playingOneShot || owner is CombatUnit { IsPlayingAttackAnimation: true })
            return;

        bool isMoving = motor != null && motor.CurrentVelocity.sqrMagnitude > 0f;
        PlayAnim(isMoving ? "Walk" : "Idle");
    }

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

    private void CheckFlameAttackAnimation()
    {
        if (owner is not CombatUnit { IsPlayingAttackAnimation: true, Weapon: { UsesFlameCone: true } } combat)
            return;

        AnimatorStateInfo state = animator != null ? animator.GetCurrentAnimatorStateInfo(0) : default;
        if (animator == null || !animator.isActiveAndEnabled || !state.IsName("Attack") || state.normalizedTime >= 1f)
            combat.CancelAttackAnimation();
    }

    public void ResetAnimState()
    {
        baseAnimState = null;
        currentAnimState = null;
    }

    // ---------------------------------------------------------------------
    // Target Marker
    // ---------------------------------------------------------------------

    public void PlayAttackTargetMarker()
    {
        if (attackTargetMarkerPrefab == null || attackTargetMarkerAnchor == null)
        {
            return;
        }

        if (activeAttackTargetMarker != null)
        {
            Object.Destroy(activeAttackTargetMarker);
        }

        activeAttackTargetMarker = Object.Instantiate(attackTargetMarkerPrefab, attackTargetMarkerAnchor);
        activeAttackTargetMarker.transform.localPosition = Vector3.zero;
        activeAttackTargetMarker.transform.localRotation = Quaternion.identity;
        activeAttackTargetMarker.GetComponent<ParticleSystem>().Play();
        Object.Destroy(activeAttackTargetMarker, attackTargetMarkerDuration);
    }

}
