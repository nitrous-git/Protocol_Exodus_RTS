using UnityEngine;

public class Faction
{
    public FactionDefinition Definition { get; }
    //public UnitManager UnitManager { get; }
    //public ResourceManager ResourceManager { get; }
    //public IFactionController Controller { get; }

    public string Name => Definition != null ? Definition.factionName : "Unnamed Faction";
    public Color FactionColor => Definition != null ? Definition.factionColor : Color.white;
    //public bool IsPlayerControlled => Definition != null && Definition.isPlayerControlled;

    //public Faction(FactionDefinition definition, IFactionController controller)
    //{
    //    Definition = definition;
    //    ResourceManager = new ResourceManager();

    //    UnitManager = IsPlayerControlled
    //        ? new PlayerUnitManager(this)
    //        : new UnitManager(this);

    //    Controller = controller;
    //    Controller?.Initialize(this);
    //}

    //public void UpdateFaction()
    //{
    //    Controller?.UpdateFaction();
    //}

    //public bool IsEnemy(Faction other)
    //{
    //    return other != null && other != this;
    //}
}
