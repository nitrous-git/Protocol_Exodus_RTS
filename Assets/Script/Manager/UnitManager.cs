using System.Collections.Generic;
using UnityEngine;

public class UnitManager 
{
    private List<UnitBase> unitList = new List<UnitBase>();

    private readonly GameContext gameContext;
    private readonly IPathfindingService pathfindingService;

    public Faction OwnerFaction { get; private set; }
    public IReadOnlyList<UnitBase> UnitList => unitList;

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
        if (unit == null)
            return;

        if (!unitList.Contains(unit))
            unitList.Add(unit);

        gameContext?.RegisterUnit(unit);
    }

    public void UnregisterUnit(UnitBase unit)
    {
        unitList.Remove(unit);
        gameContext?.UnregisterUnit(unit);
    }

    public bool Contains(UnitBase unit)
    {
        return unitList.Contains(unit);
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
        {
            unitList.AddRange(newList);
        }
    }

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
}
