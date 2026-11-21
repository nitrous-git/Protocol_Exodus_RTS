using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Generic FIFO production queue for buildings.
///
/// The component queues UnitDefinition assets, not instantiated units.
/// A unit is instantiated only when its front production order completes
/// and a safe deployment route is available.
/// </summary>
public sealed class UnitProductionComponent : MonoBehaviour
{
    public const int MaximumQueueSize = 5;
    private const float SpawnRetryInterval = 0.25f;
    private const int MaxRouteSearchesPerAttempt = 3;

    [Header("Production Options")]
    [SerializeField] private List<UnitType> producibleUnitTypes = new();

    [Header("Spawn and Rally Points")]
    [FormerlySerializedAs("SpawnPoint")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform defaultRallyPoint;

    [Header("Production Deployment")]
    [SerializeField, Min(0f)] private float gatheringPadding = 0.2f;
    [FormerlySerializedAs("exitCorridorPadding")]
    [SerializeField, Min(0f)] private float exitClearancePadding = 0.35f;
    [SerializeField, Min(1), Tooltip("Maximum distance from the rally point in grid cells.")]
    private int maxGatherSearchRadius = 12;

    [Header("Physical Clearance")]
    [SerializeField] private LayerMask spawnBlockingMask;
    [SerializeField] private float spawnHeightOffset = 0f;

    private readonly List<UnitDefinition> productionQueue = new();

    private BuildingBase building;
    private float productionElapsed;
    private float spawnRetryRemaining;
    private bool removalHandled;
    private bool warnedAboutExitTransforms;
    private float largestProductionRadius;

    private readonly List<UnitDefinition> producibleUnits = new();
    private readonly Dictionary<UnitDefinition, SpawnFootprint> spawnFootprints = new();
    private readonly List<(GridCoord Cell, float DistanceSquared)> gatheringCandidates = new();
    private readonly List<Vector3> deploymentRoute = new();
    private readonly HashSet<GridCoord> unreachableDestinations = new();
    private readonly Collider[] spawnBlockers = new Collider[8];
    private Vector2? cachedGatherCenter;
    private int cachedGatherRadius;
    private GridCoord? cachedExitCell;

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
        ResetSpawnRetry();
    }

    internal void ClearRallyPoint()
    {
        RallyPoint = null;
        RallyPointNormal = Vector3.up;
        ResetSpawnRetry();
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
    /// Production is complete and waiting for a safe deployment.
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
        cachedGatherCenter = null;
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

        spawnRetryRemaining -= Mathf.Max(0f, deltaTime);
        if (spawnRetryRemaining <= 0f && !TrySpawnActiveOrder())
            spawnRetryRemaining = SpawnRetryInterval;
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
        {
            productionElapsed = 0f;
            ResetSpawnRetry();
        }

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
        ResetSpawnRetry();
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
        if (definition == null || definition.Prefab == null || !definition.Prefab.CanReceiveCommands ||
            definition.moveSpeed <= 0f || !definition.Prefab.TryGetComponent<UnitMotor>(out _) ||
            !spawnFootprints.TryGetValue(definition, out SpawnFootprint footprint))
            return false;

        UnitManager unitManager = building.OwnerFaction?.UnitManager;
        GridNavigationStateSystem navigation = unitManager?.NavigationState;
        TerrainGrid terrainGrid = GetTerrainGrid();
        if (terrainGrid == null || navigation == null || unitManager.PathfindingService == null)
            return false;

        GetExitFrame(terrainGrid, footprint.Radius, out Vector3 entrance, out Vector3 forward);
        if (!TryFindSpawnPosition(terrainGrid, navigation, entrance, forward, footprint, out Vector3 spawnPosition) ||
            !TryFindExitWaypoint(terrainGrid, navigation, spawnPosition, entrance, forward, footprint, out Vector3 exitWaypoint) ||
            !TryFindGatheringDestination(unitManager, terrainGrid, entrance, exitWaypoint, definition, footprint, out GridCoord destination))
            return false;

        UnitBase spawnedUnit = unitManager.SpawnUnit(definition.Prefab, spawnPosition, building.transform.rotation,
            preserveSpawnPosition: true);

        if (spawnedUnit == null)
            return false;

        if (!spawnedUnit.TryStartProductionMove(destination, exitWaypoint, deploymentRoute))
        {
            // Keep the completed order and its supply reservation if the handoff fails.
            unitManager.UnregisterUnit(spawnedUnit);
            spawnedUnit.gameObject.SetActive(false);
            Destroy(spawnedUnit.gameObject);
            return false;
        }

        // UnitBase.Initialize registers the new unit and increases
        // CurrentPopulation. The queued reservation can now be released.
        unitManager.ReleaseReservedPopulation(definition);

        productionQueue.RemoveAt(0);
        productionElapsed = 0f;
        ResetSpawnRetry();

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
        ResetSpawnRetry();
    }

    private void ResetSpawnRetry()
    {
        spawnRetryRemaining = 0f;
        unreachableDestinations.Clear();
    }

    // ---------------------------------------------------------------------
    // Spawn placement
    // ---------------------------------------------------------------------

    private void GetExitFrame(TerrainGrid grid, float radius, out Vector3 entrance, out Vector3 forward)
    {
        entrance = spawnPoint != null ? spawnPoint.position : building.Position;
        bool hasSpawnMarker = spawnPoint != null && float.IsFinite(entrance.x) &&
            float.IsFinite(entrance.y) && float.IsFinite(entrance.z);
        Vector3 offset = Vector3.ProjectOnPlane(entrance - building.Position, Vector3.up);
        forward = defaultRallyPoint != null ? defaultRallyPoint.position - entrance : Vector3.zero;
        forward.y = 0f;
        float markerDistance = forward.magnitude;
        if (hasSpawnMarker && float.IsFinite(markerDistance) && markerDistance > 0.001f && Vector3.Dot(forward, offset) >= 0f)
        {
            forward /= markerDistance;
            return;
        }

        if (!warnedAboutExitTransforms)
        {
            Debug.LogWarning($"{name}: production exit transforms are missing, coincident, or point inward. Using the building's outward direction.", this);
            warnedAboutExitTransforms = true;
        }

        forward = Vector3.ProjectOnPlane(building.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        if (hasSpawnMarker)
        {
            float alignment = Vector3.Dot(offset, forward);
            if (Mathf.Abs(alignment) < 0.001f && offset.sqrMagnitude > 0.001f)
                forward = offset.normalized;
            else if (alignment < 0f)
                forward = -forward;
        }
        else
        {
            // Place the fallback just outside the front face of the producer's grid footprint.
            Vector2Int size = building.PlacedFootprintSize;
            float xDepth = forward.x == 0f ? float.PositiveInfinity : size.x * 0.5f / Mathf.Abs(forward.x);
            float zDepth = forward.z == 0f ? float.PositiveInfinity : size.y * 0.5f / Mathf.Abs(forward.z);
            Vector3 center = grid.GetFootprintWorldCenter(building.FootprintOrigin, size);
            entrance = center + forward * (Mathf.Min(xDepth, zDepth) * grid.CellSize + radius + 0.01f);
        }
    }

    private bool TryFindSpawnPosition(TerrainGrid grid, GridNavigationStateSystem navigation,
        Vector3 entrance, Vector3 forward, SpawnFootprint footprint, out Vector3 position)
    {
        float step = grid.CellSize * 0.25f;
        int maxSteps = Mathf.CeilToInt((footprint.Radius + grid.CellSize) / step);
        for (int i = 0; i <= maxSteps; i++)
        {
            position = entrance + forward * (i * step);
            float groundHeight = grid.SampleHeight(position);
            position.y = (i == 0 && spawnPoint != null ? Mathf.Max(entrance.y, groundHeight) : groundHeight) + spawnHeightOffset;

            if (!navigation.IsUnitSpaceClear(position, footprint.Radius))
                break;

            if (grid.HasNavigationClearanceAt(position, footprint.Radius) && IsPhysicsClear(position, footprint))
                return true;

            // Only correct for the producer's own footprint or collider. A blocked doorway waits.
            if (!grid.HasNavigationClearanceAt(position, footprint.Radius, building.BuildingId) ||
                !IsPhysicsClear(position, footprint, ignoreProducer: true))
                break;
        }

        position = default;
        return false;
    }

    private bool TryFindExitWaypoint(TerrainGrid grid, GridNavigationStateSystem navigation,
        Vector3 spawn, Vector3 entrance, Vector3 forward, SpawnFootprint footprint,
        out Vector3 waypoint)
    {
        float spawnDepth = Vector3.Dot(spawn - entrance, forward);
        // Share a short exit across unit sizes. A corrected spawn may need a farther waypoint.
        float depth = Mathf.Max(largestProductionRadius + exitClearancePadding, spawnDepth + grid.CellSize * 0.5f);
        Vector3 target = entrance + forward * (depth + grid.CellSize * 0.707107f);
        GridCoord waypointCell = grid.WorldToCell(target);
        if (!grid.IsInside(waypointCell))
        {
            waypoint = default;
            return false;
        }

        waypoint = grid.CellToWorld(waypointCell);

        // Keep the first leg straight and check the same grid transitions the motor will use.
        Vector3 direction = Vector3.ProjectOnPlane(waypoint - spawn, Vector3.up);
        int steps = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude / (grid.CellSize * 0.4f)));
        GridCoord previous = grid.WorldToCell(spawn);
        for (int i = 1; i <= steps; i++)
        {
            Vector3 probe = spawn + direction * (i / (float)steps);
            GridCoord cell = grid.WorldToCell(probe);
            if (!grid.IsStaticTransitionTraversable(previous, cell, footprint.Radius) ||
                !grid.HasNavigationClearanceAt(probe, footprint.Radius) ||
                !navigation.IsUnitSpaceClear(probe, footprint.Radius))
                return false;

            probe.y = grid.SampleHeight(probe) + spawnHeightOffset;
            if (!IsPhysicsClear(probe, footprint))
                return false;

            previous = cell;
        }

        return true;
    }

    private bool TryFindGatheringDestination(UnitManager unitManager, TerrainGrid grid, Vector3 entrance,
        Vector3 exitWaypoint, UnitDefinition definition, SpawnFootprint footprint, out GridCoord destination)
    {
        Vector3 rally = EffectiveRallyPoint ?? exitWaypoint;
        if (!float.IsFinite(rally.x) || !float.IsFinite(rally.z))
            rally = exitWaypoint;

        CollectGatheringCandidates(grid, rally);
        GridCoord exitCell = grid.WorldToCell(exitWaypoint);
        if (cachedExitCell is not GridCoord previousExit || previousExit.x != exitCell.x || previousExit.z != exitCell.z)
        {
            cachedExitCell = exitCell;
            unreachableDestinations.Clear();
        }

        Vector2 entranceXZ = new(entrance.x, entrance.z);
        Vector2 exitSegment = new Vector2(exitWaypoint.x, exitWaypoint.z) - entranceXZ;
        float exitLengthSquared = exitSegment.sqrMagnitude;
        // Expand the protected segment by the candidate's radius so its footprint stays outside it.
        float protectedRadius = largestProductionRadius + exitClearancePadding + footprint.Radius;
        float protectedRadiusSquared = protectedRadius * protectedRadius;
        float gatheringRadius = Mathf.Max(footprint.Radius, definition.NavigationRadius + gatheringPadding);
        GridNavigationStateSystem navigation = unitManager.NavigationState;
        IPathfindingService pathfinding = unitManager.PathfindingService;
        int routeSearches = 0;

        foreach (var (cell, _) in gatheringCandidates)
        {
            if (unreachableDestinations.Contains(cell) || !grid.IsStaticallyTraversable(cell, footprint.Radius))
                continue;

            Vector3 point = grid.CellToWorld(cell);
            Vector2 offset = new Vector2(point.x, point.z) - entranceXZ;
            float progress = exitLengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(offset, exitSegment) / exitLengthSquared) : 0f;
            if ((offset - exitSegment * progress).sqrMagnitude < protectedRadiusSquared ||
                navigation.IsDestinationReserved(cell) || !navigation.IsUnitSpaceClear(point, gatheringRadius) ||
                !IsPhysicsClear(point, footprint))
                continue;

            bool found = pathfinding is GridAStarPathfindingService gridPathfinding
                ? gridPathfinding.TryFindProductionPath(footprint.Radius, exitWaypoint, point, deploymentRoute)
                : pathfinding.TryFindPath(definition.Prefab, exitWaypoint, point, deploymentRoute);

            if (found && deploymentRoute.Count > 0 &&
                Vector3.ProjectOnPlane(deploymentRoute[deploymentRoute.Count - 1] - point, Vector3.up).sqrMagnitude < 0.0001f)
            {
                deploymentRoute[deploymentRoute.Count - 1] = point;
                destination = cell;
                return true;
            }

            unreachableDestinations.Add(cell);
            if (++routeSearches == MaxRouteSearchesPerAttempt)
                break;
        }

        // Continue past failed paths on later retries. After a full pass, reconsider them.
        if (routeSearches < MaxRouteSearchesPerAttempt)
            unreachableDestinations.Clear();

        destination = default;
        return false;
    }

    private void CollectGatheringCandidates(TerrainGrid grid, Vector3 rally)
    {
        Vector2 center = new(rally.x, rally.z);
        if (cachedGatherCenter is Vector2 previousCenter && previousCenter.Equals(center) &&
            cachedGatherRadius == maxGatherSearchRadius)
            return;

        cachedGatherCenter = center;
        cachedGatherRadius = maxGatherSearchRadius;
        gatheringCandidates.Clear();
        unreachableDestinations.Clear();

        float range = maxGatherSearchRadius * grid.CellSize;
        float rangeSquared = range * range;
        Vector3 bounds = new(range, 0f, range);
        GridCoord min = grid.WorldToCell(rally - bounds);
        GridCoord max = grid.WorldToCell(rally + bounds);
        int minX = Mathf.Max(0, min.x);
        int maxX = Mathf.Min(grid.Width - 1, max.x);
        int minZ = Mathf.Max(0, min.z);
        int maxZ = Mathf.Min(grid.Height - 1, max.z);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                GridCoord cell = new(x, z);
                Vector3 point = grid.CellToWorld(cell);
                float distanceSquared = (new Vector2(point.x, point.z) - center).sqrMagnitude;
                if (distanceSquared <= rangeSquared)
                    gatheringCandidates.Add((cell, distanceSquared));
            }
        }

        gatheringCandidates.Sort(static (left, right) =>
        {
            int comparison = left.DistanceSquared.CompareTo(right.DistanceSquared);
            if (comparison != 0)
                return comparison;

            comparison = left.Cell.z.CompareTo(right.Cell.z);
            return comparison != 0 ? comparison : left.Cell.x.CompareTo(right.Cell.x);
        });
    }

    private bool IsPhysicsClear(Vector3 position, SpawnFootprint footprint, bool ignoreProducer = false)
    {
        Vector3 lower = position + Vector3.up * footprint.LowerCenter;
        Vector3 upper = position + Vector3.up * footprint.UpperCenter;
        if (!ignoreProducer)
            return !Physics.CheckCapsule(lower, upper, footprint.Radius, spawnBlockingMask, QueryTriggerInteraction.Ignore);

        int count = Physics.OverlapCapsuleNonAlloc(lower, upper, footprint.Radius, spawnBlockers,
            spawnBlockingMask, QueryTriggerInteraction.Ignore);
        if (count == spawnBlockers.Length)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!spawnBlockers[i].transform.IsChildOf(building.transform))
                return false;
        }

        return true;
    }

    private readonly struct SpawnFootprint
    {
        public readonly float Radius;
        public readonly float LowerCenter;
        public readonly float UpperCenter;

        private SpawnFootprint(float radius, float lowerCenter, float upperCenter)
        {
            Radius = radius;
            LowerCenter = lowerCenter;
            UpperCenter = upperCenter;
        }

        public static SpawnFootprint FromDefinition(UnitDefinition definition)
        {
            float radius = Mathf.Max(0.05f, definition.NavigationRadius, definition.Prefab.NavigationRadius);
            float lower = radius;
            float upper = radius;
            Transform root = definition.Prefab.transform;
            Matrix4x4 fromWorld = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
            foreach (Collider collider in definition.Prefab.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger)
                    continue;

                Matrix4x4 toSpawn = fromWorld * collider.transform.localToWorldMatrix;
                switch (collider)
                {
                    case CapsuleCollider capsule:
                        Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                        IncludeCapsule(capsule.center, axis * Mathf.Max(0f, capsule.height * 0.5f - capsule.radius), capsule.radius);
                        break;
                    case SphereCollider sphere:
                        IncludeCapsule(sphere.center, Vector3.zero, sphere.radius);
                        break;
                    case CharacterController character:
                        IncludeCapsule(character.center, Vector3.up * Mathf.Max(0f, character.height * 0.5f - character.radius),
                            character.radius);
                        break;
                    case BoxCollider box:
                        IncludeBounds(new Bounds(box.center, box.size));
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        IncludeBounds(mesh.sharedMesh.bounds);
                        break;
                }

                void IncludeCapsule(Vector3 center, Vector3 halfSegment, float localRadius)
                {
                    float scale = Mathf.Max(toSpawn.MultiplyVector(Vector3.right).magnitude,
                        toSpawn.MultiplyVector(Vector3.up).magnitude, toSpawn.MultiplyVector(Vector3.forward).magnitude);
                    IncludePoint(toSpawn.MultiplyPoint3x4(center - halfSegment), localRadius * scale);
                    IncludePoint(toSpawn.MultiplyPoint3x4(center + halfSegment), localRadius * scale);
                }

                void IncludeBounds(Bounds bounds)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                            (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                        IncludePoint(toSpawn.MultiplyPoint3x4(corner), 0f);
                    }
                }
            }

            return new SpawnFootprint(radius, lower, upper);

            void IncludePoint(Vector3 center, float padding)
            {
                float horizontal = new Vector2(center.x, center.z).magnitude;
                radius = Mathf.Max(radius, horizontal + padding);
                lower = Mathf.Min(lower, center.y);
                upper = Mathf.Max(upper, center.y);
            }
        }
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
        spawnFootprints.Clear();
        largestProductionRadius = 0f;

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

            if (definition.Prefab != null && !spawnFootprints.ContainsKey(definition))
            {
                SpawnFootprint footprint = SpawnFootprint.FromDefinition(definition);
                spawnFootprints.Add(definition, footprint);
                largestProductionRadius = Mathf.Max(largestProductionRadius, footprint.Radius);
            }
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
        gatheringPadding = Mathf.Max(0f, gatheringPadding);
        exitClearancePadding = Mathf.Max(0f, exitClearancePadding);
        maxGatherSearchRadius = Mathf.Max(1, maxGatherSearchRadius);
    }
}
