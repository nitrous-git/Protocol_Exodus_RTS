using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Faction/Faction Definition")]
public class FactionDefinition : ScriptableObject
{
    [Header("Identity")]
    public string factionName = "Faction";

    [Header("Starting Economy")]
    [Min(0)] public int startingMinerals = 0;
    [Min(0)] public int startingGas = 0;
    [Min(0)] public int startingMaxSupply = 10;

    [Header("Roster")]
    [SerializeField] private List<UnitDefinition> unitRoster = new();

    [Header("Faction Colors")]
    public FactionColorVariant[] colorVariants;

    [Header("Roster")]
    public IReadOnlyList<UnitDefinition> UnitRoster => unitRoster;

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    public UnitDefinition GetUnitDefinition(UnitType unitType)
    {
        for (int i = 0; i < unitRoster.Count; i++)
        {
            UnitDefinition definition = unitRoster[i];

            if (definition != null && definition.Type == unitType)
                return definition;
        }

        return null;
    }

    public FactionColorVariant GetColorVariant(FactionColorType colorType)
    {
        for (int i = 0; i < colorVariants.Length; i++)
        {
            if (colorVariants[i].colorType == colorType)
            {
                return colorVariants[i];
            }
        }

        return null;
    }
}
