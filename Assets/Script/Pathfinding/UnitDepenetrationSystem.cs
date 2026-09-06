using System.Collections.Generic;
using UnityEngine;

public sealed class UnitDepenetrationSystem
{
    private const float OverlapTolerance = 0.25f;
    private const float MaxCorrectionPerUnit = 0.45f;
    private const float MovingThreshold = 0.05f;

    // A couple of passes helps dense groups settle without making
    // this into a complicated physics solver.
    private const int SolverIterations = 2;

    private const int InitialBucketCapacity = 8;

    private readonly IReadOnlyList<UnitBase> units;
    private readonly List<UnitBase> activeUnits = new();
    private readonly Dictionary<Vector2Int, List<UnitBase>> spatialBuckets = new();
    private readonly Stack<List<UnitBase>> bucketPool = new();

    public int LastCandidatePairCount { get; private set; }
    public int LastBucketCount { get; private set; }

    public UnitDepenetrationSystem(IReadOnlyList<UnitBase> units)
    {
        this.units = units;
    }

    public void Tick()
    {
        LastCandidatePairCount = 0;
        LastBucketCount = 0;

        if (units == null || units.Count < 2)
            return;

        float maximumRadius = BuildActiveUnitList();

        if (activeUnits.Count < 2)
            return;

        float bucketSize = Mathf.Max(maximumRadius * 2f, 0.01f);

        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            RebuildSpatialBuckets(bucketSize); // Rebuild in O(n)
            ResolveNearbyPairs(bucketSize);
        }
    }

    private float BuildActiveUnitList()
    {
        activeUnits.Clear();

        float maximumRadius = 0f;

        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit = units[i];

            if (!CanResolve(unit))
                continue;

            activeUnits.Add(unit);

            maximumRadius = Mathf.Max(maximumRadius, unit.Definition.NavigationRadius);
        }

        return maximumRadius;
    }

    private void RebuildSpatialBuckets(float bucketSize)
    {
        RecycleBuckets();

        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitBase unit = activeUnits[i];

            // which bucket this unit belong to
            Vector2Int cell = WorldToBucket(unit.Position, bucketSize);

            // cell has no bucket
            if (!spatialBuckets.TryGetValue(cell, out List<UnitBase> bucket))
            {
                // get one from pool
                bucket = GetBucket();
                spatialBuckets.Add(cell, bucket);
            }

            // add unit to bucket
            bucket.Add(unit);
        }

        LastBucketCount = spatialBuckets.Count;
    }

    private void ResolveNearbyPairs(float bucketSize)
    {
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitBase a = activeUnits[i];

            Vector2Int centerCell = WorldToBucket(a.Position, bucketSize);

            // check 3x3 aroud A
            for (int z = -1; z <= 1; z++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Vector2Int neighborCell = new Vector2Int(centerCell.x + x, centerCell.y + z);

                    // check if neighbor has units
                    if (!spatialBuckets.TryGetValue(neighborCell, out List<UnitBase> bucket))
                    {
                        continue;
                    }

                    // check other units
                    for (int j = 0; j < bucket.Count; j++)
                    {
                        UnitBase b = bucket[j];

                        if (ReferenceEquals(a, b))
                            continue;

                        // Every pair is solved only once.
                        // avoid double-checking
                        if (b.UnitId <= a.UnitId)
                            continue;

                        LastCandidatePairCount++;

                        ResolvePair(a, b);
                    }
                }
            }
        }
    }

    private Vector2Int WorldToBucket(Vector3 position, float bucketSize)
    {
        return new Vector2Int(Mathf.FloorToInt(position.x / bucketSize), Mathf.FloorToInt(position.z / bucketSize));
    }

    private List<UnitBase> GetBucket()
    {
        if (bucketPool.Count > 0)
            return bucketPool.Pop(); // Reuse old list

        return new List<UnitBase>(InitialBucketCapacity); // Only create if needed
    }

    private void RecycleBuckets()
    {
        foreach (List<UnitBase> bucket in spatialBuckets.Values)
        {
            bucket.Clear(); // clear the list

            // save all empty list in a stack pool
            bucketPool.Push(bucket); 
        }

        spatialBuckets.Clear();
    }

    private bool CanResolve(UnitBase unit)
    {
        return unit != null
            && unit.IsInitialized
            && unit.IsAlive
            && unit.Motor != null
            && unit.Definition != null;
    }

    private void ResolvePair(UnitBase a, UnitBase b)
    {
        Vector3 delta = b.Position - a.Position;
        delta.y = 0f;

        float radiusA = a.Definition.NavigationRadius;
        float radiusB = b.Definition.NavigationRadius;

        float minimumDistance = radiusA + radiusB - OverlapTolerance;

        float distanceSqr = delta.sqrMagnitude;

        if (distanceSqr >= minimumDistance * minimumDistance)
            return;

        Vector3 normal;
        float distance;

        if (distanceSqr <= 0.000001f)
        {
            // Exact same position.
            // Give the pair a deterministic direction.
            normal = a.UnitId < b.UnitId ? Vector3.right : Vector3.left;

            distance = 0f;
        }
        else
        {
            distance = Mathf.Sqrt(distanceSqr);
            normal = delta / distance;
        }

        float penetration = minimumDistance - distance;

        bool aMoving = a.Motor.CurrentVelocity.sqrMagnitude > MovingThreshold * MovingThreshold;
        bool bMoving = b.Motor.CurrentVelocity.sqrMagnitude > MovingThreshold * MovingThreshold;

        float aShare = 0.5f;
        float bShare = 0.5f;

        if (aMoving && !bMoving)
        {
            aShare = 0.8f;
            bShare = 0.2f;
        }
        else if (!aMoving && bMoving)
        {
            aShare = 0.2f;
            bShare = 0.8f;
        }

        float aCorrection = Mathf.Min(penetration * aShare, MaxCorrectionPerUnit);
        float bCorrection = Mathf.Min(penetration * bShare, MaxCorrectionPerUnit);

        a.Motor.ApplyDepenetration(-normal * aCorrection);
        b.Motor.ApplyDepenetration(normal * bCorrection);
    }
}