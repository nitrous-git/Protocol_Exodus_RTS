using System.Collections.Generic;
using UnityEngine;

public sealed class AIMissionDirector
{
    private const float DecisionInterval = 0.4f;

    private readonly AIFactionController controller;
    private readonly GameContext context;
    private readonly AIWaveSettings settings;
    private readonly HashSet<CombatUnit> assignedUnits = new();
    private readonly List<CombatUnit> recruits = new();

    private AISquadMission assault;
    private AISquadMission defense;
    private float time;
    private float nextDecisionAt;
    private float nextWaveAt;

    public AISquadMissionState? AssaultState => assault?.State;
    public AISquadMissionState? DefenseState => defense?.State;
    public AISquadMissionState? LastAssaultOutcome { get; private set; }
    public AISquadMissionState? LastDefenseOutcome { get; private set; }

    internal Faction Faction => controller.Faction;
    internal GameContext Context => context;
    internal AIWaveSettings Settings => settings;

    internal AIMissionDirector(AIFactionController controller, GameContext context, AIWaveSettings settings)
    {
        this.controller = controller;
        this.context = context;
        this.settings = settings;
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

    public bool TryLaunchAssault()
    {
        if (assault != null || !controller.BaseAnchor.HasValue)
            return false;

        Vector3 anchor = controller.BaseAnchor.Value;
        int minimum = Mathf.Max(1, settings.minimumParticipants);
        int maximum = Mathf.Max(minimum, settings.maximumParticipants);
        int defendersOnDuty = defense != null && defense.Covers(anchor) ? defense.MemberCount : 0;
        int reserve = Mathf.Max(0, settings.homeDefenders - defendersOnDuty);

        CollectRecruits(anchor);
        int count = Mathf.Min(maximum, recruits.Count - reserve);
        ITargetable objective = count >= minimum ? FindObjective(anchor, null, out _) : null;
        if (objective == null)
        {
            recruits.Clear();
            return false;
        }

        var mission = new AISquadMission(this, anchor, recruits, reserve, count);
        recruits.Clear();
        if (!mission.TryAssemble(objective, time))
        {
            mission.Finish(AISquadMissionState.Failed);
            return false;
        }

        assault = mission;
        return true;
    }

    public bool TryDefendArea(Vector3 anchor, float guardRadius, int participantCount)
    {
        TerrainGrid grid = context.TerrainGrid;
        if (defense != null || participantCount < 1 || !float.IsFinite(guardRadius) || guardRadius <= 0f ||
            !float.IsFinite(anchor.x) || !float.IsFinite(anchor.z) || grid == null ||
            !grid.IsInside(grid.WorldToCell(anchor)))
            return false;

        CollectRecruits(anchor);
        int count = Mathf.Min(participantCount, recruits.Count);
        if (count == 0)
            return false;

        var mission = new AISquadMission(this, anchor, recruits, 0, count);
        recruits.Clear();
        if (!mission.TryDefend(guardRadius))
        {
            mission.Finish(AISquadMissionState.Failed);
            return false;
        }

        defense = mission;
        return true;
    }

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
        if (settings.automaticWaves && assault == null && time >= nextWaveAt && !TryLaunchAssault())
            ScheduleCooldown();
    }

    private void ScheduleCooldown() => nextWaveAt = time + Mathf.Max(0.5f, settings.waveCooldown);

    private void EndAssault()
    {
        LastAssaultOutcome = assault.State;
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

    private void CollectRecruits(Vector3 anchor)
    {
        recruits.Clear();
        IReadOnlyList<UnitBase> units = Faction.UnitManager.UnitList;
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] is CombatUnit unit && CanParticipate(unit) && !assignedUnits.Contains(unit) &&
                !unit.CombatAI.IsMissionActive && unit.CurrentCommand == CommandType.Idle && unit.CurrentTarget == null)
                recruits.Add(unit);
        }

        recruits.Sort((first, second) =>
        {
            int distance = DistanceSquared(first.Position, anchor).CompareTo(DistanceSquared(second.Position, anchor));
            return distance != 0 ? distance : first.UnitId.CompareTo(second.UnitId);
        });
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
