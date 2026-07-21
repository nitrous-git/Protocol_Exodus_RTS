using UnityEngine;

public sealed class GameLoop : MonoBehaviour
{
    private MatchWorld matchWorld;
    private MatchUIController matchUI;

    private bool isInitialized;
    private bool isPaused;

    public void Initialize(
        MatchWorld matchWorld,
        MatchUIController matchUI)
    {
        this.matchWorld = matchWorld;
        this.matchUI = matchUI; 

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

        matchUI?.Tick(deltaTime);
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