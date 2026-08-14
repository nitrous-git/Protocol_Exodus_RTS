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
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private float gizmoSampleSpacing = 0.5f;
    [SerializeField] private float gizmoHeightOffset = 0.05f;

    private TerrainGrid terrainGrid;

    private UnitBase owner;
    private IPathfindingService pathfindingService;
    private float moveSpeed;

    private readonly List<Vector3> path = new List<Vector3>();
    private int pathIndex;
    private bool hasPath;

    public bool HasPath { get { return hasPath; } }
    public bool HasArrived { get { return !hasPath; } }

    public void Initialize(UnitBase owner, 
        IPathfindingService pathfindingService, 
        TerrainGrid terrainGrid,
        float moveSpeed)
    {
        this.owner = owner;
        this.pathfindingService = pathfindingService;
        this.moveSpeed = moveSpeed;
        this.terrainGrid = terrainGrid;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        SnapToGround();
        //SnapToCurrentCellCenter();
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

        AdvanceIntermediateWaypoints();

        Vector3 target = path[pathIndex];

        MoveTowardsTarget(target);
    }

    private void MoveTowardsTarget(Vector3 target)
    {
        if (HasReachedTarget(target))
        {
            CompleteWaypoint(target);
            return;
        }

        Vector3 desiredVelocity = CalculateDesiredVelocity(target);

        ApplyVelocity(desiredVelocity, target);

        if (HasReachedTarget(target))
        {
            CompleteWaypoint(target);
        }
    }

    private Vector3 CalculateDesiredVelocity(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= Mathf.Epsilon)
            return Vector3.zero;

        return direction.normalized * moveSpeed;
    }

    private void ApplyVelocity(Vector3 velocity, Vector3 target)
    {
        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 targetFlat = new Vector3(target.x, 0f, target.z);

        float moveDistance = velocity.magnitude * Time.deltaTime;
        float targetDistance = Vector3.Distance(currentFlat, targetFlat);

        Vector3 nextFlat;

        if (moveDistance >= targetDistance)
        {
            nextFlat = targetFlat;
        }
        else
        {
            nextFlat = currentFlat + velocity * Time.deltaTime;
        }

        Vector3 nextPosition = new Vector3(nextFlat.x, transform.position.y, nextFlat.z);

        nextPosition = ProjectPositionToGround(nextPosition);

        Vector3 movement = nextPosition - transform.position;

        transform.position = nextPosition;

        RotateTowardsMovement(movement);
    }


    private void CompleteWaypoint(Vector3 target)
    {
        SnapToTarget(target);

        pathIndex++;

        if (pathIndex >= path.Count)
        {
            ClearPath();
        }
    }

    private void AdvanceIntermediateWaypoints()
    {
        while (pathIndex < path.Count - 1)
        {
            if (!IsWithinWaypointReach(path[pathIndex]))
                return;

            pathIndex++;
        }
    }

    private bool HasReachedTarget(Vector3 target)
    {
        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 targetFlat = new Vector3(target.x, 0f, target.z);

        return (targetFlat - currentFlat).sqrMagnitude <= 0.000001f;
    }

    private bool IsWithinWaypointReach(Vector3 waypoint)
    {
        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 waypointFlat = new Vector3(waypoint.x, 0f, waypoint.z);

        float reachDistance = WaypointReachDistance();

        return (waypointFlat - currentFlat).sqrMagnitude <= reachDistance * reachDistance;
    }

    private float WaypointReachDistance()
    {
        if (terrainGrid == null)
            return 0.25f;
        
        return terrainGrid.CellSize * waypointReachFraction;
    }

    private void SnapToTarget(Vector3 target)
    {
        transform.position = ProjectPositionToGround(target);
    }

    private void ClearPath()
    {
        path.Clear();
        pathIndex = 0;
        hasPath = false;
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
    // Debug
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!drawPathGizmos || path == null || path.Count == 0)
            return;

        Gizmos.color = Color.cyan;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 point = GetGizmoGroundPoint(path[i]);
            Gizmos.DrawSphere(point, 0.15f);

            if (i < path.Count - 1)
                DrawGroundedGizmoLine(path[i], path[i + 1]);
        }
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

}