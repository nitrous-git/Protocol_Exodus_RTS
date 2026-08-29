using System.Collections.Generic;
using UnityEngine;

public class LocalSteeringSystem : MonoBehaviour
{
    [Header("Separation")]
    private float separationPadding = 1.0f;
    private float separationWeight = 1.5f;

    [Header("Steering")]
    private float maxSteeringAngle = 65f;

    [Header("Debug")]
    [SerializeField] private bool drawSteeringGizmos = true;

    private UnitBase owner;
    private IReadOnlyList<UnitBase> allUnits;

    private Vector3 debugSeparation;

    public void Initialize(
        UnitBase owner,
        IReadOnlyList<UnitBase> allUnits)
    {
        this.owner = owner;
        this.allUnits = allUnits;
    }

    public Vector3 CalculateVelocity(
        Vector3 preferredVelocity,
        float maxSpeed)
    {
        if (owner == null || allUnits == null)
            return preferredVelocity;

        preferredVelocity.y = 0f;

        if (preferredVelocity.sqrMagnitude <= 0.0001f)
        {
            debugSeparation = Vector3.zero;
            return Vector3.zero;
        }

        // Preserve the speed requested by UnitMotor.
        // Local steering only modifies direction.
        float preferredSpeed =
            Mathf.Min(
                preferredVelocity.magnitude,
                maxSpeed);

        Vector3 pathDirection = preferredVelocity.normalized;

        Vector3 separation = CalculateSeparation(pathDirection);

        // Path direction remains authoritative.
        Vector3 desiredDirection =
            pathDirection +
            separation * separationWeight;

        if (desiredDirection.sqrMagnitude <= 0.0001f)
            desiredDirection = pathDirection;

        desiredDirection.Normalize();

        // Local steering may bend the trajectory,
        // but never take control away from the path.
        Vector3 limitedDirection =
            Vector3.RotateTowards(
                pathDirection,
                desiredDirection,
                maxSteeringAngle * Mathf.Deg2Rad,
                0f);

        limitedDirection.y = 0f;

        if (limitedDirection.sqrMagnitude <= 0.0001f)
            return preferredVelocity;

        return limitedDirection.normalized *
            preferredSpeed;
    }

    // ---------------------------------------------------------------------
    // Separation
    // ---------------------------------------------------------------------

    private Vector3 CalculateSeparation(
        Vector3 pathDirection)
    {
        Vector3 separation =
            Vector3.zero;

        float ownerRadius =
            GetNavigationRadius(owner);

        Vector3 right =
            new Vector3(
                pathDirection.z,
                0f,
                -pathDirection.x);

        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitBase other =
                allUnits[i];

            if (other == null ||
                other == owner ||
                !other.IsAlive)
            {
                continue;
            }

            Vector3 away =
                owner.Position -
                other.Position;

            away.y = 0f;

            float distanceSquared =
                away.sqrMagnitude;

            float separationDistance =
                ownerRadius +
                GetNavigationRadius(other) +
                separationPadding;

            float separationDistanceSquared =
                separationDistance *
                separationDistance;

            if (distanceSquared >=
                separationDistanceSquared)
            {
                continue;
            }

            if (distanceSquared <= 0.0001f)
            {
                separation += right;
                continue;
            }

            float distance = Mathf.Sqrt(distanceSquared);

            float strength =
                1f -
                distance /
                separationDistance;

            Vector3 awayDirection =
                away / distance;

            // ---------------------------------------------------------
            // Determine whether radial separation can actually
            // steer us sideways.
            // ---------------------------------------------------------

            Vector3 lateral =
                awayDirection -
                Vector3.Project(
                    awayDirection,
                    pathDirection);

            // If the other unit is almost exactly in front/behind us,
            // radial separation contains no useful lateral steering.
            if (lateral.sqrMagnitude <= 0.01f)
            {
                // Choose our own right relative to travel direction.
                //
                // Two head-on moving units naturally have opposite
                // world-space "right" vectors, so they pass each other.
                lateral = right;
            }
            else
            {
                lateral.Normalize();
            }

            separation +=
                lateral * strength;
        }

        debugSeparation =
            Vector3.ClampMagnitude(
                separation,
                1f);

        return debugSeparation;
    }
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private float GetNavigationRadius(
        UnitBase unit)
    {
        if (unit == null ||
            unit.Definition == null)
        {
            return 0.45f;
        }

        return unit.Definition.NavigationRadius;
    }

    private Vector3 GetOverlapDirection(
        UnitBase other)
    {
        if (owner.UnitId < other.UnitId)
            return Vector3.right;

        return Vector3.left;
    }

    // ---------------------------------------------------------------------
    // Debug
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawSteeringGizmos)
            return;

        UnitBase debugOwner =
            owner;

        if (debugOwner == null)
        {
            debugOwner =
                GetComponent<UnitBase>();
        }

        if (debugOwner == null)
            return;

        Vector3 center =
            transform.position;

        center.y += 0.1f;

        // Green = navigation radius.
        Gizmos.color =
            Color.green;

        Gizmos.DrawWireSphere(
            center,
            GetNavigationRadius(debugOwner));

        // Magenta = soft separation steering.
        if (debugSeparation.sqrMagnitude > 0.01f)
        {
            Vector3 start =
                center +
                Vector3.up * 0.4f;

            Vector3 end =
                start +
                debugSeparation * 2f;

            Gizmos.color =
                Color.magenta;

            Gizmos.DrawLine(
                start,
                end);

            Gizmos.DrawSphere(
                end,
                0.06f);
        }
    }
}