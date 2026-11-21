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
    private const int GatherSlot = 4;
    private const int RepairSlot = 5; 

    // No-selection presentation
    private const int BuildMenuSlot = 6;

    // Build Menu presentation
    private const int CommandCenterSlot = 0;
    private const int SupplyDepotSlot = 1;
    private const int BarracksSlot = 2;
    private const int BackSlot = 8;

    // Building command presentation
    private const int TrainUnitSlot = 0;
    private const int TrainMechaSlot = 1;
    private const int UpgradeSlot = 1;
    private const int SetRallyPointSlot = 5;
    private const int CloseBuildingSelectionSlot = 8;

    private bool lastSelectedBuildingOperational;

    [Header("Command Grid")]
    [SerializeField] private CommandSlotView[] commandSlots = new CommandSlotView[SlotCount];

    [SerializeField] private CommandIconLibrary icons;

    private BuildingBase lastSelectedBuilding;
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

        RefreshCommands(forceRefresh: true);
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

        if (!layoutChanged && selectedBuilding == lastSelectedBuilding && !operationalStateChanged && !forceRefresh)
            return;

        if (layoutChanged)
        {
            currentLayout = nextLayout;

            // A gameplay-selection change always closes local submenus.
            currentMenu = CommandPanelMenu.Main;
        }

        lastSelectedBuilding = selectedBuilding;
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
            case UnitType.Mech:
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
        ConfigureLocalSlot(BuildMenuSlot, icons?.BuildMenu, true, OpenBuildMenu);
    }

    private void ShowBuildMenu()
    {
        BuildingDefinition commandCenterDefinition = playerFaction.BuildingManager?.GetProducibleBuilding(BuildingType.CommandCenter);
        BuildingDefinition barracksDefinition = playerFaction.BuildingManager?.GetProducibleBuilding(BuildingType.Barracks);
        BuildingDefinition supplyDepotDefinition = playerFaction.BuildingManager?.GetProducibleBuilding(BuildingType.SupplyDepot);

        ConfigureGameplaySlot(CommandCenterSlot, commandCenterDefinition?.Icon, true, CommandPanelAction.PlaceBuilding(commandCenterDefinition));
        ConfigureGameplaySlot(SupplyDepotSlot, supplyDepotDefinition?.Icon, true, CommandPanelAction.PlaceBuilding(supplyDepotDefinition));
        ConfigureGameplaySlot(BarracksSlot, barracksDefinition?.Icon, true, CommandPanelAction.PlaceBuilding(barracksDefinition));
        ConfigureLocalSlot(BackSlot, icons?.Back, true, CloseBuildMenu);
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
        ConfigureGameplaySlot(MoveSlot, icons?.Move, true, CommandPanelAction.Move());
        ConfigureGameplaySlot(HoldPositionSlot, icons?.HoldPosition, true, CommandPanelAction.HoldPosition());
        ConfigureGameplaySlot(AttackSlot, icons?.Attack, false, CommandPanelAction.Attack());
        ConfigureGameplaySlot(GatherSlot, icons?.Gather, false, CommandPanelAction.Gather());
        ConfigureGameplaySlot(RepairSlot, icons?.Repair, false, CommandPanelAction.Repair());
    }

    private void ShowCombatUnitCommands()
    {
        ConfigureGameplaySlot(MoveSlot, icons?.Move, true, CommandPanelAction.Move());
        ConfigureGameplaySlot(HoldPositionSlot, icons?.HoldPosition, true, CommandPanelAction.HoldPosition());
        ConfigureGameplaySlot(AttackSlot, icons?.Attack, true, CommandPanelAction.Attack());
    }

    private void ShowMultipleUnitCommands()
    {
        ConfigureGameplaySlot(MoveSlot, icons?.Move, true, CommandPanelAction.Move());
        ConfigureGameplaySlot(HoldPositionSlot, icons?.HoldPosition, true, CommandPanelAction.HoldPosition());
        ConfigureGameplaySlot(AttackSlot, icons?.Attack, true, CommandPanelAction.Attack());
    }

    // ---------------------------------------------------------------------
    // Building layouts
    // ---------------------------------------------------------------------

    private void ShowCommandCenterCommands()
    {
        bool operational = CanOperateSelectedBuilding();

        UnitDefinition workerDefinition = GetSelectedBuildingUnitDefinition(UnitType.Worker);

        ConfigureGameplaySlot(TrainUnitSlot, workerDefinition?.Icon, operational, CommandPanelAction.TrainUnit(workerDefinition));
        ConfigureLocalSlot(UpgradeSlot, icons?.Upgrade, false, null);
        ConfigureGameplaySlot(SetRallyPointSlot, icons?.RallyPoint, operational, CommandPanelAction.SetRallyPoint());
        ConfigureGameplaySlot(CloseBuildingSelectionSlot, icons?.ClearSelection, true, CommandPanelAction.ClearSelection());
    }

    private void ShowBarracksCommands()
    {
        bool operational = CanOperateSelectedBuilding();

        UnitDefinition combatDefinition = GetSelectedBuildingUnitDefinition(UnitType.Combat);
        UnitDefinition mechDefinition = GetSelectedBuildingUnitDefinition(UnitType.Mech);

        ConfigureGameplaySlot(TrainUnitSlot, combatDefinition?.Icon, operational && combatDefinition != null, CommandPanelAction.TrainUnit(combatDefinition));
        ConfigureGameplaySlot(TrainMechaSlot, mechDefinition?.Icon, operational && mechDefinition != null, CommandPanelAction.TrainUnit(mechDefinition));
        ConfigureGameplaySlot(SetRallyPointSlot, icons?.RallyPoint, operational, CommandPanelAction.SetRallyPoint());
        ConfigureGameplaySlot(CloseBuildingSelectionSlot, icons?.ClearSelection, true, CommandPanelAction.ClearSelection());
    }

    private void ShowSupplyDepotCommands()
    {
        bool operational = CanOperateSelectedBuilding();
        ConfigureGameplaySlot(CloseBuildingSelectionSlot, icons?.ClearSelection, operational, CommandPanelAction.ClearSelection());
    }

    // ---------------------------------------------------------------------
    // Slot configuration
    // ---------------------------------------------------------------------

    private void ConfigureGameplaySlot(int slotIndex, Sprite icon, bool interactable, CommandPanelAction action)
    {
        CommandSlotView slot = GetSlot(slotIndex);

        if (slot == null)
            return;

        slot.SetClickAction(interactable ? () => HandleGameplayAction(action) : null);
        slot.SetVisual(string.Empty, icon, interactable);
    }

    private void ConfigureLocalSlot(int slotIndex, Sprite icon, bool interactable, UnityEngine.Events.UnityAction clickAction)
    {
        CommandSlotView slot = GetSlot(slotIndex);

        if (slot == null)
            return;

        slot.SetClickAction(interactable ? clickAction : null);
        slot.SetVisual(string.Empty, icon, interactable);
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

    private UnitDefinition GetSelectedBuildingUnitDefinition(UnitType unitType)
    {
        BuildingBase building = gameContext?.SelectedBuilding;

        if (building == null)
            return null;

        UnitProductionComponent production = building.UnitProduction;

        if (production == null)
            return null;

        return production.GetProducibleUnit(unitType);
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