using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a navigation solution owned by a GroupNavigator.
///
/// Different implementations may represent navigation differently
/// (shared path, flow field, etc.).
/// </summary>
public interface INavigationSolution
{
    bool IsValid { get; }

    Vector3 Destination { get; }

    IReadOnlyList<Vector3> DebugPath { get; }
}