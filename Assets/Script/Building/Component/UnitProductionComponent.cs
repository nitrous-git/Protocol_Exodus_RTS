using System.Collections.Generic;
using UnityEngine;

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
    [SerializeField] private List<UnitDefinition> producibleUnits = new();

    [Header("Spawn Search")]
    [SerializeField, Min(1)] private int initialSpawnDepth = 1;
    [SerializeField, Min(0)] private int maximumExtraSpawnDepth = 4;
    [SerializeField, Min(0)] private int spawnOpennessRadius = 2;
    [SerializeField, Min(0)] private int spawnOpennessWeight = 4;
    [SerializeField, Min(0)] private int spawnDistanceWeight = 2;

    [Header("Physical Clearance")]
    [SerializeField] private LayerMask spawnBlockingMask;
    [SerializeField, Min(0.01f)] private float spawnClearanceRadius = 0.4f;
    [SerializeField] private float spawnHeightOffset = 0f;

    private readonly List<UnitDefinition> productionQueue = new();
    private readonly Collider[] overlapBuffer = new Collider[16];

    private BuildingBase building;
    private float productionElapsed;
    private bool removalHandled;

    // ---------------------------------------------------------------------
    // Public state
    // ---------------------------------------------------------------------

    public BuildingBase Building => building;

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

        // Production is lost when the building is destroyed.
        // Resources are not refunded, but reserved supply must be released.
        ClearQueue(refundResources: false);
    }

    private void OnDestroy()
    {
        // Safety fallback for destruction paths that bypass
        // BuildingBase.NotifyRemoved().
        if (!removalHandled)
        {
            removalHandled = true;
            ClearQueue(refundResources: false);
        }
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

        GridCoord? spawnCell = FindSpawnCell(terrainGrid);

        if (!spawnCell.HasValue)
            return false;

        Vector3 spawnPosition = terrainGrid.CellToWorld(spawnCell.Value);

        spawnPosition.y += spawnHeightOffset;

        UnitBase spawnedUnit = unitManager.SpawnUnit(definition.Prefab, spawnPosition, building.transform.rotation);

        if (spawnedUnit == null)
            return false;

        // UnitBase.Initialize registers the new unit and increases
        // CurrentPopulation. The queued reservation can now be released.
        unitManager.ReleaseReservedPopulation(definition);

        productionQueue.RemoveAt(0);
        productionElapsed = 0f;

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

    private GridCoord? FindSpawnCell(TerrainGrid terrainGrid)
    {
        if (building.Definition == null)
            return null;

        Vector2Int gridForward = GetBuildingGridForward();

        GridCoord preferredFrontCell = 
            PlacementUtil.GetFootprintSideCenter(
                building.FootprintOrigin,
                building.Definition.FootprintSize,
                gridForward,
                distance: initialSpawnDepth);

        return PlacementUtil
            .GetPlacementAroundFootprintScoredWithFallback(
                terrainGrid,
                building.FootprintOrigin,
                building.Definition.FootprintSize,
                initialDepth: initialSpawnDepth,
                maxExtraDepth: maximumExtraSpawnDepth,
                preferredCell: preferredFrontCell,
                policy:
                    PlacementUtil.PlacementPolicy.OpenThenClose,
                openRadius: spawnOpennessRadius,
                openWeight: spawnOpennessWeight,
                distanceWeight: spawnDistanceWeight,
                additionalValidator: IsSpawnCellPhysicallyClear);
    }

    /// <summary>
    /// Converts the building's world-space forward direction into one
    /// cardinal grid direction.
    ///
    /// Quaternion.identity therefore produces toward positive grid Z.
    /// </summary>
    private Vector2Int GetBuildingGridForward()
    {
        Vector3 forward = building.transform.forward;

        if (Mathf.Abs(forward.x) > Mathf.Abs(forward.z))
        {
            return forward.x >= 0f ? Vector2Int.right : Vector2Int.left;
        }

        return forward.z >= 0f ? Vector2Int.up : Vector2Int.down;
    }

    private bool IsSpawnCellPhysicallyClear(GridCoord coord)
    {
        TerrainGrid terrainGrid = GetTerrainGrid();

        if (terrainGrid == null)
            return false;

        Vector3 center = terrainGrid.CellToWorld(coord);

        center.y += spawnClearanceRadius;

        int overlapCount = Physics.OverlapSphereNonAlloc(
            center,
            spawnClearanceRadius,
            overlapBuffer,
            spawnBlockingMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlapCount; i++)
        {
            Collider overlap = overlapBuffer[i];

            if (overlap == null)
                continue;

            // Ignore colliders belonging to the producing building.
            if (overlap.transform.IsChildOf(building.transform))
                continue;

            return false;
        }

        return true;
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