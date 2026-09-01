using System.Collections.Generic;
using UnityEngine;

public sealed class SharedAStarNavigationSolution : INavigationSolution
{
    private readonly List<Vector3> path = new List<Vector3>();

    public bool IsValid => path.Count > 0;

    public Vector3 Destination { get; }

    public IReadOnlyList<Vector3> DebugPath => path;

    public SharedAStarNavigationSolution(Vector3 destination, IReadOnlyList<Vector3> sourcePath)
    {
        Destination = destination;

        if (sourcePath == null)
            return;

        for (int i = 0; i < sourcePath.Count; i++)
        {
            path.Add(sourcePath[i]);
        }
    }

}