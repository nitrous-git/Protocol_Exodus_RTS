using Unity.Profiling;
using UnityEngine;

public class StaticClearanceField
{
    private static readonly ProfilerMarker RebuildMarker = new ProfilerMarker("StaticClearanceField.Rebuild");

    private bool staticClearanceDirty = true;

    private byte[] clearanceBlocked;
    private float[] clearanceTemp;

    private float[] transformInput;
    private float[] transformOutput;
    private int[] transformSites;
    private float[] transformBreaks;

    private TerrainGrid terrainGrid;

    public StaticClearanceField(TerrainGrid terrainGrid) 
    { 
        this.terrainGrid = terrainGrid; 
    }

    private void EnsureStaticClearanceField()
    {
        if (!staticClearanceDirty)
            return;

        using (RebuildMarker.Auto())
        {
            RebuildStaticClearanceField();
        }
    }

    public bool IsStaticNavigationBlocked(GridCell cell)
    {
        if (cell == null)
            return true;

        if (!cell.Walkable)
            return true;

        if (!cell.Occupied)
            return false;

        //
        // Dynamic unit occupancy is NOT static navigation geometry.
        //
        // Buildings/resources/other non-unit occupancy are.
        //
        return cell.OccupyingUnitId < 0;
    }

    public bool HasNavigationClearance(GridCoord coord, float radius)
    {
        GridCell cell = terrainGrid.GetCell(coord);

        if (cell == null)
            return false;

        EnsureStaticClearanceField();

        if (IsStaticNavigationBlocked(cell))
            return false;

        radius = Mathf.Max(0f, radius);

        return radius <= cell.StaticClearanceRadius + 0.0001f;
    }

    public void RebuildStaticClearanceField()
    {
        if (terrainGrid.cells == null ||
            terrainGrid.Width <= 0 ||
            terrainGrid.Height <= 0)
        {
            return;
        }

        //double startTime = Time.realtimeSinceStartupAsDouble;

        //
        // Half-cell-resolution grid.
        //
        int highWidth = terrainGrid.Width * 2 + 1;
        int highHeight = terrainGrid.Height * 2 + 1;

        int highCount = highWidth * highHeight;

        EnsureClearanceBuffers(highWidth, highHeight);

        System.Array.Clear(clearanceBlocked, 0, highCount);

        // ---------------------------------------------------------
        // World/grid boundary.
        // ---------------------------------------------------------

        for (int x = 0; x < highWidth;x++)
        {
            clearanceBlocked[x] = 1;
            clearanceBlocked[ x + (highHeight - 1) * highWidth] = 1;
        }

        for (int z = 0; z < highHeight; z++)
        {
            clearanceBlocked[ z * highWidth] = 1;
            clearanceBlocked[(highWidth - 1) + z * highWidth] = 1;
        }

        // ---------------------------------------------------------
        // Static blocked cells.
        //
        // Each original cell occupies a 3x3 set of points
        // on the half-resolution lattice.
        // ---------------------------------------------------------

        for (int z = 0; z < terrainGrid.Height; z++)
        {
            for (int x = 0; x < terrainGrid.Width; x++)
            {
                GridCell cell = terrainGrid.cells[x, z];

                if (!IsStaticNavigationBlocked(cell))
                {
                    continue;
                }

                int originX = x * 2;
                int originZ = z * 2;

                for (int dz = 0; dz <= 2; dz++)
                {
                    int row = (originZ + dz) * highWidth;

                    for (int dx = 0; dx <= 2; dx++)
                    {
                        clearanceBlocked[row + originX + dx] = 1;
                    }
                }
            }
        }

        // ---------------------------------------------------------
        // Horizontal EDT.
        // ---------------------------------------------------------

        for (int z = 0; z < highHeight; z++)
        {
            int row = z * highWidth;

            for (int x = 0; x < highWidth; x++)
            {
                transformInput[x] = clearanceBlocked[row + x] != 0 ? 0f : float.PositiveInfinity;
            }

            DistanceTransform1D(
                transformInput,
                highWidth,
                transformOutput,
                transformSites,
                transformBreaks);

            for (int x = 0; x < highWidth; x++)
            {
                clearanceTemp[row + x] = transformOutput[x];
            }
        }

        // ---------------------------------------------------------
        // Vertical EDT.
        //
        // We only need final results at original cell centers:
        //
        // high-grid coordinate:
        //      x = 2*cellX + 1
        //      z = 2*cellZ + 1
        // ---------------------------------------------------------

        float halfCell = terrainGrid.CellSize * 0.5f;

        for (int x = 1; x < highWidth - 1; x += 2)
        {
            for (int z = 0; z < highHeight; z++)
            {
                transformInput[z] = clearanceTemp[x + z * highWidth];
            }

            DistanceTransform1D(
                transformInput,
                highHeight,
                transformOutput,
                transformSites,
                transformBreaks);

            int cellX = (x - 1) / 2;

            for (int z = 1; z < highHeight - 1; z += 2)
            {
                int cellZ = (z - 1) / 2;

                GridCell cell = terrainGrid.cells[cellX, cellZ];

                float distanceHalfCells = Mathf.Sqrt(transformOutput[z]);

                cell.StaticClearanceRadius =
                    distanceHalfCells *
                    halfCell;
            }
        }

        staticClearanceDirty = false;

//#if UNITY_EDITOR || DEVELOPMENT_BUILD

//        double elapsedMs = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;

//        Debug.Log(
//            "[ClearanceField] " +
//            "Grid=" + terrainGrid.Width + "x" + terrainGrid.Height +
//            " HighGrid=" + highWidth + "x" + highHeight +
//            " TimeMs=" + elapsedMs.ToString("F2"));

//#endif
    }

    private static void DistanceTransform1D(
        float[] input,
        int count,
        float[] output,
        int[] sites,
        float[] breaks)
    {
        int firstSite = -1;

        for (int i = 0; i < count; i++)
        {
            if (!float.IsPositiveInfinity(input[i]))
            {
                firstSite = i;
                break;
            }
        }

        if (firstSite < 0)
        {
            for (int i = 0; i < count; i++)
            {
                output[i] = float.PositiveInfinity;
            }

            return;
        }

        int k = 0;

        sites[0] = firstSite;

        breaks[0] = float.NegativeInfinity;
        breaks[1] = float.PositiveInfinity;

        for (int q = firstSite + 1; q < count; q++)
        {
            if (float.IsPositiveInfinity(input[q]))
            {
                continue;
            }

            float intersection;

            while (true)
            {
                int vk = sites[k];

                float qSquared = q * q;
                float vkSquared = vk * vk;

                intersection = ((input[q] + qSquared) - (input[vk] + vkSquared)) / (2f * (q - vk));

                if (intersection > breaks[k] || k == 0)
                {
                    break;
                }

                k--;
            }

            k++;

            sites[k] = q;
            breaks[k] = intersection;
            breaks[k + 1] = float.PositiveInfinity;
        }

        k = 0;

        for (int q = 0; q < count; q++)
        {
            while (breaks[k + 1] < q)
            {
                k++;
            }

            float delta = q - sites[k];

            output[q] = delta * delta + input[sites[k]];
        }
    }

    private void EnsureClearanceBuffers(
        int highWidth,
        int highHeight)
    {
        int highCount = highWidth * highHeight;

        int transformLength = Mathf.Max(highWidth, highHeight);

        if (clearanceBlocked == null || clearanceBlocked.Length <
                highCount)
        {
            clearanceBlocked = new byte[highCount];
            clearanceTemp = new float[highCount];
        }

        if (transformInput == null || transformInput.Length < transformLength)
        {
            transformInput = new float[transformLength];
            transformOutput = new float[transformLength];
            transformSites = new int[transformLength];
            transformBreaks =new float[transformLength + 1];
        }
    }

    public void SetDirty(bool isDirty)
    { 
        staticClearanceDirty = isDirty;
    }
}
