public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly UnitBase Source;
    public readonly Faction SourceFaction;

    public DamageInfo(float amount, UnitBase source)
    {
        Amount = amount;
        Source = source;
        SourceFaction = source != null ? source.OwnerFaction : null;
    }
}
