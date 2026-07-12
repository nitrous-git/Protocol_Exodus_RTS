using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class UnitMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float stoppingDistance = 0.15f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Ground Following")]
    [SerializeField] private bool followTerrainHeight = true;
    [SerializeField] private bool useNavMeshHeight = false;
    [SerializeField] private Terrain terrain;
    [SerializeField] private float baseHeightOffset = 0f;
    [SerializeField] private float navMeshSampleRadius = 2f;
    [SerializeField] private int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Local Avoidance")]
    [SerializeField] private bool useLocalSeparation = true;
    [SerializeField] private float separationRadius = 0.9f;
    [SerializeField] private float separationWeight = 1.25f;
    [SerializeField] private LayerMask unitMask;

    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private float gizmoSampleSpacing = 0.5f;
    [SerializeField] private float gizmoHeightOffset = 0.05f;

    private UnitBase owner;
    private IPathfindingService pathfindingService;
    private float moveSpeed;

    private readonly List<Vector3> path = new List<Vector3>();
    private int pathIndex;
    private bool hasPath;

    public bool HasPath { get { return hasPath; } }
    public bool HasArrived { get { return !hasPath; } }

    public void Initialize(UnitBase owner, IPathfindingService pathfindingService, float moveSpeed)
    {
        this.owner = owner;
        this.pathfindingService = pathfindingService;
        this.moveSpeed = moveSpeed;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        SnapToGround();
    }

    public bool MoveTo(Vector3 destination)
    {
        if (pathfindingService == null)
        {
            Debug.LogError(name + " cannot move because no IPathfindingService is available.");
            Stop();
            return false;
        }

        bool foundPath = pathfindingService.TryFindPath(transform.position, destination, path);

        if (!foundPath)
        {
            Stop();
            return false;
        }

        SnapPathToGround();

        pathIndex = 0;
        hasPath = true;
        SkipReachedCorners();
        return true;
    }

    public void Stop()
    {
        path.Clear();
        pathIndex = 0;
        hasPath = false;
    }

    public void Tick()
    {
        if (!hasPath)
            return;

        FollowPath();
    }

    private void FollowPath()
    {
        if (pathIndex >= path.Count)
        {
            Stop();
            return;
        }

        Vector3 target = path[pathIndex];

        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 targetFlat = new Vector3(target.x, 0f, target.z);

        Vector3 toTargetFlat = targetFlat - currentFlat;

        if (toTargetFlat.magnitude <= stoppingDistance)
        {
            pathIndex++;
            SkipReachedCorners();

            if (pathIndex >= path.Count)
                Stop();

            return;
        }

        Vector3 desiredDirection = (targetFlat - currentFlat).normalized;
        Vector3 separationDirection = GetSeparationDirection();

        Vector3 finalDirection = desiredDirection + separationDirection * separationWeight;

        if (finalDirection.sqrMagnitude <= 0.0001f)
            finalDirection = desiredDirection;

        finalDirection.Normalize();

        Vector3 nextFlat = currentFlat + finalDirection * moveSpeed * Time.deltaTime;

        Vector3 nextPosition = new Vector3(
            nextFlat.x,
            transform.position.y,
            nextFlat.z
        );

        nextPosition = ProjectPositionToGround(nextPosition);

        Vector3 movementDirection = nextPosition - transform.position;

        transform.position = nextPosition;
        RotateTowardsMovement(movementDirection);
    }

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

    private void SnapPathToGround()
    {
        for (int i = 0; i < path.Count; i++)
        {
            path[i] = ProjectPositionToGround(path[i]);
        }
    }

    private Vector3 GetSeparationDirection()
    {
        if (!useLocalSeparation)
            return Vector3.zero;

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            separationRadius,
            unitMask
        );

        Vector3 separation = Vector3.zero;
        int count = 0;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];

            if (hit == null)
                continue;

            UnitBase otherUnit = hit.GetComponentInParent<UnitBase>();

            if (otherUnit == null || otherUnit == owner)
                continue;

            Vector3 away = transform.position - otherUnit.transform.position;
            away.y = 0f;

            float distance = away.magnitude;

            if (distance <= 0.001f)
                continue;

            float strength = 1f - Mathf.Clamp01(distance / separationRadius);

            separation += away.normalized * strength;
            count++;
        }

        if (count <= 0)
            return Vector3.zero;

        return separation.normalized;
    }

    private void SkipReachedCorners()
    {
        while (pathIndex < path.Count)
        {
            Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
            Vector3 cornerFlat = new Vector3(path[pathIndex].x, 0f, path[pathIndex].z);

            float distance = Vector3.Distance(currentFlat, cornerFlat);

            if (distance > stoppingDistance)
                break;

            pathIndex++;
        }

        if (pathIndex >= path.Count)
            Stop();
    }

    private void RotateTowardsMovement(Vector3 movementDirection)
    {
        movementDirection.y = 0f;

        if (movementDirection.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(movementDirection.normalized, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private void OnDrawGizmos()
    {
        if (!drawPathGizmos || path == null || path.Count == 0)
            return;

        Gizmos.color = Color.cyan;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 point = GetGizmoGroundPoint(path[i]);
            Gizmos.DrawSphere(point, 0.45f);

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
}