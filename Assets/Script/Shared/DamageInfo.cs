public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly ITargetable Source;
    public readonly Faction SourceFaction;

    public DamageInfo(float amount, ITargetable source)
        : this(amount, source, source?.OwnerFaction)
    {
    }

    public DamageInfo(float amount, ITargetable source, Faction sourceFaction)
    {
        Amount = amount;
        Source = source;
        SourceFaction = sourceFaction;
    }
}
