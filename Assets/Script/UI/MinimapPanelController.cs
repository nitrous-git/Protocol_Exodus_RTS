using UnityEngine;

public sealed class MinimapPanelController : MonoBehaviour
{
    private GameContext gameContext;
    private MatchWorld matchWorld;

    public void Initialize(GameContext gameContext, MatchWorld matchWorld)
    {
        this.gameContext = gameContext;
        this.matchWorld = matchWorld;

        // Later:
        // Build static terrain/minimap texture.
        // Cache unit dot UI pool.
    }

    public void Tick(float deltaTime)
    {
        if (gameContext == null || matchWorld == null)
            return;

        // Later:
        // Update unit dots from gameContext.AllUnits.
        // Update camera viewport rectangle.
    }
}