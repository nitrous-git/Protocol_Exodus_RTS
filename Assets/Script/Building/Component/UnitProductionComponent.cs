using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Generic FIFO production queue for buildings.
///
/// The component queues UnitDefinition assets, not instantiated units.
/// A unit is instantiated only when its front production order completes
/// and a valid spawn cell is available.
/// </summary>
public sealed class UnitProductionComponent : MonoBehaviour
{
    public const int MaximumQueueSize = 5;

    [Header("Production Options")]
    [SerializeField] private List<UnitType> producibleUnitTypes = new();

    [Header("Spawn and Rally Points")]
    [FormerlySerializedAs("SpawnPoint")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform defaultRallyPoint;

    [Header("Spawn Search")]
    [SerializeField, Min(1)] private int initialSpawnDepth = 1;
    [SerializeField, Min(0)] private int maximumExtraSpawnDepth = 4;
    [SerializeField, Min(0)] private int spawnOpennessRadius = 2;
    [SerializeField, Min(0)] private int spawnOpennessWeight = 1;
    [SerializeField, Min(0)] private int spawnDistanceWeight = 2;

    [Header("Physical Clearance")]
    [SerializeField] private LayerMask spawnBlockingMask;
    [SerializeField, Min(0.01f)] private float spawnClearanceRadius = 0.4f;
    [SerializeField] private float spawnHeightOffset = 0f;

    private readonly List<UnitDefinition> productionQueue = new();

    private BuildingBase building;
    private float productionElapsed;
    private bool removalHandled;

    private readonly List<UnitDefinition> producibleUnits = new();

    // ---------------------------------------------------------------------
    // Public state
    // ---------------------------------------------------------------------

    public BuildingBase Building => building;
    public Vector3? RallyPoint { get; private set; }
    public Vector3? EffectiveRallyPoint => RallyPoint ?? (defaultRallyPoint != null ? defaultRallyPoint.position : null);
    public Vector3 RallyPointNormal { get; private set; } = Vector3.up;

    internal void SetRallyPoint(Vector3 position, Vector3 normal)
    {
        RallyPoint = position;
        RallyPointNormal = normal;
    }

    internal void ClearRallyPoint()
    {
        RallyPoint = null;
        RallyPointNormal = Vector3.up;
    }

    public IReadOnlyList<UnitDefinition> ProducibleUnits => producibleUnits;
    public IReadOnlyList<UnitDefinition> ProductionQueue => productionQueue;
    public int QueueCount => productionQueue.Count;
    public int QueueCapacity => MaximumQueueSize;

    public bool IsQueueEmpty => productionQueue.Count == 0;
    public bool IsQueueFull => productionQueue.Count >= MaximumQueueSize;
    public bool IsProducing => productionQueue.Count > 0;

    public UnitDefinition ActiveDefinition => productionQueue.Count > 0 ? productionQueue[0] : null;

    public float ProductionElapsed => productionElapsed;

    public float ProductionProgress()
    {
        UnitDefinition activeDefinition = ActiveDefinition;

        if (activeDefinition == null)
            return 0f;

        float duration = activeDefinition.ProductionDuration;

        if (duration <= 0f)
            return 1f;

        return Mathf.Clamp01(
            productionElapsed / duration);
    }

    public float ProductionRemainingNormalized => IsProducing ? 1f - ProductionProgress() : 0f;

    /// <summary>
    /// Production is complete, but no valid spawn cell currently exists.
    /// </summary>
    public bool IsWaitingForSpawn => IsProducing && ProductionProgress() >= 1f;

    // ---------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------

    public void Initialize(BuildingBase building)
    {
        if (building == null)
        {
            Debug.LogError($"{name} cannot initialize production because BuildingBase is missing.");
            return;
        }

        this.building = building;
        ClearRallyPoint();

        ResolveProducibleUnits();

        productionElapsed = 0f;
        removalHandled = false;
    }

    public void Tick(float deltaTime)
    {
        if (!CanTickProduction())
            return;

        UnitDefinition activeDefinition = ActiveDefinition;

        if (activeDefinition == null)
        {
            RemoveInvalidFrontOrder();
            return;
        }

        float duration = Mathf.Max(0f, activeDefinition.ProductionDuration);

        if (productionElapsed < duration)
        {
            productionElapsed = Mathf.Min(duration, productionElapsed + Mathf.Max(0f, deltaTime));
        }

        if (productionElapsed < duration)
            return;

        // When spawning fails, the completed order remains at the front.
        // The component retries on the next building tick.
        TrySpawnActiveOrder();
    }

    public void NotifyBuildingRemoved()
    {
        if (removalHandled)
            return;

        removalHandled = true;
        ClearRallyPoint();

        // Production is lost when the building is destroyed.
        // Resources are not refunded, but reserved supply must be released.
        ClearQueue(refundResources: false);
    }

    private void OnDestroy()
    {
        // Safety fallback for destruction paths that bypass
        // BuildingBase.NotifyRemoved().
        NotifyBuildingRemoved();
    }

    // ---------------------------------------------------------------------
    // Queue API
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns whether this building is configured to produce the unit.
    /// This does not check resources, population, or queue capacity.
    /// </summary>
    public bool Supports(UnitDefinition definition)
    {
        return definition != null && producibleUnits.Contains(definition);
    }

    /// <summary>
    /// Performs every validation required to enqueue an order.
    /// </summary>
    public bool CanEnqueue(UnitDefinition definition)
    {
        if (building == null)
            return false;

        if (!building.IsInitialized)
            return false;

        if (!building.IsAlive)
            return false;

        if (!building.IsOperational)
            return false;

        if (!Supports(definition))
            return false;

        if (definition.Prefab == null)
            return false;

        if (IsQueueFull)
            return false;

        Faction ownerFaction = building.OwnerFaction;

        if (ownerFaction == null)
            return false;

        UnitManager unitManager = ownerFaction.UnitManager;
        ResourceManager resourceManager = ownerFaction.ResourceManager;

        if (unitManager == null || resourceManager == null)
            return false;

        if (!resourceManager.CanAffordResources(definition.Cost))
            return false;

        return unitManager.CanReservePopulation(definition);
    }

    /// <summary>
    /// Reserves population, spends resources, and appends one unit
    /// definition to the FIFO production queue.
    /// </summary>
    public bool TryEnqueue(UnitDefinition definition)
    {
        if (!CanEnqueue(definition))
            return false;

        Faction ownerFaction = building.OwnerFaction;
        UnitManager unitManager = ownerFaction.UnitManager;
        ResourceManager resourceManager = ownerFaction.ResourceManager;

        bool populationReserved = unitManager.TryReservePopulation(definition);

        if (!populationReserved)
            return false;

        bool resourcesSpent = resourceManager.TrySpendResources(definition.Cost);

        if (!resourcesSpent)
        {
            unitManager.ReleaseReservedPopulation(definition);
            return false;
        }

        productionQueue.Add(definition);
        return true;
    }

    /// <summary>
    /// Cancels one queued order and returns its mineral and gas cost.
    ///
    /// Canceling the active order resets progress because the next
    /// queued order becomes the active order.
    /// </summary>
    public bool TryCancelAt(int queueIndex)
    {
        if (queueIndex < 0 || queueIndex >= productionQueue.Count)
            return false;

        UnitDefinition definition = productionQueue[queueIndex];

        productionQueue.RemoveAt(queueIndex);

        ReleaseReservation(definition);
        RefundResources(definition);

        if (queueIndex == 0)
            productionElapsed = 0f;

        return true;
    }

    public void ClearQueue(bool refundResources)
    {
        for (int i = 0; i < productionQueue.Count; i++)
        {
            UnitDefinition definition = productionQueue[i];

            ReleaseReservation(definition);

            if (refundResources)
                RefundResources(definition);
        }

        productionQueue.Clear();
        productionElapsed = 0f;
    }

    public UnitDefinition GetQueuedDefinition(int queueIndex)
    {
        if (queueIndex < 0 || queueIndex >= productionQueue.Count)
            return null;

        return productionQueue[queueIndex];
    }

    // ---------------------------------------------------------------------
    // Production completion
    // ---------------------------------------------------------------------

    private bool TrySpawnActiveOrder()
    {
        UnitDefinition definition = ActiveDefinition;

        if (definition == null)
            return false;

        UnitManager unitManager = building.OwnerFaction?.UnitManager;

        if (unitManager == null)
            return false;

        TerrainGrid terrainGrid = GetTerrainGrid();

        if (terrainGrid == null)
            return false;

        if (!TryFindSpawnPosition(terrainGrid, unitManager.NavigationState, out Vector3 spawnPosition))
            return false;

        UnitBase spawnedUnit = unitManager.SpawnUnit(definition.Prefab, spawnPosition, building.transform.rotation,
            preserveSpawnPosition: spawnPoint != null);

        if (spawnedUnit == null)
            return false;

        // UnitBase.Initialize registers the new unit and increases
        // CurrentPopulation. The queued reservation can now be released.
        unitManager.ReleaseReservedPopulation(definition);

        productionQueue.RemoveAt(0);
        productionElapsed = 0f;

        if (EffectiveRallyPoint is Vector3 destination)
            spawnedUnit.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination));

        return true;
    }

    private void RemoveInvalidFrontOrder()
    {
        if (productionQueue.Count == 0)
            return;

        UnitDefinition invalidDefinition = productionQueue[0];

        ReleaseReservation(invalidDefinition);

        productionQueue.RemoveAt(0);
        productionElapsed = 0f;
    }

    // ---------------------------------------------------------------------
    // Spawn placement
    // ---------------------------------------------------------------------

    private bool TryFindSpawnPosition(TerrainGrid terrainGrid, GridNavigationStateSystem navigationState, out Vector3 position)
    {
        position = default;
        if (building.Definition == null)
            return false;

        bool IsCellClear(GridCoord coord) => navigationState?.HasUnitOccupancy(coord) != true &&
            IsSpawnPositionClear(terrainGrid.CellToWorld(coord) + Vector3.up * spawnHeightOffset);

        GridCoord? fallbackCell;
        if (spawnPoint != null)
        {
            Vector3 doorway = spawnPoint.position;
            GridCoord doorwayCell = terrainGrid.WorldToCell(doorway);
            if (terrainGrid.IsWalkable(doorwayCell) && navigationState?.HasUnitOccupancy(doorwayCell) != true)
            {
                doorway.y = Mathf.Max(doorway.y, terrainGrid.SampleHeight(doorway)) + spawnHeightOffset;
                if (IsSpawnPositionClear(doorway))
                {
                    position = doorway;
                    return true;
                }
            }

            fallbackCell = PlacementUtil.GetPlacementAroundFootprintScoredWithFallback(
                terrainGrid, doorwayCell, Vector2Int.one, initialDepth: 0,
                maxExtraDepth: initialSpawnDepth + maximumExtraSpawnDepth,
                preferredCell: doorwayCell, policy: PlacementUtil.PlacementPolicy.Closest,
                openRadius: 0, openWeight: 0, distanceWeight: 1, additionalValidator: IsCellClear);
        }
        else
        {
            fallbackCell = PlacementUtil.GetPlacementAroundFootprintScoredWithFallback(
                terrainGrid,
                building.FootprintOrigin,
                building.PlacedFootprintSize,
                initialDepth: initialSpawnDepth,
                maxExtraDepth: maximumExtraSpawnDepth,
                preferredCell: null,
                policy: PlacementUtil.PlacementPolicy.OpenThenClose,
                openRadius: spawnOpennessRadius,
                openWeight: spawnOpennessWeight,
                distanceWeight: spawnDistanceWeight,
                additionalValidator: IsCellClear);
        }

        if (fallbackCell is not GridCoord cell)
            return false;

        position = terrainGrid.CellToWorld(cell) + Vector3.up * spawnHeightOffset;
        return true;
    }

    private bool IsSpawnPositionClear(Vector3 position)
    {
        return !Physics.CheckSphere(
            position + Vector3.up * spawnClearanceRadius,
            spawnClearanceRadius,
            spawnBlockingMask,
            QueryTriggerInteraction.Ignore);
    }

    // ---------------------------------------------------------------------
    // Transaction helpers
    // ---------------------------------------------------------------------

    private void ReleaseReservation(UnitDefinition definition)
    {
        if (definition == null)
            return;

        building?.OwnerFaction?.UnitManager?.ReleaseReservedPopulation(definition);
    }

    private void RefundResources(UnitDefinition definition)
    {
        if (definition == null)
            return;

        ResourceManager resourceManager =
            building?.OwnerFaction?.ResourceManager;

        if (resourceManager == null)
            return;

        resourceManager.AddMinerals(definition.Cost.Minerals);
        resourceManager.AddGas(definition.Cost.Gas);
    }

    // ---------------------------------------------------------------------
    // Production Roster
    // ---------------------------------------------------------------------

    internal Vector3? GetDefaultRallyOffset(Transform buildingRoot, FactionDefinition factionDefinition)
    {
        if (defaultRallyPoint == null || factionDefinition == null)
            return null;

        for (int i = 0; i < producibleUnitTypes.Count; i++)
        {
            UnitDefinition definition = factionDefinition.GetUnitDefinition(producibleUnitTypes[i]);
            if (definition == null || definition.Prefab == null)
                continue;

            Vector3 localPoint = buildingRoot.InverseTransformPoint(defaultRallyPoint.position);
            return Vector3.Scale(localPoint, buildingRoot.localScale);
        }

        return null;
    }

    private void ResolveProducibleUnits()
    {
        producibleUnits.Clear();

        FactionDefinition factionDefinition = building.OwnerFaction?.Definition;

        if (factionDefinition == null)
        {
            Debug.LogError($"{name} cannot resolve producible units because the owning faction has no definition.");
            return;
        }

        for (int i = 0; i < producibleUnitTypes.Count; i++)
        {
            UnitType unitType = producibleUnitTypes[i];
            UnitDefinition definition = factionDefinition.GetUnitDefinition(unitType);

            if (definition == null)
            {
                Debug.LogWarning($"{name}: faction '{factionDefinition.factionName}' has no unit definition for {unitType}.");
                continue;
            }

            producibleUnits.Add(definition);
        }
    }

    public UnitDefinition GetProducibleUnit(UnitType unitType)
    {
        for (int i = 0; i < producibleUnits.Count; i++)
        {
            UnitDefinition definition = producibleUnits[i];

            if (definition != null && definition.Type == unitType)
                return definition;
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // Validation helpers
    // ---------------------------------------------------------------------

    private bool CanTickProduction()
    {
        return building != null && building.IsInitialized && building.IsAlive && building.IsOperational && productionQueue.Count > 0;
    }

    private TerrainGrid GetTerrainGrid()
    {
        return building?.OwningBuildingManager?.TerrainGrid;
    }

    private void OnValidate()
    {
        initialSpawnDepth = Mathf.Max(1, initialSpawnDepth);
        maximumExtraSpawnDepth = Mathf.Max(0, maximumExtraSpawnDepth);
        spawnOpennessRadius = Mathf.Max(0, spawnOpennessRadius);
        spawnOpennessWeight = Mathf.Max(0, spawnOpennessWeight);
        spawnDistanceWeight = Mathf.Max(0, spawnDistanceWeight);
        spawnClearanceRadius = Mathf.Max(0.01f, spawnClearanceRadius);
    }
}
