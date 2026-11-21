using System.Collections.Generic;
using UnityEngine;

public sealed class CombatUnitAI
{
    private static readonly CombatUnitAISettings DefaultSettings = new();

    private readonly CombatUnit unit;
    private readonly Vector3 baseAnchor;
    private CombatMission mission;
    private Vector3 objectiveOrigin;
    private ITargetable objective;
    private HashSet<ITargetable> failedObjectives;
    private ITargetable threat;
    private Vector3 threatOrigin;
    private ITargetable damageSource;
    private float damageExpiresAt;
    private float threatReengageAt;
    private HashSet<ITargetable> failedThreats;
    private Vector3 threatFailurePosition;

    private AttackState attackOrder;
    private MoveState moveOrder;
    private GridCoord travelCell;
    private Vector3 travelProgressPosition;
    private float travelProgressAt;
    private Vector3? guardPost;
    private int guardFailures;
    private int nextRetreatAnchor;
    private BuildingBase retreatBuilding;

    private float time;
    private float nextDecisionAt;

    public CombatMission CurrentMission => mission;
    public CombatMissionStatus MissionStatus { get; private set; }
    public bool IsMissionActive => MissionStatus == CombatMissionStatus.Active;
    public CombatUnitRole CurrentRole => mission.Type == CombatMissionType.AttackTarget ||
        mission.Type == CombatMissionType.AttackFaction ? CombatUnitRole.Attacker : CombatUnitRole.Defender;
    public CombatTacticalBehavior CurrentTacticalBehavior { get; private set; }

    internal CombatUnitAISettings Settings => unit.Definition.CombatAI ?? DefaultSettings;
    internal bool AllowsCombatAwareness => !IsMissionActive && CurrentTacticalBehavior != CombatTacticalBehavior.Retreat;

    internal CombatUnitAI(CombatUnit unit, Vector3 baseAnchor)
    {
        this.unit = unit;
        this.baseAnchor = baseAnchor;
        uint phase = unchecked((uint)unit.UnitId * 2654435761u) & 0xFFFFu;
        nextDecisionAt = phase / 65536f * Settings.decisionInterval;
    }

    /// <summary>Valid assignments supersede the current mission. Invalid assignments leave it in place.</summary>
    public bool TryStartMission(CombatMission assignment)
    {
        if (!unit.IsAlive || !IsValidAssignment(assignment))
            return false;

        StopOrder();
        Disengage();
        ResetMission(CombatMissionStatus.Active);
        mission = assignment;
        if (assignment.Type == CombatMissionType.AttackTarget)
            objectiveOrigin = assignment.Target.Position;
        CurrentTacticalBehavior = CombatTacticalBehavior.Awareness;
        if (IsCriticallyDamaged)
        {
            // A deliberate reassignment can move a recovering unit. Fresh hostile damage can trigger retreat again.
            damageExpiresAt = 0f;
            damageSource = null;
        }

        return true;
    }

    public void ClearMission()
    {
        if (!IsMissionActive && CurrentTacticalBehavior != CombatTacticalBehavior.Retreat)
            return;

        var next = CurrentTacticalBehavior == CombatTacticalBehavior.Retreat
            ? CombatTacticalBehavior.Recovering : CombatTacticalBehavior.Awareness;
        FinishMission(CombatMissionStatus.Cancelled, next);
    }

    internal void RecordDamage(DamageInfo damage)
    {
        if (damage.Amount <= 0f || (damage.SourceFaction != null && !unit.OwnerFaction.IsEnemy(damage.SourceFaction)))
            return;

        damageExpiresAt = time + Settings.recentDamageWindow;
        damageSource = unit.IsValidAttackTarget(damage.Source) ? damage.Source : null;
    }

    internal void Tick(float deltaTime)
    {
        time += deltaTime;
        if (time >= damageExpiresAt)
            damageSource = null;

        if (time < nextDecisionAt)
            return;

        nextDecisionAt = time + Mathf.Max(0.05f, Settings.decisionInterval);
        if (CurrentTacticalBehavior == CombatTacticalBehavior.Retreat)
        {
            TickRetreat();
            return;
        }

        if (CurrentTacticalBehavior == CombatTacticalBehavior.Recovering)
        {
            if (unit.Health.HealthRatio >= Mathf.Min(1f, Settings.retreatHealthThreshold + 0.1f) &&
                time >= damageExpiresAt)
                CurrentTacticalBehavior = CombatTacticalBehavior.Awareness;
            return;
        }

        if (IsCriticallyDamaged && time < damageExpiresAt)
        {
            ChangeBehavior(CombatTacticalBehavior.Retreat);
            Disengage();
            TickRetreat();
            return;
        }

        if (!IsMissionActive || !RefreshObjective())
            return;

        if (TryRespondToThreat())
            return;

        if (mission.Type == CombatMissionType.DefendArea)
            TickGuard();
        else if (CurrentTacticalBehavior != CombatTacticalBehavior.ObjectiveAttack)
        {
            ChangeBehavior(CombatTacticalBehavior.ObjectiveAttack);
            attackOrder = unit.StartTacticalAttack(objective);
        }
    }

    private bool IsCriticallyDamaged => unit.Health.HealthRatio <= Settings.retreatHealthThreshold;

    private bool IsValidAssignment(CombatMission assignment)
    {
        switch (assignment.Type)
        {
            case CombatMissionType.AttackTarget:
                return unit.IsValidAttackTarget(assignment.Target);
            case CombatMissionType.AttackFaction:
                return unit.OwnerFaction.IsEnemy(assignment.TargetFaction);
            case CombatMissionType.DefendArea:
                Vector3 position = assignment.GuardPosition;
                return float.IsFinite(position.x) && float.IsFinite(position.z) &&
                    float.IsFinite(assignment.GuardRadius) && assignment.GuardRadius > 0f &&
                    unit.TerrainGrid != null && unit.TerrainGrid.IsInside(unit.TerrainGrid.WorldToCell(position));
            default:
                return false;
        }
    }

    private bool RefreshObjective()
    {
        if (mission.Type == CombatMissionType.AttackTarget)
        {
            objective = mission.Target;
            if ((objective is Object targetObject && targetObject == null) || !objective.IsAlive)
                FinishMission(CombatMissionStatus.Completed);
            else if (!unit.IsValidAttackTarget(objective) || ApproachFailed ||
                     (objective is UnitBase && !InRadius(objective.Position, objectiveOrigin, Settings.pursuitDistance)))
                FinishMission(CombatMissionStatus.Failed);

            return IsMissionActive;
        }

        if (mission.Type == CombatMissionType.DefendArea)
            return true;

        if (!unit.OwnerFaction.IsEnemy(mission.TargetFaction))
        {
            FinishMission(CombatMissionStatus.Failed);
            return false;
        }

        if (unit.IsValidAttackTarget(objective))
        {
            if (!ApproachFailed)
                return true;

            (failedObjectives ??= new()).Add(objective);
        }

        if (CurrentTacticalBehavior == CombatTacticalBehavior.ObjectiveAttack)
            ChangeBehavior(CombatTacticalBehavior.Awareness);

        objective = FindStructure(mission.TargetFaction, isRetreat: false, headquarters: false, out bool hasSurvivors);
        if (objective == null)
            FinishMission(hasSurvivors ? CombatMissionStatus.Failed : CombatMissionStatus.Completed);

        return IsMissionActive;
    }

    private bool ApproachFailed => CurrentTacticalBehavior == CombatTacticalBehavior.ObjectiveAttack &&
        !unit.IsExecutingTacticalOrder(attackOrder);

    private bool TryRespondToThreat()
    {
        bool defending = mission.Type == CombatMissionType.DefendArea;
        Vector3 position = unit.Position;
        Vector3 center = defending ? mission.GuardPosition : position;
        float leash = defending ? mission.GuardRadius : Settings.pursuitDistance;
        float range = defending ? leash * 2f : Mathf.Min(Settings.threatRadius, leash);
        float retaliationRange = Mathf.Max(range, unit.GetAttackRange());
        if (CurrentTacticalBehavior == CombatTacticalBehavior.SelfDefense)
        {
            Vector3 origin = defending ? center : threatOrigin;
            bool hasAttackOrder = unit.IsExecutingTacticalOrder(attackOrder);
            bool threatIsValid = unit.IsValidAttackTarget(threat);
            if (hasAttackOrder && threatIsValid &&
                InRadius(position, origin, leash) && InRadius(threat.Position, origin, leash) &&
                InRadius(threat.Position, position, retaliationRange + Settings.threatExitPadding))
                return true;

            if (!hasAttackOrder && threatIsValid)
            {
                (failedThreats ??= new()).Add(threat);
                threatFailurePosition = position;
            }

            threatReengageAt = time + Settings.threatReengageDelay;
            ChangeBehavior(CombatTacticalBehavior.Awareness);
        }

        if (time < threatReengageAt || !InRadius(position, center, leash))
            return false;

        // A failed local approach can be retried from a different position.
        if (failedThreats?.Count > 0 && !InRadius(position, threatFailurePosition, Settings.repathDistance))
            failedThreats.Clear();

        ITargetable candidate = damageSource;
        if (candidate == objective || !unit.IsValidAttackTarget(candidate) || failedThreats?.Contains(candidate) == true ||
            !InRadius(candidate.Position, position, retaliationRange) || !InRadius(candidate.Position, center, leash))
        {
            candidate = unit.Sensor.FindClosestEnemy(range, center, leash, true, objective, failedThreats);
        }

        if (candidate == null)
            return false;

        ChangeBehavior(CombatTacticalBehavior.SelfDefense);
        threat = candidate;
        threatOrigin = position;
        attackOrder = unit.StartTacticalAttack(candidate);
        return true;
    }

    private void TickGuard()
    {
        ChangeBehavior(CombatTacticalBehavior.Guard);
        TerrainGrid grid = unit.TerrainGrid;
        if (moveOrder != null)
        {
            if (IsTraveling())
                return;

            if (HasReachedTravelCell())
                guardPost = grid.CellToWorld(travelCell);
            else
                guardFailures++;
            StopOrder();
        }

        float tolerance = Mathf.Min(mission.GuardRadius, grid.CellSize * 0.5f);
        if (InRadius(unit.Position, guardPost ?? mission.GuardPosition, tolerance))
        {
            guardFailures = 0;
            return;
        }

        if (guardFailures >= 2)
        {
            FinishMission(CombatMissionStatus.Failed);
            return;
        }

        GridCoord? cell = AllocateNear(mission.GuardPosition);
        if (cell.HasValue && !InRadius(grid.CellToWorld(cell.Value), mission.GuardPosition, mission.GuardRadius))
        {
            unit.ReleaseDestination(cell.Value);
            cell = null;
        }

        if (!StartTravel(cell))
            guardFailures++;
    }

    private void TickRetreat()
    {
        if (moveOrder != null)
        {
            bool anchorValid = ReferenceEquals(retreatBuilding, null) ||
                (retreatBuilding != null && retreatBuilding.IsAlive && retreatBuilding.IsOperational &&
                 retreatBuilding.OwnerFaction == unit.OwnerFaction);
            if (anchorValid && IsTraveling())
                return;

            if (anchorValid && HasReachedTravelCell())
            {
                FinishMission(CombatMissionStatus.Retreated, CombatTacticalBehavior.Recovering);
                return;
            }

            StopOrder();
        }

        // Try a headquarters, another operational structure, then the faction's starting anchor.
        if (nextRetreatAnchor < 3)
        {
            GridCoord? cell;
            if (nextRetreatAnchor < 2)
            {
                retreatBuilding = FindStructure(unit.OwnerFaction, isRetreat: true,
                    headquarters: nextRetreatAnchor == 0, out _);
                cell = retreatBuilding == null ? null : unit.DestinationAllocationSystem?.Ring.TryAllocate(
                    unit, retreatBuilding.FootprintOrigin, retreatBuilding.PlacedFootprintSize);
            }
            else
            {
                retreatBuilding = null;
                cell = AllocateNear(baseAnchor);
            }

            nextRetreatAnchor++;
            StartTravel(cell);
            return;
        }

        FinishMission(CombatMissionStatus.Failed, CombatTacticalBehavior.Recovering);
    }

    private BuildingBase FindStructure(Faction faction, bool isRetreat, bool headquarters, out bool hasSurvivors)
    {
        hasSurvivors = false;
        IReadOnlyList<BuildingBase> buildings = faction.BuildingManager?.BuildingList;
        if (buildings == null)
            return null;

        BuildingBase closest = null;
        Vector3 position = unit.Position;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingBase building = buildings[i];
            if (building == null || !building.IsAlive || building.OwnerFaction != faction)
                continue;

            if (isRetreat && (!building.IsOperational || (building.Headquarters != null) != headquarters))
                continue;

            hasSurvivors = true;
            if (!isRetreat && failedObjectives?.Contains(building) == true)
                continue;

            float distance = DistanceSquared(position, building.Position);
            if (distance < bestDistance)
            {
                closest = building;
                bestDistance = distance;
            }
        }
        return closest;
    }

    private GridCoord? AllocateNear(Vector3 position)
    {
        TerrainGrid grid = unit.TerrainGrid;
        return grid == null ? null : unit.DestinationAllocationSystem?.Formation.TryAllocate(
            unit, grid.WorldToCell(position), 0, 1, unit.NavigationRadius);
    }

    private bool StartTravel(GridCoord? cell)
    {
        if (!cell.HasValue)
            return false;

        if (unit.Motor == null)
        {
            unit.ReleaseDestination(cell.Value);
            return false;
        }

        travelCell = cell.Value;
        travelProgressAt = time;
        travelProgressPosition = unit.Position;
        moveOrder = unit.StartTacticalMove(travelCell);
        return moveOrder != null;
    }

    private bool IsTraveling()
    {
        if (!unit.IsExecutingTacticalOrder(moveOrder) || unit.Motor.HasArrived ||
            !unit.TerrainGrid.HasNavigationClearance(travelCell, unit.NavigationRadius))
            return false;

        if (!InRadius(unit.Position, travelProgressPosition, CombatUnitAISettings.ProgressDistance))
        {
            travelProgressPosition = unit.Position;
            travelProgressAt = time;
        }

        return time - travelProgressAt < CombatUnitAISettings.StalledTravelTimeout;
    }

    private bool HasReachedTravelCell() =>
        unit.TerrainGrid.HasNavigationClearance(travelCell, unit.NavigationRadius) &&
        InRadius(unit.Position, unit.TerrainGrid.CellToWorld(travelCell), unit.TerrainGrid.CellSize * 0.25f);

    private void ChangeBehavior(CombatTacticalBehavior next)
    {
        if (CurrentTacticalBehavior == next)
            return;

        StopOrder();
        threat = null;
        CurrentTacticalBehavior = next;
    }

    private void StopOrder()
    {
        if (unit.IsExecutingTacticalOrder(attackOrder) || unit.IsExecutingTacticalOrder(moveOrder))
            unit.EnterCombatIdle();
        attackOrder = null;
        moveOrder = null;
    }

    private void Disengage()
    {
        if (unit.CurrentCommand != CommandType.Idle || unit.CurrentTarget != null)
            unit.EnterCombatIdle();
    }

    private void FinishMission(CombatMissionStatus status, CombatTacticalBehavior next = CombatTacticalBehavior.Awareness)
    {
        ChangeBehavior(next);
        ResetMission(IsMissionActive ? status : MissionStatus);
    }

    private void ResetMission(CombatMissionStatus status)
    {
        mission = default;
        objective = null;
        failedObjectives?.Clear();
        failedThreats?.Clear();
        threat = null;
        threatReengageAt = 0f;
        guardPost = null;
        guardFailures = 0;
        retreatBuilding = null;
        nextRetreatAnchor = 0;
        MissionStatus = status;
    }

    internal void Release()
    {
        ResetMission(IsMissionActive ? CombatMissionStatus.Cancelled : MissionStatus);
        damageSource = null;
        attackOrder = null;
        moveOrder = null;
        CurrentTacticalBehavior = CombatTacticalBehavior.Awareness;
    }

    private static bool InRadius(Vector3 first, Vector3 second, float radius) =>
        DistanceSquared(first, second) <= radius * radius;

    private static float DistanceSquared(Vector3 first, Vector3 second)
    {
        Vector3 difference = first - second;
        difference.y = 0f;
        return difference.sqrMagnitude;
    }
}
