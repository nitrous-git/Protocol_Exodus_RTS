using System.Collections.Generic;
using UnityEngine;

public class UnitManager 
{
    private List<UnitBase> unitList = new List<UnitBase>();
    private readonly List<UnitBase> pendingRemovals = new List<UnitBase>();

    private readonly GameContext gameContext;
    private readonly IPathfindingService pathfindingService;

    private int currentPopulation;

    public Faction OwnerFaction { get; private set; }
    public IReadOnlyList<UnitBase> UnitList => unitList;
    public int CurrentPopulation => currentPopulation;


    public UnitManager(GameContext gameContext, IPathfindingService pathfindingService)
    {
        this.gameContext = gameContext;
        this.pathfindingService = pathfindingService;
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


    // Getter & Setter

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

    // Helpers

    public UnitBase SpawnUnit(UnitBase prefab, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (prefab == null)
        {
            Debug.LogError("UnitManager cannot spawn unit because prefab is missing.");
            return null;
        }

        UnitBase unit = Object.Instantiate(prefab, position, rotation, parent);
        unit.Initialize(OwnerFaction, gameContext, pathfindingService, this);

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
}
