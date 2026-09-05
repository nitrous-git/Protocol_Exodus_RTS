using UnityEngine;

/// <summary>
/// Stores one shared flow-field navigation result.
///
/// The field contains:
/// - static traversability for the requested clearance radius,
/// - integration cost toward the destination,
/// - flow direction for each grid cell.
///
/// FlowFieldBuilder is responsible for populating the data.
/// </summary>
public sealed class FlowField
{
    public const int UnreachableCost = int.MaxValue;
    private const float MinDirectionSqr = 0.0001f;

    private readonly bool[] traversable;
    private readonly int[] integrationCosts;
    private readonly Vector3[] directions;
    private readonly int[] clearancePenalties;
    private readonly bool[] goalCells;

    public TerrainGrid Grid { get; }
    public int Width { get; }
    public int Height { get; }
    public Vector3 Destination { get; }
    public GridCoord DestinationCell { get; }
    public float NavigationRadius { get; }
    public Vector2 GoalHalfExtents { get; }

    public bool IsBuilt { get; private set; }
    public int GoalCellCount { get; internal set; }

    public FlowField(
        TerrainGrid grid,
        Vector3 destination,
        float navigationRadius, 
        Vector2 goalHalfExtents)
    {
        Grid = grid;

        Destination = destination;

        NavigationRadius =
            Mathf.Max(
                0f,
                navigationRadius);

        GoalHalfExtents = new Vector2(
            Mathf.Max(goalHalfExtents.x, grid != null ? grid.CellSize * 0.5f : 0f), 
            Mathf.Max(goalHalfExtents.y, grid != null ? grid.CellSize * 0.5f: 0f));

        if (grid == null)
        {
            Width = 0;
            Height = 0;

            traversable = new bool[0];
            integrationCosts = new int[0];
            directions = new Vector3[0];
            DestinationCell = default;

            return;
        }

        Width = grid.Width;
        Height = grid.Height;

        DestinationCell =
            grid.WorldToCell(
                destination);

        int cellCount = Width * Height;

        traversable = new bool[cellCount];
        integrationCosts = new int[cellCount];
        directions = new Vector3[cellCount];

        clearancePenalties = new int[cellCount];
        goalCells = new bool[cellCount];


        for (int i = 0; i < cellCount; i++)
        {
            integrationCosts[i] = UnreachableCost;
        }
    }

    public bool IsInside(GridCoord coord)
    {
        return
            coord.x >= 0 &&
            coord.z >= 0 &&
            coord.x < Width &&
            coord.z < Height;
    }

    public bool IsTraversable(GridCoord coord)
    {
        if (!IsInside(coord))
            return false;

        return traversable[GetIndex(coord)];
    }

    public bool IsReachable(GridCoord coord)
    {
        if (!IsInside(coord))
            return false;

        int index = GetIndex(coord);

        return
            traversable[index] &&
            integrationCosts[index] !=
                UnreachableCost;
    }

    public int GetIntegrationCost(GridCoord coord)
    {
        if (!IsInside(coord))
        {
            return UnreachableCost;
        }

        return integrationCosts[
            GetIndex(coord)];
    }

    public Vector3 GetDirection(GridCoord coord)
    {
        if (!IsInside(coord))
        {
            return Vector3.zero;
        }

        return directions[
            GetIndex(coord)];
    }

    internal void SetTraversable(GridCoord coord, bool value)
    {
        if (!IsInside(coord))
            return;

        int index =
            GetIndex(coord);

        traversable[index] = value;

        if (value)
            return;

        integrationCosts[index] = UnreachableCost;

        directions[index] = Vector3.zero;
    }

    internal void SetIntegrationCost(GridCoord coord, int cost)
    {
        if (!IsInside(coord))
            return;

        integrationCosts[GetIndex(coord)] = cost;
    }

    internal void SetDirection(
        GridCoord coord,
        Vector3 direction)
    {
        if (!IsInside(coord))
            return;

        direction.y = 0f;

        if (direction.sqrMagnitude <= MinDirectionSqr)
        {
            directions[GetIndex(coord)] = Vector3.zero;
            return;
        }

        directions[GetIndex(coord)] = direction.normalized;
    }

    internal void SetNormalizedDirection(GridCoord coord, Vector3 direction)
    {
        if (!IsInside(coord))
            return;

        directions[GetIndex(coord)] = direction;
    }

    internal void CompleteBuild()
    {
        IsBuilt = true;
    }

    // -----------------------------------------------------------
    // Getter & Setter
    // -----------------------------------------------------------

    private int GetIndex(GridCoord coord)
    {
        return coord.z * Width + coord.x;
    }

    public int GetClearancePenalty(GridCoord coord)
    {
        if (!IsInside(coord))
            return 0;

        return clearancePenalties[
            GetIndex(coord)];
    }

    internal void SetClearancePenalty(
        GridCoord coord,
        int penalty)
    {
        if (!IsInside(coord))
            return;

        clearancePenalties[GetIndex(coord)] = Mathf.Max(0, penalty);
    }

    public bool IsGoalCell(GridCoord coord)
    {
        if (!IsInside(coord))
            return false;

        return goalCells[GetIndex(coord)];
    }

    internal void SetGoalCell(GridCoord coord, bool value)
    {
        if (!IsInside(coord))
            return;

        goalCells[GetIndex(coord)] = value;
    }

    // no bound check methods
    internal bool IsTraversableAt(int index)
    {
        return traversable[index];
    }

    internal int GetIntegrationCostAt(int index)
    {
        return integrationCosts[index];
    }

    internal void SetIntegrationCostAt(int index, int cost)
    {
        integrationCosts[index] = cost;
    }

    internal int GetClearancePenaltyAt(int index)
    {
        return clearancePenalties[index];
    }

    internal bool IsReachableAt(int index)
    {
        return traversable[index] && integrationCosts[index] !=  UnreachableCost;
    }

    internal bool IsGoalCellAt(int index)
    {
        return goalCells[index];
    }

    internal void SetNormalizedDirectionAt(int index, Vector3 direction)
    {
        directions[index] = direction;
    }

}