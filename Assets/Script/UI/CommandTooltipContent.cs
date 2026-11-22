internal readonly struct CommandTooltipContent
{
    public static readonly CommandTooltipContent BuildMenu = new("Build Menu", "Choose a structure to place.");
    public static readonly CommandTooltipContent Back = new("Back", "Return to the main command menu.");
    public static readonly CommandTooltipContent Upgrade = new("Upgrade", "Upgrades are currently unavailable.");

    public string Title { get; }
    public string Description { get; }
    public Cost Cost { get; }

    private CommandTooltipContent(string title, string description, Cost cost = default)
    {
        Title = title;
        Description = description;
        Cost = cost;
    }

    public static CommandTooltipContent ForAction(CommandPanelAction action, bool interactable)
    {
        switch (action.Type)
        {
            case CommandPanelActionType.BeginBuildingPlacement when action.BuildingDefinition != null:
                BuildingDefinition building = action.BuildingDefinition;
                return new(
                    string.IsNullOrWhiteSpace(building.DisplayName) ? "Place Building" : building.DisplayName.Trim(),
                    "Choose a location to place this structure.", building.Cost);
            case CommandPanelActionType.TrainUnit when action.UnitDefinition != null:
                UnitDefinition unit = action.UnitDefinition;
                return new(
                    string.IsNullOrWhiteSpace(unit.DisplayName) ? "Train Unit" : unit.DisplayName.Trim(),
                    "Queue this unit for training.", unit.Cost);
            case CommandPanelActionType.BeginMoveTargeting:
                return new("Move", "Choose a destination for the selected units.");
            case CommandPanelActionType.HoldPosition:
                return new("Hold Position", "Order the selected units to hold their current position.");
            case CommandPanelActionType.BeginAttackTargeting:
                return interactable
                    ? new("Attack", "Choose an enemy to attack or a location to attack-move to.")
                    : new("Attack", "Attack is unavailable for this selection.");
            case CommandPanelActionType.BeginGatherTargeting:
                return new("Gather", "This button is unavailable. Right-click a resource node to gather.");
            case CommandPanelActionType.BeginRepairTargeting:
                return new("Repair", "The Repair command is currently unavailable.");
            case CommandPanelActionType.BeginRallyPointTargeting:
                return new("Set Rally Point", "Choose where newly trained units should go.");
            case CommandPanelActionType.ClearSelection:
                return new("Clear Selection", "Deselect the current building.");
            default:
                return default;
        }
    }
}
