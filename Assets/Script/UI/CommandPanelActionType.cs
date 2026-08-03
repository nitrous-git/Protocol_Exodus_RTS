public enum CommandPanelActionType
{
    None,

    // Immediate unit actions
    HoldPosition,

    // World-targeting actions
    BeginMoveTargeting,
    BeginAttackTargeting,
    BeginBuildingPlacement,
    BeginRallyPointTargeting,
    BeginGatherTargeting,
    BeginRepairTargeting,

    // Immediate production actions
    TrainUnit,
    CancelProduction,
    CancelConstruction,

    // Selection control
    ClearSelection,

    // Interaction control
    CancelInteraction
}