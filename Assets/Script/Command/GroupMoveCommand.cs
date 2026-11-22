using System.Collections.Generic;
using UnityEngine;

public static class GroupMoveCommand
{
    private const int SharedNavigationMinGroupSize = 36;

    public static bool TryMoveGroup(GameContext gameContext, Faction issuingFaction,
        IReadOnlyList<UnitBase> units, Vector3 destination)
    {
        if (gameContext?.TerrainGrid == null ||
            issuingFaction == null || units == null || !float.IsFinite(destination.x) || !float.IsFinite(destination.z))
            return false;

        var members = new List<UnitBase>(units.Count);
        List<UnitBase> aircraft = null;
        var seen = new HashSet<UnitBase>();
        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit = units[i];
            if (!issuingFaction.CanIssueCommandsTo(unit) || !unit.IsInitialized || !unit.IsAlive || !seen.Add(unit))
                continue;

            if (unit.IsAirUnit)
                (aircraft ??= new List<UnitBase>()).Add(unit);
            else
                members.Add(unit);
        }

        bool issuedToAir = aircraft != null && IssueAirTravel(aircraft, destination, CommandType.Move);
        return TryMoveGroundGroup(gameContext, members, destination) || issuedToAir;
    }

    private static bool TryMoveGroundGroup(GameContext gameContext, List<UnitBase> members, Vector3 destination)
    {
        if (members.Count == 0 || gameContext.DestinationAllocationSystem == null)
            return false;

        if (members.Count == 1)
        {
            UnitBase unit = members[0];
            unit.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination,
                formationSlotIndex: 0, formationUnitCount: 1, formationMaxNavigationRadius: unit.NavigationRadius));
            return true;
        }

        float maxRadius = GetMaxNavigationRadius(members);
        int groupId = gameContext.AllocateMovementGroupId();
        PrepareMovementGroup(members, groupId);

        var formation = new FormationMovementGroup(groupId, members, destination, maxRadius,
            gameContext.TerrainGrid, gameContext.DestinationAllocationSystem.Formation);
        MovementGroup sharedMovement = null;
        if (members.Count >= SharedNavigationMinGroupSize)
        {
            sharedMovement = new MovementGroup(groupId, members, destination, maxRadius, formation,
                gameContext.PathfindingService, gameContext.TerrainGrid);
            sharedMovement.Navigator.Build();
        }

        bool issued = false;
        for (int i = 0; i < members.Count; i++)
        {
            UnitBase unit = members[i];
            int slot = formation.GetAssignedSlotIndex(unit);
            if (slot < 0)
                continue;

            unit.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination, slot, members.Count,
                maxRadius, groupId, formation, sharedMovement));
            issued = true;
        }

        return issued;
    }

    internal static bool IssueAirTravel(IReadOnlyList<UnitBase> aircraft, Vector3 destination, CommandType command)
    {
        if (aircraft.Count == 0 || !float.IsFinite(destination.x) || !float.IsFinite(destination.z))
            return false;

        float radius = GetMaxNavigationRadius(aircraft);
        float spacing = AirMovement.Spacing(radius, radius);
        int columns = Mathf.CeilToInt(Mathf.Sqrt(aircraft.Count));
        int rows = (aircraft.Count + columns - 1) / columns;
        for (int i = 0; i < aircraft.Count; i++)
        {
            int row = i / columns;
            int rowSize = Mathf.Min(columns, aircraft.Count - row * columns);
            Vector3 offset = new((i % columns - (rowSize - 1) * 0.5f) * spacing, 0f,
                (row - (rows - 1) * 0.5f) * spacing);
            UnitBase unit = aircraft[i];
            bool attackMove = command == CommandType.AttackMove && unit is CombatUnit combat && combat.CanAttack();
            unit.IssueCommand(attackMove ? CommandType.AttackMove : CommandType.Move,
                CommandContext.MoveTo(destination + offset));
        }

        return true;
    }

    internal static float GetMaxNavigationRadius(IReadOnlyList<UnitBase> units)
    {
        float radius = 0f;
        for (int i = 0; i < units.Count; i++)
            radius = Mathf.Max(radius, units[i].NavigationRadius);
        return radius;
    }

    internal static void PrepareMovementGroup(IReadOnlyList<UnitBase> units, int groupId)
    {
        for (int i = 0; i < units.Count; i++)
            units[i].PrepareMovementGroup(groupId);
    }
}
