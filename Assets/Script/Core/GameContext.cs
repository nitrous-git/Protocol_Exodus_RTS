using System.Collections.Generic;
using System.Linq;

public sealed class GameContext
{
    private readonly List<UnitBase> allUnits = new();
    private readonly List<BuildingBase> allBuildings = new();
    private readonly List<ResourceNode> allResourceNodes = new();
    private readonly List<ITargetable> allTargetables = new();

    private readonly List<UnitBase> selectedUnits = new();
    private BuildingBase selectedBuilding;
    private ResourceNode selectedResourceNode;

    public IReadOnlyList<UnitBase> AllUnits => allUnits;
    public IReadOnlyList<BuildingBase> AllBuildings => allBuildings;
    public IReadOnlyList<ResourceNode> AllResourceNodes => allResourceNodes;
    public IReadOnlyList<ITargetable> AllTargetables => allTargetables;

    public IReadOnlyList<UnitBase> SelectedUnits => selectedUnits;
    public BuildingBase SelectedBuilding => selectedBuilding;
    public ResourceNode SelectedResourceNode => selectedResourceNode;

    public FactionManager FactionManager { get; private set; }
    public Faction PlayerFaction { get; private set; }
    public ProjectileManager ProjectileManager { get; private set; }
    public ResourceNodeRepository ResourceNodeRepository { get; private set; }

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

    public void SetResourceNodeRepository(ResourceNodeRepository resourceNodeRepository)
    {
        ResourceNodeRepository = resourceNodeRepository;
    }

    // ---------------------------------------------------------------------
    // Unit registration
    // ---------------------------------------------------------------------

    public void RegisterUnit(UnitBase unit)
    {
        if (unit == null)
            return;

        if (!allUnits.Contains(unit))
            allUnits.Add(unit);

        RegisterTargetable(unit);
    }

    public void UnregisterUnit(UnitBase unit)
    {
        if (unit == null)
            return;

        allUnits.Remove(unit);

        if (selectedUnits.Remove(unit))
            unit.SetSelected(false);

        UnregisterTargetable(unit);
    }

    // ---------------------------------------------------------------------
    // Building registration
    // ---------------------------------------------------------------------

    public void RegisterBuilding(BuildingBase building)
    {
        if (building == null)
            return;

        if (!allBuildings.Contains(building))
            allBuildings.Add(building);

        RegisterTargetable(building);
    }

    public void UnregisterBuilding(BuildingBase building)
    {
        if (building == null)
            return;

        allBuildings.Remove(building);

        if (selectedBuilding == building)
            ClearSelectedBuilding();

        UnregisterTargetable(building);
    }

    // ---------------------------------------------------------------------
    // Resource Node registration
    // ---------------------------------------------------------------------

    public void RegisterResourceNode(ResourceNode resourceNode)
    {
        if (resourceNode == null)
            return;

        if (!allResourceNodes.Contains(resourceNode))
            allResourceNodes.Add(resourceNode);
    }

    public void UnregisterResourceNode(ResourceNode resourceNode)
    {
        if (resourceNode == null)
            return;

        allResourceNodes.Remove(resourceNode);

        if (selectedResourceNode == resourceNode)
            ClearSelectedResourceNode();
    }

    // ---------------------------------------------------------------------
    // Targetable registration
    // ---------------------------------------------------------------------

    public void RegisterTargetable(ITargetable targetable)
    {
        if (targetable == null)
            return;

        if (!allTargetables.Contains(targetable))
        {
            allTargetables.Add(targetable);
        }
    }

    public void UnregisterTargetable(ITargetable targetable)
    {
        if (targetable == null)
            return;

        allTargetables.Remove(targetable);
    }

    // ---------------------------------------------------------------------
    // Unit selection
    // ---------------------------------------------------------------------

    public void SelectUnit(UnitBase unit, bool append)
    {
        if (unit == null || !unit.CanBeSelected) 
            return;

        // Units and buildings cannot coexist in the active selection.
        ClearSelectedBuilding();
        ClearSelectedResourceNode();

        if (!append)
            ClearSelectedUnits();

        if (!selectedUnits.Contains(unit))
            selectedUnits.Add(unit);

        unit.SetSelected(true);
    }

    public void SelectUnits(IEnumerable<UnitBase> units, bool append)
    {
        if (units == null)
            return;

        // Units and buildings cannot coexist in the active selection.
        ClearSelectedBuilding();
        ClearSelectedResourceNode();

        if (!append)
            ClearSelectedUnits();

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

    // ---------------------------------------------------------------------
    // Building selection
    // ---------------------------------------------------------------------

    public void SelectBuilding(BuildingBase building)
    {
        if (building == null || !building.CanBeSelected)
            return;

        // A building selection always replaces the previous category.
        ClearSelectedUnits();
        ClearSelectedBuilding();
        ClearSelectedResourceNode();

        selectedBuilding = building;
        selectedBuilding.SetSelected(true);
    }

    public void ClearSelectedBuilding()
    {
        if (selectedBuilding == null)
            return;

        selectedBuilding.SetSelected(false);
        selectedBuilding = null;
    }

    // ---------------------------------------------------------------------
    // Building selection
    // ---------------------------------------------------------------------

    public void SelectResourceNode(ResourceNode resourceNode)
    {
        if (resourceNode == null || !resourceNode.CanBeSelected)
            return;

        ClearSelectedUnits();
        ClearSelectedBuilding();
        ClearSelectedResourceNode();

        selectedResourceNode = resourceNode;
        selectedResourceNode.SetSelected(true);
    }

    public void ClearSelectedResourceNode()
    {
        if (selectedResourceNode == null)
            return;

        selectedResourceNode.SetSelected(false);
        selectedResourceNode = null;
    }

    // ---------------------------------------------------------------------
    // General selection
    // ---------------------------------------------------------------------

    public void ClearSelection()
    {
        ClearSelectedUnits();
        ClearSelectedBuilding();
        ClearSelectedResourceNode();
    }

    // ---------------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------------

    public void Clear()
    {
        ClearSelection();

        allUnits.Clear();
        allBuildings.Clear();
        allTargetables.Clear();
        allResourceNodes.Clear();

        FactionManager = null;
        PlayerFaction = null;
        ProjectileManager = null;
        ResourceNodeRepository = null;
    }
}