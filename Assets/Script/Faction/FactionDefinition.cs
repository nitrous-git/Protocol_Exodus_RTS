using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Faction/Faction Definition")]
public class FactionDefinition : ScriptableObject
{
    [Header("Identity")]
    public string factionName = "Faction";
    public Color factionColor = Color.white;

    [Header("Control")]
    public bool isPlayerControlled;
}
