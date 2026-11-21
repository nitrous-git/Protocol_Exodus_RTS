using UnityEngine;

public enum CombatMissionType
{
    None,
    AttackTarget,
    AttackFaction,
    DefendArea
}

public enum CombatUnitRole
{
    Defender,
    Attacker
}

public enum CombatMissionStatus
{
    None,
    Active,
    Completed,
    Failed,
    Cancelled,
    Retreated
}

public enum CombatTacticalBehavior
{
    Awareness,
    ObjectiveAttack,
    SelfDefense,
    Guard,
    Retreat,
    Recovering
}

public readonly struct CombatMission
{
    public CombatMissionType Type { get; }
    public ITargetable Target { get; }
    public Faction TargetFaction { get; }
    public Vector3 GuardPosition { get; }
    public float GuardRadius { get; }

    private CombatMission(CombatMissionType type, ITargetable target = null, Faction targetFaction = null,
        Vector3 guardPosition = default, float guardRadius = 0f)
    {
        Type = type;
        Target = target;
        TargetFaction = targetFaction;
        GuardPosition = guardPosition;
        GuardRadius = guardRadius;
    }

    public static CombatMission AttackTarget(ITargetable target) =>
        new(CombatMissionType.AttackTarget, target: target);

    public static CombatMission AttackFaction(Faction faction) =>
        new(CombatMissionType.AttackFaction, targetFaction: faction);

    /// <summary>Guards until cancelled, replaced, or unable to reach or hold the post.</summary>
    public static CombatMission DefendArea(Vector3 position, float radius) =>
        new(CombatMissionType.DefendArea, guardPosition: position, guardRadius: radius);
}
