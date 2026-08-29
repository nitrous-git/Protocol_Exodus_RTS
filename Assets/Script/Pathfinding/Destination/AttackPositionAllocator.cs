using System.Collections.Generic;
using UnityEngine;

public sealed class AttackPositionAllocator
{
    private readonly TerrainGrid terrainGrid;
    private readonly GridNavigationStateSystem navigationState;

    private const float PreferredMinRangeFraction = 0.70f;
    private const float MaximumRangeFraction = 0.95f;

    private const float MaximumApproachAngle = 90f;

    private const float AngularWeight = 4f;
    private const float RadialDepthWeight = 2f;
    private const float TravelWeight = 1f;
    private const float TooCloseWeight = 4f;

    private readonly struct AttackCandidate
    {
        public readonly GridCoord Cell;
        public readonly float Score;

        public AttackCandidate(
            GridCoord cell,
            float score)
        {
            Cell = cell;
            Score = score;
        }
    }

    private sealed class AttackRequest
    {
        public CombatUnit Unit;
        public readonly List<AttackCandidate>
            Candidates =
                new List<AttackCandidate>();
    }

    public AttackPositionAllocator(TerrainGrid terrainGrid, GridNavigationStateSystem navigationState)
    {
        this.terrainGrid = terrainGrid;
        this.navigationState = navigationState;
    }

    public GridCoord? TryAllocate(CombatUnit unit, ITargetable target)
    {
        if (unit == null ||
            target == null ||
            terrainGrid == null ||
            navigationState == null)
        {
            return null;
        }

        float attackRange = unit.GetAttackRange();
        float cellSize = terrainGrid.CellSize;

        if (attackRange <= 0f || cellSize <= 0f)
            return null;

        GridCoord targetCell;
        Vector2Int targetFootprint;

        if (target is BuildingBase building && building.Definition != null)
        {
            targetCell = building.FootprintOrigin;
            targetFootprint = building.Definition.footprintSize;
        }
        else
        {
            targetCell = terrainGrid.WorldToCell(target.Position);
            targetFootprint = Vector2Int.one;
        }

        // Search every meaningful ring that can still
        // contain a valid firing position.
        int maxDepth = Mathf.Max(1, Mathf.CeilToInt(attackRange / cellSize));

        // Prefer standing somewhat inside maximum weapon range.
        // This gives us a small safety margin instead of
        // positioning exactly on the weapon-range boundary.
        float desiredDistance = attackRange * 0.80f;
        float maximumAllowedDistance = attackRange * 0.95f;

        List<GridCoord> candidates = new List<GridCoord>();

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            List<GridCoord> ring =
                PlacementUtil.GetAllFreePlacementsOnRing(
                    terrainGrid,
                    targetCell,
                    targetFootprint,
                    depth,
                    cell =>
                    {
                        Vector3 worldPosition = terrainGrid.CellToWorld(cell);
                        float distanceToTarget = FlatDistance(worldPosition, target.Position);

                        return distanceToTarget <= maximumAllowedDistance;
                    });

            candidates.AddRange(ring);
        }

        // Try candidates from best to worst.
        //
        // Reservation can still fail because another unit
        // may physically occupy the cell, so if that happens
        // we simply try the next candidate.
        while (candidates.Count > 0)
        {
            int bestIndex = -1;
            float bestScore = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 candidatePosition = terrainGrid.CellToWorld(candidates[i]);

                float distanceToTarget = FlatDistance(candidatePosition, target.Position);
                float travelDistance = FlatDistance(unit.Position, candidatePosition);

                float rangeError = Mathf.Abs(desiredDistance - distanceToTarget);

                // Range placement matters more than
                // simply choosing the nearest cell.
                float score = rangeError * 4f + travelDistance;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
                break;

            GridCoord candidate = candidates[bestIndex];

            candidates.RemoveAt(bestIndex);

            if (navigationState.TryReserveDestination(candidate, unit))
            {
                return candidate;
            }
        }

        return null;
    }

    public void AllocateGroup(
        IReadOnlyList<CombatUnit> units,
        ITargetable target,
        Dictionary<CombatUnit, GridCoord> result)
    {
        result.Clear();

        if (units == null ||
            target == null ||
            terrainGrid == null ||
            navigationState == null)
        {
            return;
        }

        // ---------------------------------------------------------
        // Snapshot current radial topology.
        // ---------------------------------------------------------

        float minDistance =
            float.PositiveInfinity;

        float maxDistance =
            float.NegativeInfinity;

        for (int i = 0; i < units.Count; i++)
        {
            CombatUnit unit = units[i];

            if (unit == null)
                continue;

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

        if (float.IsInfinity(minDistance))
            return;

        List<AttackRequest> requests =
            new List<AttackRequest>();

        // ---------------------------------------------------------
        // Build each unit's candidate set.
        // ---------------------------------------------------------

        for (int i = 0; i < units.Count; i++)
        {
            CombatUnit unit = units[i];

            if (unit == null ||
                !unit.CanAttack())
            {
                continue;
            }

            // Already tactically useful.
            // Do not redeploy it.
            if (unit.IsWithinAttackRange(target))
                continue;

            float currentDistance =
                FlatDistance(
                    unit.Position,
                    target.Position);

            float radialDepth01;

            if (maxDistance - minDistance <=
                0.001f)
            {
                radialDepth01 = 0.5f;
            }
            else
            {
                radialDepth01 =
                    Mathf.InverseLerp(
                        minDistance,
                        maxDistance,
                        currentDistance);
            }

            AttackRequest request =
                new AttackRequest
                {
                    Unit = unit
                };

            BuildGroupCandidates(
                unit,
                target,
                radialDepth01,
                request.Candidates);

            requests.Add(request);
        }

        // ---------------------------------------------------------
        // Most-constrained unit first.
        //
        // Important for mixed navigation radii.
        // ---------------------------------------------------------

        requests.Sort(
            (first, second) =>
            {
                int countComparison =
                    first.Candidates.Count.CompareTo(
                        second.Candidates.Count);

                if (countComparison != 0)
                    return countComparison;

                return first.Unit.UnitId.CompareTo(
                    second.Unit.UnitId);
            });

        // ---------------------------------------------------------
        // One-shot claims.
        // ---------------------------------------------------------

        for (int i = 0; i < requests.Count; i++)
        {
            AttackRequest request =
                requests[i];

            for (int candidateIndex = 0;
                 candidateIndex <
                 request.Candidates.Count;
                 candidateIndex++)
            {
                GridCoord cell =
                    request
                        .Candidates[
                            candidateIndex]
                        .Cell;

                //
                // Final authority:
                // radius, occupancy and existing reservations.
                //
                if (!navigationState
                        .TryReserveDestination(
                            cell,
                            request.Unit))
                {
                    continue;
                }

                result.Add(
                    request.Unit,
                    cell);

                break;
            }
        }
    }

    private void BuildGroupCandidates(
    CombatUnit unit,
    ITargetable target,
    float radialDepth01,
    List<AttackCandidate> result)
    {
        result.Clear();

        float attackRange =
            unit.GetAttackRange();

        float cellSize =
            terrainGrid.CellSize;

        if (attackRange <= 0f ||
            cellSize <= 0f)
        {
            return;
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

        float maximumDistance =
            attackRange *
            MaximumRangeFraction;

        //
        // Front members prefer the inner part.
        // Rear members prefer the outer part.
        //
        float preferredRangeFraction =
            Mathf.Lerp(
                PreferredMinRangeFraction,
                MaximumRangeFraction,
                radialDepth01);

        Vector3 approachDirection =
            unit.Position -
            target.Position;

        approachDirection.y = 0f;

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
                                terrainGrid
                                    .CellToWorld(cell);

                            float distance =
                                FlatDistance(
                                    worldPosition,
                                    target.Position);

                            if (distance >
                                maximumDistance)
                            {
                                return false;
                            }

                            //
                            // Cheap radius-aware static filtering.
                            //
                            return terrainGrid
                                .HasNavigationClearance(
                                    cell,
                                    unit.NavigationRadius);
                        });

            for (int i = 0;
                 i < ring.Count;
                 i++)
            {
                GridCoord cell =
                    ring[i];

                Vector3 candidatePosition =
                    terrainGrid.CellToWorld(
                        cell);

                Vector3 candidateDirection =
                    candidatePosition -
                    target.Position;

                candidateDirection.y = 0f;

                float angularDifference = 0f;

                if (approachDirection.sqrMagnitude >
                        0.0001f &&
                    candidateDirection.sqrMagnitude >
                        0.0001f)
                {
                    angularDifference =
                        Vector3.Angle(
                            approachDirection,
                            candidateDirection);
                }

                //
                // HARD rule:
                // never intentionally cross to the
                // opposite side of the enemy.
                //
                if (angularDifference >
                    MaximumApproachAngle)
                {
                    continue;
                }

                float distanceToTarget =
                    FlatDistance(
                        candidatePosition,
                        target.Position);

                float rangeFraction =
                    distanceToTarget /
                    attackRange;

                float angularCost =
                    angularDifference /
                    MaximumApproachAngle;

                //
                // Weak front/back topology preference.
                //
                float radialDepthCost =
                    Mathf.Abs(
                        rangeFraction -
                        preferredRangeFraction)
                    /
                    (MaximumRangeFraction -
                     PreferredMinRangeFraction);

                //
                // 70-95% is the GOOD band.
                //
                // Cells closer than 70% remain legal,
                // but are less attractive.
                //
                float tooCloseCost = 0f;

                if (rangeFraction <
                    PreferredMinRangeFraction)
                {
                    tooCloseCost =
                        (PreferredMinRangeFraction -
                         rangeFraction)
                        /
                        PreferredMinRangeFraction;
                }

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
                    radialDepthCost *
                        RadialDepthWeight
                    +
                    travelCost *
                        TravelWeight
                    +
                    tooCloseCost *
                        TooCloseWeight;

                result.Add(
                    new AttackCandidate(
                        cell,
                        score));
            }
        }

        //
        // Deterministic candidate order.
        //
        result.Sort(
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
                    return first.Cell.z.CompareTo(
                        second.Cell.z);
                }

                return first.Cell.x.CompareTo(
                    second.Cell.x);
            });
    }

    // ---------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------

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

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }
}