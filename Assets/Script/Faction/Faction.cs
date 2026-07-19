using UnityEngine;

public sealed class Faction
{
    public FactionDefinition Definition { get; }
    public IFactionController Controller { get; }
    public UnitManager UnitManager { get; }
    public ResourceManager ResourceManager { get; }

    public string Name => Definition != null ? Definition.factionName : "Unnamed Faction";
    public Color FactionColor => Definition != null ? Definition.factionColor : Color.white;
    public Color SelectionRingColor => Definition != null ? Definition.selectionRingColor : Color.white;

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

    public void TickInput(float deltaTime)
    {
        if (Controller is IPlayerInputController playerInputController)
            playerInputController.TickInput(deltaTime);
    }

    public void Tick(float deltaTime)
    {
        UnitManager?.Tick(deltaTime);
        Controller?.Tick();
    }

    public void TickLate(float deltaTime)
    {
        UnitManager?.TickLate(deltaTime);
    }

    public bool IsEnemy(Faction other)
    {
        return other != null && other != this;
    }

    public bool CanIssueCommandsTo(UnitBase unit)
    {
        if (unit == null || !unit.CanReceiveCommands)
            return false;

        if (unit.OwnerFaction == this)
            return true;

        // Later:
        // shared allied control
        // temporary mind control
        // transferred ownership
        // network authority

        return false;
    }
}