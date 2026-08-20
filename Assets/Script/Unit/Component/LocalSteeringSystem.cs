using System.Collections.Generic;
using UnityEngine;

public class LocalSteeringSystem : MonoBehaviour
{
    [Header("Separation")]
    [SerializeField] private float separationPadding = 0.25f;
    [SerializeField] private float separationWeight = 1.0f;

    [Header("Predictive Avoidance")]
    [SerializeField] private float predictionTime = 1.75f;
    [SerializeField] private float avoidancePadding = 0.30f;
    [SerializeField] private float avoidanceWeight = 1.2f;

    [Header("Debug")]
    [SerializeField] private bool drawSteeringGizmos = true;
    [SerializeField] private int gizmoCircleSegments = 32;

    private UnitBase owner;
    private IReadOnlyList<UnitBase> allUnits;

    private Vector3 debugSeparation;

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

        Vector3 velocity = 
            preferredVelocity 
            + separation * maxSpeed * separationWeight
            + avoidance * maxSpeed * avoidanceWeight;

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
        if (preferredVelocity.sqrMagnitude <= Mathf.Epsilon)
            return Vector3.zero;

        UnitBase threat = null;

        float earliestCollisionTime = predictionTime;
        Vector3 threatFutureOffset = Vector3.zero;
        float threatAvoidanceDistance = 0f;

        float ownerRadius = GetNavigationRadius(owner);

        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitBase other = allUnits[i];

            if (other == null ||
                other == owner ||
                !other.IsAlive)
            {
                continue;
            }

            Vector3 relativePosition = other.Position - owner.Position;
            relativePosition.y = 0f;
            Vector3 otherVelocity = Vector3.zero;

            if (other.Motor != null)
                otherVelocity = other.Motor.CurrentVelocity;

            Vector3 relativeVelocity = otherVelocity - preferredVelocity;
            relativeVelocity.y = 0f;
            float relativeSpeedSquared = relativeVelocity.sqrMagnitude;

            if (relativeSpeedSquared <= 0.0001f)
                continue;

            float timeToClosestApproach = -Vector3.Dot(relativePosition, relativeVelocity) / relativeSpeedSquared;

            if (timeToClosestApproach <= 0f || timeToClosestApproach > predictionTime)
            {
                continue;
            }

            Vector3 futureOffset = relativePosition + relativeVelocity * timeToClosestApproach;

            float avoidanceDistance = ownerRadius + GetNavigationRadius(other) + avoidancePadding;

            if (futureOffset.sqrMagnitude >= avoidanceDistance * avoidanceDistance)
            {
                continue;
            }

            if (timeToClosestApproach >= earliestCollisionTime)
                continue;

            threat = other;
            earliestCollisionTime = timeToClosestApproach;
            threatFutureOffset = futureOffset;
            threatAvoidanceDistance = avoidanceDistance;
        }

        if (threat == null)
            return Vector3.zero;

        return CalculateAvoidanceDirection(preferredVelocity, threatFutureOffset, earliestCollisionTime, threatAvoidanceDistance);
    }

    private Vector3 CalculateAvoidanceDirection(Vector3 preferredVelocity, Vector3 futureOffset, float collisionTime, float avoidanceDistance)
    {
        Vector3 forward = preferredVelocity.normalized;
        Vector3 right = new Vector3(forward.z, 0f, -forward.x);

        Vector3 avoidanceDirection;

        if (futureOffset.sqrMagnitude <= 0.0001f)
        {
            // Perfectly symmetric/head-on situation.
            // Both agents move to their own right.
            avoidanceDirection = right;
        }
        else
        {
            float otherSide = Vector3.Dot(futureOffset, right);
            avoidanceDirection = otherSide >= 0f ? -right : right;
        }

        float futureDistance = futureOffset.magnitude;

        float distanceUrgency = 1f - Mathf.Clamp01(futureDistance / avoidanceDistance);

        float timeUrgency = 1f - Mathf.Clamp01(collisionTime / predictionTime);

        float strength = Mathf.Clamp01(0.5f * distanceUrgency + 0.5f * timeUrgency);

        return avoidanceDirection * strength;
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

}