using UnityEngine;

public struct GridCell
{
        public GridCoord Coord;
        public Vector3 WorldCenter;

        public float Height;
        public float Slope;

        public bool Walkable;
        public bool Buildable;

        public bool Occupied;
        public bool Reserved;

        public int OccupyingUnitId;
        public int OccupyingBuildingId;

        public bool IsFreeForMovement()
        {
            return Walkable &&
                   !Occupied &&
                   !Reserved;
        }

        public bool IsFreeForBuilding()
        {
            return Buildable &&
                   !Occupied &&
                   !Reserved;
        }
}