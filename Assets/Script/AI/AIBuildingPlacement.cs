using System.Collections.Generic;
using UnityEngine;

internal sealed class AIBuildingPlacement
{
    private readonly BuildingManager buildings;
    private readonly GameContext context;
    private readonly List<Rect> existingExits = new();
    private readonly Vector2Int[] footprints = new Vector2Int[4];
    private readonly (Vector3 Entrance, Vector3 End, float Radius)[] exits = new (Vector3, Vector3, float)[4];

    public AIBuildingPlacement(BuildingManager buildings, GameContext context)
    {
        this.buildings = buildings;
        this.context = context;
    }

    public bool TryFind(BuildingDefinition definition, Vector3 anchor, int radius, int spacing,
        out GridCoord origin, out int rotation)
    {
        TerrainGrid grid = buildings.TerrainGrid;
        origin = default;
        rotation = 0;
        GridCoord center = grid.WorldToCell(anchor);
        if (!grid.IsInside(center))
            return false;

        CollectExistingExits(grid);
        var producer = definition.Prefab.GetComponent<UnitProductionComponent>();
        float spawnRadius = producer != null ? producer.GetPlacementSpawnRadius(buildings.OwnerFaction.Definition) : 0f;
        int rotations = spawnRadius > 0f ? 4 : definition.FootprintSize.x == definition.FootprintSize.y ? 1 : 2;
        for (int i = 0; i < rotations; i++)
        {
            footprints[i] = definition.GetRotatedFootprintSize(i);
            if (spawnRadius > 0f)
                exits[i] = producer.GetPlacementExit(grid, BuildingManager.GetPlacementRotation(i), footprints[i], spawnRadius);
        }

        int radiusSquared = radius * radius;
        for (int ring = 0; ring <= radius; ring++)
        {
            for (int z = -ring; z <= ring; z++)
            {
                int step = Mathf.Abs(z) == ring ? 1 : Mathf.Max(1, 2 * ring);
                for (int x = -ring; x <= ring; x += step)
                {
                    if (x * x + z * z > radiusSquared)
                        continue;

                    for (int i = 0; i < rotations; i++)
                    {
                        Vector2Int size = footprints[i];
                        GridCoord candidate = new(center.x + x - size.x / 2, center.z + z - size.y / 2);
                        if (!buildings.CanConstruct(definition, candidate, i) ||
                            !HasSpacing(grid, candidate, size, spacing))
                            continue;

                        Vector3 worldCenter = grid.GetFootprintWorldCenter(candidate, size);
                        Vector2 halfSize = new Vector2(size.x, size.y) * (grid.CellSize * 0.5f);
                        Vector2 min = new Vector2(worldCenter.x, worldCenter.z) - halfSize;
                        Rect footprint = new(min, halfSize * 2f);
                        if (OverlapsExistingExit(footprint) ||
                            (spawnRadius > 0f && !HasOpenExit(grid, footprint, worldCenter, exits[i], spawnRadius)))
                            continue;

                        origin = candidate;
                        rotation = i;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void CollectExistingExits(TerrainGrid grid)
    {
        existingExits.Clear();
        foreach (BuildingBase building in context.AllBuildings)
        {
            if (building == null || !building.IsAlive || building.UnitProduction == null ||
                building.UnitProduction.ProducibleUnits.Count == 0)
                continue;

            var exit = building.UnitProduction.GetPlacementExit(grid);
            existingExits.Add(Rect.MinMaxRect(
                Mathf.Min(exit.Entrance.x, exit.End.x) - exit.Radius,
                Mathf.Min(exit.Entrance.z, exit.End.z) - exit.Radius,
                Mathf.Max(exit.Entrance.x, exit.End.x) + exit.Radius,
                Mathf.Max(exit.Entrance.z, exit.End.z) + exit.Radius));
        }
    }

    private bool OverlapsExistingExit(Rect footprint)
    {
        foreach (Rect exit in existingExits)
        {
            if (exit.Overlaps(footprint))
                return true;
        }

        return false;
    }

    private bool HasSpacing(TerrainGrid grid, GridCoord origin, Vector2Int size, int spacing)
    {
        // Leave a ring for workers to approach nodes and move between structures.
        int resourceSpacing = Mathf.Max(2, spacing);
        var navigation = context.GridNavigationStateSystem;
        for (int z = -resourceSpacing; z < size.y + resourceSpacing; z++)
        {
            for (int x = -resourceSpacing; x < size.x + resourceSpacing; x++)
            {
                GridCoord coord = new(origin.x + x, origin.z + z);
                GridCell cell = grid.GetCell(coord);
                if (cell == null)
                    continue;

                if (cell.OccupyingResourceNodeId >= 0)
                    return false;

                if (x >= -spacing && x < size.x + spacing && z >= -spacing && z < size.y + spacing &&
                    cell.OccupyingBuildingId >= 0)
                    return false;

                if (x >= 0 && x < size.x && z >= 0 && z < size.y &&
                    (navigation.HasUnitOccupancy(coord) || navigation.IsDestinationReserved(coord)))
                    return false;
            }
        }

        return true;
    }

    private bool HasOpenExit(TerrainGrid grid, Rect footprint, Vector3 center,
        (Vector3 Entrance, Vector3 End, float Radius) exit, float spawnRadius)
    {
        Vector3 entrance = center + exit.Entrance;
        Vector3 direction = exit.End - exit.Entrance;
        Vector3 forward = direction.normalized;
        float step = grid.CellSize * 0.25f;
        float correctionLimit = spawnRadius + grid.CellSize;
        float length = direction.magnitude;
        bool canSpawn = false;
        for (float depth = 0f; depth < length + step; depth += step)
        {
            Vector3 probe = entrance + forward * Mathf.Min(depth, length);
            if (!grid.IsWalkable(grid.WorldToCell(probe)) || !grid.HasNavigationClearanceAt(probe, exit.Radius))
                return false;

            if (depth <= correctionLimit && !context.GridNavigationStateSystem.IsUnitSpaceClear(probe, spawnRadius))
                return false;

            float dx = Mathf.Max(0f, footprint.xMin - probe.x, probe.x - footprint.xMax);
            float dz = Mathf.Max(0f, footprint.yMin - probe.z, probe.z - footprint.yMax);
            if (dx * dx + dz * dz >= spawnRadius * spawnRadius)
                canSpawn = true;

            if (!canSpawn && depth >= correctionLimit)
                return false;
        }

        return canSpawn;
    }
}
