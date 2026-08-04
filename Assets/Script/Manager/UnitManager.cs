using System.Collections.Generic;
using UnityEngine;

public class UnitManager 
{
    private List<UnitBase> unitList = new List<UnitBase>();
    private readonly List<UnitBase> pendingRemovals = new List<UnitBase>();

    private readonly GameContext gameContext;
    private readonly IPathfindingService pathfindingService;

    private int currentPopulation;
    private int reservedPopulation;

    private readonly Transform unitsRoot;

    public Faction OwnerFaction { get; private set; }
    public IReadOnlyList<UnitBase> UnitList => unitList;

    public int CurrentPopulation => currentPopulation;
    public int ReservedPopulation => reservedPopulation;
    public int OccupiedPopulation => currentPopulation + reservedPopulation;

    public UnitManager(GameContext gameContext, IPathfindingService pathfindingService, Transform unitsRoot)
    {
        this.gameContext = gameContext;
        this.pathfindingService = pathfindingService;
        this.unitsRoot = unitsRoot;
    }

    public void Tick(float deltaTime)
    {
        for (int i = 0; i < unitList.Count; i++)
        {
            unitList[i]?.Tick(deltaTime);
        }

        ProcessPendingRemovals();
    }

    public void TickLate(float deltaTime)
    {
        for (int i = 0; i < unitList.Count; i++)
        {
            unitList[i]?.TickLate(deltaTime);
        }
    }

    public void SetOwnerFaction(Faction ownerFaction)
    {
        OwnerFaction = ownerFaction;
    }

    public void RegisterUnit(UnitBase unit)
    {
        if (unit == null || unitList.Contains(unit)) 
            return;

        currentPopulation += GetPopulationCost(unit);
        unitList.Add(unit);
        gameContext?.RegisterUnit(unit);
    }

    public void UnregisterUnit(UnitBase unit)
    {
        if (unit == null) 
            return;

        //unitList.Remove(unit);
        //pendingRemovals.Remove(unit);
        //gameContext?.UnregisterUnit(unit);

        bool wasRegistered = unitList.Remove(unit);

        pendingRemovals.Remove(unit);

        // might already have been unregister by OnDestroy()
        if (!wasRegistered)
            return;

        currentPopulation = Mathf.Max(0, currentPopulation - GetPopulationCost(unit));

        gameContext?.UnregisterUnit(unit);
    }

    // ---------------------------------------------------------------------
    // Getter & Setter
    // ---------------------------------------------------------------------

    public IReadOnlyList<UnitBase> getUnitList()
    {
        return unitList;
    }

    public void setUnitList(List<UnitBase> newList)
    {
        unitList.Clear();

        if (newList != null)
            unitList.AddRange(newList);

        RecalculateCurrentPopulation();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    public UnitBase SpawnUnit(UnitBase prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null)
        {
            Debug.LogError("UnitManager cannot spawn unit because prefab is missing.");
            return null;
        }

        Transform resolvedParent = parent != null ? parent : unitsRoot;

        UnitBase unit = Object.Instantiate(prefab, position, rotation, resolvedParent);
        unit.Initialize(OwnerFaction, gameContext, pathfindingService, this);

        if (!unit.IsInitialized)
        {
            Debug.LogError($"UnitManager failed to initialize spawned unit {prefab.name}.");
            Object.Destroy(unit.gameObject);
            return null;
        }

        return unit;
    }

    private void ProcessPendingRemovals()
    {
        for (int i = pendingRemovals.Count - 1; i >= 0; i--)
        {
            UnitBase unit = pendingRemovals[i];
            pendingRemovals.RemoveAt(i);

            if (unit == null) continue;

            UnregisterUnit(unit);
            Object.Destroy(unit.gameObject);
        }
    }

    public void RequestRemoveUnit(UnitBase unit)
    {
        if (unit == null || pendingRemovals.Contains(unit))
            return;

        pendingRemovals.Add(unit);
    }

    private static int GetPopulationCost(UnitBase unit)
    {
        if (unit == null || unit.Definition == null)
            return 0;

        return Mathf.Max(0, unit.Definition.Cost.Supply);
    }

    private void RecalculateCurrentPopulation()
    {
        currentPopulation = 0;
        for (int i = 0; i < unitList.Count; i++)
        {
            currentPopulation += GetPopulationCost(unitList[i]);
        }
    }

    // ---------------------------------------------------------------------
    // Population reservation
    // ---------------------------------------------------------------------

    public bool CanReservePopulation(int populationCost)
    {
        populationCost = Mathf.Max(0, populationCost);

        if (populationCost == 0)
            return true;

        ResourceManager resourceManager = OwnerFaction != null ? OwnerFaction.ResourceManager : null;

        if (resourceManager == null)
            return false;

        return OccupiedPopulation + populationCost <= resourceManager.MaxSupply;
    }

    public bool TryReservePopulation(int populationCost)
    {
        populationCost = Mathf.Max(0, populationCost);

        if (!CanReservePopulation(populationCost))
            return false;

        reservedPopulation += populationCost;
        return true;
    }

    public void ReleaseReservedPopulation(int populationCost)
    {
        if (populationCost <= 0)
            return;

        if (populationCost > reservedPopulation)
        {
            Debug.LogWarning($"Attempted to release {populationCost} reserved population, but only {reservedPopulation} is currently reserved.");
        }

        reservedPopulation = Mathf.Max(0, reservedPopulation - populationCost);
    }

    // Definition overload wrapper
    public bool CanReservePopulation(UnitDefinition definition)
    {
        if (definition == null)
            return false;

        return CanReservePopulation(definition.Cost.Supply);
    }

    public bool TryReservePopulation(UnitDefinition definition)
    {
        if (definition == null)
            return false;

        return TryReservePopulation(definition.Cost.Supply);
    }

    public void ReleaseReservedPopulation(UnitDefinition definition)
    {
        if (definition == null)
            return;

        ReleaseReservedPopulation(definition.Cost.Supply);
    }

}
