using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents one command-level group movement operation.
///
/// Owns the units participating in the movement and the shared
/// navigation-related data associated with that command.
///
/// Navigation behavior itself will be added through GroupNavigator
/// in a later pass.
/// </summary>
public sealed class MovementGroup
{
    private readonly List<UnitBase> members = new List<UnitBase>();
    private int lastCenterFrame = -1;
    private Vector3 currentCenter;

    public IReadOnlyList<UnitBase> Members => members;
    public int UnitCount => members.Count;

    public int Id { get; }
    public Vector3 Destination { get; }
    public float MaxNavigationRadius { get; }
    public FormationMovementGroup Formation { get; }
    public GroupNavigator Navigator { get; }

    public MovementGroup(
        int id,
        IReadOnlyList<UnitBase> units,
        Vector3 destination,
        float maxNavigationRadius,
        FormationMovementGroup formation,
        IPathfindingService pathfindingService)
    {
        Id = id;
        Destination = destination;
        MaxNavigationRadius = maxNavigationRadius;
        Formation = formation;

        if (units == null)
        {
            return;
        }

        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit = units[i];

            if (unit == null)
            {
                continue;
            }

            members.Add(unit);
        }

        Navigator = new GroupNavigator(this, pathfindingService);
    }

    public Vector3 GetCurrentCenter()
    {
        if (lastCenterFrame == Time.frameCount)
        {
            return currentCenter;
        }

        lastCenterFrame = Time.frameCount;

        Vector3 sum = Vector3.zero;

        int count = 0;

        for (int i = 0; i < members.Count; i++)
        {
            UnitBase unit =
                members[i];

            if (unit == null || !unit.IsAlive)
            {
                continue;
            }

            if (unit.MovementGroupId != Id)
            {
                continue;
            }

            sum += unit.Position;
            count++;
        }

        currentCenter = count > 0 ? sum / count : Destination;

        return currentCenter;
    }
}