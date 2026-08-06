using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Faction/Faction Definition")]
public class FactionDefinition : ScriptableObject
{
    [Header("Identity")]
    public string factionName = "Faction";
    public Color factionColor = Color.white;
    public Color selectionRingColor = Color.white;

    [Header("Starting Economy")]
    [Min(0)] public int startingMinerals = 0;
    [Min(0)] public int startingGas = 0;
    [Min(0)] public int startingMaxSupply = 10;

    [Header("Faction Color Palette")]
    public Color combatUnitColor = Color.white;
    public Color workerUnitColor = Color.white;
    public Color barracksColor = Color.white;
    public Color commandCenterColor = Color.white;
    public Color supplyDepotColor = Color.white;
    public Color selectionRing_BuildingColor = Color.white;
    public Color selectionRing_UnitColor = Color.white;
}
