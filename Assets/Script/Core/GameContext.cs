using System.Collections.Generic;
using System.Linq;

public sealed class GameContext
{
    private readonly List<UnitBase> allUnits = new();
    private readonly List<UnitBase> selectedUnits = new();
    private readonly List<ITargetable> allTargetables = new();

    public IReadOnlyList<UnitBase> AllUnits => allUnits;
    public IReadOnlyList<UnitBase> SelectedUnits => selectedUnits;
    public IReadOnlyList<ITargetable> AllTargetables => allTargetables;

    public FactionManager FactionManager { get; private set; }
    public Faction PlayerFaction { get; private set; }
    public ProjectileManager ProjectileManager { get; private set; }


    public void SetFactionManager(FactionManager factionManager)
    {
        FactionManager = factionManager;
    }

    public void SetProjectileManager(ProjectileManager projectileManager)
    {
        ProjectileManager = projectileManager;
    }

    public void SetPlayerFaction(Faction faction)
    {
        PlayerFaction = faction;
    }

    public void RegisterUnit(UnitBase unit)
    {
        //if (unit == null) return;

        if (!allUnits.Contains(unit))
            allUnits.Add(unit);

        if (!allTargetables.Contains(unit))
            allTargetables.Add(unit);
    }

    public void UnregisterUnit(UnitBase unit)
    {
        //if (unit == null) return;

        allUnits.Remove(unit);
        selectedUnits.Remove(unit);
        allTargetables.Remove(unit);
    }

    public void SelectUnit(UnitBase unit, bool append)
    {
        if (unit == null || !unit.CanBeSelected) return;

        if (!append)
            ClearSelectedUnits();

        if (!selectedUnits.Contains(unit))
            selectedUnits.Add(unit);

        unit.SetSelected(true);
    }

    public void SelectUnits(IEnumerable<UnitBase> units, bool append)
    {
        if (!append)
            ClearSelectedUnits();

        if (units == null) return;

        foreach (UnitBase unit in units)
        {
            if (unit == null || !unit.CanBeSelected) continue;

            if (!selectedUnits.Contains(unit))
                selectedUnits.Add(unit);

            unit.SetSelected(true);
        }
    }

    public void DeselectUnit(UnitBase unit)
    {
        if (unit == null) return;

        selectedUnits.Remove(unit);
        unit.SetSelected(false);
    }

    public void ClearSelectedUnits()
    {
        for (int i = selectedUnits.Count - 1; i >= 0; i--)
        {
            if (selectedUnits[i] != null)
                selectedUnits[i].SetSelected(false);
        }

        selectedUnits.Clear();
    }

    public void Clear()
    {
        ClearSelectedUnits();
        allUnits.Clear();
        allTargetables.Clear();
        FactionManager = null;
        PlayerFaction = null;
        ProjectileManager = null;
    }
}