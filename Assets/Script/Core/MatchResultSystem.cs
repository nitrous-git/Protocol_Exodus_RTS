using System.Collections.Generic;

public enum MatchOutcome
{
    Victory,
    Defeat,
    Draw
}

public readonly struct MatchResult
{
    public MatchOutcome Outcome { get; }
    public Faction Winner { get; }

    public MatchResult(MatchOutcome outcome, Faction winner)
    {
        Outcome = outcome;
        Winner = winner;
    }
}

public sealed class MatchResultSystem
{
    private readonly Faction[] participants;
    private readonly Faction localFaction;
    private bool hasResult;

    public bool IsEnabled => participants != null;

    // Create after all initial assets have been registered.
    public MatchResultSystem(IReadOnlyList<Faction> factions, Faction localFaction)
    {
        this.localFaction = localFaction;
        if (factions == null || factions.Count < 2 || localFaction == null)
            return;

        bool hasLocalFaction = false;
        for (int i = 0; i < factions.Count; i++)
        {
            Faction faction = factions[i];
            if (faction?.UnitManager == null || faction.BuildingManager == null || !HasLivingAssets(faction))
                return;

            hasLocalFaction |= faction == localFaction;
        }

        if (!hasLocalFaction)
            return;

        participants = new Faction[factions.Count];
        for (int i = 0; i < participants.Length; i++)
            participants[i] = factions[i];
    }

    public bool TryResolve(out MatchResult result)
    {
        result = default;
        if (!IsEnabled || hasResult)
            return false;

        Faction survivor = null;
        for (int i = 0; i < participants.Length; i++)
        {
            Faction faction = participants[i];
            if (!HasLivingAssets(faction))
                continue;

            if (survivor != null)
                return false;

            survivor = faction;
        }

        MatchOutcome outcome = survivor == null
            ? MatchOutcome.Draw
            : survivor == localFaction ? MatchOutcome.Victory : MatchOutcome.Defeat;
        result = new MatchResult(outcome, survivor);
        hasResult = true;
        return true;
    }

    private static bool HasLivingAssets(Faction faction)
    {
        IReadOnlyList<UnitBase> units = faction.UnitManager.UnitList;
        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit = units[i];
            if (unit != null && unit.IsAlive)
                return true;
        }

        IReadOnlyList<BuildingBase> buildings = faction.BuildingManager.BuildingList;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingBase building = buildings[i];
            if (building != null && building.IsAlive)
                return true;
        }

        return false;
    }
}
