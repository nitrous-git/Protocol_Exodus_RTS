using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Presents the command layout appropriate for the current selection
/// and routes gameplay requests to the human faction controller.
///
/// Panel-only navigation, such as opening the Build Menu,
/// remains local to this controller.
/// </summary>
public sealed class CommandPanelController : MonoBehaviour
{
    private CommandPanelMenu currentMenu = CommandPanelMenu.Main;

    private const int SlotCount = 9;

    // Grid indices:
    //
    // 0 1 2
    // 3 4 5
    // 6 7 8

    // Unit command presentation
    private const int MoveSlot = 0;
    private const int HoldPositionSlot = 1;
    private const int AttackSlot = 2;

    // No-selection presentation
    private const int BuildMenuSlot = 6;

    // Build Menu presentation
    private const int CommandCenterSlot = 0;
    private const int SupplyDepotSlot = 1;
    private const int BarracksSlot = 2;
    private const int BackSlot = 8;

    // Building command presentation
    private const int TrainUnitSlot = 0;
    private const int UpgradeSlot = 1;
    private const int RallyPointSlot = 5;
    private const int ClearSelectionSlot = 8;

    private bool lastSelectedBuildingOperational;

    [Header("Command Grid")]
    [SerializeField] private CommandSlotView[] commandSlots = new CommandSlotView[SlotCount];

    [Header("Building Definition")]
    [SerializeField] private BuildingDefinition commandCenterDefinition;
    [SerializeField] private BuildingDefinition supplyDepotDefinition;
    [SerializeField] private BuildingDefinition barracksDefinition;

    [Header("Unit Definitions")]
    [SerializeField] private UnitDefinition workerUnitDefinition;
    [SerializeField] private UnitDefinition combatUnitDefinition;

    private Faction playerFaction;
    private GameContext gameContext;

    private CommandPanelLayout currentLayout = CommandPanelLayout.Uninitialized;

    private bool isInitialized;

    public void Initialize(Faction playerFaction, GameContext gameContext)
    {
        this.playerFaction = playerFaction;
        this.gameContext = gameContext;

        if (!HasValidSlotConfiguration())
        {
            Debug.LogError("CommandPanelController requires exactly nine assigned CommandSlotView references.", this);
            return;
        }

        isInitialized = true;

        RefreshCommands();
    }

    /// <summary>
    /// Called by MatchUIController through the centralized GameLoop.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!isInitialized)
            return;

        RefreshCommands();
    }

    // ---------------------------------------------------------------------
    // Refresh
    // ---------------------------------------------------------------------

    public void RefreshCommands(bool forceRefresh = false)
    {
        CommandPanelLayout nextLayout = DetermineLayout();

        BuildingBase selectedBuilding = gameContext.SelectedBuilding;
        bool selectedBuildingOperational = selectedBuilding != null && selectedBuilding.IsOperational;

        bool layoutChanged = nextLayout != currentLayout;
        bool operationalStateChanged = selectedBuildingOperational != lastSelectedBuildingOperational;

        if (!layoutChanged && !operationalStateChanged && !forceRefresh)
            return;

        if (layoutChanged)
        {
            currentLayout = nextLayout;

            // A gameplay-selection change always closes local submenus.
            currentMenu = CommandPanelMenu.Main;
        }

        lastSelectedBuildingOperational = selectedBuildingOperational;

        ClearAllSlots();
        ShowCurrentLayoutCommands();
    }

    private void ShowCurrentLayoutCommands() {
        switch (currentLayout)
        {
            case CommandPanelLayout.NoSelection:
                ShowNoSelectionCommands();
                break;

            case CommandPanelLayout.WorkerUnit:
                ShowWorkerUnitCommands();
                break;

            case CommandPanelLayout.CombatUnit:
                ShowCombatUnitCommands();
                break;

            case CommandPanelLayout.MultipleUnits:
                ShowMultipleUnitCommands();
                break;

            case CommandPanelLayout.CommandCenter:
                ShowCommandCenterCommands();
                break;

            case CommandPanelLayout.Barracks:
                ShowBarracksCommands();
                break;

            case CommandPanelLayout.SupplyDepot:
                ShowSupplyDepotCommands();
                break;

            case CommandPanelLayout.Empty:
            case CommandPanelLayout.Uninitialized:
            default:
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Layout resolution
    // ---------------------------------------------------------------------

    private CommandPanelLayout DetermineLayout()
    {
        if (playerFaction == null || gameContext == null)
            return CommandPanelLayout.Empty;

        IReadOnlyList<UnitBase> selectedUnits = gameContext.SelectedUnits;

        if (selectedUnits != null && selectedUnits.Count > 0)
            return DetermineUnitLayout(selectedUnits);

        BuildingBase selectedBuilding = gameContext.SelectedBuilding;

        if (selectedBuilding != null)
            return DetermineBuildingLayout(selectedBuilding);

        return CommandPanelLayout.NoSelection;
    }

    private CommandPanelLayout DetermineUnitLayout(IReadOnlyList<UnitBase> selectedUnits)
    {
        UnitBase firstSelectedUnit = null;
        int validSelectionCount = 0;

        for (int i = 0; i < selectedUnits.Count; i++)
        {
            UnitBase unit = selectedUnits[i];

            if (unit == null)
                continue;

            // Enemy, mixed-faction, or otherwise non-commandable
            // selections display no gameplay commands.
            if (!playerFaction.CanIssueCommandsTo(unit))
                return CommandPanelLayout.Empty;

            if (firstSelectedUnit == null)
            {
                firstSelectedUnit = unit;
            }

            validSelectionCount++;
        }

        if (validSelectionCount == 0)
            return CommandPanelLayout.NoSelection;

        if (validSelectionCount > 1)
            return CommandPanelLayout.MultipleUnits;

        if (firstSelectedUnit.Definition == null)
            return CommandPanelLayout.Empty;

        switch (firstSelectedUnit.Definition.Type)
        {
            case UnitType.Worker:
                return CommandPanelLayout.WorkerUnit;

            case UnitType.Combat:
                return CommandPanelLayout.CombatUnit;

            default:
                return CommandPanelLayout.Empty;
        }
    }

    private CommandPanelLayout DetermineBuildingLayout(BuildingBase selectedBuilding)
    {
        if (selectedBuilding.Definition == null)
            return CommandPanelLayout.Empty;

        // Enemy buildings remain selectable for information,
        // but their commands are not available to this player.
        if (selectedBuilding.OwnerFaction != playerFaction)
            return CommandPanelLayout.Empty;

        switch (selectedBuilding.Definition.Type)
        {
            case BuildingType.CommandCenter:
                return CommandPanelLayout.CommandCenter;

            case BuildingType.Barracks:
                return CommandPanelLayout.Barracks;

            case BuildingType.SupplyDepot:
                return CommandPanelLayout.SupplyDepot;

            default:
                return CommandPanelLayout.Empty;
        }
    }

    // ---------------------------------------------------------------------
    // No-selection menus
    // ---------------------------------------------------------------------

    private void ShowNoSelectionCommands()
    {
        switch (currentMenu)
        {
            case CommandPanelMenu.Main:
                ShowMainMenu();
                break;

            case CommandPanelMenu.Build:
                ShowBuildMenu();
                break;
        }
    }

    private void ShowMainMenu()
    {
        ConfigureLocalSlot(BuildMenuSlot, "B", true, OpenBuildMenu);
    }

    private void ShowBuildMenu()
    {
        // Visible but disabled until BuildingPlacement is implemented.
        ConfigureGameplaySlot(CommandCenterSlot, "C", true, CommandPanelAction.PlaceBuilding(commandCenterDefinition));
        ConfigureGameplaySlot(SupplyDepotSlot, "S", true, CommandPanelAction.PlaceBuilding(supplyDepotDefinition));
        ConfigureGameplaySlot(BarracksSlot, "B", true, CommandPanelAction.PlaceBuilding(barracksDefinition));
        ConfigureLocalSlot(BackSlot, "X", true, CloseBuildMenu);
    }

    private void OpenBuildMenu()
    {
        if (currentMenu == CommandPanelMenu.Build)
            return;

        currentMenu = CommandPanelMenu.Build;

        RefreshCommands(forceRefresh: true);
    }

    private void CloseBuildMenu()
    {
        if (currentMenu == CommandPanelMenu.Main)
            return;

        currentMenu = CommandPanelMenu.Main;

        PlayerFactionController controller = playerFaction.Controller as PlayerFactionController;
        controller.CancelCurrentInteraction();

        RefreshCommands(forceRefresh: true);
    }

    // ---------------------------------------------------------------------
    // Unit layouts
    // ---------------------------------------------------------------------

    private void ShowWorkerUnitCommands()
    {
        ConfigureGameplaySlot(MoveSlot,"M", true, CommandPanelAction.Move());
        ConfigureGameplaySlot(HoldPositionSlot,"H", true, CommandPanelAction.HoldPosition());
        ConfigureGameplaySlot(AttackSlot,"A", false, CommandPanelAction.Attack());

        // Later:
        // Repair
        // Gather
    }

    private void ShowCombatUnitCommands()
    {
        ConfigureGameplaySlot(MoveSlot, "M", true, CommandPanelAction.Move());
        ConfigureGameplaySlot(HoldPositionSlot, "H", true, CommandPanelAction.HoldPosition());
        ConfigureGameplaySlot(AttackSlot, "A", false, CommandPanelAction.Attack());
    }

    private void ShowMultipleUnitCommands()
    {
        ConfigureGameplaySlot(MoveSlot, "M", true, CommandPanelAction.Move());
        ConfigureGameplaySlot(HoldPositionSlot, "H", true, CommandPanelAction.HoldPosition());
        ConfigureGameplaySlot(AttackSlot, "A", false, CommandPanelAction.Attack());
    }

    // ---------------------------------------------------------------------
    // Building layouts
    // ---------------------------------------------------------------------

    private void ShowCommandCenterCommands()
    {
        bool operational = CanOperateSelectedBuilding();
        ConfigureGameplaySlot(TrainUnitSlot, "W", operational, CommandPanelAction.TrainUnit(workerUnitDefinition));
        ConfigureDisabledSlot(UpgradeSlot, "U");
        ConfigureGameplaySlot(RallyPointSlot, "R", operational, CommandPanelAction.SetRallyPoint());
        ConfigureGameplaySlot(ClearSelectionSlot, "X", true, CommandPanelAction.ClearSelection());
    }

    private void ShowBarracksCommands()
    {
        bool operational = CanOperateSelectedBuilding();
        ConfigureGameplaySlot(TrainUnitSlot, "C", operational, CommandPanelAction.TrainUnit(combatUnitDefinition));
        ConfigureDisabledSlot(UpgradeSlot, "U");
        ConfigureGameplaySlot(RallyPointSlot, "R", operational, CommandPanelAction.SetRallyPoint());
        ConfigureGameplaySlot(ClearSelectionSlot, "X", true,CommandPanelAction.ClearSelection());
    }

    private void ShowSupplyDepotCommands()
    {
        bool operational = CanOperateSelectedBuilding();
        ConfigureGameplaySlot(ClearSelectionSlot, "X", operational, CommandPanelAction.ClearSelection());
    }

    // ---------------------------------------------------------------------
    // Slot configuration
    // ---------------------------------------------------------------------

    private void ConfigureGameplaySlot(int slotIndex, string label, bool interactable, CommandPanelAction action)
    {
        CommandSlotView slot = GetSlot(slotIndex);

        if (slot == null)
            return;

        slot.SetClickAction(interactable ? () => HandleGameplayAction(action) : null);
        slot.SetVisual(label, null, interactable);
    }

    private void ConfigureLocalSlot(int slotIndex, string label, bool interactable, UnityEngine.Events.UnityAction clickAction)
    {
        CommandSlotView slot = GetSlot(slotIndex);

        if (slot == null)
            return;

        slot.SetClickAction(interactable ? clickAction : null);
        slot.SetVisual(label, null, interactable);
    }

    private void ConfigureDisabledSlot(int slotIndex, string label)
    {
        CommandSlotView slot = GetSlot(slotIndex);

        if (slot == null)
            return;

        slot.SetClickAction(null);
        slot.SetVisual(label, null, false);
    }

    private void HandleGameplayAction(CommandPanelAction action)
    {
        PlayerFactionController playerController = playerFaction?.Controller as PlayerFactionController;
        playerController?.HandleCommandPanelAction(action);
    }

    private CommandSlotView GetSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= commandSlots.Length)
        {
            return null;
        }

        return commandSlots[slotIndex];
    }

    private void ClearAllSlots()
    {
        for (int i = 0; i < commandSlots.Length; i++)
        {
            commandSlots[i]?.ClearVisual();
        }
    }

    private bool HasValidSlotConfiguration()
    {
        if (commandSlots == null || commandSlots.Length != SlotCount)
        {
            return false;
        }

        for (int i = 0; i < commandSlots.Length; i++)
        {
            if (commandSlots[i] == null)
                return false;
        }

        return true;
    }

#if UNITY_EDITOR

    [ContextMenu("Collect Command Slots")]
    private void CollectCommandSlots()
    {
        commandSlots = transform.GetChild(1).GetComponentsInChildren<CommandSlotView>(true);
    }

#endif

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------
    private bool CanOperateSelectedBuilding()
    {
        BuildingBase building = gameContext.SelectedBuilding;
        return building != null && building.OwnerFaction == playerFaction && building.IsOperational && building.IsAlive;
    }

    // ---------------------------------------------------------------------
    // Internal presentation state
    // ---------------------------------------------------------------------

    // CommandPanelMenu
    // Describes which local panel page is open
    private enum CommandPanelMenu
    {
        Main,
        Build
    }

    // CommandPanelLayout
    // Describes the selected gameplay entities
    private enum CommandPanelLayout
    {
        Uninitialized,
        Empty,
        NoSelection,

        WorkerUnit,
        CombatUnit,
        MultipleUnits,

        CommandCenter,
        Barracks,
        SupplyDepot
    }
}