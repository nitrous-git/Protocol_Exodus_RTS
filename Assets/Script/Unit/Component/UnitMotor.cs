using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class UnitMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Ground Following")]
    [SerializeField] private bool followTerrainHeight = true;
    [SerializeField] private bool useNavMeshHeight = false;
    [SerializeField] private Terrain terrain;
    [SerializeField] private float baseHeightOffset = 0f;
    [SerializeField] private float navMeshSampleRadius = 2f;
    [SerializeField] private int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Blocked Movement")]
    [SerializeField] private float blockedRepathDelay = 0.75f;

    [Header("Congestion Lookahead")]
    [SerializeField] private int lookaheadCells = 4;
    [SerializeField] private float congestionRepathDelay = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool drawPathGizmos = true;
    [SerializeField] private bool drawTraversalGizmos = true;
    [SerializeField] private float gizmoSampleSpacing = 0.5f;
    [SerializeField] private float gizmoHeightOffset = 0.05f;

    private TerrainGrid terrainGrid;
    private GridReservationSystem gridReservationSystem;

    private UnitBase owner;
    private IPathfindingService pathfindingService;
    private float moveSpeed;

    private readonly List<Vector3> path = new List<Vector3>();
    private int pathIndex;
    private bool hasPath;

    private GridCoord currentCell;
    private GridCoord nextCell; 
    private bool hasCurrentCellClaim;
    private bool hasNextCellClaim;

    private Vector3 pendingDestination;
    private bool hasPendingDestination;
    private bool stopAfterCurrentStep;

    private Vector3 activeDestination;

    private float blockedTime;
    private GridCoord blockedCell;
    private bool hasBlockedCell;

    private float congestionTime;
    private GridCoord congestionCell;
    private bool hasCongestionCell;
    private bool repathAfterCurrentStep;

    private readonly List<Vector3> repathBuffer = new List<Vector3>();

    public bool HasPath { get { return hasPath; } }
    public bool HasArrived { get { return !hasPath; } }

    public GridCoord NextCell => nextCell;
    public GridCoord CurrentCell => currentCell;
    public bool HasCurrentCellClaim => hasCurrentCellClaim;
    public bool HasNextCellClaim => hasNextCellClaim;

    public void Initialize(UnitBase owner, 
        IPathfindingService pathfindingService, 
        TerrainGrid terrainGrid,
        GridReservationSystem gridReservationSystem,
        float moveSpeed)
    {
        this.owner = owner;
        this.pathfindingService = pathfindingService;
        this.moveSpeed = moveSpeed;
        this.terrainGrid = terrainGrid;
        this.gridReservationSystem = gridReservationSystem;

        if (terrain == null)
            terrain = Terrain.activeTerrain;

        SnapToGround();

        if (TryClaimCurrentCell())
        {
            SnapToCurrentCellCenter();
        }
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
        if (!hasCurrentCellClaim && !TryClaimCurrentCell())
        {
            Debug.LogWarning(name + " cannot move because it does not own its current grid cell.");
            ClearPath();
            return false;
        }

        // A traversal step is already underway.
        // Finish it before changing direction.
        if (hasNextCellClaim)
        {
            pendingDestination = destination;
            hasPendingDestination = true;
            stopAfterCurrentStep = false;
            return true;
        }

        hasPendingDestination = false;
        stopAfterCurrentStep = false;

        return TryBuildPath(destination);
    }

    public void Stop()
    {
        hasPendingDestination = false;

        // Already travelling toward a claimed cell.
        // Finish that safe traversal first.
        if (hasNextCellClaim)
        {
            stopAfterCurrentStep = true;
            return;
        }

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

        activeDestination = destination;

        pathIndex = 0;
        hasPath = path.Count > 0;

        ResetBlockedState();
        //ResetCongestionState();

        return true;
    }


    private void FollowPath()
    {
        if (!hasPath || pathIndex >= path.Count)
        {
            ClearPath();
            return;
        }

        // Get next node
        Vector3 target = path[pathIndex];
        GridCoord targetCell = terrainGrid.WorldToCell(target);

        if (IsSameCell(targetCell, currentCell))
        {
            pathIndex++;

            if (pathIndex >= path.Count)
                ClearPath();

            return;
        }

        // If I don't own it
        if (!hasNextCellClaim)
        {
            // try to claim it
            if (!TryClaimNextCell(targetCell))
            {
                HandleBlockedCell(targetCell);
                return;
            }

            ResetBlockedState();
        }
        else if (!IsSameCell(nextCell, targetCell))
        {
            Debug.LogError(name + " path target " + targetCell + " does not match claimed next cell " + nextCell +"." );
            return;
        }

        MoveTowardsTarget(target);
    }

    private void MoveTowardsTarget(Vector3 target)
    {
        Vector3 currentFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 targetFlat = new Vector3(target.x, 0f, target.z);

        Vector3 nextFlat = Vector3.MoveTowards(currentFlat, targetFlat, moveSpeed * Time.deltaTime);

        Vector3 nextPosition = new Vector3(nextFlat.x, transform.position.y, nextFlat.z);
        nextPosition = ProjectPositionToGround(nextPosition);

        Vector3 movementDirection = nextPosition - transform.position;

        transform.position = nextPosition;

        RotateTowardsMovement(movementDirection);

        if ((nextFlat - targetFlat).sqrMagnitude <= 0.000001f)
        {
            SnapToTarget(target);
            CompleteTraversalStep();
        }
    }

    private void SnapToTarget(Vector3 target)
    {
        transform.position = ProjectPositionToGround(target);
    }

    private void CompleteTraversalStep()
    {
        if (!hasNextCellClaim)
        {
            Debug.LogError(name + " completed a traversal step without owning the target cell.");
            ClearPath();
            return;
        }

        GridCoord previousCell = currentCell;

        currentCell = nextCell;
        hasNextCellClaim = false;

        gridReservationSystem.Release(previousCell, owner, GridReservationType.Traversal);

        pathIndex++;

        HandleCompletedStep();
    }

    private void HandleCompletedStep()
    {
        if (hasPendingDestination)
        {
            Vector3 destination = pendingDestination;

            hasPendingDestination = false;
            stopAfterCurrentStep = false;

            TryBuildPath(destination);
            return;
        }

        if (stopAfterCurrentStep)
        {
            ClearPath();
            return;
        }

        if (pathIndex >= path.Count)
        {
            ClearPath();
        }
    }


    private void ClearPath()
    {
        path.Clear();
        pathIndex = 0;
        hasPath = false;
        stopAfterCurrentStep = false;
    }

    // ---------------------------------------------------------------------
    // Block
    // ---------------------------------------------------------------------

    private void HandleBlockedCell(GridCoord cell)
    {
        if (!hasBlockedCell || !IsSameCell(blockedCell, cell))
        {
            blockedCell = cell;
            hasBlockedCell = true;
            blockedTime = 0.0f;
            return;
        }

        blockedTime += Time.deltaTime;

        if (blockedTime < blockedRepathDelay)
        {
            return;
        }

        blockedTime = 0.0f;

        TryRepath();
    }

    private bool TryRepath()
    {
        if (pathfindingService == null)
        {
            return false;   
        }

        repathBuffer.Clear();

        Vector3 start = terrainGrid.CellToWorld(currentCell);
        start = ProjectPositionToGround(start);

        bool foundPath = pathfindingService.TryFindPath(owner, start, activeDestination, repathBuffer);

        if (!foundPath)
        {
            return false;
        }

        SnapPathToGround(repathBuffer);

        path.Clear();
        path.AddRange(repathBuffer);

        pathIndex = 0;
        hasPath = path.Count > 0;

        ResetBlockedState();
        //ResetCongestionState();

        return true;
    }

    private void ResetCongestionState()
    {
        throw new NotImplementedException();
    }

    private void ResetBlockedState()
    {
        blockedTime = 0.0f;
        hasBlockedCell = false;
    }

    // ---------------------------------------------------------------------
    // Ground
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

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    private void SnapToCurrentCellCenter()
    {
        Vector3 center = terrainGrid.CellToWorld(currentCell);
        transform.position = ProjectPositionToGround(center);
    }

    // ---------------------------------------------------------------------
    // Reservation
    // ---------------------------------------------------------------------

    private bool TryClaimCurrentCell()
    {
        if (owner == null || terrainGrid == null || gridReservationSystem == null)
            return false;

        GridCoord cell = terrainGrid.WorldToCell(transform.position);

        if (!terrainGrid.IsInside(cell))
        {
            Debug.LogWarning($"{name} cannot claim its current cell because it is outside the terrain grid.");
            return false;
        }

        if (!gridReservationSystem.TryReserve(cell, owner, GridReservationType.Traversal))
        {
            Debug.LogWarning($"{name} cannot claim traversal cell {cell}.");
            return false;
        }

        currentCell = cell;
        hasCurrentCellClaim = true;

        return true;
    }

    private bool TryClaimNextCell(GridCoord cell)
    {
        if (gridReservationSystem == null || owner == null)
            return false;

        if (IsSameCell(cell, currentCell))
            return true;

        if (hasNextCellClaim)
            return IsSameCell(nextCell, cell);

        if (!gridReservationSystem.TryReserve(cell, owner, GridReservationType.Traversal))
        {
            return false;
        }

        nextCell = cell;
        hasNextCellClaim = true;

        return true;
    }

    private bool IsSameCell(GridCoord first, GridCoord second)
    {
        return first.x == second.x && first.z == second.z;
    }

    // ---------------------------------------------------------------------
    // Debug
    // ---------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        DrawTraversalGizmos();

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

    private void DrawTraversalGizmos()
    {
        if (!drawTraversalGizmos || terrainGrid == null)
            return;

        if (hasCurrentCellClaim)
        {
            DrawCellGizmo(currentCell, Color.green);
        }

        if (hasNextCellClaim)
        {
            DrawCellGizmo(nextCell, Color.yellow);
        }

        if (hasBlockedCell) 
        {
            DrawCellGizmo(blockedCell, Color.red);
        }
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