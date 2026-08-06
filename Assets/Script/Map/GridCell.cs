using UnityEngine;

public sealed class GridCell
{
    public GridCoord Coord;
    public Vector3 WorldCenter;

    public float Height;
    public float Slope;

    public bool Walkable;
    public bool Buildable;

    public bool Occupied;
    public bool Reserved;

    public int OccupyingUnitId = -1;
    public int OccupyingBuildingId = -1;
    public int OccupyingResourceNodeId = -1;

    public bool IsFreeForMovement()
    {
        return Walkable && !Occupied && !Reserved;
    }

    public bool IsFreeForBuilding()
    {
        return Buildable && !Occupied && !Reserved;
    }
}