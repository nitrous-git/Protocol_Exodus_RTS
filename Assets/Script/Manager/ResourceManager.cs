using System;

/// <summary>
/// Owns the mutable resource state of one faction.
///
/// Unit lifecycle and current population are intentionally owned
/// by UnitManager, not ResourceManager.
/// </summary>
public sealed class ResourceManager
{
    public int Minerals { get; private set; }
    public int Gas { get; private set; }
    public int MaxSupply { get; private set; }

    public ResourceManager(
    int startingMinerals = 0,
    int startingGas = 0,
    int startingMaxSupply = 0)
    {
        Minerals = Math.Max(0, startingMinerals);
        Gas = Math.Max(0, startingGas);
        MaxSupply = Math.Max(0, startingMaxSupply);
    }

    /// <summary>
    /// Checks only mineral and gas affordability.
    ///
    /// Population availability belongs to the faction/unit side
    /// and will be coordinated when production is implemented.
    /// </summary>
    public bool CanAffordResources(Cost cost)
    {
        return Minerals >= cost.Minerals && Gas >= cost.Gas;
    }

    /// <summary>
    /// Spends mineral and gas resources only.
    /// </summary>
    public bool TrySpendResources(Cost cost)
    {
        if (!CanAffordResources(cost))
            return false;

        Minerals -= cost.Minerals;
        Gas -= cost.Gas;

        return true;
    }

    public void AddMinerals(int amount)
    {
        if (amount <= 0)
            return;

        Minerals += amount;
    }

    public void AddGas(int amount)
    {
        if (amount <= 0)
            return;

        Gas += amount;
    }

    public void IncreaseMaxSupply(int amount)
    {
        if (amount <= 0)
            return;

        MaxSupply += amount;
    }

    public void DecreaseMaxSupply(int amount)
    {
        if (amount <= 0)
            return;

        MaxSupply = Math.Max(0, MaxSupply - amount);
    }
}