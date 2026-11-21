using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

// PERF:
// Target acquisition currently scans GameContext.AllTargetables.
// Sensor scans are phase-staggered to distribute the workload,
// which is sufficient at current unit counts.
//
// At large army sizes, replace the global target scan with a
// spatial neighborhood query rather than increasing scan throttling.
public class UnitSensor : MonoBehaviour
{
    private static readonly ProfilerMarker SensorScanMarker = new("RTS.SensorScan");

    [SerializeField] private float sensorInterval = 0.2f;

    private UnitBase owner;
    private GameContext gameContext;
    private float timeUntilNextScan;

    public bool IsReady => timeUntilNextScan <= 0f;

    public void Initialize(UnitBase owner, GameContext gameContext)
    {
        this.owner = owner;
        this.gameContext = gameContext;

        // Golden Ratio hash for pseudo-random distribution
        // avoid synchronising all timer each frame, create a smooth distribution
        timeUntilNextScan = CalculateInitialScanDelay();
    }

    public void Tick(float deltaTime)
    {
        if (timeUntilNextScan > 0f)
            timeUntilNextScan -= deltaTime;
    }

    public ITargetable FindClosestEnemy(float maxRange)
    {
        return FindClosestEnemy(maxRange, default, float.PositiveInfinity, false, null, null);
    }

    internal ITargetable FindClosestEnemy(float maxRange, Vector3 areaCenter, float areaRadius,
        bool unitsOnly, ITargetable objective, HashSet<ITargetable> excluded)
    {

        if (owner == null || gameContext == null || owner.OwnerFaction == null)
            return null;

        if (!IsReady)
            return null;

        //timeUntilNextScan = sensorInterval;
        ScheduleNextScan();

        using (SensorScanMarker.Auto())
        {
            ITargetable closestTarget = null;
            float maxRangeSqr = maxRange * maxRange;
            float areaRadiusSqr = areaRadius * areaRadius;
            bool limitToArea = !float.IsPositiveInfinity(areaRadius);
            float bestDistanceSqr = float.MaxValue;
            Vector3 position = owner.Position;

            IReadOnlyList<ITargetable> targetables = gameContext.AllTargetables;

            for (int i = 0; i < targetables.Count; i++)
            {
                ITargetable candidate = targetables[i];
                if ((unitsOnly && candidate is not UnitBase) || candidate == objective)
                    continue;

                if (!IsValidEnemyTarget(candidate))
                    continue;

                Vector3 candidatePosition = candidate.Position;
                Vector3 difference = candidatePosition - position;

                difference.y = 0f;

                float distanceSqr = difference.sqrMagnitude;

                if (distanceSqr > maxRangeSqr)
                    continue;

                if (distanceSqr >= bestDistanceSqr)
                    continue;
                if (excluded?.Contains(candidate) == true)
                    continue;
                if (limitToArea)
                {
                    Vector3 fromCenter = candidatePosition - areaCenter;
                    fromCenter.y = 0f;
                    if (fromCenter.sqrMagnitude > areaRadiusSqr)
                        continue;
                }

                closestTarget = candidate;
                bestDistanceSqr = distanceSqr;
            }

            return closestTarget;
        }
    }

    public bool IsValidEnemyTarget(ITargetable candidate)
    {
        if (candidate == null || (candidate is Object unityObject && unityObject == null) || owner == null)
            return false;

        if (ReferenceEquals(candidate, owner) || !candidate.IsAlive)
            return false;

        if (owner.OwnerFaction == null || candidate.OwnerFaction == null)
            return false;

        return owner.OwnerFaction.IsEnemy(candidate.OwnerFaction);
    }

    // ---------------------------------------------------
    // Scan helper methods
    // ---------------------------------------------------

    private float CalculateInitialScanDelay()
    {
        if (owner == null || sensorInterval <= 0f)
            return 0f;

        uint hash = unchecked((uint)owner.UnitId * 2654435761u);
        float phase = (hash & 0xFFFFu) / 65535f;
        return phase * sensorInterval;
    }

    private void ScheduleNextScan()
    {
        if (sensorInterval <= 0f)
        {
            timeUntilNextScan = 0f;
            return;
        }

        timeUntilNextScan += sensorInterval;

        // Only needed after an unusually large hitch.
        while (timeUntilNextScan <= 0f)
            timeUntilNextScan += sensorInterval;
    }
}
