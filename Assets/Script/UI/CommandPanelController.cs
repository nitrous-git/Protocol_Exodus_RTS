using UnityEngine;

public sealed class CommandPanelController : MonoBehaviour
{
    private Faction playerFaction;
    private GameContext gameContext;

    public void Initialize(Faction playerFaction, GameContext gameContext)
    {
        this.playerFaction = playerFaction;
        this.gameContext = gameContext;

        RefreshCommands();
    }

    public void Tick(float deltaTime)
    {
        // Usually empty.
        // Keep this only if command availability needs continuous refresh.
    }

    public void RefreshCommands()
    {
        // Later:
        // No selection:
        //      default/build menu
        //
        // Worker selected:
        //      move, stop, attack, repair, gather, build
        //
        // Combat unit selected:
        //      move, stop, attack
        //
        // Barracks selected:
        //      train combat unit, set waypoint, cancel queue
        //
        // Command center selected:
        //      train worker, set waypoint, cancel queue
    }

    public void OnMoveButtonPressed()
    {
        // Enter move command mode.
    }

    public void OnStopButtonPressed()
    {
        // Issue stop to selected units.
    }

    public void OnBuildBarracksPressed()
    {
        // Enter building placement mode.
    }
}