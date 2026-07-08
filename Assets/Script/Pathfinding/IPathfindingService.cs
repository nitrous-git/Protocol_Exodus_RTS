using System.Collections.Generic;
using UnityEngine;

public interface IPathfindingService
{
    bool TryFindPath(Vector3 start, Vector3 end, List<Vector3> result);
}
