using System.Collections.Generic;
using UnityEngine;

public interface IPathfindingService
{
    void Initialize(TerrainGrid terrainGrid);

    bool TryFindPath(UnitBase requester, Vector3 start, Vector3 end, List<Vector3> result);
}
