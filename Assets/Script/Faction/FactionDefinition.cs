using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Faction/Faction Definition")]
public class FactionDefinition : ScriptableObject
{
    [Header("Identity")]
    public string factionName = "Faction";
    public Color factionColor = Color.white;
    public Color selectionRingColor = Color.white;

}
