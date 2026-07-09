using System.Collections.Generic;

public class GameContext
{
    // Thread-safe Lazy Singleton Setup
    private static readonly object _lock = new object();
    private static GameContext _instance;

    public static GameContext Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new GameContext();
                }
                return _instance;
            }
        }
    }

    private readonly List<UnitBase> allUnits = new List<UnitBase>();
    private readonly List<UnitBase> selectedUnits = new List<UnitBase>();

    public IReadOnlyList<UnitBase> AllUnits => allUnits;
    public IReadOnlyList<UnitBase> SelectedUnits => selectedUnits;

    private GameContext() { }

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

    /// <summary>
    /// Resets the context. For transitioning between matches or loading levels.
    /// </summary>
    public void Reset()
    {
        allUnits.Clear();
        selectedUnits.Clear();
    }
}