using UnityEngine;

public sealed class GameLoop : MonoBehaviour
{
    private MatchWorld matchWorld;
    private MatchUIController matchUI;
    private PlayerFactionController playerController;

    private bool isInitialized;
    private bool isPaused;
    private int pauseChangedFrame = -1;

    public bool IsGameplayInputBlocked => isPaused || pauseChangedFrame == Time.frameCount ||
        SceneTransitionController.BlocksGameplay;

    public void Initialize(
        MatchWorld matchWorld,
        MatchUIController matchUI)
    {
        this.matchWorld = matchWorld;
        this.matchUI = matchUI;
        playerController = matchWorld?.PlayerFaction?.Controller as PlayerFactionController;

        isInitialized = true;
    }

    private void Update()
    {
        if (!isInitialized)
            return;

        float deltaTime = Time.deltaTime;

        bool escapeHandled = !SceneTransitionController.BlocksGameplay && HandleEscape();
        if (!IsGameplayInputBlocked && !escapeHandled)
            matchWorld?.TickInput(deltaTime);

        if (!IsGameplayInputBlocked)
            matchWorld?.TickSimulation(deltaTime);

        matchUI?.Tick(deltaTime);
    }

    private void LateUpdate()
    {
        if (!isInitialized || IsGameplayInputBlocked)
            return;

        float deltaTime = Time.deltaTime;

        matchWorld?.TickLate(deltaTime);
    }

    public void SetPaused(bool paused)
    {
        if (!isInitialized || isPaused == paused || SceneTransitionController.BlocksGameplay)
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
