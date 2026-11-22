using System.Collections.Generic;
using UnityEngine;
using static AIMissionDirector;

public enum AISquadMissionState
{
    Form,
    Assemble,
    Advance,
    Engage,
    Evaluate,
    Defend,
    Withdraw,
    Completed,
    Withdrawn,
    Failed,
    Cancelled
}

internal sealed class AISquadMission
{
    private const float AssemblyGrace = 1.5f;
    private const float ContactRange = 10f;
    private const float TravelStallTimeout = 7f;
    private const float EngagementStallTimeout = 40f;
    private const float RallyHoldRadiusInCells = 6f;
    private static readonly Vector3[] RallyOffsets =
    {
        Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        new(1f, 0f, 1f), new(-1f, 0f, 1f), new(1f, 0f, -1f), new(-1f, 0f, -1f)
    };

    private sealed class Member
    {
        public readonly CombatUnit Unit;
        // Manual orders can supersede either phase while the unit is still in this list.
        public int CommandRevision;
        public int MissionRevision;
        public MoveState TravelOrder;
        public bool HasTacticalAssignment;
        public Vector3 LastPosition;
        public int GuardSlot;

        public Member(CombatUnit unit)
        {
            Unit = unit;
            CommandRevision = unit.CommandRevision;
            MissionRevision = unit.CombatAI.MissionRevision;
        }

        public bool IsTraveling => Unit.IsExecutingTacticalOrder(TravelOrder) && !Unit.Motor.HasArrived;

        public void StopOwnedOrders()
        {
            CombatUnitAI ai = Unit != null ? Unit.CombatAI : null;
            if (ai != null && Unit.IsAlive && !ai.IsRetreatingOrRecovering)
            {
                if (HasTacticalAssignment && ai.MissionRevision == MissionRevision && ai.IsMissionActive)
                    ai.ClearMission();
                if (Unit.IsExecutingTacticalOrder(TravelOrder))
                    Unit.EnterCombatIdle();
            }

            HasTacticalAssignment = false;
            TravelOrder = null;
        }
    }

    private readonly AIMissionDirector director;
    private Vector3 anchor;
    private readonly List<Member> members;
    private readonly int initialCount;
    private readonly float readiness;
    private readonly float assemblyTimeout;
    private readonly float withdrawalThreshold;
    private readonly List<UnitBase> movers = new();
    private readonly HashSet<ITargetable> rejectedObjectives = new();

    private ITargetable objective;
    private ITargetable resumeObjective;
    private Vector3 stagingPosition;
    private Vector3 travelDestination;
    private Vector3 approachTargetPosition;
    private Vector3 pursuitOrigin;
    private Vector3 resumePursuitOrigin;
    private float guardRadius;
    private float guardSpacing;
    private int guardSlots;
    private Vector3? guardRally;
    private float arrivalRadius;
    private float stateDeadline;
    private float lastProgressAt;
    private float readySince = float.PositiveInfinity;
    private float objectiveHealth;
    private bool retriedAdvance;
    private float operationDeadline = float.PositiveInfinity;
    private bool plannedReturn;

    private TerrainGrid Grid => director.Context.TerrainGrid;
    private FormationDestinationAllocator Formation => director.Context.DestinationAllocationSystem.Formation;
    private int RequiredReady => Mathf.CeilToInt(members.Count * readiness);
    public int MemberCount => members.Count;
    public int Casualties { get; private set; }
    public AISquadMissionState State { get; private set; } = AISquadMissionState.Form;
    public bool IsFinished => State is AISquadMissionState.Completed or AISquadMissionState.Withdrawn or
        AISquadMissionState.Failed or AISquadMissionState.Cancelled;

    public AISquadMission(AIMissionDirector director, Vector3 anchor, IReadOnlyList<CombatUnit> recruits,
        int start, int count)
    {
        this.director = director;
        this.anchor = anchor;
        readiness = Mathf.Clamp(director.Settings.assemblyReadiness, 0.5f, 1f);
        assemblyTimeout = Mathf.Max(1f, director.Settings.assemblyTimeout);
        withdrawalThreshold = Mathf.Clamp(director.Settings.withdrawalThreshold, 0.1f, 1f);
        members = new List<Member>(count);
        for (int i = start; i < start + count; i++)
        {
            CombatUnit unit = recruits[i];
            if (director.Reserve(unit))
                members.Add(new Member(unit));
        }

        initialCount = members.Count;
    }

    public bool TryAssemble(ITargetable target, float time, float maximumDuration)
    {
        objective = target;
        if (maximumDuration > 0f)
            operationDeadline = time + maximumDuration;
        Vector3 direction = Direction(anchor, target.Position);
        float stagingDistance = Mathf.Max(0f, director.Settings.stagingDistance);
        if (DistanceSquared(anchor, target.Position) < stagingDistance * stagingDistance * 2f)
            direction = -direction;

        if (!TryTravel(anchor + direction * stagingDistance, time, nearHome: true))
            return false;

        stagingPosition = travelDestination;
        stateDeadline = time + assemblyTimeout;
        State = AISquadMissionState.Assemble;
        return true;
    }

    public bool TryDefend(float radius, int slotCount, Vector3? rally)
    {
        guardRadius = radius;
        guardSlots = Mathf.Max(initialCount, slotCount);
        guardSpacing = GetMaxRadius();
        guardRally = rally;
        for (int i = members.Count - 1; i >= 0; i--)
        {
            members[i].GuardSlot = i;
            if (!TryAssignGuard(members[i]))
                RemoveMember(i);
        }

        State = AISquadMissionState.Defend;
        return members.Count > 0;
    }

    public void UpdateGuardArea(Vector3 position, float radius, int desiredCount, int slotCount, Vector3? rally)
    {
        PruneMembers();
        while (members.Count > desiredCount)
            RemoveMember(members.Count - 1);

        int capacity = Mathf.Max(guardSlots, slotCount);
        if (anchor == position && guardRadius == radius && guardSlots == capacity && guardRally == rally)
            return;

        anchor = position;
        guardRadius = radius;
        guardSlots = capacity;
        guardRally = rally;
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (!TryAssignGuard(members[i]))
                RemoveMember(i);
        }
    }

    public void ReinforceGuard(IReadOnlyList<CombatUnit> recruits, int desiredCount)
    {
        int slot = 0;
        foreach (CombatUnit unit in recruits)
        {
            if (members.Count >= desiredCount)
                break;
            if (!director.Reserve(unit))
                continue;

            while (HasGuardSlot(slot))
                slot++;

            var member = new Member(unit) { GuardSlot = slot };
            if (TryAssignGuard(member))
                members.Add(member);
            else
                director.Release(unit);
        }
    }

    private bool HasGuardSlot(int slot)
    {
        foreach (Member member in members)
        {
            if (member.GuardSlot == slot)
                return true;
        }
        return false;
    }

    private bool TryAssignGuard(Member member)
    {
        Vector3 post = FindGuardPost(member);

        return TryAssign(member, CombatMission.DefendArea(anchor, guardRadius, post));
    }

    private Vector3 FindGuardPost(Member member)
    {
        float unitRadius = member.Unit.NavigationRadius;
        Vector3 position = member.Unit.Position;
        if (guardRally is Vector3 rally && IsNear(position, rally, Grid.CellSize * RallyHoldRadiusInCells) &&
            IsNear(position, anchor, guardRadius) && Grid.IsStaticallyTraversable(Grid.WorldToCell(position), unitRadius) &&
            !IsNearProductionExit(position, unitRadius))
            return position;

        GridCoord center = Grid.WorldToCell(guardRally ?? anchor);
        GridCoord preferred = Formation.GetPreferredSlot(center, member.GuardSlot, guardSlots, guardSpacing);
        if (TryGetGuardPost(preferred, unitRadius, out Vector3 post))
            return post;

        // A building center often covers every formation slot. Spread posts around its perimeter and clear its exit.
        int directions = RallyOffsets.Length;
        int searchCells = Mathf.Min(12, Mathf.CeilToInt(guardRadius * 0.8f / Grid.CellSize));
        for (int ring = 1; ring <= searchCells; ring++)
        {
            for (int i = 0; i < directions; i++)
            {
                Vector3 offset = RallyOffsets[(member.GuardSlot + i) % directions] * ring;
                GridCoord cell = new(center.x + (int)offset.x, center.z + (int)offset.z);
                if (TryGetGuardPost(cell, unitRadius, out post))
                    return post;
            }
        }

        return anchor;
    }

    private bool TryGetGuardPost(GridCoord cell, float unitRadius, out Vector3 position)
    {
        position = default;
        if (!Grid.IsStaticallyTraversable(cell, unitRadius))
            return false;

        position = Grid.CellToWorld(cell);
        return IsNear(position, anchor, guardRadius * 0.8f) && !IsNearProductionExit(position, unitRadius + Grid.CellSize);
    }

    public bool Covers(Vector3 position) => IsNear(position, anchor, guardRadius);

    public void Tick(float time)
    {
        PruneMembers();
        if (members.Count == 0)
        {
            Finish(AISquadMissionState.Failed);
            return;
        }

        if (State == AISquadMissionState.Defend)
            return;

        if (State != AISquadMissionState.Withdraw && TooManyLosses())
        {
            BeginWithdrawal(time);
            return;
        }

        if (State != AISquadMissionState.Withdraw && time >= operationDeadline)
        {
            plannedReturn = true;
            BeginWithdrawal(time);
            return;
        }

        switch (State)
        {
            case AISquadMissionState.Assemble:
                TickAssemble(time);
                break;
            case AISquadMissionState.Advance:
                TickAdvance(time);
                break;
            case AISquadMissionState.Engage:
                TickEngage(time);
                break;
            case AISquadMissionState.Evaluate:
                Evaluate(time);
                break;
            case AISquadMissionState.Withdraw:
                TickWithdraw(time);
                break;
        }
    }

    private void TickAssemble(float time)
    {
        int arrived = CountNear(stagingPosition, arrivalRadius);
        if (arrived < RequiredReady)
        {
            readySince = float.PositiveInfinity;
            if (time >= stateDeadline)
                Finish(AISquadMissionState.Failed);
            return;
        }

        readySince = Mathf.Min(readySince, time);
        if (arrived < members.Count && time < stateDeadline && time < readySince + AssemblyGrace)
            return;

        DropOutside(stagingPosition, arrivalRadius);
        if (TooManyLosses())
            BeginWithdrawal(time);
        else if (director.IsValidObjective(objective))
            BeginAdvance(time);
        else
            State = AISquadMissionState.Evaluate;
    }

    private void BeginAdvance(float time, Vector3? previousPursuitOrigin = null)
    {
        pursuitOrigin = previousPursuitOrigin ?? objective.Position;
        if (IsOutsidePursuitLeash())
        {
            State = AISquadMissionState.Evaluate;
            return;
        }

        retriedAdvance = false;
        State = AISquadMissionState.Advance;
        if (!TryEngageNearby(time) && !TryApproach(time))
        {
            retriedAdvance = true;
            if (!TryApproach(time))
                State = AISquadMissionState.Evaluate;
        }
    }

    private bool TryApproach(float time)
    {
        Vector3 target = objective.Position;
        Vector3 approach = Direction(target, GetCenter());
        GridCoord? previous = null;
        if (retriedAdvance)
        {
            approach = Quaternion.Euler(0f, 55f, 0f) * approach;
            previous = Grid.WorldToCell(travelDestination);
        }

        float standOff = ObjectiveRadius() + Mathf.Max(3f, Grid.CellSize * 2f);
        approachTargetPosition = target;
        return TryTravel(target + approach * standOff, time, avoid: previous);
    }

    private void TickAdvance(float time)
    {
        if (!director.IsValidObjective(objective) || IsOutsidePursuitLeash())
        {
            State = AISquadMissionState.Evaluate;
            return;
        }

        if (TryEngageNearby(time))
            return;

        if (IsNear(objective.Position, approachTargetPosition, ContactRange * 0.5f) &&
            CountNear(travelDestination, arrivalRadius) >= RequiredReady)
        {
            BeginEngagement(time, travelDestination, arrivalRadius * 1.5f);
            return;
        }

        if (!TravelExpired(time))
            return;

        if (!retriedAdvance)
        {
            retriedAdvance = true;
            if (TryApproach(time))
                return;
        }

        State = AISquadMissionState.Evaluate;
    }

    private bool TryEngageNearby(float time)
    {
        Vector3 target = objective.Position;
        CombatUnit lead = FindLead(target);
        Vector3 front = lead.Position;
        float cohesionRadius = Mathf.Max(ContactRange, arrivalRadius * 2f);
        if (CountNear(front, cohesionRadius) < RequiredReady)
            return false;

        if (!IsNear(front, target, ObjectiveRadius() + ContactRange))
        {
            ITargetable contact = lead.Sensor.FindClosestEnemy(ContactRange, front, ContactRange,
                unitsOnly: true, objective: null, excluded: rejectedObjectives);
            if (contact is not CombatUnit || !director.IsValidObjective(contact))
                return false;

            resumeObjective = objective;
            resumePursuitOrigin = pursuitOrigin;
            objective = contact;
        }

        BeginEngagement(time, front, cohesionRadius);
        return true;
    }

    private void BeginEngagement(float time, Vector3 front, float cohesionRadius)
    {
        DropOutside(front, cohesionRadius);
        if (TooManyLosses())
        {
            BeginWithdrawal(time);
            return;
        }

        CombatMission assignment = CombatMission.AttackTarget(objective);
        for (int i = members.Count - 1; i >= 0; i--)
        {
            members[i].StopOwnedOrders();
            if (!TryAssign(members[i], assignment))
                RemoveMember(i);
        }

        objectiveHealth = GetObjectiveHealth();
        lastProgressAt = time;
        State = AISquadMissionState.Engage;
    }

    private void TickEngage(float time)
    {
        if (!float.IsPositiveInfinity(operationDeadline) && !director.IsValidObjective(objective))
        {
            plannedReturn = true;
            BeginWithdrawal(time);
            return;
        }

        if (!director.IsValidObjective(objective) || !HasActiveAttackers())
        {
            State = AISquadMissionState.Evaluate;
            return;
        }

        float health = GetObjectiveHealth();
        if (health < objectiveHealth)
            lastProgressAt = time;
        objectiveHealth = health;
        if (time >= lastProgressAt + EngagementStallTimeout)
            State = AISquadMissionState.Evaluate;
    }

    private void Evaluate(float time)
    {
        if (director.IsValidObjective(objective))
            rejectedObjectives.Add(objective);

        if (director.IsValidObjective(resumeObjective))
        {
            objective = resumeObjective;
            resumeObjective = null;
            BeginAdvance(time, resumePursuitOrigin);
            return;
        }

        resumeObjective = null;
        objective = director.FindObjective(GetCenter(), rejectedObjectives, out bool hasObjectives);
        if (objective != null)
            BeginAdvance(time);
        else
            Finish(hasObjectives ? AISquadMissionState.Failed : AISquadMissionState.Completed);
    }

    private void BeginWithdrawal(float time)
    {
        if (TryTravel(stagingPosition, time, nearHome: true))
            State = AISquadMissionState.Withdraw;
        else
            Finish(AISquadMissionState.Failed);
    }

    private void TickWithdraw(float time)
    {
        int returned = CountNear(travelDestination, arrivalRadius);
        AISquadMissionState outcome = plannedReturn && !TooManyLosses() ? AISquadMissionState.Completed : AISquadMissionState.Withdrawn;
        if (returned == members.Count)
            Finish(outcome);
        else if (TravelExpired(time))
            Finish(returned >= RequiredReady ? outcome : AISquadMissionState.Failed);
    }

    private bool TooManyLosses() => initialCount - members.Count >= initialCount * withdrawalThreshold;

    private bool TryTravel(Vector3 desired, float time, bool nearHome = false, GridCoord? avoid = null)
    {
        float maxRadius = GetMaxRadius();
        arrivalRadius = GetFormationRadius(maxRadius) + Grid.CellSize * 1.5f;
        if (!TryFindTravelCenter(desired, maxRadius, nearHome, avoid, out Vector3 destination))
            return false;

        float travelSeconds = 0f;
        foreach (Member member in members)
        {
            member.StopOwnedOrders();
            movers.Add(member.Unit);
            member.LastPosition = member.Unit.Position;
            float distance = Mathf.Sqrt(DistanceSquared(member.LastPosition, destination));
            travelSeconds = Mathf.Max(travelSeconds, distance / Mathf.Max(0.1f, member.Unit.Definition.moveSpeed));
        }

        bool issued = GroupMoveCommand.TryMoveGroup(director.Context, director.Faction, movers, destination);
        movers.Clear();
        foreach (Member member in members)
        {
            member.CommandRevision = member.Unit.CommandRevision;
            member.TravelOrder = issued ? member.Unit.CurrentMoveOrder : null;
        }

        travelDestination = destination;
        lastProgressAt = time;
        stateDeadline = time + Mathf.Max(assemblyTimeout, travelSeconds * 2f + TravelStallTimeout);
        return issued;
    }

    private bool TravelExpired(float time)
    {
        bool traveling = false;
        foreach (Member member in members)
        {
            if (!member.IsTraveling)
                continue;

            traveling = true;
            if (!IsNear(member.Unit.Position, member.LastPosition, Grid.CellSize * 0.25f))
            {
                member.LastPosition = member.Unit.Position;
                lastProgressAt = time;
            }
        }

        return !traveling || time >= stateDeadline || time >= lastProgressAt + TravelStallTimeout;
    }

    private bool TryFindTravelCenter(Vector3 desired, float maxRadius, bool nearHome, GridCoord? avoid, out Vector3 position)
    {
        position = default;
        float minRadius = maxRadius;
        int smallerUnits = 0;
        foreach (Member member in members)
        {
            float radius = member.Unit.NavigationRadius;
            minRadius = Mathf.Min(minRadius, radius);
            if (radius < maxRadius)
                smallerUnits++;
        }

        float step = Mathf.Max(Grid.CellSize * 2f, arrivalRadius * 0.5f);
        int bestClearSlots = RequiredReady - 1;
        for (int ring = 0; ring <= 3; ring++)
        {
            int candidates = ring == 0 ? 1 : RallyOffsets.Length;
            for (int i = 0; i < candidates; i++)
            {
                GridCoord center = Grid.WorldToCell(desired + RallyOffsets[i] * (ring * step));
                if ((avoid is GridCoord previous && center.x == previous.x && center.z == previous.z) ||
                    !Grid.IsStaticallyTraversable(center, minRadius))
                    continue;

                Vector3 centerPosition = Grid.CellToWorld(center);
                if (nearHome && IsNearProductionExit(centerPosition, arrivalRadius))
                    continue;

                int largeSlots = 0;
                int smallSlots = 0;
                for (int slot = 0; slot < members.Count; slot++)
                {
                    GridCoord cell = Formation.GetPreferredSlot(center, slot, members.Count, maxRadius);
                    if (Grid.IsStaticallyTraversable(cell, maxRadius))
                        largeSlots++;
                    else if (smallerUnits > 0 && Grid.IsStaticallyTraversable(cell, minRadius))
                        smallSlots++;
                }

                int clearSlots = largeSlots + Mathf.Min(smallSlots, smallerUnits);
                if (clearSlots <= bestClearSlots)
                    continue;

                position = centerPosition;
                bestClearSlots = clearSlots;
                if (clearSlots == members.Count)
                    return true;
            }
        }

        return bestClearSlots >= RequiredReady;
    }

    private bool IsNearProductionExit(Vector3 position, float padding)
    {
        IReadOnlyList<BuildingBase> buildings = director.Faction.BuildingManager.BuildingList;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingBase building = buildings[i];
            if (building != null && building.IsAlive && building.UnitProduction != null &&
                building.UnitProduction.IsNearExit(Grid, position, padding))
                return true;
        }

        return false;
    }

    private float GetMaxRadius()
    {
        float radius = 0f;
        foreach (Member member in members)
            radius = Mathf.Max(radius, member.Unit.NavigationRadius);
        return radius;
    }

    private bool IsOutsidePursuitLeash()
    {
        if (objective is not UnitBase)
            return false;

        float distance = float.PositiveInfinity;
        foreach (Member member in members)
            distance = Mathf.Min(distance, Mathf.Max(0.1f, member.Unit.CombatAI.Settings.pursuitDistance));

        return !IsNear(objective.Position, pursuitOrigin, distance);
    }

    private bool HasActiveAttackers()
    {
        foreach (Member member in members)
        {
            if (member.Unit.CombatAI.IsMissionActive)
                return true;
        }

        return false;
    }

    private float GetFormationRadius(float maxRadius)
    {
        float distanceSquared = 0f;
        for (int i = 0; i < members.Count; i++)
        {
            GridCoord offset = Formation.GetPreferredSlot(default, i, members.Count, maxRadius);
            distanceSquared = Mathf.Max(distanceSquared, (float)offset.x * offset.x + (float)offset.z * offset.z);
        }

        return Mathf.Sqrt(distanceSquared) * Grid.CellSize;
    }

    private float ObjectiveRadius() => objective is BuildingBase building
        ? ((Vector2)building.PlacedFootprintSize).magnitude * Grid.CellSize * 0.5f : 0f;

    private float GetObjectiveHealth() => objective is BuildingBase building
        ? building.Health.HealthRatio : ((UnitBase)objective).Health.HealthRatio;

    private CombatUnit FindLead(Vector3 position)
    {
        CombatUnit lead = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Member member in members)
        {
            float distance = DistanceSquared(member.Unit.Position, position);
            if (distance >= bestDistance)
                continue;

            lead = member.Unit;
            bestDistance = distance;
        }

        return lead;
    }

    private Vector3 GetCenter()
    {
        Vector3 center = Vector3.zero;
        foreach (Member member in members)
            center += member.Unit.Position;
        return center / members.Count;
    }

    internal int CountNear(Vector3 position, float radius)
    {
        int count = 0;
        foreach (Member member in members)
        {
            if (IsNear(member.Unit.Position, position, radius))
                count++;
        }

        return count;
    }

    private void DropOutside(Vector3 position, float radius)
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (!IsNear(members[i].Unit.Position, position, radius))
                RemoveMember(i);
        }
    }

    private static bool TryAssign(Member member, CombatMission mission)
    {
        CombatUnitAI ai = member.Unit.CombatAI;
        if (!ai.TryStartMission(mission))
            return false;

        member.MissionRevision = ai.MissionRevision;
        member.HasTacticalAssignment = true;
        return true;
    }

    private void PruneMembers()
    {
        for (int i = members.Count - 1; i >= 0; i--)
        {
            Member member = members[i];
            CombatUnit unit = member.Unit;
            if (!director.CanParticipate(unit) || unit.CommandRevision != member.CommandRevision ||
                unit.CombatAI.MissionRevision != member.MissionRevision ||
                (member.HasTacticalAssignment && (unit.CombatAI.MissionStatus == CombatMissionStatus.Cancelled ||
                 (State == AISquadMissionState.Defend && !unit.CombatAI.IsMissionActive))))
                RemoveMember(i);
        }
    }

    private void RemoveMember(int index)
    {
        Member member = members[index];
        if (member.Unit == null || !member.Unit.IsAlive)
            Casualties++;
        member.StopOwnedOrders();
        director.Release(member.Unit);
        members.RemoveAt(index);
    }

    public void Finish(AISquadMissionState outcome)
    {
        for (int i = members.Count - 1; i >= 0; i--)
            RemoveMember(i);

        objective = null;
        resumeObjective = null;
        rejectedObjectives.Clear();
        State = outcome;
    }

    private static bool IsNear(Vector3 position, Vector3 center, float radius) =>
        DistanceSquared(position, center) <= radius * radius;

    private static Vector3 Direction(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
    }
}
