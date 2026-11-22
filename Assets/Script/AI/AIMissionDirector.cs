using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class AIMissionDirector
{
    internal enum LaunchResult { Started, AssaultActive, NoBase, InsufficientRecruits, NoObjective, StagingUnavailable }

    internal struct MilitaryStatus
    {
        public int Living, Available, Defending, Assaulting, Recovering, NearbyDefenders, NearbyAvailable;
        public bool HasAssault;
    }

    private const float DecisionInterval = 0.4f;

    private readonly AIFactionController controller;
    private readonly GameContext context;
    private readonly AIWaveSettings settings;
    private readonly HashSet<CombatUnit> assignedUnits = new();
    private readonly List<CombatUnit> recruits = new();
    private readonly Comparison<CombatUnit> compareRecruits;
    private Vector3 recruitAnchor;

    private AISquadMission assault;
    private AISquadMission defense;
    private float time;
    private float nextDecisionAt;
    private float nextWaveAt;

    public AISquadMissionState? AssaultState => assault?.State;
    public AISquadMissionState? DefenseState => defense?.State;
    public AISquadMissionState? LastAssaultOutcome { get; private set; }
    public AISquadMissionState? LastDefenseOutcome { get; private set; }
    internal int AssaultCompletionRevision { get; private set; }
    internal int LastAssaultCasualties { get; private set; }
    internal int ManualCancellationRevision { get; private set; }

    internal Faction Faction => controller.Faction;
    internal GameContext Context => context;
    internal AIWaveSettings Settings => settings;

    internal AIMissionDirector(AIFactionController controller, GameContext context, AIWaveSettings settings)
    {
        this.controller = controller;
        this.context = context;
        this.settings = settings;
        compareRecruits = CompareRecruits;
        nextWaveAt = Mathf.Max(0f, settings.initialDelay);
    }

    internal void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        time += deltaTime;
        if (time < nextDecisionAt)
            return;

        nextDecisionAt = time + DecisionInterval;
        TickMissions();
        DispatchWave();
    }

    public bool TryLaunchAssault() => TryLaunchAssault(settings.minimumParticipants, settings.maximumParticipants,
        settings.homeDefenders) == LaunchResult.Started;

    internal LaunchResult TryLaunchAssault(int minimum, int maximum, int homeReserve, float maximumDuration = 0f,
        Vector3? home = null)
    {
        if (assault != null)
            return LaunchResult.AssaultActive;
        if ((home ?? controller.BaseAnchor) is not Vector3 anchor)
            return LaunchResult.NoBase;

        minimum = Mathf.Max(1, minimum);
        maximum = Mathf.Max(minimum, maximum);
        int reserve = GetUnassignedReserve(homeReserve, anchor);

        CollectRecruits(anchor);
        int count = Mathf.Min(maximum, recruits.Count - reserve);
        ITargetable objective = count >= minimum ? FindObjective(anchor, null, out _) : null;
        if (objective == null)
        {
            recruits.Clear();
            return count < minimum ? LaunchResult.InsufficientRecruits : LaunchResult.NoObjective;
        }

        var mission = new AISquadMission(this, anchor, recruits, reserve, count);
        recruits.Clear();
        if (!mission.TryAssemble(objective, time, maximumDuration))
        {
            mission.Finish(AISquadMissionState.Failed);
            return LaunchResult.StagingUnavailable;
        }

        assault = mission;
        return LaunchResult.Started;
    }

    public bool TryDefendArea(Vector3 anchor, float guardRadius, int participantCount) =>
        TryDefendArea(anchor, guardRadius, participantCount, 0);

    private bool TryDefendArea(Vector3 anchor, float guardRadius, int participantCount, int guardSlots, Vector3? rally = null)
    {
        if (defense != null || participantCount < 1 || !IsValidGuardArea(anchor, guardRadius))
            return false;

        CollectRecruits(rally ?? anchor);
        int count = Mathf.Min(participantCount, recruits.Count);
        if (count == 0)
            return false;

        var mission = new AISquadMission(this, anchor, recruits, 0, count);
        recruits.Clear();
        if (!mission.TryDefend(guardRadius, guardSlots, rally))
        {
            mission.Finish(AISquadMissionState.Failed);
            return false;
        }

        defense = mission;
        return true;
    }

    internal void MaintainDefense(Vector3 anchor, float guardRadius, int desiredCount, int guardSlots, Vector3? rally)
    {
        if (desiredCount < 1)
        {
            CancelDefense();
            return;
        }
        if (!IsValidGuardArea(anchor, guardRadius))
            return;

        if (defense == null)
        {
            TryDefendArea(anchor, guardRadius, desiredCount, guardSlots, rally);
            return;
        }

        defense.UpdateGuardArea(anchor, guardRadius, desiredCount, guardSlots, rally);
        if (defense.MemberCount >= desiredCount)
            return;

        CollectRecruits(rally ?? anchor);
        defense.ReinforceGuard(recruits, desiredCount);
        recruits.Clear();
        if (defense.MemberCount == 0)
        {
            defense.Finish(AISquadMissionState.Failed);
            EndDefense();
        }
    }

    internal int GetUnassignedReserve(int homeReserve, Vector3 home)
    {
        int defendersOnDuty = defense != null && defense.Covers(home) ? defense.MemberCount : 0;
        return Mathf.Max(0, homeReserve - defendersOnDuty);
    }

    private bool IsValidGuardArea(Vector3 anchor, float radius) => float.IsFinite(radius) && radius > 0f &&
        float.IsFinite(anchor.x) && float.IsFinite(anchor.z) && context.TerrainGrid is TerrainGrid grid &&
        grid.IsInside(grid.WorldToCell(anchor));

    public void CancelAssault()
    {
        if (assault == null)
            return;

        assault.Finish(AISquadMissionState.Cancelled);
        EndAssault();
    }

    public void CancelDefense()
    {
        if (defense == null)
            return;

        defense.Finish(AISquadMissionState.Cancelled);
        EndDefense();
    }

    public void CancelAll()
    {
        ManualCancellationRevision++;
        CancelAssault();
        CancelDefense();
        ScheduleCooldown();
    }

    private void TickMissions()
    {
        if (assault != null)
        {
            assault.Tick(time);
            if (assault.IsFinished)
                EndAssault();
        }

        if (defense != null)
        {
            defense.Tick(time);
            if (defense.IsFinished)
                EndDefense();
        }
    }

    private void DispatchWave()
    {
        if (controller.Strategy == null && settings.automaticWaves && assault == null && time >= nextWaveAt &&
            !TryLaunchAssault())
            ScheduleCooldown();
    }

    private void ScheduleCooldown() => nextWaveAt = time + Mathf.Max(0.5f, settings.waveCooldown);

    private void EndAssault()
    {
        LastAssaultOutcome = assault.State;
        LastAssaultCasualties = assault.Casualties;
        AssaultCompletionRevision++;
        assault = null;
        ScheduleCooldown();
    }

    private void EndDefense()
    {
        LastDefenseOutcome = defense.State;
        defense = null;
    }

    internal bool Reserve(CombatUnit unit) => assignedUnits.Add(unit);
    internal void Release(CombatUnit unit) => assignedUnits.Remove(unit);

    internal bool CanParticipate(CombatUnit unit) => Faction.CanIssueCommandsTo(unit) &&
        unit.IsInitialized && unit.IsAlive && unit.Motor != null && unit.CanAttack() &&
        unit.CombatAI?.IsFitForSquad == true;

    private bool CanRecruit(CombatUnit unit) => CanParticipate(unit) && !assignedUnits.Contains(unit) &&
        !unit.CombatAI.IsMissionActive && unit.CurrentCommand == CommandType.Idle && unit.CurrentTarget == null;

    internal MilitaryStatus ObserveMilitary(Vector3 guardAnchor, float guardRadius)
    {
        MilitaryStatus status = new()
        {
            Defending = defense?.MemberCount ?? 0,
            Assaulting = assault?.MemberCount ?? 0,
            HasAssault = assault != null,
            NearbyDefenders = defense?.CountNear(guardAnchor, guardRadius) ?? 0
        };
        float radiusSquared = guardRadius * guardRadius;
        IReadOnlyList<UnitBase> units = Faction.UnitManager.UnitList;
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] is not CombatUnit unit || unit == null || !unit.IsAlive)
                continue;

            status.Living++;
            if (unit.CombatAI?.IsFitForSquad == false)
                status.Recovering++;
            if (CanRecruit(unit))
            {
                status.Available++;
                if (DistanceSquared(unit.Position, guardAnchor) <= radiusSquared)
                    status.NearbyAvailable++;
            }
        }
        return status;
    }

    private void CollectRecruits(Vector3 anchor)
    {
        recruits.Clear();
        IReadOnlyList<UnitBase> units = Faction.UnitManager.UnitList;
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] is CombatUnit unit && CanRecruit(unit))
                recruits.Add(unit);
        }

        recruitAnchor = anchor;
        recruits.Sort(compareRecruits);
    }

    private int CompareRecruits(CombatUnit first, CombatUnit second)
    {
        int distance = DistanceSquared(first.Position, recruitAnchor).CompareTo(DistanceSquared(second.Position, recruitAnchor));
        return distance != 0 ? distance : first.UnitId.CompareTo(second.UnitId);
    }

    internal bool IsValidObjective(ITargetable target) => target is Object unityObject && unityObject != null &&
        target.IsAlive && Faction.IsEnemy(target.OwnerFaction);

    internal ITargetable FindObjective(Vector3 origin, HashSet<ITargetable> rejected, out bool hasObjectives)
    {
        hasObjectives = false;
        BuildingBase structure = null;
        CombatUnit combatUnit = null;
        float structureDistance = float.PositiveInfinity;
        float unitDistance = float.PositiveInfinity;
        IReadOnlyList<Faction> factions = context.FactionManager.Factions;
        for (int i = 0; i < factions.Count; i++)
        {
            Faction enemy = factions[i];
            if (!Faction.IsEnemy(enemy))
                continue;

            IReadOnlyList<BuildingBase> buildings = enemy.BuildingManager.BuildingList;
            for (int j = 0; j < buildings.Count; j++)
            {
                BuildingBase candidate = buildings[j];
                if (candidate == null || !candidate.IsInitialized || candidate.OwnerFaction != enemy ||
                    !IsValidObjective(candidate))
                    continue;

                hasObjectives = true;
                float distance = DistanceSquared(origin, candidate.Position);
                if (distance < structureDistance && rejected?.Contains(candidate) != true)
                {
                    structure = candidate;
                    structureDistance = distance;
                }
            }

            if (structure != null)
                continue;

            IReadOnlyList<UnitBase> units = enemy.UnitManager.UnitList;
            for (int j = 0; j < units.Count; j++)
            {
                if (units[j] is not CombatUnit candidate || candidate == null || !candidate.IsInitialized ||
                    candidate.OwnerFaction != enemy || !IsValidObjective(candidate))
                    continue;

                hasObjectives = true;
                float distance = DistanceSquared(origin, candidate.Position);
                if (distance < unitDistance && rejected?.Contains(candidate) != true)
                {
                    combatUnit = candidate;
                    unitDistance = distance;
                }
            }
        }

        return structure != null ? structure : combatUnit;
    }

    internal static float DistanceSquared(Vector3 first, Vector3 second)
    {
        Vector3 offset = first - second;
        return offset.x * offset.x + offset.z * offset.z;
    }
}
