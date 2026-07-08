using System.Collections.Generic;
using UnityEngine;

public class UnitManager : MonoBehaviour
{
    private readonly List<UnitBase> unitList = new List<UnitBase>();

    public void RegisterUnit(UnitBase unit)
    {
        if (unit == null)
            return;

        if (!unitList.Contains(unit))
            unitList.Add(unit);
    }

    public void UnregisterUnit(UnitBase unit)
    {
        unitList.Remove(unit);
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
}
