using UnityEngine;

public sealed class SelectionPanelController : MonoBehaviour
{
    private Faction playerFaction;
    private GameContext gameContext;

    private int lastSelectionVersion = -1;

    public void Initialize(Faction playerFaction, GameContext gameContext)
    {
        this.playerFaction = playerFaction;
        this.gameContext = gameContext;

        Refresh();
    }

    public void Tick(float deltaTime)
    {
        if (gameContext == null)
            return;

        // MVP simple version:
        // Refresh every frame.
        // Later better:
        // Refresh only when selection version changes.
        Refresh();
    }

    private void Refresh()
    {
        // Later:
        // If no selection:
        //      show economy info from playerFaction.ResourceManager.
        //
        // If unit selected:
        //      show unit tag, health, faction, command/state.
        //
        // If building selected:
        //      show building health, queue, construction progress.
        //
        // If resource node selected:
        //      show resource amount.
    }
}