using UnityEngine;

/// <summary>
/// Runtime coordinator for all match HUD sections.
///
/// MatchUIController owns initialization and centralized ticking of the
/// Minimap, Selection, and Command panels. Individual panels remain
/// responsible for their own presentation behavior.
/// </summary>
[DisallowMultipleComponent]
public sealed class MatchUIController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private MinimapPanelController minimapPanel;
    [SerializeField] private SelectionPanelController selectionPanel;
    [SerializeField] private CommandPanelController commandPanel;

    private GameContext gameContext;
    private MatchWorld matchWorld;
    private Faction playerFaction;

    private bool isInitialized;

    public void Initialize(
        Faction playerFaction,
        GameContext gameContext,
        MatchWorld matchWorld)
    {
        this.playerFaction = playerFaction;
        this.gameContext = gameContext;
        this.matchWorld = matchWorld;

        ResolvePanelReferences();

        minimapPanel?.Initialize(gameContext, matchWorld);
        selectionPanel?.Initialize(playerFaction, gameContext);
        commandPanel?.Initialize(playerFaction, gameContext);

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
    }

    private void ResolvePanelReferences()
    {
        if (minimapPanel == null)
            minimapPanel = GetComponentInChildren<MinimapPanelController>(true);

        if (selectionPanel == null)
            selectionPanel = GetComponentInChildren<SelectionPanelController>(true);

        if (commandPanel == null)
            commandPanel = GetComponentInChildren<CommandPanelController>(true);
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