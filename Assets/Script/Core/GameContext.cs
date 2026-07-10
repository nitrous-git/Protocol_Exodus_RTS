using System.Collections.Generic;

public sealed class GameContext
{
    private readonly List<UnitBase> allUnits = new List<UnitBase>();
    private readonly List<UnitBase> selectedUnits = new List<UnitBase>();

    public IReadOnlyList<UnitBase> AllUnits => allUnits;
    public IReadOnlyList<UnitBase> SelectedUnits => selectedUnits;

    public FactionManager FactionManager { get; private set; }
    public Faction PlayerFaction { get; private set; }

    public void SetFactionManager(FactionManager factionManager)
    {
        FactionManager = factionManager;
    }

    public void SetPlayerFaction(Faction faction)
    {
        PlayerFaction = faction;
    }

    public void RegisterUnit(UnitBase unit)
    {
        if (unit == null) return;

        if (!allUnits.Contains(unit))
            allUnits.Add(unit);
    }

    public void UnregisterUnit(UnitBase unit)
    {
        if (unit == null) return;

        allUnits.Remove(unit);
        selectedUnits.Remove(unit);
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
        FactionManager = null;
        PlayerFaction = null;
    }
}