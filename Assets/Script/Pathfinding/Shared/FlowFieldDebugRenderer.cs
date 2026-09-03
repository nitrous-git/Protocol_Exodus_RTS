using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class FlowFieldDebugRenderer : MonoBehaviour
{
    [Header("Debug Draw")]
    [SerializeField] private bool drawFlowField = true;
    [SerializeField] private bool drawOnlyNearCamera = true;
    [SerializeField] private float drawRadius = 80f;
    [SerializeField] private int stride = 1;

    [Header("Cell Visualization")]
    [SerializeField] private float yOffset = 0.08f;
    [SerializeField] private float cellFillScale = 0.95f;
    [SerializeField] private float cellAlpha = 0.35f;

    [Header("Direction Visualization")]
    [SerializeField] private float arrowYOffset = 0.12f;
    [SerializeField] private float arrowLength = 0.65f;

    [Header("Special Cells")]
    [SerializeField]
    private Color unreachableColor =
        new Color(0.5f, 0.5f, 0.5f, 0.35f);

    private readonly Vector3[] quad = new Vector3[4];

    private FlowField field;
    private int maxReachableCost = 1;

    public void SetField(FlowField field)
    {
        this.field = field;

        maxReachableCost =
            FindMaximumReachableCost(field);

        Debug.Log(
            $"FlowField debug max reachable cost: {maxReachableCost}");
    }

    public void Clear()
    {
        field = null;
        maxReachableCost = 1;
    }

    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (!drawFlowField)
            return;

        if (field == null ||
            !field.IsBuilt ||
            field.Grid == null)
        {
            return;
        }

        Camera camera = Camera.current;

        bool hasCamera =
            camera != null;

        Vector3 cameraPosition =
            hasCamera
                ? camera.transform.position
                : Vector3.zero;

        float drawRadiusSqr =
            drawRadius * drawRadius;

        int step =
            Mathf.Max(1, stride);

        for (int z = 0; z < field.Height; z += step)
        {
            for (int x = 0; x < field.Width; x += step)
            {
                GridCoord coord =
                    new GridCoord(x, z);

                Vector3 center =
                    field.Grid.CellToWorld(coord);

                if (drawOnlyNearCamera && hasCamera)
                {
                    Vector3 offset =
                        center - cameraPosition;

                    offset.y = 0f;

                    if (offset.sqrMagnitude >
                        drawRadiusSqr)
                    {
                        continue;
                    }
                }

                center += Vector3.up * yOffset;

                if (!field.IsTraversable(coord))
                    continue;

                //if (!field.IsReachable(coord))
                //{
                //    DrawQuad(
                //        center,
                //        unreachableColor);

                //    continue;
                //}

                //DrawIntegrationCost(
                //    coord,
                //    center);

                DrawDirection(
                    coord,
                    center);
            }
        }
#endif
    }

#if UNITY_EDITOR
    private void DrawIntegrationCost(
        GridCoord coord,
        Vector3 center)
    {
        int cost =
            field.GetIntegrationCost(coord);

        float normalizedCost =
            Mathf.Clamp01(
                cost / (float)maxReachableCost);

        Color color =
            EvaluateCostColor(normalizedCost);

        color.a = cellAlpha;

        DrawQuad(center, color);
    }

    private void DrawQuad(
        Vector3 center,
        Color color)
    {
        float halfSize =
            field.Grid.CellSize *
            cellFillScale *
            0.5f;

        quad[0] =
            center +
            new Vector3(-halfSize, 0f, -halfSize);

        quad[1] =
            center +
            new Vector3(-halfSize, 0f, halfSize);

        quad[2] =
            center +
            new Vector3(halfSize, 0f, halfSize);

        quad[3] =
            center +
            new Vector3(halfSize, 0f, -halfSize);

        Handles.color = color;
        Handles.DrawAAConvexPolygon(quad);
    }

    private void DrawDirection(
        GridCoord coord,
        Vector3 center)
    {
        Vector3 direction =
            field.GetDirection(coord);

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Gizmos.color = Color.yellow;

        Vector3 start =
            center +
            Vector3.up * arrowYOffset;

        Gizmos.DrawRay(
            start,
            direction.normalized *
            field.Grid.CellSize *
            arrowLength);
    }

    private Color EvaluateCostColor(
        float normalizedCost)
    {
        if (normalizedCost < 0.5f)
        {
            return Color.Lerp(
                Color.green,
                Color.yellow,
                normalizedCost * 2f);
        }

        return Color.Lerp(
            Color.yellow,
            Color.magenta,
            (normalizedCost - 0.5f) * 2f);
    }
#endif

    private static int FindMaximumReachableCost(
        FlowField field)
    {
        if (field == null || !field.IsBuilt)
            return 1;

        int maxCost = 1;

        for (int z = 0; z < field.Height; z++)
        {
            for (int x = 0; x < field.Width; x++)
            {
                GridCoord coord =
                    new GridCoord(x, z);

                // Important:
                // only genuine reachable cells participate
                // in normalization.
                if (!field.IsTraversable(coord) ||
                    !field.IsReachable(coord))
                {
                    continue;
                }

                int cost =
                    field.GetIntegrationCost(coord);

                if (cost > maxCost)
                    maxCost = cost;
            }
        }

        return maxCost;
    }
}