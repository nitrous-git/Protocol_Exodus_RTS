using UnityEngine;

public sealed class AIFactionController : IFactionController
{
    private Faction faction;
    private GameContext gameContext;

    public Vector3? BaseAnchor { get; private set; }

    internal void SetBaseAnchor(Vector3 position)
    {
        BaseAnchor = position;
    }

    public void Initialize(Faction faction, GameContext gameContext)
    {
        this.faction = faction;
        this.gameContext = gameContext;
    }

    public void Tick()
    {
        // AI Manager decision logic later.
    }
}
