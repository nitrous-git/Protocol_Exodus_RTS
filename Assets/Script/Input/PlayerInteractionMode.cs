/// <summary>
/// Defines how player pointer and keyboard input is currently
/// interpreted by PlayerFactionController.
///
/// </summary>
public enum PlayerInteractionMode
{
    /// <summary>
    /// Standard RTS interaction (Default) :
    /// - Primary pointer controls selection.
    /// - Secondary pointer issues the default context command.
    /// </summary>
    Default,
    BuildPlacement,
    AttackTarget
}