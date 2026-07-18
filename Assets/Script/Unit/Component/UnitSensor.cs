using System.Collections.Generic;
using UnityEngine;

public class UnitSensor : MonoBehaviour
{
    [SerializeField] private float sensorInterval = 0.2f;

    private UnitBase owner;
    private GameContext gameContext;
    private float timeUntilNextScan;

    public bool IsReady => timeUntilNextScan <= 0f;

    public void Initialize(UnitBase owner, GameContext gameContext)
    {
        this.owner = owner;
        this.gameContext = gameContext;

        // Permit an immediate first scan after initialization.
        timeUntilNextScan = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (timeUntilNextScan > 0f)
            timeUntilNextScan -= deltaTime;
    }

    public ITargetable FindClosestEnemy(float maxRange)
    {

        if (owner == null || gameContext == null || owner.OwnerFaction == null)
            return null;

        if (!IsReady)
            return null;

        timeUntilNextScan = sensorInterval;

        ITargetable closestTarget = null;
        float maxRangeSqr = maxRange * maxRange;
        float bestDistanceSqr = float.MaxValue;

        IReadOnlyList<ITargetable> targetables = gameContext.AllTargetables;

        for (int i = 0; i < targetables.Count; i++)
        {
            ITargetable candidate = targetables[i];

            if (!IsValidEnemyTarget(candidate))
                continue;

            Vector3 difference = candidate.Position - owner.Position;

            difference.y = 0f;

            float distanceSqr = difference.sqrMagnitude;

            if (distanceSqr > maxRangeSqr)
                continue;

            if (distanceSqr >= bestDistanceSqr)
                continue;

            closestTarget = candidate;
            bestDistanceSqr = distanceSqr;
        }

        return closestTarget;
    }

    public bool IsValidEnemyTarget(ITargetable candidate)
    {
        if (candidate == null || owner == null)
            return false;

        if (ReferenceEquals(candidate, owner) || !candidate.IsAlive)
            return false;

        if (owner.OwnerFaction == null || candidate.OwnerFaction == null)
            return false;

        return owner.OwnerFaction.IsEnemy(candidate.OwnerFaction);
    }
}
