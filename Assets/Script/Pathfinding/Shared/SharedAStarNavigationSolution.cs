using System.Collections.Generic;
using UnityEngine;

public sealed class SharedAStarNavigationSolution : INavigationSolution
{
    private const float DuplicatePointDistanceSqr = 0.0001f;

    private readonly float navigationRadius;
    private readonly List<Vector3> path = new List<Vector3>();
    private readonly SharedRouteSampler sampler;

    public bool IsValid => path.Count > 0 && sampler != null;
    public IReadOnlyList<Vector3> DebugPath => path;
    public float NavigationRadius => navigationRadius;  

    public Vector3 Destination { get; }

    public SharedAStarNavigationSolution(Vector3 startPosition, Vector3 destination, IReadOnlyList<Vector3> sourcePath)
    {
        Destination = destination;

        // A* reconstruction doesnt include start node
        // we had the representative start position
        AddPointIfDistinct(startPosition);

        if (sourcePath != null)
        {
            for (int i = 0; i < sourcePath.Count; i++)
            {
                AddPointIfDistinct(sourcePath[i]);
            }
        }

        if (path.Count > 0)
        {
            sampler = new SharedRouteSampler(path);
        }
    }

    public NavigationSample SampleDirection(Vector3 worldPosition, int previousRouteSegmentIndex = -1)
    {
        if (sampler == null)
        {
            return NavigationSample.Invalid;
        }

        return sampler.SampleDirection(worldPosition, previousRouteSegmentIndex);
    }

    private void AddPointIfDistinct(Vector3 point)
    {
        if (path.Count == 0)
        {
            path.Add(point);
            return;
        }

        Vector3 previous = path[path.Count - 1];
        previous.y = 0f;

        Vector3 current = point;
        current.y = 0f;

        if ((current - previous).sqrMagnitude <= DuplicatePointDistanceSqr)
        {
            return;
        }

        path.Add(point);
    }
}