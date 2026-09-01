using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class UnitMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float rotationSpeed = 12f;
    [SerializeField] private float waypointReachFraction = 0.25f;

    [Header("Ground Following")]
    [SerializeField] private bool followTerrainHeight = true;
    [SerializeField] private bool useNavMeshHeight = false;
    [SerializeField] private Terrain terrain;
    [SerializeField] private float baseHeightOffset = 0f;
    [SerializeField] private float navMeshSampleRadius = 2f;
    [SerializeField] private int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Debug")]
    private bool drawPathGizmos = true;
    [SerializeField] private bool drawOccupancyGizmo = true;
    [SerializeField] private float gizmoSampleSpacing = 0.5f;
    [SerializeField] private float gizmoHeightOffset = 0.05f;

    private TerrainGrid terrainGrid;

    private UnitBase owner;
    private IPathfindingService pathfindingService;
    private GridNavigationStateSystem navigationState;
    private LocalSteeringSystem localSteeringSystem;
    private float moveSpeed;
    private Vector3 currentVelocity; // actual resulting movement
    private Vector3 preferredVelocity; // navigation intent

    private readonly List<Vector3> path = new List<Vector3>();
    private int pathIndex;
    private bool hasPath;
    private float pathLookAheadCells = 2;// 1
    private float maxLookAheadTurnAngle = 35f; //35
    private Vector3 debugLookAheadTarget;
    private float finalArrivalFraction = 0.15f;
    private int pathRelaxLookAheadNodes = 8;

    public bool HasPath { get { return hasPath; } }
    public bool HasArrived { get { return !hasPath; } }

    public Vector3 CurrentVelocity => currentVelocity;
    public Vector3 PreferredVelocity => preferredVelocity;

    public void Initialize(UnitBase owner, 
        IPathfindingService pathfindingService, 
        TerrainGrid terrainGrid,
        GridNavigationStateSystem navigationState,
        LocalSteeringSystem localSteeringSystem,
        float moveSpeed)
    {
        this.owner = owner;
        this.pathfindingService = pathfindingService;
        this.navigationState = navigationState;
        this.localSteeringSystem = localSteeringSystem;
        this.moveSpeed = moveSpeed;
        this.terrainGrid = terrainGrid;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        SnapToGround();
        SnapToCurrentCellCenter(terrainGrid.WorldToCell(owner.transform.position));

        UpdateUnitOccupancy();
    }

    public void Tick()
    {
        if (!hasPath)
            return;

        FollowPath();
    }


    // ---------------------------------------------------------------------
    // Movement
    // ---------------------------------------------------------------------

    public bool MoveTo(Vector3 destination)
    {
        return TryBuildPath(destination);
    }

    public void Stop()
    {
        ClearPath();
    }

    // ---------------------------------------------------------------------
    // Path
    // ---------------------------------------------------------------------

    private bool TryBuildPath(Vector3 destination)
    {
        if (pathfindingService == null)
        {
            Debug.LogError(name + " cannot move because no IPathfindingService is available.");
            ClearPath();
            return false;
        }

        path.Clear();

        bool foundPath = pathfindingService.TryFindPath(owner, transform.position, destination, path);

        if (!foundPath)
        {
            ClearPath();
            return false;
        }

        SnapPathToGround(path);

        pathIndex = 0;
        hasPath = path.Count > 0;

        return true;
    }

    private void FollowPath()
    {
        if (!hasPath || pathIndex >= path.Count)
        {
            ClearPath();
            return;
        }

        bool advancedWaypoint = AdvanceIntermediateWaypoints();

        //if (advancedWaypoint)
        //    TryRelaxPath();

        Vector3 target = path[pathIndex];

        MoveTowardsTarget(target);
    }

    private void MoveTowardsTarget(Vector3 target)
    {
        bool isFinalWaypoint = pathIndex >= path.Count - 1;

        // only the actual destination must be reached exactly.
        if (isFinalWaypoint && HasReachedTarget(target))
        {
            CompleteDestination(target);
            return;
        }

        //preferredVelocity = CalculateDesiredVelocity(target); // navigation intent
        preferredVelocity = CalculatePathFollowingVelocity(target);
        Vector3 finalVelocity = preferredVelocity;

        if (localSteeringSystem != null)
        {
            finalVelocity = localSteeringSystem.CalculateVelocity(preferredVelocity, moveSpeed);
        }

        //ApplyVelocity(finalVelocity, target, isFinalWaypoint);
        ApplyVelocity(finalVelocity);

        if (isFinalWaypoint && HasReachedTarget(target))
        {
            CompleteDestination(target);
        }
    }


    //private Vector3 CalculateDesiredVelocity(Vector3 target)
    //{
    //    Vector3 direction = target - transform.position;
    //    direction.y = 0f;

    //    if (direction.sqrMagnitude <= Mathf.Epsilon)
    //        return Vector3.zero;

    //    return direction.normalized * moveSpeed;
    //}

    private Vector3 CalculatePathFollowingVelocity(
    Vector3 target)
    {
        Vector3 steeringTarget = CalculateLookAheadTarget(target);

        debugLookAheadTarget =
            steeringTarget;

        Vector3 direction =
            steeringTarget -
            transform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector3.zero;
        }

        return direction.normalized *
            moveSpeed;
    }

    private void ApplyVelocity(Vector3 velocity)
    {
        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);

        Vector3 nextFlat = currentFlat + velocity * Time.deltaTime;

        Vector3 nextPosition = new Vector3(nextFlat.x, transform.position.y, nextFlat.z);

        nextPosition = ProjectPositionToGround(nextPosition);

        Vector3 previousPosition = transform.position;

        transform.position = nextPosition;

        if (Time.deltaTime > Mathf.Epsilon)
        {
            currentVelocity = (nextPosition - previousPosition) / Time.deltaTime;
            currentVelocity.y = 0f;
        }
        else
        {
            currentVelocity = Vector3.zero;
        }

        UpdateUnitOccupancy();

        RotateTowardsMovement(nextPosition - previousPosition);
    }

    private void CompleteDestination(Vector3 target)
    {
        SnapToTarget(target);
        ClearPath();
    }

    private bool AdvanceIntermediateWaypoints()
    {
        bool advanced = false;

        while (pathIndex < path.Count - 1)
        {
            bool reached = IsWithinWaypointReach(path[pathIndex]);

            bool passed = HasPassedIntermediateWaypoint(pathIndex);

            if (!reached && !passed)
                break;

            pathIndex++;
            advanced = true;
        }

        return advanced;
    }

    private bool HasReachedTarget(Vector3 target)
    {
        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 targetFlat = new Vector3(target.x, 0f, target.z);

        float arrivalDistance = FinalArrivalReachDistance();

        return (targetFlat - currentFlat).sqrMagnitude <= arrivalDistance * arrivalDistance;
    }

    private bool IsWithinWaypointReach(Vector3 waypoint)
    {
        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 waypointFlat = new Vector3(waypoint.x, 0f, waypoint.z);

        float reachDistance = WaypointReachDistance();

        return (waypointFlat - currentFlat).sqrMagnitude <= reachDistance * reachDistance;
    }

    private bool HasPassedIntermediateWaypoint(int index)
    {
        if (index < 0 ||
               index >= path.Count - 1)
        {
            return false;
        }

        Vector3 waypoint =
            path[index];

        Vector3 direction;

        if (index == 0)
        {
            // First waypoint has no previous path point.
            // Use the direction toward the next waypoint.
            direction =
                path[index + 1] -
                waypoint;
        }
        else
        {
            direction =
                waypoint -
                path[index - 1];
        }

        waypoint.y = 0f;
        direction.y = 0f;

        Vector3 current =
            transform.position;

        current.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        return Vector3.Dot(
            current - waypoint,
            direction) > 0f;
    }

    private float WaypointReachDistance()
    {
        if (terrainGrid == null)
            return 0.25f;
        
        return terrainGrid.CellSize * waypointReachFraction;
    }

    private float FinalArrivalReachDistance()
    {
        if (terrainGrid == null)
            return 0.30f;

        return terrainGrid.CellSize * finalArrivalFraction;
    }

    private void SnapToTarget(Vector3 target)
    {
        transform.position = ProjectPositionToGround(target);
        UpdateUnitOccupancy();
    }

    private void ClearPath()
    {
        path.Clear();
        pathIndex = 0;
        hasPath = false;
        currentVelocity = Vector3.zero;
        preferredVelocity = Vector3.zero;   
    }

    // ---------------------------------------------------------------------
    // Ground Projection
    // ---------------------------------------------------------------------

    private Vector3 ProjectPositionToGround(Vector3 position)
    {
        if (useNavMeshHeight)
        {
            NavMeshHit hit;

            if (NavMesh.SamplePosition(position, out hit, navMeshSampleRadius, navMeshAreaMask))
            {
                return hit.position + Vector3.up * baseHeightOffset;
            }
        }

        if (followTerrainHeight && terrain != null)
        {
            float terrainY = terrain.SampleHeight(position) + terrain.transform.position.y;
            position.y = terrainY + baseHeightOffset;
            return position;
        }

        return position;
    }

    private void SnapToGround()
    {
        transform.position = ProjectPositionToGround(transform.position);
    }

    private void SnapPathToGround(List<Vector3> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            points[i] = ProjectPositionToGround(points[i]);
        }
    }

    // ---------------------------------------------------------------------
    // Visual
    // ---------------------------------------------------------------------

    private void RotateTowardsMovement(Vector3 movementDirection)
    {
        movementDirection.y = 0f;

        if (movementDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(movementDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void SnapToCurrentCellCenter(GridCoord currentCell)
    {
        Vector3 center = terrainGrid.CellToWorld(currentCell);
        transform.position = ProjectPositionToGround(center);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private void UpdateUnitOccupancy()
    {
        if (owner == null || terrainGrid == null || navigationState == null)
        {
            return;
        }

        GridCoord currentCell = terrainGrid.WorldToCell(transform.position);
        navigationState.UpdateUnitOccupancy(owner, currentCell);
    }

    public void ApplyDepenetration(Vector3 displacement)
    {
        displacement.y = 0f;

        if (displacement.sqrMagnitude <= Mathf.Epsilon)
            return;

        Vector3 nextPosition = transform.position + displacement;

        nextPosition = ProjectPositionToGround(nextPosition);

        transform.position = nextPosition;

        UpdateUnitOccupancy();
    }

    // ---------------------------------------------------------------------
    // LocalSteering LookAhead Calculation
    // ---------------------------------------------------------------------
    private Vector3 CalculateLookAheadTarget(
        Vector3 target)
    {
        if (pathIndex <= 0 ||
            pathIndex >= path.Count)
        {
            return target;
        }

        Vector3 segmentStart =
            path[pathIndex - 1];

        Vector3 segmentEnd =
            path[pathIndex];

        segmentStart.y = 0f;
        segmentEnd.y = 0f;

        Vector3 current =
            transform.position;

        current.y = 0f;

        Vector3 segment =
            segmentEnd - segmentStart;

        float segmentLength =
            segment.magnitude;

        if (segmentLength <= 0.0001f)
        {
            return target;
        }

        Vector3 segmentDirection =
            segment / segmentLength;

        // ---------------------------------------------------------
        // Find where we currently are along this path segment.
        // ---------------------------------------------------------

        float t =
            Vector3.Dot(
                current - segmentStart,
                segment)
            / segment.sqrMagnitude;

        t = Mathf.Clamp01(t);

        Vector3 lookAheadPoint =
            segmentStart +
            segment * t;

        float remainingLookAhead =
            PathLookAheadDistance();

        // ---------------------------------------------------------
        // First consume the remaining part of the current segment.
        // ---------------------------------------------------------

        float distanceToSegmentEnd =
            Vector3.Distance(
                lookAheadPoint,
                segmentEnd);

        if (remainingLookAhead <= distanceToSegmentEnd)
        {
            lookAheadPoint +=
                segmentDirection *
                remainingLookAhead;

            return ProjectPositionToGround(
                lookAheadPoint);
        }

        remainingLookAhead -=
            distanceToSegmentEnd;

        lookAheadPoint =
            segmentEnd;

        // ---------------------------------------------------------
        // Continue through following path segments,
        // but only while the path stays approximately straight.
        // ---------------------------------------------------------

        Vector3 previousDirection =
            segmentDirection;

        int nextIndex =
            pathIndex + 1;

        while (nextIndex < path.Count &&
               remainingLookAhead > 0f)
        {
            Vector3 nextPoint =
                path[nextIndex];

            nextPoint.y = 0f;

            Vector3 nextSegment =
                nextPoint -
                lookAheadPoint;

            float nextSegmentLength =
                nextSegment.magnitude;

            if (nextSegmentLength <= 0.0001f)
            {
                nextIndex++;
                continue;
            }

            Vector3 nextDirection =
                nextSegment /
                nextSegmentLength;

            float turnAngle =
                Vector3.Angle(
                    previousDirection,
                    nextDirection);

            // Do not look through an actual corner.
            if (turnAngle >
                maxLookAheadTurnAngle)
            {
                break;
            }

            float distanceToTravel =
                Mathf.Min(
                    remainingLookAhead,
                    nextSegmentLength);

            lookAheadPoint +=
                nextDirection *
                distanceToTravel;

            remainingLookAhead -=
                distanceToTravel;

            // We stopped somewhere inside this segment.
            if (distanceToTravel <
                nextSegmentLength)
            {
                break;
            }

            previousDirection =
                nextDirection;

            nextIndex++;
        }

        return ProjectPositionToGround(
            lookAheadPoint);
    }

    private float PathLookAheadDistance()
    {
        if (terrainGrid == null)
            return 1f;

        return terrainGrid.CellSize *
            pathLookAheadCells;
    }

    // ---------------------------------------------------------------------
    // Path relaxation 
    // ---------------------------------------------------------------------

    private void TryRelaxPath()
    {
        if (terrainGrid == null ||
            owner == null ||
            path.Count == 0 ||
            pathIndex >= path.Count - 1)
        {
            return;
        }

        int furthestIndex = Mathf.Min(pathIndex + pathRelaxLookAheadNodes, path.Count - 1);

        for (int candidateIndex = furthestIndex; candidateIndex > pathIndex; candidateIndex--)
        {
            if (!CanRelaxDirectlyTo(path[candidateIndex]))
                continue;
            
            pathIndex = candidateIndex;
            return;
        }
    }

    private bool CanRelaxDirectlyTo(Vector3 target)
    {
        if (terrainGrid == null || owner == null)
        {
            return false;
        }

        GridCoord start = terrainGrid.WorldToCell(transform.position);
        GridCoord end = terrainGrid.WorldToCell(target);
        return HasClearGridLine(start, end);
    }

    // Supercover liner traversal algorithm
    private bool HasClearGridLine(GridCoord start, GridCoord end)
    {
        int x = start.x;
        int z = start.z;

        int deltaX = end.x - start.x;
        int deltaZ = end.z - start.z;

        int countX = Mathf.Abs(deltaX);
        int countZ = Mathf.Abs(deltaZ);

        int stepX = deltaX == 0 ? 0 : deltaX > 0 ? 1 : -1;
        int stepZ = deltaZ == 0 ? 0 : deltaZ > 0 ? 1 : -1;

        int progressedX = 0;
        int progressedZ = 0;

        if (!CanUseRelaxationCell(new GridCoord(x, z)))
        {
            return false;
        }

        while (progressedX < countX || progressedZ < countZ)
        {
            int decision = (1 + 2 * progressedX) * countZ - (1 + 2 * progressedZ) * countX;

            if (decision == 0)
            {
                // The line crosses exactly through a grid corner.
                //
                // Check both side cells before allowing
                // the diagonal transition.

                GridCoord sideX = new GridCoord(x + stepX, z);

                GridCoord sideZ = new GridCoord(x, z + stepZ);

                if (!CanUseRelaxationCell(sideX) || !CanUseRelaxationCell(sideZ))
                {
                    return false;
                }

                x += stepX;
                z += stepZ;

                progressedX++;
                progressedZ++;
            }
            else if (decision < 0)
            {
                x += stepX;
                progressedX++;
            }
            else
            {
                z += stepZ;
                progressedZ++;
            }

            GridCoord current = new GridCoord(x, z);

            if (!CanUseRelaxationCell(current))
                return false;
        }

        return true;
    }

    private bool CanUseRelaxationCell(GridCoord coord)
    {
        if (!terrainGrid.IsInside(coord))
            return false;

        // Static geometry + requester radius.
        if (!terrainGrid.HasNavigationClearance(coord, owner.NavigationRadius))
        {
            return false;
        }

        // Current mobile-unit geometry.
        if (navigationState != null && navigationState.WouldOverlapOccupiedUnit(coord, owner))
        {
            return false;
        }

        return true;
    }

    // ---------------------------------------------------------------------
    // Debug
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        DrawOccupancyGizmo(); 

        if (!drawPathGizmos || path == null || path.Count == 0)
            return;

        Gizmos.color = Color.cyan;

        //for (int i = 0; i < path.Count; i++)
        //{
        //    Vector3 point = GetGizmoGroundPoint(path[i]);
        //    Gizmos.DrawSphere(point, 0.15f);

        //    if (i < path.Count - 1)
        //        DrawGroundedGizmoLine(path[i], path[i + 1]);
        //}

        //if (hasPath)
        //{
        //    Vector3 point =
        //        GetGizmoGroundPoint(
        //            debugLookAheadTarget);

        //    Gizmos.color =
        //        new Color(1f, 0.5f, 0f);

        //    Gizmos.DrawSphere(
        //        point,
        //        0.12f);

        //    Vector3 unitPosition =
        //        GetGizmoGroundPoint(
        //            transform.position);

        //    Gizmos.DrawLine(
        //        unitPosition,
        //        point);
        //}
    }

    private void DrawGroundedGizmoLine(Vector3 start, Vector3 end)
    {
        Vector3 startFlat = new Vector3(start.x, 0f, start.z);
        Vector3 endFlat = new Vector3(end.x, 0f, end.z);

        float distance = Vector3.Distance(startFlat, endFlat);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / gizmoSampleSpacing));

        Vector3 previous = GetGizmoGroundPoint(start);

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;

            Vector3 sample = Vector3.Lerp(start, end, t);
            Vector3 current = GetGizmoGroundPoint(sample);

            Gizmos.DrawLine(previous, current);
            previous = current;
        }
    }

    private Vector3 GetGizmoGroundPoint(Vector3 position)
    {
        Vector3 grounded = position;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        if (terrain != null)
        {
            float terrainY = terrain.SampleHeight(position) + terrain.transform.position.y;
            grounded.y = terrainY + gizmoHeightOffset;
            return grounded;
        }

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleRadius, navMeshAreaMask))
        {
            grounded = hit.position + Vector3.up * gizmoHeightOffset;
            return grounded;
        }

        return grounded + Vector3.up * gizmoHeightOffset;
    }


    private void DrawCellGizmo(GridCoord cell, Color color)
    {
        if (!terrainGrid.IsInside(cell))
            return;

        Vector3 center = terrainGrid.CellToWorld(cell);
        center.y += gizmoHeightOffset;

        Vector3 size = new Vector3(terrainGrid.CellSize * 0.9f, 0.05f, terrainGrid.CellSize * 0.9f);

        Color fillColor = color;
        fillColor.a = 0.25f;

        Gizmos.color = fillColor;
        Gizmos.DrawCube(center, size);

        Gizmos.color = color;
        Gizmos.DrawWireCube(center, size);
    }

    private void DrawOccupancyGizmo()
    {
        if (!drawOccupancyGizmo ||
            owner == null ||
            terrainGrid == null ||
            navigationState == null)
        {
            return;
        }

        GridCoord physicalCell = terrainGrid.WorldToCell(transform.position);
        GridCoord? registeredCell = navigationState.GetOccupiedCell(owner);

        float cellSize = terrainGrid.CellSize;

        // No occupancy registered at all.
        if (!registeredCell.HasValue)
        {
            Gizmos.color = Color.yellow;

            Vector3 physicalCenter =
                GetGizmoGroundPoint(terrainGrid.CellToWorld(physicalCell));

            Gizmos.DrawWireCube(
                physicalCenter,
                new Vector3(cellSize * 0.9f, 0.15f, cellSize * 0.9f));

            return;
        }

        bool cellMatches =
            registeredCell.Value.x == physicalCell.x &&
            registeredCell.Value.z == physicalCell.z;

        int occupantCount =
            navigationState.GetOccupantCount(registeredCell.Value);

        if (!cellMatches)
        {
            // Navigation state is stale / incorrect.
            Gizmos.color = Color.magenta;
        }
        else if (occupantCount > 1)
        {
            // Multiple units registered in the exact same cell.
            Gizmos.color = Color.red;
        }
        else
        {
            // Everything healthy.
            Gizmos.color = Color.green;
        }

        Vector3 registeredCenter =
            GetGizmoGroundPoint(
                terrainGrid.CellToWorld(registeredCell.Value));

        Gizmos.DrawWireCube(
            registeredCenter,
            new Vector3(cellSize * 0.9f, 0.15f, cellSize * 0.9f));

        // If physical position and registered occupancy disagree,
        // also draw the physical cell.
        if (!cellMatches)
        {
            Gizmos.color = Color.yellow;

            Vector3 physicalCenter =
                GetGizmoGroundPoint(
                    terrainGrid.CellToWorld(physicalCell));

            Gizmos.DrawWireCube(
                physicalCenter,
                new Vector3(cellSize * 0.65f, 0.2f, cellSize * 0.65f));
        }

    }

}