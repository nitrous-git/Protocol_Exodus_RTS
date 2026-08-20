using System.Collections.Generic;
using UnityEngine;

public class LocalSteeringSystem : MonoBehaviour
{
    [Header("Separation")]
    [SerializeField] private float separationPadding = 0.25f;
    [SerializeField] private float separationWeight = 0.0f; // 1.0f;

    [Header("Predictive Avoidance")]
    [SerializeField] private float avoidancePadding = 0.30f;
    [SerializeField] private float avoidanceWeight = 1.2f;

    private float predictionTime = 2.2f;

    [Header("Debug")]
    [SerializeField] private bool drawSteeringGizmos = true;
    [SerializeField] private int gizmoCircleSegments = 32;

    private UnitBase owner;
    private IReadOnlyList<UnitBase> allUnits;

    private Vector3 debugSeparation;
    private Vector3 debugAvoidance;

    private bool debugPredictionCandidate;
    private bool debugThreatDetected;

    private Vector3 debugOwnerPosition;
    private Vector3 debugOtherPosition;

    private Vector3 debugOwnerVelocity;
    private Vector3 debugOtherVelocity;

    private float debugTimeToClosestApproach;
    private float debugClosestDistance;
    private float debugAvoidanceDistance;

    public void Initialize(UnitBase owner, IReadOnlyList<UnitBase> allUnits)
    {
        this.owner = owner;
        this.allUnits = allUnits;
    }

    public Vector3 CalculateVelocity(Vector3 preferredVelocity, float maxSpeed)
    {
        if (owner == null || allUnits == null)
            return preferredVelocity;

        Vector3 separation = CalculateSeparation();
        Vector3 avoidance = CalculatePredictiveAvoidance(preferredVelocity);

        debugAvoidance = avoidance;

        Vector3 velocity = 
            preferredVelocity 
            + avoidance * maxSpeed * avoidanceWeight;

        //+separation * maxSpeed * separationWeight

        velocity.y = 0f;

        return Vector3.ClampMagnitude(velocity, maxSpeed);
    }

    // ---------------------------------------------------------------------
    // Separation
    // ---------------------------------------------------------------------

    private Vector3 CalculateSeparation()
    {
        Vector3 separation = Vector3.zero;

        float ownerRadius = GetNavigationRadius(owner);

        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitBase other = allUnits[i];

            if (other == null || other == owner || !other.IsAlive)
            {
                continue;
            }

            Vector3 away = owner.Position - other.Position;
            away.y = 0f;

            float distanceSquared = away.sqrMagnitude;

            float separationDistance = ownerRadius + GetNavigationRadius(other) + separationPadding;

            if (distanceSquared >= separationDistance * separationDistance)
                continue;

            if (distanceSquared <= 0.0001f)
            {
                separation += GetOverlapDirection(other);
                continue;
            }

            float distance = Mathf.Sqrt(distanceSquared);
            float strength = 1f - distance / separationDistance;

            separation += away / distance * strength;
        }

        debugSeparation = Vector3.ClampMagnitude(separation, 1f);
        return debugSeparation;
    }

    private float GetNavigationRadius(UnitBase unit)
    {
        if (unit == null || unit.Definition == null)
            return 0.45f;

        return unit.Definition.NavigationRadius;
    }

    private Vector3 GetOverlapDirection(UnitBase other)
    {
        if (owner.UnitId < other.UnitId)
            return Vector3.right;

        return Vector3.left;
    }

    // ---------------------------------------------------------------------
    // Predictive Avoidance
    // ---------------------------------------------------------------------

    private Vector3 CalculatePredictiveAvoidance(Vector3 preferredVelocity)
    {
        ResetPredictiveDebug();

        if (preferredVelocity.sqrMagnitude <= Mathf.Epsilon)
            return Vector3.zero;

        float ownerRadius = GetNavigationRadius(owner);

        Vector3 ownerVelocity = preferredVelocity;

        if (owner.Motor != null &&
            owner.Motor.CurrentVelocity.sqrMagnitude > 0.0001f)
        {
            ownerVelocity = owner.Motor.CurrentVelocity;
        }

        ownerVelocity.y = 0f;

        UnitBase closestThreat = null;

        float earliestCollisionTime = float.PositiveInfinity;
        Vector3 closestFutureOffset = Vector3.zero;
        float closestAvoidanceDistance = 0f;
        Vector3 closestOtherVelocity = Vector3.zero;

        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitBase other = allUnits[i];

            if (other == null ||
                other == owner ||
                !other.IsAlive)
            {
                continue;
            }

            Vector3 relativePosition =
                other.Position - owner.Position;

            relativePosition.y = 0f;

            Vector3 otherVelocity = Vector3.zero;

            if (other.Motor != null)
            {
                otherVelocity = other.Motor.CurrentVelocity;
            }

            otherVelocity.y = 0f;

            Vector3 relativeVelocity =
                otherVelocity - ownerVelocity;

            float relativeSpeedSquared =
                relativeVelocity.sqrMagnitude;

            if (relativeSpeedSquared <= 0.0001f)
                continue;

            float timeToClosestApproach =
                -Vector3.Dot(relativePosition, relativeVelocity)
                / relativeSpeedSquared;

            // Closest approach is behind us:
            // units are no longer approaching.
            if (timeToClosestApproach <= 0f)
                continue;

            Vector3 futureOffset =
                relativePosition +
                relativeVelocity * timeToClosestApproach;

            float avoidanceDistance =
                ownerRadius +
                GetNavigationRadius(other) +
                avoidancePadding;

            // Closest approach is safe.
            if (futureOffset.sqrMagnitude >=
                avoidanceDistance * avoidanceDistance)
            {
                continue;
            }

            // We now KNOW this unit is on a collision course.
            // Keep the collision that will happen first.
            if (timeToClosestApproach >= earliestCollisionTime)
                continue;

            closestThreat = other;

            earliestCollisionTime =
                timeToClosestApproach;

            closestFutureOffset =
                futureOffset;

            closestAvoidanceDistance =
                avoidanceDistance;

            closestOtherVelocity =
                otherVelocity;
        }

        // No unit is currently on a predicted collision course.
        if (closestThreat == null)
            return Vector3.zero;

        // -------------------------------------------------------------
        // DEBUG
        // -------------------------------------------------------------

        debugPredictionCandidate = true;

        debugOwnerPosition = owner.Position;
        debugOtherPosition = closestThreat.Position;

        debugOwnerVelocity = ownerVelocity;
        debugOtherVelocity = closestOtherVelocity;

        debugTimeToClosestApproach =
            earliestCollisionTime;

        debugClosestDistance =
            closestFutureOffset.magnitude;

        debugAvoidanceDistance =
            closestAvoidanceDistance;

        // -------------------------------------------------------------
        // PREDICTION HORIZON
        // -------------------------------------------------------------

        if (earliestCollisionTime > predictionTime)
        {
            // Collision exists, but it is still too far in the future.
            // Debug remains yellow.
            return Vector3.zero;
        }

        // SAME candidate, SAME TCA.
        // It has now entered our prediction horizon.
        debugThreatDetected = true;

        return CalculateAvoidanceDirection(
            preferredVelocity,
            closestFutureOffset,
            earliestCollisionTime,
            closestAvoidanceDistance);
    }

    private Vector3 CalculateAvoidanceDirection(
        Vector3 preferredVelocity,
        Vector3 futureOffset,
        float collisionTime,
        float avoidanceDistance)
    {
        Vector3 forward = preferredVelocity.normalized;
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);

        float sideOffset = Vector3.Dot(futureOffset, right);

        // Treat nearly head-on encounters as symmetric.
        // Using "own right" for both head-on units causes them
        // to pass on opposite world-space sides.
        float sideDeadZone = avoidanceDistance * 0.15f;

        Vector3 avoidanceDirection;

        if (Mathf.Abs(sideOffset) <= sideDeadZone)
        {
            avoidanceDirection = right;
        }
        else
        {
            avoidanceDirection = sideOffset > 0f ? -right : right;
        }

        float futureDistance = futureOffset.magnitude;

        float distanceUrgency = 1f - Mathf.Clamp01(futureDistance / avoidanceDistance);

        float timeUrgency =  1f - Mathf.Clamp01(collisionTime / predictionTime);

        float strength = Mathf.Clamp01(0.5f * distanceUrgency + 0.5f * timeUrgency);

        return avoidanceDirection * strength;
    }

    // ---------------------------------------------------------------------
    // Debug Helpers
    // ---------------------------------------------------------------------

    private void ResetPredictiveDebug()
    {
        debugPredictionCandidate = false;
        debugThreatDetected = false;

        debugOwnerPosition = Vector3.zero;
        debugOtherPosition = Vector3.zero;

        debugOwnerVelocity = Vector3.zero;
        debugOtherVelocity = Vector3.zero;

        debugTimeToClosestApproach = 0f;
        debugClosestDistance = 0f;
        debugAvoidanceDistance = 0f;
    }

    private void StorePredictiveDebug(
        UnitBase other,
        Vector3 ownerVelocity,
        Vector3 otherVelocity,
        float timeToClosestApproach,
        float closestDistance,
        float avoidanceDistance)
    {
        debugPredictionCandidate = true;

        debugOwnerPosition = owner.Position;
        debugOtherPosition = other.Position;

        debugOwnerVelocity = ownerVelocity;
        debugOtherVelocity = otherVelocity;

        debugTimeToClosestApproach =
            timeToClosestApproach;

        debugClosestDistance =
            closestDistance;

        debugAvoidanceDistance =
            avoidanceDistance;
    }

    // ---------------------------------------------------------------------
    // Gizmos
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawSteeringGizmos)
            return;

        DrawSteeringGizmos();
    }

    private void DrawSteeringGizmos()
    {
        UnitBase debugOwner = owner;

        if (debugOwner == null)
            debugOwner = GetComponent<UnitBase>();

        if (debugOwner == null)
            return;

        float navigationRadius = GetNavigationRadius(debugOwner);
        float separationEnvelope = navigationRadius + separationPadding * 0.5f;

        Vector3 center = transform.position;
        center.y += 0.05f;

        DrawCircle(center, navigationRadius, Color.green);
        DrawCircle(center, separationEnvelope, Color.yellow);

        if (debugSeparation.sqrMagnitude > 0.0001f)
        {
            Vector3 start = transform.position;
            start.y += 0.2f;

            Vector3 end = start + debugSeparation * 2f;

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end,0.06f);
        }

        if (debugAvoidance.sqrMagnitude > 0.0001f)
        {
            Vector3 start = transform.position;
            start.y += 0.35f;

            Vector3 end = start + debugAvoidance * 2f;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end, 0.06f);
        }


        if (debugPredictionCandidate)
        {
            DrawPredictiveAvoidanceDebug();
        }
    }

    private void DrawCircle(Vector3 center, float radius, Color color)
    {
        if (radius <= 0f)
            return;

        Gizmos.color = color;

        int segments = Mathf.Max(8, gizmoCircleSegments);
        float step = Mathf.PI * 2f / segments;

        Vector3 previous = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = step * i;

            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);

            previous = next;
        }
    }


    private void DrawPredictiveAvoidanceDebug()
    {
        Vector3 ownerStart = debugOwnerPosition;
        Vector3 otherStart = debugOtherPosition;

        ownerStart.y += 0.45f;
        otherStart.y += 0.45f;

        // -------------------------------------------------------------
        // Prediction horizon
        // -------------------------------------------------------------

        Vector3 ownerHorizon =
            ownerStart +
            debugOwnerVelocity * predictionTime;

        Vector3 otherHorizon =
            otherStart +
            debugOtherVelocity * predictionTime;

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(ownerStart, ownerHorizon);
        Gizmos.DrawWireSphere(ownerHorizon, 0.08f);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(otherStart, otherHorizon);
        Gizmos.DrawWireSphere(otherHorizon, 0.08f);

        // -------------------------------------------------------------
        // Actual closest approach
        // -------------------------------------------------------------

        Vector3 ownerClosest =
            ownerStart +
            debugOwnerVelocity *
            debugTimeToClosestApproach;

        Vector3 otherClosest =
            otherStart +
            debugOtherVelocity *
            debugTimeToClosestApproach;

        // If threat is active -> white.
        // If collision is still outside prediction horizon -> yellow.
        Gizmos.color =
            debugThreatDetected
                ? Color.white
                : Color.yellow;

        Gizmos.DrawLine(
            ownerClosest,
            otherClosest);

        Gizmos.DrawSphere(
            ownerClosest,
            0.07f);

        Gizmos.DrawSphere(
            otherClosest,
            0.07f);

        // Show the combined avoidance envelope at closest approach.
        DrawCircle(
            ownerClosest,
            debugAvoidanceDistance,
            debugThreatDetected
                ? Color.cyan
                : Color.yellow);

        // -------------------------------------------------------------
        // Current prediction state marker
        // -------------------------------------------------------------

        Gizmos.color =
            debugThreatDetected
                ? Color.blue
                : Color.yellow;

        Gizmos.DrawWireSphere(
            ownerStart,
            0.18f);

#if UNITY_EDITOR
        string state =
            debugThreatDetected
                ? "ACTIVE AVOIDANCE"
                : "FUTURE COLLISION";

        Vector3 labelPosition =
            ownerStart + Vector3.up * 1.2f;

        UnityEditor.Handles.Label(
            labelPosition,
            $"{state}\n" +
            $"TCA: {debugTimeToClosestApproach:F3}s\n" +
            $"Horizon: {predictionTime:F3}s\n" +
            $"Delta: {debugTimeToClosestApproach - predictionTime:F3}s\n" +
            $"Closest: {debugClosestDistance:F2}\n" +
            $"Avoid Dist: {debugAvoidanceDistance:F2}");
#endif
    }
}