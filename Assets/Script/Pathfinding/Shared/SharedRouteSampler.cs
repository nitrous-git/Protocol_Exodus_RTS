using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Samples navigation direction from a shared polyline route.
///
/// A consumer tracks only a lightweight segment cursor.
/// The route itself is shared by the entire MovementGroup.
/// </summary>
public sealed class SharedRouteSampler
{
    private const float MinSegmentLengthSqr = 0.0001f;
    private const int InitialSearchSegmentCount = 32;
    private const int ForwardSearchSegmentCount = 8;
    private const float LookAheadSteps = 4f; // 2
    private const float MaxLookAheadTurnAngle = 55f; //35
    private const float ArrivalStepFraction = 0.15f;

    private readonly IReadOnlyList<Vector3> route;
    private readonly float routeStepDistance;
    private readonly float lookAheadDistance;
    private readonly float arrivalDistance;

    public SharedRouteSampler(IReadOnlyList<Vector3> route)
    {
        this.route = route;

        routeStepDistance = EstimateRouteStepDistance();

        lookAheadDistance =
            routeStepDistance *
            LookAheadSteps;

        arrivalDistance =
            Mathf.Max(
                routeStepDistance *
                ArrivalStepFraction,
                0.05f);
    }

    public NavigationSample SampleDirection(Vector3 worldPosition, int previousRouteSegmentIndex = -1)
    {
        if (route == null || route.Count == 0)
        {
            return NavigationSample.Invalid;
        }

        Vector3 current = Flatten(worldPosition);
        Vector3 finalPoint = Flatten(route[route.Count - 1]);

        float destinationDistanceSqr = (finalPoint - current).sqrMagnitude;

        if (destinationDistanceSqr <= arrivalDistance * arrivalDistance)
        {
            return new NavigationSample(
                true,
                true,
                Vector3.zero,
                finalPoint,
                finalPoint,
                0f,
                previousRouteSegmentIndex);
        }

        if (route.Count == 1)
        {
            Vector3 direction =
                finalPoint - current;

            if (direction.sqrMagnitude <= MinSegmentLengthSqr)
            {
                return NavigationSample.Invalid;
            }

            return new NavigationSample(
                true,
                false,
                direction.normalized,
                finalPoint,
                finalPoint,
                direction.magnitude,
                -1);
        }

        SegmentProjection projection =
            FindRelevantSegment(
                current,
                previousRouteSegmentIndex);

        if (!projection.IsValid)
        {
            return NavigationSample.Invalid;
        }

        Vector3 lookAheadPoint =
            CalculateLookAheadPoint(
                projection);

        Vector3 directionToLookAhead = lookAheadPoint - current;
        directionToLookAhead.y = 0f;

        if (directionToLookAhead.sqrMagnitude <= MinSegmentLengthSqr)
        {
            directionToLookAhead = GetSegmentDirection(projection.SegmentIndex);
        }

        if (directionToLookAhead.sqrMagnitude <= MinSegmentLengthSqr)
        {
            return NavigationSample.Invalid;
        }

        return new NavigationSample(
            true,
            false,
            directionToLookAhead.normalized,
            projection.Point,
            lookAheadPoint,
            Mathf.Sqrt(projection.DistanceSqr),
            projection.SegmentIndex);
    }

    private SegmentProjection FindRelevantSegment(
        Vector3 current,
        int previousRouteSegmentIndex)
    {
        int lastSegmentIndex =
            route.Count - 2;

        int searchStart;
        int searchEnd;

        if (previousRouteSegmentIndex < 0)
        {
            // First attachment.
            searchStart = 0;

            searchEnd =
                Mathf.Min(
                    lastSegmentIndex,
                    InitialSearchSegmentCount - 1);
        }
        else
        {
            searchStart =
                Mathf.Clamp(
                    previousRouteSegmentIndex,
                    0,
                    lastSegmentIndex);

            searchEnd =
                Mathf.Min(
                    lastSegmentIndex,
                    searchStart +
                    ForwardSearchSegmentCount);
        }

        return FindClosestSegment(
            current,
            searchStart,
            searchEnd);
    }

    private SegmentProjection FindClosestSegment(
        Vector3 current,
        int startIndex,
        int endIndex)
    {
        SegmentProjection best =
            SegmentProjection.Invalid;

        float bestDistanceSqr =
            float.PositiveInfinity;

        for (int i = startIndex; i <= endIndex; i++)
        {
            SegmentProjection candidate = ProjectOntoSegment(current, i);

            if (!candidate.IsValid)
                continue;

            if (candidate.DistanceSqr >=
                bestDistanceSqr)
            {
                continue;
            }

            best =
                candidate;

            bestDistanceSqr =
                candidate.DistanceSqr;
        }

        return best;
    }

    private SegmentProjection ProjectOntoSegment(
        Vector3 current,
        int segmentIndex)
    {
        if (segmentIndex < 0 ||
            segmentIndex >= route.Count - 1)
        {
            return SegmentProjection.Invalid;
        }

        Vector3 start =
            Flatten(route[segmentIndex]);

        Vector3 end =
            Flatten(route[segmentIndex + 1]);

        Vector3 segment =
            end - start;

        float segmentLengthSqr =
            segment.sqrMagnitude;

        if (segmentLengthSqr <=
            MinSegmentLengthSqr)
        {
            return SegmentProjection.Invalid;
        }

        float t =
            Vector3.Dot(
                current - start,
                segment)
            /
            segmentLengthSqr;

        t = Mathf.Clamp01(t);

        Vector3 point =
            start +
            segment * t;

        float distanceSqr =
            (current - point)
            .sqrMagnitude;

        return new SegmentProjection(
            true,
            segmentIndex,
            t,
            point,
            distanceSqr);
    }

    private Vector3 CalculateLookAheadPoint(
        SegmentProjection projection)
    {
        int segmentIndex = projection.SegmentIndex;

        Vector3 segmentEnd =
            Flatten(
                route[segmentIndex + 1]);

        Vector3 currentPoint =
            projection.Point;

        Vector3 currentDirection =
            GetSegmentDirection(
                segmentIndex);

        float remainingLookAhead =
            lookAheadDistance;

        float distanceToSegmentEnd =
            Vector3.Distance(
                currentPoint,
                segmentEnd);

        if (remainingLookAhead <= distanceToSegmentEnd)
        {
            return currentPoint +
                   currentDirection *
                   remainingLookAhead;
        }

        remainingLookAhead -=
            distanceToSegmentEnd;

        currentPoint =
            segmentEnd;

        int nextSegmentIndex =
            segmentIndex + 1;

        Vector3 previousDirection = currentDirection;

        while (nextSegmentIndex < route.Count - 1 && remainingLookAhead > 0f)
        {
            Vector3 nextDirection =
                GetSegmentDirection(
                    nextSegmentIndex);

            if (nextDirection.sqrMagnitude <=
                MinSegmentLengthSqr)
            {
                nextSegmentIndex++;
                continue;
            }

            float turnAngle =
                Vector3.Angle(
                    previousDirection,
                    nextDirection);

            // Do not sample through a genuine
            // navigation corner.
            if (turnAngle >
                MaxLookAheadTurnAngle)
            {
                break;
            }

            Vector3 nextEnd =
                Flatten(
                    route[nextSegmentIndex + 1]);

            float segmentLength =
                Vector3.Distance(
                    currentPoint,
                    nextEnd);

            if (segmentLength <=
                Mathf.Epsilon)
            {
                currentPoint =
                    nextEnd;

                previousDirection =
                    nextDirection;

                nextSegmentIndex++;

                continue;
            }

            float distanceToTravel =
                Mathf.Min(
                    remainingLookAhead,
                    segmentLength);

            currentPoint +=
                nextDirection *
                distanceToTravel;

            remainingLookAhead -=
                distanceToTravel;

            if (distanceToTravel <
                segmentLength)
            {
                break;
            }

            currentPoint =
                nextEnd;

            previousDirection =
                nextDirection;

            nextSegmentIndex++;
        }

        return currentPoint;
    }

    private Vector3 GetSegmentDirection(
        int segmentIndex)
    {
        if (segmentIndex < 0 ||
            segmentIndex >= route.Count - 1)
        {
            return Vector3.zero;
        }

        Vector3 start =
            Flatten(route[segmentIndex]);

        Vector3 end =
            Flatten(route[segmentIndex + 1]);

        Vector3 direction =
            end - start;

        if (direction.sqrMagnitude <=
            MinSegmentLengthSqr)
        {
            return Vector3.zero;
        }

        return direction.normalized;
    }

    private float EstimateRouteStepDistance()
    {
        if (route == null ||
            route.Count < 2)
        {
            return 1f;
        }

        float smallestStep =
            float.PositiveInfinity;

        // When possible, ignore the first segment.
        // That segment begins at the representative's
        // exact world position and may therefore be
        // much shorter than one grid step.
        int startSegment =
            route.Count > 2
                ? 1
                : 0;

        for (int i = startSegment;
             i < route.Count - 1;
             i++)
        {
            Vector3 start =
                Flatten(route[i]);

            Vector3 end =
                Flatten(route[i + 1]);

            float length =
                Vector3.Distance(
                    start,
                    end);

            if (length <= 0.01f)
                continue;

            if (length <
                smallestStep)
            {
                smallestStep =
                    length;
            }
        }

        if (float.IsInfinity(
            smallestStep))
        {
            return 1f;
        }

        return smallestStep;
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private struct SegmentProjection
    {
        public bool IsValid { get; }
        public int SegmentIndex { get; }
        public float T { get; }
        public Vector3 Point { get; }
        public float DistanceSqr { get; }

        public SegmentProjection(
            bool isValid,
            int segmentIndex,
            float t,
            Vector3 point,
            float distanceSqr)
        {
            IsValid = isValid;
            SegmentIndex = segmentIndex;
            T = t;
            Point = point;
            DistanceSqr = distanceSqr;
        }

        public static SegmentProjection Invalid => default;
    }
}