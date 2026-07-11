public sealed class PlayerFactionController : IFactionController
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
        // Player input is handled by SelectionManager and CommandIssuer for now.
        // CommandPanel and GUI later.
    }
}