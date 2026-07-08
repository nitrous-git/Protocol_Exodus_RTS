using System.Collections.Generic;
using UnityEngine;

public class UnitSensor : MonoBehaviour
{
    [SerializeField] private float sensorInterval = 0.2f;

    private UnitBase owner;
    private float nextSensorTime;

    public void Initialize(UnitBase owner)
    {
        this.owner = owner;
    }

    //public void TickSensor()
    //{
    //    if (owner == null || Time.time < nextSensorTime)
    //        return;

    //    nextSensorTime = Time.time + sensorInterval;

    //    if (!(owner is CombatUnit combatUnit))
    //        return;

    //    if (combatUnit.Definition == null || !combatUnit.Definition.canAttack)
    //        return;

    //    if (combatUnit.CurrentCommand != CommandType.Idle)
    //        return;

    //    ITargetable target = combatUnit.FindBestTargetInVision();

    //    if (target != null)
    //        combatUnit.IssueCommand(CommandType.Attack, CommandContext.AttackTarget(target));
    //}

    //public ITargetable FindClosestEnemy(float maxRange)
    //{
    //    if (owner == null || owner.OwnerFaction == null || GameContext.Instance == null)
    //        return null;

    //    float maxRangeSqr = maxRange * maxRange;
    //    ITargetable bestTarget = null;
    //    float bestDistanceSqr = float.MaxValue;

    //    IReadOnlyList<ITargetable> targetables = GameContext.Instance.AllTargetables;

    //    for (int i = 0; i < targetables.Count; i++)
    //    {
    //        ITargetable candidate = targetables[i];

    //        if (candidate == null)
    //            continue;

    //        if (ReferenceEquals(candidate, owner))
    //            continue;

    //        if (!candidate.IsAlive)
    //            continue;

    //        if (candidate.OwnerFaction == null)
    //            continue;

    //        if (!owner.OwnerFaction.IsEnemy(candidate.OwnerFaction))
    //            continue;

    //        float distanceSqr = (candidate.Position - owner.Position).sqrMagnitude;

    //        if (distanceSqr > maxRangeSqr)
    //            continue;

    //        if (distanceSqr < bestDistanceSqr)
    //        {
    //            bestDistanceSqr = distanceSqr;
    //            bestTarget = candidate;
    //        }
    //    }

    //    return bestTarget;
    //}
}
