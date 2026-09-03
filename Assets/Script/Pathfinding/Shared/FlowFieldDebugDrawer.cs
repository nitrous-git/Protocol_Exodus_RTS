using UnityEngine;

/// <summary>
/// Temporary FlowField visualization.
///
/// Intended for editor/debug validation, not runtime gameplay.
/// </summary>
public static class FlowFieldDebugDrawer
{
    public static void Draw(
        FlowField field,
        int stride = 2,
        float duration = 10f,
        float arrowLength = 0.65f)
    {
        if (field == null ||
            !field.IsBuilt ||
            field.Grid == null)
        {
            return;
        }

        stride =
            Mathf.Max(
                1,
                stride);

        int maxCost =
            FindMaximumReachableCost(
                field);

        for (int z = 0;
             z < field.Height;
             z += stride)
        {
            for (int x = 0;
                 x < field.Width;
                 x += stride)
            {
                GridCoord coord =
                    new GridCoord(x, z);

                Vector3 center =
                    field.Grid.CellToWorld(
                        coord);

                center +=
                    Vector3.up * 0.35f;

                if (!field.IsTraversable(
                    coord))
                {
                    DrawBlockedCell(
                        center,
                        field.Grid.CellSize,
                        duration);

                    continue;
                }

                if (!field.IsReachable(
                    coord))
                {
                    DrawUnreachableCell(
                        center,
                        duration);

                    continue;
                }

                //DrawIntegrationCost(
                //    field,
                //    coord,
                //    center,
                //    maxCost,
                //    duration);

                Vector3 direction =
                    field.GetDirection(
                        coord);

                if (direction.sqrMagnitude <=
                    0.0001f)
                {
                    continue;
                }

                Debug.DrawRay(
                    center,
                    direction *
                    field.Grid.CellSize *
                    arrowLength,
                    Color.yellow,
                    duration,
                    false);
            }
        }
    }

    private static int FindMaximumReachableCost(
        FlowField field)
    {
        int maxCost = 1;

        for (int z = 0;
             z < field.Height;
             z++)
        {
            for (int x = 0;
                 x < field.Width;
                 x++)
            {
                GridCoord coord =
                    new GridCoord(x, z);

                int cost =
                    field.GetIntegrationCost(
                        coord);

                if (cost ==
                    FlowField.UnreachableCost)
                {
                    continue;
                }

                if (cost > maxCost)
                {
                    maxCost = cost;
                }
            }
        }

        return maxCost;
    }

    private static void DrawIntegrationCost(
        FlowField field,
        GridCoord coord,
        Vector3 center,
        int maxCost,
        float duration)
    {
        int cost =
            field.GetIntegrationCost(
                coord);

        float normalizedCost =
            Mathf.Clamp01(
                cost /
                (float)maxCost);

        Color costColor =
            Color.Lerp(
                Color.green,
                Color.magenta,
                normalizedCost);

        Debug.DrawLine(
            center,
            center +
                Vector3.up * 0.15f,
            costColor,
            duration,
            false);
    }

    private static void DrawBlockedCell(
        Vector3 center,
        float cellSize,
        float duration)
    {
        float halfSize =
            cellSize * 0.2f;

        Debug.DrawLine(
            center +
                new Vector3(
                    -halfSize,
                    0f,
                    -halfSize),
            center +
                new Vector3(
                    halfSize,
                    0f,
                    halfSize),
            Color.red,
            duration,
            false);

        Debug.DrawLine(
            center +
                new Vector3(
                    -halfSize,
                    0f,
                    halfSize),
            center +
                new Vector3(
                    halfSize,
                    0f,
                    -halfSize),
            Color.red,
            duration,
            false);
    }

    private static void DrawUnreachableCell(
        Vector3 center,
        float duration)
    {
        Debug.DrawLine(
            center,
            center +
                Vector3.up * 0.1f,
            Color.gray,
            duration,
            false);
    }
}