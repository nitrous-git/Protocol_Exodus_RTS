public interface IFactionController
{
    void Initialize(Faction faction, GameContext gameContext);
    void Tick();
}