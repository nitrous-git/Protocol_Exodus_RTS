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

    // Interaction control
    CancelInteraction
}