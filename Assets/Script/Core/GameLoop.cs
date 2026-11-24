using UnityEngine;

public sealed class GameLoop : MonoBehaviour
{
    private enum MatchState { Initializing, Running, Ended }

    private MatchWorld matchWorld;
    private MatchUIController matchUI;
    private PlayerFactionController playerController;
    private MatchResultSystem matchResults;

    private MatchState state = MatchState.Initializing;
    private bool isPaused;
    private int pauseChangedFrame = -1;

    public bool IsGameplayInputBlocked => state != MatchState.Running || isPaused || pauseChangedFrame == Time.frameCount ||
        SceneTransitionController.BlocksGameplay;

    public void Initialize(
        MatchWorld matchWorld,
        MatchUIController matchUI,
        MatchResultSystem matchResults)
    {
        if (state != MatchState.Initializing)
            return;

        this.matchWorld = matchWorld;
        this.matchUI = matchUI;
        this.matchResults = matchResults;
        playerController = matchWorld?.PlayerFaction?.Controller as PlayerFactionController;

        state = MatchState.Running;
    }

    private void Update()
    {
        if (state != MatchState.Running)
            return;

        float deltaTime = Time.deltaTime;

        bool escapeHandled = !SceneTransitionController.BlocksGameplay && HandleEscape();
        if (!IsGameplayInputBlocked && !escapeHandled)
            matchWorld?.TickInput(deltaTime);

        if (!IsGameplayInputBlocked)
        {
            matchWorld?.TickSimulation(deltaTime);

            // Projectiles can deal damage after factions finish ticking.
            if (matchResults.TryResolve(out MatchResult result))
            {
                state = MatchState.Ended;
                isPaused = false;
                matchWorld?.ProjectileManager?.Clear();
                playerController?.CancelPlayerInput();
                matchUI?.ShowResult(result);
                return;
            }
        }

        matchUI?.Tick(deltaTime);
    }

    private void LateUpdate()
    {
        if (IsGameplayInputBlocked)
            return;

        float deltaTime = Time.deltaTime;

        matchWorld?.TickLate(deltaTime);
    }

    public void SetPaused(bool paused)
    {
        if (state != MatchState.Running || isPaused == paused || SceneTransitionController.BlocksGameplay)
            return;

        isPaused = paused;
        // A menu click can be dispatched before or after the world tick in the same frame.
        pauseChangedFrame = Time.frameCount;
        if (paused)
            playerController?.CancelPlayerInput();

        matchUI?.SetPaused(paused);
    }

    private bool HandleEscape()
    {
        if (pauseChangedFrame == Time.frameCount || !Input.GetKeyDown(KeyCode.Escape))
            return false;

        if (!isPaused && playerController?.TryCancelCurrentInteraction() == true)
            return true;

        if (isPaused || matchUI?.CanOpenPauseMenu == true)
        {
            SetPaused(!isPaused);
            return true;
        }

        return false;
    }
}
