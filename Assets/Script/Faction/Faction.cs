using UnityEngine;

public sealed class Faction
{
    public FactionDefinition Definition { get; }
    public IFactionController Controller { get; }
    public UnitManager UnitManager { get; }
    public BuildingManager BuildingManager { get; }
    public ResourceManager ResourceManager { get; }
    public FactionColorType ColorType { get; }
    public FactionColorVariant ColorVariant { get; }

    public string Name => Definition != null ? Definition.factionName : "Unnamed Faction";
    public Color FactionColor => ColorVariant.factionColor;
    public Color SelectionRingColor => ColorVariant.selectionRingColor;

    public Faction(
        FactionDefinition definition,
        FactionColorType colorType,
        IFactionController controller,
        UnitManager unitManager,
        BuildingManager buildingManager,
        ResourceManager resourceManager,
        GameContext gameContext)
    {
        Definition = definition;
        ColorType = colorType;
        Controller = controller;

        UnitManager = unitManager;
        BuildingManager = buildingManager;  
        ResourceManager = resourceManager;

        ColorVariant = definition.GetColorVariant(colorType);

        UnitManager?.SetOwnerFaction(this);
        BuildingManager?.SetOwnerFaction(this);

        Controller?.Initialize(this, gameContext);
    }

    public void TickInput(float deltaTime)
    {
        if (Controller is IPlayerInputController playerInputController)
            playerInputController.TickInput(deltaTime);
    }

    public void Tick(float deltaTime)
    {
        BuildingManager.Tick(deltaTime);
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