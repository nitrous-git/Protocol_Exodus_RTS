using UnityEngine;

public readonly struct NavigationSample
{
    public bool IsValid { get; }

    public bool ReachedDestination { get; }

    public Vector3 RouteDirection { get; }

    public Vector3 RoutePoint { get; }

    public Vector3 LookAheadPoint { get; }

    public float DistanceFromRoute { get; }

    public int RouteSegmentIndex { get; }

    public NavigationSample(
        bool isValid,
        bool reachedDestination,
        Vector3 routeDirection,
        Vector3 routePoint,
        Vector3 lookAheadPoint,
        float distanceFromRoute,
        int routeSegmentIndex)
    {
        IsValid = isValid;
        ReachedDestination = reachedDestination;
        RouteDirection = routeDirection;
        RoutePoint = routePoint;
        LookAheadPoint = lookAheadPoint;
        DistanceFromRoute = distanceFromRoute;
        RouteSegmentIndex = routeSegmentIndex;
    }

    public static NavigationSample Invalid => default;
}