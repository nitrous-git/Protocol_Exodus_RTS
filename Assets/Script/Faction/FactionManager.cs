using System.Collections.Generic;

public sealed class FactionManager
{
    private readonly List<Faction> factions = new List<Faction>();

    public IReadOnlyList<Faction> Factions => factions;

    public void AddFaction(Faction faction)
    {
        if (faction == null)
            return;

        if (!factions.Contains(faction))
            factions.Add(faction);
    }

    public void RemoveFaction(Faction faction)
    {
        if (faction == null)
            return;

        factions.Remove(faction);
    }

    public void Tick(float deltaTime)
    {
        for (int i = 0; i < factions.Count; i++)
        {
            factions[i]?.Tick(deltaTime);
        }
    }

    public void TickLate(float deltaTime)
    {
        for (int i = 0; i < factions.Count; i++)
        {
            factions[i]?.TickLate(deltaTime);
        }
    }
}