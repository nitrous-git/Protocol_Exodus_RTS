using UnityEngine;

/// <summary>
/// Shared runtime representation of every building.
///
/// Specialized behavior is supplied by optional components such as
/// UnitProductionComponent, SupplyProviderComponent, and HeadquartersComponent.
/// </summary>
[RequireComponent(typeof(Health))]
public class BuildingBase : MonoBehaviour, ISelectable, ITargetable
{
    [Header("Selection")]
    [SerializeField] private bool canBeSelected = true;
    [SerializeField] private Transform selectionPoint;

    [Header("Targeting")]
    [SerializeField] private Transform aimPoint;

    protected Faction ownerFaction;
    protected GameContext gameContext;
    protected BuildingManager owningBuildingManager;

    protected Health health;
    protected BuildingView view;
    protected UnitProductionComponent unitProduction;
    protected SupplyProviderComponent supplyProvider;
    protected HeadquartersComponent headquarters;

    private bool removalNotified;

    public Faction OwnerFaction => ownerFaction;
    public BuildingManager OwningBuildingManager => owningBuildingManager;

    public BuildingDefinition Definition { get; private set; }
    public GridCoord FootprintOrigin { get; private set; }
    public int BuildingId => 0; // find a unique ID 
    public bool IsInitialized { get; private set; }

    public BuildingState State { get; private set; }
    public float ConstructionElapsed { get; private set; }

    public bool IsSelected { get; private set; }
    public bool CanBeSelected => canBeSelected;

    public Vector3 Position => transform.position;
    public bool IsAlive => health != null && health.IsAlive;
    public Transform AimPoint => aimPoint != null ? aimPoint : transform;
    public Vector3 SelectionPosition => selectionPoint != null ? selectionPoint.position : transform.position;

    public Health Health => health;
    public UnitProductionComponent UnitProduction => unitProduction;
    public SupplyProviderComponent SupplyProvider => supplyProvider;
    public HeadquartersComponent Headquarters => headquarters;
    public BuildingView View => view;

    public bool IsOperational => State == BuildingState.InOperation;
    public bool IsUnderConstruction => State == BuildingState.UnderConstruction;

    protected virtual void Awake()
    {
        CacheComponents();
    }

    protected virtual void CacheComponents()
    {
        health = GetComponent<Health>();
        view = GetComponent<BuildingView>();

        unitProduction = GetComponent<UnitProductionComponent>();
        supplyProvider = GetComponent<SupplyProviderComponent>();
        headquarters = GetComponent<HeadquartersComponent>();
    }

    public virtual void Initialize(
        BuildingDefinition definition,
        Faction ownerFaction,
        GameContext gameContext,
        BuildingManager owningBuildingManager,
        GridCoord footprintOrigin)
    {
        if (IsInitialized)
            return;

        CacheComponents();

        if (definition == null)
        {
            Debug.LogError(name + " cannot initialize because BuildingDefinition is missing.");
            return;
        }

        if (gameContext == null)
        {
            Debug.LogError(name + " cannot initialize because GameContext is missing.");
            return;
        }

        if (owningBuildingManager == null)
        {
            Debug.LogError(name + " cannot initialize because BuildingManager is missing.");
            return;
        }

        if (health == null)
        {
            Debug.LogError(name + " cannot initialize because Health is missing.");
            return;
        }

        Definition = definition;

        this.ownerFaction = ownerFaction;
        this.gameContext = gameContext;
        this.owningBuildingManager = owningBuildingManager;

        FootprintOrigin = footprintOrigin;

        health.Initialize(definition.maxHealth);
        health.OnDied += HandleDied;

        view?.Initialize(this);
        unitProduction?.Initialize(this);
        supplyProvider?.Initialize(this);
        headquarters?.Initialize(this);

        BeginConstruction();

        IsInitialized = true;

        owningBuildingManager.RegisterBuilding(this);
    }

    public virtual void Tick(float deltaTime)
    {
        if (!IsInitialized || !IsAlive)
            return;

        view?.Tick(deltaTime);

        if (State == BuildingState.UnderConstruction)
        {
            TickConstruction(deltaTime);
            return;
        }

        unitProduction?.Tick(deltaTime);
        supplyProvider?.Tick(deltaTime);
        headquarters?.Tick(deltaTime);
    }

    public virtual void SetSelected(bool selected)
    {
        IsSelected = canBeSelected && selected;
        view?.SetSelected(IsSelected);
    }

    public virtual void TakeDamage(DamageInfo damageInfo)
    {
        health?.ApplyDamage(damageInfo);
    }

    protected virtual void HandleDied()
    {
        SetSelected(false);
        owningBuildingManager?.RequestRemoveBuilding(this);
    }

    protected virtual void OnDestroy()
    {
        if (health != null)
        {
            health.OnDied -= HandleDied;
        }

        owningBuildingManager?.UnregisterBuilding(this);
    }

    /// <summary>
    /// Called once before this building is removed from the match.
    /// </summary>
    public virtual void NotifyRemoved()
    {
        if (removalNotified)
            return;

        removalNotified = true;

        unitProduction?.NotifyBuildingRemoved();
        supplyProvider?.NotifyBuildingRemoved();
        headquarters?.NotifyBuildingRemoved();
    }

    // ---------------------------------------------------------------------
    // Progress
    // ---------------------------------------------------------------------

    public float ConstructionProgress()
    {
        float duration = Definition != null ? Definition.ConstructionDuration : 0f;

        if (duration <= 0f)
            return 1f;

        return Mathf.Clamp01(ConstructionElapsed / duration);

    }

    public float GetConstructionRemainingNormalized()
    {   
        return 1f - ConstructionProgress();
    }

    // ---------------------------------------------------------------------
    // Construction lifecycle
    // ---------------------------------------------------------------------

    private void BeginConstruction()
    {
        State = BuildingState.UnderConstruction;
        ConstructionElapsed = 0f;

        view?.ShowUnderConstruction();
    }

    private void TickConstruction(float deltaTime)
    {
        ConstructionElapsed += deltaTime;

        if (ConstructionElapsed < Definition.ConstructionDuration)
            return;

        CompleteConstruction();
    }

    private void CompleteConstruction()
    {
        if (State == BuildingState.InOperation)
            return;

        ConstructionElapsed = Definition.ConstructionDuration;
        State = BuildingState.InOperation;
        view?.TransitionToOperational();
        supplyProvider?.Activate();
    }

}