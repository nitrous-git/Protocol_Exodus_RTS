/// <summary>
/// A gameplay request originating from the Command Panel.
///
/// The action type describes what the player wants to do.
/// Optional definitions identify the concrete content involved.
/// </summary>
public readonly struct CommandPanelAction
{
    public CommandPanelActionType Type { get; }

    public BuildingDefinition BuildingDefinition { get; }
    public UnitDefinition UnitDefinition { get; }

    private CommandPanelAction(
        CommandPanelActionType type,
        BuildingDefinition buildingDefinition = null,
        UnitDefinition unitDefinition = null)
    {
        Type = type;
        BuildingDefinition = buildingDefinition;
        UnitDefinition = unitDefinition;
    }

    public static CommandPanelAction HoldPosition()
    {
        return new CommandPanelAction(CommandPanelActionType.HoldPosition);
    }

    public static CommandPanelAction Move()
    {
        return new CommandPanelAction(CommandPanelActionType.BeginMoveTargeting);
    }

    public static CommandPanelAction Attack()
    {
        return new CommandPanelAction(CommandPanelActionType.BeginAttackTargeting);
    }

    public static CommandPanelAction Gather()
    {
        return new CommandPanelAction(CommandPanelActionType.BeginGatherTargeting);
    }

    public static CommandPanelAction Repair()
    {
        return new CommandPanelAction(CommandPanelActionType.BeginRepairTargeting);
    }

    public static CommandPanelAction PlaceBuilding(BuildingDefinition buildingDefinition)
    {
        return new CommandPanelAction(CommandPanelActionType.BeginBuildingPlacement, buildingDefinition, null);
    }

    public static CommandPanelAction TrainUnit(UnitDefinition unitDefinition)
    {
        return new CommandPanelAction(CommandPanelActionType.TrainUnit, null, unitDefinition);
    }

    public static CommandPanelAction SetRallyPoint()
    {
        return new CommandPanelAction(CommandPanelActionType.BeginRallyPointTargeting);
    }

    public static CommandPanelAction CancelProduction()
    {
        return new CommandPanelAction(CommandPanelActionType.CancelProduction);
    }

    public static CommandPanelAction CancelInteraction()
    {
        return new CommandPanelAction(CommandPanelActionType.CancelInteraction);
    }

    public static CommandPanelAction ClearSelection()
    {
        return new CommandPanelAction(CommandPanelActionType.ClearSelection);
    }
}