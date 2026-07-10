public sealed class AIFactionController : IFactionController
{
    private Faction faction;
    private GameContext gameContext;

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