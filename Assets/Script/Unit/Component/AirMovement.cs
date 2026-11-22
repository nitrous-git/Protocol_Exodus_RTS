using System.Collections.Generic;
using UnityEngine;

internal sealed class AirMovement
{
    private const float FlightAltitude = 6f;
    private const float SeparationPadding = 0.25f;
    private const float ArrivalTolerance = 0.05f;
    private const float YawSpeed = 240f;

    private readonly UnitBase owner;
    private readonly TerrainGrid grid;
    private readonly IReadOnlyList<UnitBase> aircraft;
    private readonly float speed;
    private Vector3 destination;
    private bool spaceArrival;

    public bool HasDestination { get; private set; }

    public AirMovement(UnitBase owner, TerrainGrid grid, IReadOnlyList<UnitBase> aircraft, float speed)
    {
        this.owner = owner;
        this.grid = grid;
        this.aircraft = aircraft;
        this.speed = speed;
        owner.transform.position = Project(owner.Position, grid, owner.NavigationRadius);
    }

    public bool MoveTo(Vector3 position, bool spaceArrival)
    {
        if (!float.IsFinite(position.x) || !float.IsFinite(position.z))
        {
            Stop();
            return false;
        }

        destination = Project(position, grid, owner.NavigationRadius);
        this.spaceArrival = spaceArrival;
        HasDestination = true;
        return true;
    }

    public void Stop() => HasDestination = false;

    public Vector3 Tick(float deltaTime)
    {
        if (!HasDestination || deltaTime <= 0f)
            return Vector3.zero;

        Vector3 position = owner.Position;
        float radius = owner.NavigationRadius;
        if (spaceArrival && (destination - position).sqrMagnitude <= 4f * radius * radius &&
            GetClearance(destination, radius, aircraft, owner, stationaryOnly: true) < 0f)
        {
            destination = FindSpacedPosition(destination, Vector3.right, grid, radius, aircraft, owner, stationaryOnly: true);
            spaceArrival = false;
        }

        Vector3 offset = destination - position;
        float distance = offset.magnitude;
        float step = speed * deltaTime;
        Vector3 next;
        if (distance <= Mathf.Max(step, ArrivalTolerance))
        {
            next = destination;
            Stop();
        }
        else
        {
            Vector3 direction = offset / distance;
            Vector3 steering = distance > 2f * radius ? GetLateralSteering(position, direction) : Vector3.zero;
            next = Project(position + (direction + steering).normalized * step, grid, radius);
        }

        Vector3 displacement = next - position;
        if (displacement.sqrMagnitude > 0.000001f)
        {
            Quaternion heading = Quaternion.LookRotation(displacement, Vector3.up);
            owner.transform.rotation = Quaternion.RotateTowards(owner.transform.rotation, heading, YawSpeed * deltaTime);
        }

        owner.transform.position = next;
        return displacement / deltaTime;
    }

    private Vector3 GetLateralSteering(Vector3 position, Vector3 direction)
    {
        Vector3 right = Vector3.Cross(Vector3.up, direction);
        float steering = 0f;
        for (int i = 0; i < aircraft.Count; i++)
        {
            UnitBase other = aircraft[i];
            if (other == null || other == owner || !other.IsAlive)
                continue;

            Vector3 offset = other.Position - position;
            float range = Spacing(owner.NavigationRadius, other.NavigationRadius);
            float ahead = Vector3.Dot(offset, direction);
            if (ahead < 0f || offset.sqrMagnitude >= range * range)
                continue;

            float lateral = Vector3.Dot(offset, right);
            float side = lateral > 0.1f ? -1f : 1f;
            steering += side * (1f - offset.magnitude / range);
        }

        return right * Mathf.Clamp(steering, -0.75f, 0.75f);
    }

    internal static float Spacing(float firstRadius, float secondRadius) => firstRadius + secondRadius + SeparationPadding;

    internal static Vector3 Project(Vector3 position, TerrainGrid grid, float radius)
    {
        position.y = FlightAltitude;
        return grid == null ? position : grid.ClampHorizontalPosition(position, radius);
    }

    internal static Vector3 FindSpacedPosition(Vector3 center, Vector3 right, TerrainGrid grid, float radius,
        IReadOnlyList<UnitBase> aircraft, UnitBase owner = null, bool stationaryOnly = false)
    {
        center = Project(center, grid, radius);
        Vector3 best = center;
        float bestClearance = GetClearance(best, radius, aircraft, owner, stationaryOnly);
        if (bestClearance >= 0f)
            return best;

        float spacing = Spacing(radius, radius);
        for (int ring = 1; ring <= 2; ring++)
        {
            for (int slot = 0; slot < 8; slot++)
            {
                Vector3 offset = Quaternion.AngleAxis(slot * 45f, Vector3.up) * right * (ring * spacing);
                Vector3 candidate = Project(center + offset, grid, radius);
                float clearance = GetClearance(candidate, radius, aircraft, owner, stationaryOnly);
                if (clearance >= 0f)
                    return candidate;
                if (clearance > bestClearance)
                {
                    best = candidate;
                    bestClearance = clearance;
                }
            }
        }

        // A crowded launch site can still dispatch the next ship.
        return best;
    }

    private static float GetClearance(Vector3 position, float radius, IReadOnlyList<UnitBase> aircraft,
        UnitBase owner, bool stationaryOnly)
    {
        float clearance = float.PositiveInfinity;
        for (int i = 0; i < aircraft.Count; i++)
        {
            UnitBase other = aircraft[i];
            if (other == null || other == owner || !other.IsAlive ||
                (stationaryOnly && other.Motor is { HasArrived: false }))
                continue;

            float spacing = Spacing(radius, other.NavigationRadius);
            clearance = Mathf.Min(clearance, (other.Position - position).sqrMagnitude - spacing * spacing);
        }

        return clearance;
    }
}
