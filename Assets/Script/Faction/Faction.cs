using UnityEngine;

public sealed class Faction
{
    public FactionDefinition Definition { get; }
    public IFactionController Controller { get; }
    public UnitManager UnitManager { get; }
    public ResourceManager ResourceManager { get; }

    public string Name => Definition != null ? Definition.factionName : "Unnamed Faction";
    public Color FactionColor => Definition != null ? Definition.factionColor : Color.white;
    public bool IsPlayerControlled => Definition != null && Definition.isPlayerControlled;

    public Faction(
        FactionDefinition definition,
        IFactionController controller,
        UnitManager unitManager,
        ResourceManager resourceManager,
        GameContext gameContext)
    {
        Definition = definition;
        Controller = controller;
        UnitManager = unitManager;
        ResourceManager = resourceManager;

        UnitManager?.SetOwnerFaction(this);
        Controller?.Initialize(this, gameContext);
    }

    public void Tick()
    {
        Controller?.Tick();
    }

    public bool IsEnemy(Faction other)
    {
        return other != null && other != this;
    }
}