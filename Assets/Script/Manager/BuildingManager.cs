using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns and manages all runtime buildings belonging to one faction.
///
/// </summary>
public sealed class BuildingManager
{
    //[Header("Production Options")]
    //[SerializeField] private List<BuildingType> producibleBuildingTypes = new();

    //private readonly List<BuildingDefinition> producibleBuildings = new();

    private readonly List<BuildingBase> buildingList = new();
    private readonly List<BuildingBase> pendingRemovalList = new();

    private readonly GameContext gameContext;
    private readonly TerrainGrid terrainGrid;
    private readonly Transform buildingsRoot;

    public Faction OwnerFaction { get; private set; }

    public IReadOnlyList<BuildingBase> BuildingList => buildingList;
    public IReadOnlyList<BuildingDefinition> ProducibleBuildings => OwnerFaction?.Definition.BuildingRoster;
    public TerrainGrid TerrainGrid => terrainGrid;

    public BuildingManager(GameContext gameContext, TerrainGrid terrainGrid, Transform buildingsRoot)
    {
        this.gameContext = gameContext;
        this.terrainGrid = terrainGrid;
        this.buildingsRoot = buildingsRoot;

        //ResolveProducibleBuildings();
    }

    public void SetOwnerFaction(Faction ownerFaction)
    {
        OwnerFaction = ownerFaction;
    }

    // ---------------------------------------------------------------------
    // Tick
    // ---------------------------------------------------------------------

    public void Tick(float deltaTime)
    {
        for (int i = buildingList.Count - 1; i >= 0; i--)
        {
            BuildingBase building = buildingList[i];

            if (building == null)
            {
                buildingList.RemoveAt(i);
                continue;
            }

            building.Tick(deltaTime);
        }

        ProcessPendingRemovals();
    }

    // ---------------------------------------------------------------------
    // Construction
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns whether the faction can currently construct the given
    /// building at the requested footprint origin.
    /// </summary>
    public bool CanConstruct(BuildingDefinition definition, GridCoord footprintOrigin)
    {
        if (OwnerFaction == null)
            return false;

        if (OwnerFaction.ResourceManager == null)
            return false;

        if (terrainGrid == null)
            return false;

        if (definition == null)
            return false;

        if (definition.Prefab == null)
            return false;

        if (!OwnerFaction.ResourceManager.CanAffordResources(definition.Cost))
        {
            return false;
        }

        return terrainGrid.CanPlaceFootprint(footprintOrigin, definition.FootprintSize);
    }

    /// <summary>
    /// Constructs and returns a building.
    ///
    /// Returns null when construction is not permitted.
    /// </summary>
    public BuildingBase Construct(BuildingDefinition definition, GridCoord footprintOrigin)
    {
        if (!CanConstruct(definition, footprintOrigin))
        {
            return null;
        }

        bool resourcesSpent = OwnerFaction.ResourceManager.TrySpendResources(definition.Cost);

        if (!resourcesSpent)
            return null;

        Vector3 worldPosition = terrainGrid.GetFootprintWorldCenter(footprintOrigin, definition.FootprintSize);

        BuildingBase building = Object.Instantiate(definition.Prefab, worldPosition, Quaternion.identity, buildingsRoot);

        building.Initialize(definition, OwnerFaction, gameContext, this, footprintOrigin);

        if (!building.IsInitialized)
        {
            Debug.LogError($"BuildingManager failed to initialize " + $"{definition.DisplayName}.");
            Object.Destroy(building.gameObject);
            return null;
        }

        terrainGrid.SetFootprintOccupied(footprintOrigin, definition.FootprintSize, building.BuildingId);

        return building;
    }

    // ---------------------------------------------------------------------
    // Registration
    // ---------------------------------------------------------------------

    /// <summary>
    /// Registers an initialized building with its owning faction
    /// and the shared targetable repository.
    ///
    /// Called by BuildingBase.Initialize().
    /// </summary>
    public void RegisterBuilding(BuildingBase building)
    {
        if (building == null)
            return;

        if (!buildingList.Contains(building))
        {
            buildingList.Add(building);
        }

        gameContext?.RegisterBuilding(building);
    }

    /// <summary>
    /// Removes a building from runtime repositories and clears
    /// its occupied terrain-grid footprint.
    ///
    /// Safe to call more than once.
    /// </summary>
    public void UnregisterBuilding(BuildingBase building)
    {
        if (building == null)
            return;

        pendingRemovalList.Remove(building);
        buildingList.Remove(building);

        gameContext?.UnregisterBuilding(building);

        if (terrainGrid == null || building.Definition == null)
        {
            return;
        }

        terrainGrid.ClearFootprintOccupied(building.FootprintOrigin, building.Definition.FootprintSize, building.BuildingId);
    }

    public bool Contains(BuildingBase building)
    {
        return building != null && buildingList.Contains(building);
    }

    // ---------------------------------------------------------------------
    // Removal
    // ---------------------------------------------------------------------

    /// <summary>
    /// Schedules a building for removal after the current manager tick.
    ///
    /// This avoids modifying the building collection while it is
    /// being iterated.
    /// </summary>
    public void RequestRemoveBuilding(BuildingBase building)
    {
        if (building == null)
            return;

        if (!buildingList.Contains(building))
            return;

        if (!pendingRemovalList.Contains(building))
        {
            pendingRemovalList.Add(building);
        }
    }

    private void ProcessPendingRemovals()
    {
        if (pendingRemovalList.Count == 0)
            return;

        for (int i = pendingRemovalList.Count - 1; i >= 0; i--)
        {
            BuildingBase building = pendingRemovalList[i];
            RemoveBuilding(building);
        }

        pendingRemovalList.Clear();
    }

    private void RemoveBuilding(BuildingBase building)
    {
        if (building == null)
            return;

        if (!buildingList.Contains(building))
            return;

        building.NotifyRemoved();

        UnregisterBuilding(building);

        Object.Destroy(building.gameObject);
    }

    // ---------------------------------------------------------------------
    // Construction Roster
    // ---------------------------------------------------------------------

    private void ResolveProducibleBuildings()
    {
        //producibleBuildings.Clear();

        //FactionDefinition factionDefinition = OwnerFaction?.Definition;

        //if (factionDefinition == null)
        //{
        //    Debug.LogError($"{OwnerFaction?.Name} cannot resolve producible buildings because the owning faction has no definition.");
        //    return;
        //}

        //for (int i = 0; i < producibleBuildingTypes.Count; i++)
        //{
        //    BuildingType buildingType = producibleBuildingTypes[i];
        //    BuildingDefinition definition = factionDefinition.GetBuildingDefinition(buildingType);

        //    if (definition == null)
        //    {
        //        Debug.LogWarning($"{OwnerFaction?.Name}: faction '{factionDefinition.factionName}' has no unit definition for {buildingType}.");
        //        continue;
        //    }

        //    producibleBuildings.Add(definition);
        //}
    }

    public BuildingDefinition GetProducibleBuilding(BuildingType buildingType)
    {
        for (int i = 0; i < ProducibleBuildings.Count; i++)
        {
            BuildingDefinition definition = ProducibleBuildings[i];

            if (definition != null && definition.Type == buildingType)
                return definition;
        }

        return null;
    }


    // ---------------------------------------------------------------------
    // Placement
    // ---------------------------------------------------------------------

    /// <summary>
    /// Returns the center of the rectangular footprint in world space.
    ///
    /// The same calculation works for odd, even, square, and
    /// rectangular footprints.
    /// </summary>
    //private Vector3 GetFootprintWorldCenter(GridCoord footprintOrigin, Vector2Int footprintSize)
    //{
    //    GridCoord finalCell = new GridCoord(footprintOrigin.x + footprintSize.x - 1, footprintOrigin.z + footprintSize.y - 1);

    //    Vector3 firstCellWorld = terrainGrid.CellToWorld(footprintOrigin);
    //    Vector3 finalCellWorld = terrainGrid.CellToWorld(finalCell);

    //    return (firstCellWorld + finalCellWorld) * 0.5f;
    //}
}