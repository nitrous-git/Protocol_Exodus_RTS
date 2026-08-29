using System.Collections.Generic;
using UnityEngine;

public sealed class AttackPositionAllocator
{
    private readonly TerrainGrid terrainGrid;
    private readonly GridNavigationStateSystem navigationState;

    // ---------------------------------------------------------------------
    // Group combat deployment
    // ---------------------------------------------------------------------

    //
    // Every attacker receives a preferred radial depth
    // somewhere inside this firing band.
    //
    // Front units tend toward 70%.
    // Rear units tend toward 95%.
    //
    private const float PreferredMinRangeFraction = 0.50f;
    private const float MaximumRangeFraction = 0.95f;

    //
    // Do NOT search the entire attack ring.
    //
    // We calculate one topology-preferred polar position
    // and inspect only a small local neighborhood around it.
    //
    // Radius 4 => maximum 9 x 9 = 81 inspected cells / unit.
    //
    private const int LocalSearchRadiusCells = 4;

    //
    // Keep the unit close to its own intended radial layer.
    //
    // The cell-size minimum guarantees that grid discretization
    // still leaves some candidates.
    //
    private const float RadialWindowFraction = 0.06f;
    private const float MinimumRadialWindowCells = 0.75f;

    //
    // Candidate may deviate around the target if necessary,
    // but never intentionally cross to the opposite hemisphere.
    //
    // Dot >= 0 == <= 90 degrees from original approach sector.
    //
    private const float MinimumAlignment = 0f;

    //
    // Topology is more important than shaving off travel distance.
    //
    private const float AngularWeight = 4f;
    private const float RadialWeight = 4f;
    private const float TravelWeight = 0.5f;

    // ---------------------------------------------------------------------
    // Reused temporary buffers
    // ---------------------------------------------------------------------

    private readonly List<DeploymentEntry> deploymentBuffer =
        new List<DeploymentEntry>(64);

    private readonly List<ScoredCandidate> candidateBuffer =
        new List<ScoredCandidate>(128);

    private readonly struct DeploymentEntry
    {
        public readonly CombatUnit Unit;
        public readonly float Depth01;
        public readonly Vector3 ApproachDirection;

        public DeploymentEntry(
            CombatUnit unit,
            float depth01,
            Vector3 approachDirection)
        {
            Unit = unit;
            Depth01 = depth01;
            ApproachDirection = approachDirection;
        }
    }

    private readonly struct ScoredCandidate
    {
        public readonly GridCoord Cell;
        public readonly float Score;

        public ScoredCandidate(
            GridCoord cell,
            float score)
        {
            Cell = cell;
            Score = score;
        }
    }

    // ---------------------------------------------------------------------
    // Construction
    // ---------------------------------------------------------------------

    public AttackPositionAllocator(
        TerrainGrid terrainGrid,
        GridNavigationStateSystem navigationState)
    {
        this.terrainGrid = terrainGrid;
        this.navigationState = navigationState;
    }

    // =====================================================================
    // Individual allocation
    //
    // KEEP the original simple allocator for automatic / opportunistic
    // combat. Group player Attack commands use AllocateGroup() below.
    // =====================================================================

    public GridCoord? TryAllocate(
        CombatUnit unit,
        ITargetable target)
    {
        if (unit == null ||
            target == null ||
            terrainGrid == null ||
            navigationState == null)
        {
            return null;
        }

        float attackRange =
            unit.GetAttackRange();

        float cellSize =
            terrainGrid.CellSize;

        if (attackRange <= 0f ||
            cellSize <= 0f)
        {
            return null;
        }

        ResolveTargetFootprint(
            target,
            out GridCoord targetCell,
            out Vector2Int targetFootprint);

        int maxDepth =
            Mathf.Max(
                1,
                Mathf.CeilToInt(
                    attackRange /
                    cellSize));

        float desiredDistance =
            attackRange * 0.80f;

        float maximumAllowedDistance =
            attackRange * 0.95f;

        List<GridCoord> candidates =
            new List<GridCoord>();

        for (int depth = 1;
             depth <= maxDepth;
             depth++)
        {
            List<GridCoord> ring =
                PlacementUtil
                    .GetAllFreePlacementsOnRing(
                        terrainGrid,
                        targetCell,
                        targetFootprint,
                        depth,
                        cell =>
                        {
                            Vector3 worldPosition =
                                terrainGrid.CellToWorld(
                                    cell);

                            float distanceToTarget =
                                FlatDistance(
                                    worldPosition,
                                    target.Position);

                            return distanceToTarget <=
                                   maximumAllowedDistance;
                        });

            candidates.AddRange(ring);
        }

        while (candidates.Count > 0)
        {
            int bestIndex = -1;

            float bestScore =
                float.MaxValue;

            for (int i = 0;
                 i < candidates.Count;
                 i++)
            {
                Vector3 candidatePosition =
                    terrainGrid.CellToWorld(
                        candidates[i]);

                float distanceToTarget =
                    FlatDistance(
                        candidatePosition,
                        target.Position);

                float travelDistance =
                    FlatDistance(
                        unit.Position,
                        candidatePosition);

                float rangeError =
                    Mathf.Abs(
                        desiredDistance -
                        distanceToTarget);

                float score =
                    rangeError * 4f +
                    travelDistance;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestIndex = i;
            }

            if (bestIndex < 0)
                break;

            GridCoord candidate =
                candidates[bestIndex];

            candidates.RemoveAt(
                bestIndex);

            if (navigationState
                .TryReserveDestination(
                    candidate,
                    unit))
            {
                return candidate;
            }
        }

        return null;
    }

    // =====================================================================
    // Explicit player Attack ¡ª one-shot group deployment
    // =====================================================================

    public void AllocateGroup(
        IReadOnlyList<CombatUnit> units,
        ITargetable target,
        Dictionary<CombatUnit, GridCoord> result)
    {
        result.Clear();
        deploymentBuffer.Clear();

        if (units == null ||
            units.Count == 0 ||
            target == null ||
            terrainGrid == null ||
            navigationState == null)
        {
            return;
        }

        float minDistance =
            float.PositiveInfinity;

        float maxDistance =
            float.NegativeInfinity;

        // -------------------------------------------------------------
        // STEP A
        //
        // Snapshot the radial topology of ALL attackers.
        //
        // Important:
        // units already inside attack range still participate in this
        // measurement. Otherwise rear units could suddenly become the
        // new "front" simply because the actual front row was already
        // in firing range.
        // -------------------------------------------------------------

        for (int i = 0;
             i < units.Count;
             i++)
        {
            CombatUnit unit =
                units[i];

            if (unit == null ||
                !unit.CanAttack())
            {
                continue;
            }

            float distance =
                FlatDistance(
                    unit.Position,
                    target.Position);

            minDistance =
                Mathf.Min(
                    minDistance,
                    distance);

            maxDistance =
                Mathf.Max(
                    maxDistance,
                    distance);
        }

        if (float.IsInfinity(
                minDistance))
        {
            return;
        }

        // -------------------------------------------------------------
        // STEP B
        //
        // Build topology entries only for units that actually
        // need to approach.
        // -------------------------------------------------------------

        for (int i = 0;
             i < units.Count;
             i++)
        {
            CombatUnit unit =
                units[i];

            if (unit == null ||
                !unit.CanAttack())
            {
                continue;
            }

            //
            // Already has firing access.
            // It is effectively already deployed.
            //
            if (unit.IsWithinAttackRange(
                    target))
            {
                continue;
            }

            float distance =
                FlatDistance(
                    unit.Position,
                    target.Position);

            float depth01;

            if (maxDistance -
                minDistance >
                0.001f)
            {
                depth01 =
                    Mathf.InverseLerp(
                        minDistance,
                        maxDistance,
                        distance);
            }
            else
            {
                depth01 = 0.5f;
            }

            Vector3 approachDirection =
                unit.Position -
                target.Position;

            approachDirection.y = 0f;

            if (approachDirection
                    .sqrMagnitude <=
                0.0001f)
            {
                continue;
            }

            approachDirection.Normalize();

            deploymentBuffer.Add(
                new DeploymentEntry(
                    unit,
                    depth01,
                    approachDirection));
        }

        // -------------------------------------------------------------
        // STEP C
        //
        // Larger units get first choice because they have fewer
        // physically valid cells.
        //
        // Same-sized units are handled front -> rear.
        //
        // Final UnitId tie break keeps this deterministic.
        // -------------------------------------------------------------

        deploymentBuffer.Sort(
            (first, second) =>
            {
                int radiusComparison =
                    second.Unit
                        .NavigationRadius
                        .CompareTo(
                            first.Unit
                                .NavigationRadius);

                if (radiusComparison != 0)
                    return radiusComparison;

                int depthComparison =
                    first.Depth01.CompareTo(
                        second.Depth01);

                if (depthComparison != 0)
                    return depthComparison;

                return first.Unit.UnitId
                    .CompareTo(
                        second.Unit.UnitId);
            });

        // -------------------------------------------------------------
        // STEP D
        //
        // One local topology allocation per unit.
        //
        // No retry later.
        // No polling.
        // No final deployment pass.
        // -------------------------------------------------------------

        for (int i = 0;
             i < deploymentBuffer.Count;
             i++)
        {
            DeploymentEntry entry =
                deploymentBuffer[i];

            if (!TryAllocateNearTopology(
                    entry,
                    target,
                    out GridCoord cell))
            {
                //
                // No usable position.
                //
                // This unit simply receives no deployment cell and
                // stays where it is.
                //
                continue;
            }

            result[entry.Unit] =
                cell;
        }
    }

    // =====================================================================
    // Local topology search
    // =====================================================================

    private bool TryAllocateNearTopology(
        DeploymentEntry entry,
        ITargetable target,
        out GridCoord result)
    {
        result = default;

        CombatUnit unit =
            entry.Unit;

        float attackRange =
            unit.GetAttackRange();

        float cellSize =
            terrainGrid.CellSize;

        if (attackRange <= 0f ||
            cellSize <= 0f)
        {
            return false;
        }

        // -------------------------------------------------------------
        // Convert original radial depth into this unit's own
        // weapon-range band.
        //
        // depth = 0:
        //     front of group
        //     approximately 70% weapon range
        //
        // depth = 1:
        //     rear of group
        //     approximately 95% weapon range
        // -------------------------------------------------------------

        float preferredFraction =
            Mathf.Lerp(
                PreferredMinRangeFraction,
                MaximumRangeFraction,
                entry.Depth01);

        float preferredDistance =
            attackRange *
            preferredFraction;

        Vector3 preferredWorldPosition =
            target.Position +
            entry.ApproachDirection *
            preferredDistance;

        GridCoord preferredCell =
            terrainGrid.WorldToCell(
                preferredWorldPosition);

        // -------------------------------------------------------------
        // Keep the search close to this specific radial layer.
        // -------------------------------------------------------------

        float radialWindow =
            Mathf.Max(
                attackRange *
                    RadialWindowFraction,

                cellSize *
                    MinimumRadialWindowCells);

        float minimumDistance =
            Mathf.Max(
                attackRange *
                    PreferredMinRangeFraction,

                preferredDistance -
                    radialWindow);

        float maximumDistance =
            Mathf.Min(
                attackRange *
                    MaximumRangeFraction,

                preferredDistance +
                    radialWindow);

        candidateBuffer.Clear();

        // -------------------------------------------------------------
        // BOUNDED SEARCH
        //
        // Radius 4 means at most 81 cells are inspected.
        // -------------------------------------------------------------

        for (int z =
                 -LocalSearchRadiusCells;
             z <=
                 LocalSearchRadiusCells;
             z++)
        {
            for (int x =
                     -LocalSearchRadiusCells;
                 x <=
                     LocalSearchRadiusCells;
                 x++)
            {
                GridCoord cell =
                    new GridCoord(
                        preferredCell.x + x,
                        preferredCell.z + z);

                if (!terrainGrid
                    .IsInside(cell))
                {
                    continue;
                }

                //
                // Static radius-aware clearance.
                //
                // Dynamic unit occupancy and destination reservations
                // remain the responsibility of TryReserveDestination.
                //
                if (!terrainGrid
                    .HasNavigationClearance(
                        cell,
                        unit.NavigationRadius))
                {
                    continue;
                }

                Vector3 candidatePosition =
                    terrainGrid.CellToWorld(
                        cell);

                float distanceToTarget =
                    FlatDistance(
                        candidatePosition,
                        target.Position);

                // -----------------------------------------------------
                // Radial topology hard window.
                // -----------------------------------------------------

                if (distanceToTarget <
                        minimumDistance ||
                    distanceToTarget >
                        maximumDistance)
                {
                    continue;
                }

                Vector3 candidateDirection =
                    candidatePosition -
                    target.Position;

                candidateDirection.y = 0f;

                if (candidateDirection
                        .sqrMagnitude <=
                    0.0001f)
                {
                    continue;
                }

                candidateDirection.Normalize();

                float alignment =
                    Vector3.Dot(
                        entry.ApproachDirection,
                        candidateDirection);

                //
                // Never deliberately move to the opposite side
                // of the target.
                //
                if (alignment <
                    MinimumAlignment)
                {
                    continue;
                }

                // -----------------------------------------------------
                // Score
                // -----------------------------------------------------

                float angularCost =
                    1f - alignment;

                float radialCost =
                    Mathf.Abs(
                        distanceToTarget -
                        preferredDistance)
                    /
                    Mathf.Max(
                        radialWindow,
                        cellSize *
                        0.25f);

                float travelCost =
                    FlatDistance(
                        unit.Position,
                        candidatePosition)
                    /
                    Mathf.Max(
                        attackRange,
                        cellSize);

                float score =
                    angularCost *
                        AngularWeight
                    +
                    radialCost *
                        RadialWeight
                    +
                    travelCost *
                        TravelWeight;

                candidateBuffer.Add(
                    new ScoredCandidate(
                        cell,
                        score));
            }
        }

        // -------------------------------------------------------------
        // Deterministic best-first order.
        // -------------------------------------------------------------

        candidateBuffer.Sort(
            (first, second) =>
            {
                int scoreComparison =
                    first.Score.CompareTo(
                        second.Score);

                if (scoreComparison != 0)
                    return scoreComparison;

                if (first.Cell.z !=
                    second.Cell.z)
                {
                    return first.Cell.z
                        .CompareTo(
                            second.Cell.z);
                }

                return first.Cell.x
                    .CompareTo(
                        second.Cell.x);
            });

        // -------------------------------------------------------------
        // Final authority:
        //
        // dynamic occupancy
        // other destination reservations
        // requester navigation radius
        //
        // We simply take the first candidate that successfully
        // reserves.
        // -------------------------------------------------------------

        for (int i = 0;
             i < candidateBuffer.Count;
             i++)
        {
            GridCoord candidate =
                candidateBuffer[i].Cell;

            if (!navigationState
                .TryReserveDestination(
                    candidate,
                    unit))
            {
                continue;
            }

            result = candidate;
            return true;
        }

        return false;
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private void ResolveTargetFootprint(
        ITargetable target,
        out GridCoord targetCell,
        out Vector2Int targetFootprint)
    {
        if (target is BuildingBase building &&
            building.Definition != null)
        {
            targetCell =
                building.FootprintOrigin;

            targetFootprint =
                building.Definition
                    .footprintSize;

            return;
        }

        targetCell =
            terrainGrid.WorldToCell(
                target.Position);

        targetFootprint =
            Vector2Int.one;
    }

    private static float FlatDistance(
        Vector3 first,
        Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;

        return Vector3.Distance(
            first,
            second);
    }
}