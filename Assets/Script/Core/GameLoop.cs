using UnityEngine;

public sealed class GameLoop : MonoBehaviour
{
    private MatchWorld matchWorld;
    private MinimapPanelController minimapPanel;
    private SelectionPanelController selectionPanel;
    private CommandPanelController commandPanel;

    private bool isInitialized;
    private bool isPaused;

    public void Initialize(
        MatchWorld matchWorld,
        MinimapPanelController minimapPanel,
        SelectionPanelController selectionPanel,
        CommandPanelController commandPanel)
    {
        this.matchWorld = matchWorld;
        this.minimapPanel = minimapPanel;
        this.selectionPanel = selectionPanel;
        this.commandPanel = commandPanel;

        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized)
            return;

        float deltaTime = Time.deltaTime;

        matchWorld?.TickInput(deltaTime);

        if (!isPaused)
            matchWorld?.TickSimulation(deltaTime);

        minimapPanel?.Tick(deltaTime);
        selectionPanel?.Tick(deltaTime);

        // Usually event-driven, but useful if we need to refresh button state.
        commandPanel?.Tick(deltaTime);
    }

    private void LateUpdate()
    {
        if (!isInitialized)
            return;

        float deltaTime = Time.deltaTime;

        matchWorld?.TickLate(deltaTime);
    }

    public void SetPaused(bool paused)
    {
        isPaused = paused;
    }
}