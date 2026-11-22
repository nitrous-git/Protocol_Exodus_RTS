using UnityEngine;

/// <summary>
/// Runtime coordinator for all match HUD sections.
///
/// MatchUIController owns initialization and centralized ticking of the
/// Minimap, Selection, Command, Game Log, and Top Bar panels. Individual panels remain
/// responsible for their own presentation behavior.
/// </summary>
[DisallowMultipleComponent]
public sealed class MatchUIController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private MinimapPanelController minimapPanel;
    [SerializeField] private SelectionPanelController selectionPanel;
    [SerializeField] private CommandPanelController commandPanel;
    [SerializeField] private GameLogPanel gameLogPanel;
    [SerializeField] private TopBarPanelController topBarPanel;

    private GameContext gameContext;
    private MatchWorld matchWorld;
    private Faction playerFaction;

    private bool isInitialized;

    public bool CanOpenPauseMenu => topBarPanel != null && topBarPanel.CanOpenPauseMenu;

    public void Initialize(
        Faction playerFaction,
        GameContext gameContext,
        MatchWorld matchWorld,
        GameLoop gameLoop)
    {
        this.playerFaction = playerFaction;
        this.gameContext = gameContext;
        this.matchWorld = matchWorld;

        ResolvePanelReferences();

        minimapPanel?.Initialize(gameContext, matchWorld, gameLoop);
        selectionPanel?.Initialize(playerFaction, gameContext);
        commandPanel?.Initialize(playerFaction, gameContext);
        gameLogPanel?.Initialize(gameContext.GameLog);
        topBarPanel?.Initialize(playerFaction, gameLoop);

        isInitialized = true;
    }

    /// <summary>
    /// Centralized update for match HUD sections.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!isInitialized)
            return;

        minimapPanel?.Tick(deltaTime);
        selectionPanel?.Tick(deltaTime);
        commandPanel?.Tick(deltaTime);
        gameLogPanel?.Tick();
        topBarPanel?.Tick();
    }

    public void SetPaused(bool paused)
    {
        if (paused)
            minimapPanel?.CancelNavigation();

        commandPanel?.SetTooltipsPaused(paused);
        topBarPanel?.SetPauseMenuVisible(paused);
    }

    private void OnDisable()
    {
        if (commandPanel != null)
            commandPanel.HideTooltip();
    }

    private void ResolvePanelReferences()
    {
        if (minimapPanel == null)
            minimapPanel = GetComponentInChildren<MinimapPanelController>(true);

        if (selectionPanel == null)
            selectionPanel = GetComponentInChildren<SelectionPanelController>(true);

        if (commandPanel == null)
            commandPanel = GetComponentInChildren<CommandPanelController>(true);

        if (gameLogPanel == null)
            gameLogPanel = GetComponentInChildren<GameLogPanel>(true);

        if (topBarPanel == null)
            topBarPanel = GetComponentInChildren<TopBarPanelController>(true);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        ResolvePanelReferences();
    }

    private void OnValidate()
    {
        ResolvePanelReferences();
    }
#endif
}
