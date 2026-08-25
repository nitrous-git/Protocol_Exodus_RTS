using System.Collections.Generic;
using UnityEngine;

public sealed class AttackPositionAllocator
{
    private readonly TerrainGrid terrainGrid;
    private readonly GridNavigationStateSystem navigationState;

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

    private static float FlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;
        return Vector3.Distance(first, second);
    }
}