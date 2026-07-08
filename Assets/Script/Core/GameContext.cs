using System.Collections.Generic;
using UnityEngine;

public class GameContext : MonoBehaviour
{
    public static GameContext Instance { get; private set; }

    [Header("Scene Services")]
    [SerializeField] private MonoBehaviour pathfindingServiceComponent;
    [SerializeField] private UnitManager unitManager;
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private CommandIssuer commandIssuer;

    private List<UnitBase> allUnits = new List<UnitBase>();
    private List<UnitBase> selectedUnits = new List<UnitBase>();

    public IPathfindingService PathfindingService { get; private set; }
    public UnitManager UnitManager { get { return unitManager; } }
    public SelectionManager SelectionManager { get { return selectionManager; } }
    public CommandIssuer CommandIssuer { get { return commandIssuer; } }

    public IReadOnlyList<UnitBase> AllUnits { get { return allUnits; } }
    public IReadOnlyList<UnitBase> SelectedUnits { get { return selectedUnits; } }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicate GameContext found. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        PathfindingService = pathfindingServiceComponent as IPathfindingService;

        if (PathfindingService == null)
            Debug.LogError("Pathfinding service is missing or invalid.");

        ResolveSceneReferences();
    }

    private void ResolveSceneReferences()
    {
        if (unitManager == null)
            unitManager = GetComponentInChildren<UnitManager>();

        if (selectionManager == null)
            selectionManager = GetComponentInChildren<SelectionManager>();

        if (commandIssuer == null)
            commandIssuer = GetComponentInChildren<CommandIssuer>();
    }

    public void RegisterUnit(UnitBase unit)
    {
        if (unit == null)
            return;

        if (!allUnits.Contains(unit))
            allUnits.Add(unit);

        if (unitManager != null)
            unitManager.RegisterUnit(unit);
    }

    public void UnregisterUnit(UnitBase unit)
    {
        if (unit == null)
            return;

        allUnits.Remove(unit);
        selectedUnits.Remove(unit);

        if (unitManager != null)
            unitManager.UnregisterUnit(unit);
    }

    public void SelectUnit(UnitBase unit, bool append)
    {
        if (unit == null || !unit.CanBeSelected)
            return;

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

        if (units == null)
            return;

        foreach (UnitBase unit in units)
        {
            if (unit == null || !unit.CanBeSelected)
                continue;

            if (!selectedUnits.Contains(unit))
                selectedUnits.Add(unit);

            unit.SetSelected(true);
        }
    }

    public void DeselectUnit(UnitBase unit)
    {
        if (unit == null)
            return;

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

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
