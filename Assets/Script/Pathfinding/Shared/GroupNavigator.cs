using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the shared global navigation solution for one MovementGroup.
///
/// Units do not consume this navigator yet.
/// Commit 2 only builds and exposes the shared route.
/// </summary>
public sealed class GroupNavigator
{
    private const float RadiusComparisonEpsilon = 0.001f;

    private readonly MovementGroup movementGroup;
    private readonly IPathfindingService pathfindingService;
    private readonly TerrainGrid terrainGrid;
    private readonly List<Vector3> pathBuffer = new List<Vector3>();

    public INavigationSolution Solution { get; private set; }
    public UnitBase Representative { get; private set; }

    public bool HasValidSolution => Solution != null && Solution.IsValid;

    public GroupNavigator(MovementGroup movementGroup, IPathfindingService pathfindingService, TerrainGrid terrainGrid)
    {
        this.movementGroup = movementGroup;
        this.pathfindingService = pathfindingService;
        this.terrainGrid = terrainGrid; 
    }

    /// <summary>
    /// Builds one shared global route for the MovementGroup.
    ///
    /// The route is currently produced by the existing
    /// IPathfindingService and is not consumed by units yet.
    /// </summary>
    public bool BuildSharedAstar()
    {
        Solution = null;

        if (movementGroup == null)
        {
            Debug.LogWarning("[GroupNav] Cannot build navigation: MovementGroup is missing.");
            return false;
        }

        Representative = null;

        pathBuffer.Clear();

        if (pathfindingService == null)
        {
            Debug.LogWarning($"[GroupNav] Group={movementGroup.Id} Cannot build navigation: IPathfindingService is missing.");
            return false;
        }

        Representative = SelectRepresentative();

        if (Representative == null)
        {
            Debug.LogWarning($"[GroupNav] Group={movementGroup.Id} Cannot build navigation: no representative unit.");
            return false;
        }

        double startTime = Time.realtimeSinceStartupAsDouble;

        bool success =
            pathfindingService.TryFindPath(
                Representative,
                Representative.Position,
                movementGroup.Destination,
                pathBuffer);

        double timeMs = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;

        if (!success || pathBuffer.Count == 0)
        {
            Debug.Log(
                $"[GroupNav] " +
                $"Group={movementGroup.Id} " +
                $"Units={movementGroup.UnitCount} " +
                $"Representative={Representative.UnitId} " +
                $"Radius={Representative.NavigationRadius:F2} " +
                $"Success=False " +
                $"Points={pathBuffer.Count} " +
                $"TimeMs={timeMs:F2}");

            pathBuffer.Clear();

            return false;
        }

        Solution = new SharedAStarNavigationSolution(Representative.Position, movementGroup.Destination, pathBuffer);

        Debug.Log(
            $"[GroupNav] " +
            $"Group={movementGroup.Id} " +
            $"Units={movementGroup.UnitCount} " +
            $"Representative={Representative.UnitId} " +
            $"Radius={Representative.NavigationRadius:F2} " +
            $"Success=True " +
            $"Points={pathBuffer.Count} " +
            $"TimeMs={timeMs:F2}");

        pathBuffer.Clear();

        return true;
    }

    public bool Build()
    {
        Solution = null;

        if (movementGroup == null)
        {
            Debug.LogWarning("[GroupNav] Cannot build: MovementGroup missing.");

            return false;
        }

        double startTime = Time.realtimeSinceStartupAsDouble;

        Vector2 goalHalfExtents = movementGroup.Formation != null ? movementGroup.Formation.FormationHalfExtents : Vector2.zero;

        FlowField field =
            FlowFieldBuilder.Build(
                terrainGrid,
                movementGroup.Destination,
                movementGroup.MaxNavigationRadius,
                goalHalfExtents);

        double timeMs = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;

        if (field == null || !field.IsBuilt)
        {
            Debug.LogWarning(
                $"[GroupNav] " +
                $"Group={movementGroup.Id} " +
                $"Units={movementGroup.UnitCount} " +
                $"Backend=FlowField " +
                $"Success=False " +
                $"TimeMs={timeMs:F2}");

            return false;
        }

        Solution = new FlowFieldNavigationSolution(field);

        Debug.Log(
            $"[GroupNav] " +
            $"Group={movementGroup.Id} " +
            $"Units={movementGroup.UnitCount} " +
            $"Backend=FlowField " +
            $"Radius={movementGroup.MaxNavigationRadius:F2} " +
            $"Success=True " +
            $"TimeMs={timeMs:F2}");

        return true;
    }

    private UnitBase SelectRepresentative()
    {
        IReadOnlyList<UnitBase> members = movementGroup.Members;

        if (members == null || members.Count == 0)
        {
            return null;
        }

        Vector3 groupCenter = CalculateGroupCenter(members);

        UnitBase bestUnit = null;

        float bestRadius = float.NegativeInfinity;
        float bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < members.Count; i++)
        {
            UnitBase unit = members[i];

            if (unit == null)
            {
                continue;
            }

            float radius = unit.NavigationRadius;

            float distanceSqr = (unit.Position - groupCenter).sqrMagnitude;

            bool largerRadius = radius > bestRadius + RadiusComparisonEpsilon;

            bool sameRadius = Mathf.Abs(radius - bestRadius) <= RadiusComparisonEpsilon;

            if (largerRadius || (sameRadius && distanceSqr < bestDistanceSqr))
            {
                bestUnit = unit;
                bestRadius = radius;
                bestDistanceSqr = distanceSqr;
            }
        }

        return bestUnit;
    }

    private static Vector3 CalculateGroupCenter(IReadOnlyList<UnitBase> members)
    {
        Vector3 sum = Vector3.zero;

        int count = 0;

        for (int i = 0; i < members.Count; i++)
        {
            UnitBase unit = members[i];

            if (unit == null)
            {
                continue;
            }

            sum += unit.Position;
            count++;
        }

        if (count == 0)
        {
            return Vector3.zero;
        }

        return sum / count;
    }

    // ---------------------------------------------------------------------
    // Debug 
    // ---------------------------------------------------------------------

    public void DrawDebugInitialSamples(float directionLength = 2f, float duration = 5f)
    {
        if (Solution == null || !Solution.IsValid)
        {
            return;
        }

        IReadOnlyList<UnitBase> members = movementGroup.Members;

        if (members == null)
            return;

        for (int i = 0;
             i < members.Count;
             i++)
        {
            UnitBase unit =
                members[i];

            if (unit == null)
                continue;

            NavigationSample sample = Solution.SampleDirection(unit.Position);

            if (!sample.IsValid || sample.ReachedDestination)
            {
                continue;
            }

            Vector3 origin =
                unit.Position +
                Vector3.up * 0.35f;

            Debug.DrawRay(
                origin,
                sample.RouteDirection *
                directionLength,
                Color.yellow,
                duration,
                false);

            Vector3 routePoint =
                sample.RoutePoint;

            routePoint.y =
                origin.y;

            Debug.DrawLine(
                origin,
                routePoint,
                Color.gray,
                duration,
                false);
        }
    }

    /// <summary>
    /// Draws the current shared route for temporary inspection.
    /// </summary>
    public void DrawDebugRoute(float duration = 5f)
    {
        if (Solution == null)
            return;

        IReadOnlyList<Vector3> path = Solution.DebugPath;

        if (path == null || path.Count < 2)
        {
            return;
        }

        Vector3 verticalOffset = Vector3.up * 0.25f;

        for (int i = 1; i < path.Count; i++)
        {
            Debug.DrawLine(
                path[i - 1] + verticalOffset,
                path[i] + verticalOffset,
                Color.red,
                duration,
                false);
        }
    }

    // ---------------------------------------------------------------------
    // Getter 
    // ---------------------------------------------------------------------

    public FlowField GetActiveFlowField() 
    {
        FlowFieldNavigationSolution flowSolution = Solution as FlowFieldNavigationSolution;
        return flowSolution?.Field;
    }
}