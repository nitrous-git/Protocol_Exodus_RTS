using System.Collections.Generic;
using UnityEngine;

public sealed class AIFactionStrategy
{
    private const int OpeningAssaultCount = 3;
    private const float LaunchRetryDelay = 6f;
    private const float CasualtyWindow = 15f;

    private enum Mode { Opening, Developing, PreparingAttack, Attacking, Recovering, Defending, EmergencyRecovery }

    private readonly AIFactionController controller;
    private readonly GameContext context;
    private readonly AIStrategySettings settings;
    private readonly AIStrategyProfile profile;
    private readonly List<(BuildingBase Building, int Hostiles)> baseAssets = new();

    private AIEconomyManager Economy => controller.EconomyManager;
    private AIMissionDirector Missions => controller.MissionDirector;
    private int InitialWorkers => Mathf.Max(2, settings.initialWorkerGoal);
    private int HomeReserve => Mathf.Max(0, profile.homeDefenseReserve);
    private int MaximumAttack => Mathf.Max(1, profile.maximumAttackForce);
    private int MinimumAttack => Mathf.Clamp(profile.minimumAttackForce, 1, MaximumAttack);
    private int DesiredAttack => Mathf.Clamp(profile.desiredAttackForce, MinimumAttack, MaximumAttack);
    private float Grace => Mathf.Max(0f, settings.baseBuildingGrace);
    private float DefenseHold => Mathf.Max(1f, profile.defenseStandDownDelay);
    private float RecoveryDuration => Mathf.Max(1f, InOpening ? settings.openingRecoveryDuration : profile.recoveryDuration);
    private float OpeningRetryWindow => Mathf.Max(1f, settings.openingRetryWindow);
    private float ProbeAt => Mathf.Max(Grace, settings.openingProbeTime);
    private float FirstAssaultAt => Mathf.Max(ProbeAt, settings.firstAssaultTime);
    private float SecondAssaultAt => Mathf.Max(FirstAssaultAt, settings.secondAssaultTime);

    private AIEconomyManager.Situation economy;
    private AIMissionDirector.MilitaryStatus military;
    private AIEconomyGoals goals;
    private Mode mode;
    private string attackStatus = "Awaiting first evaluation";
    private string lastLaunchStatus;
    private int openingAssault;
    private int localThreat;
    private int defenseTarget;
    private int availableForAttack;
    private int requiredForAttack;
    private int completionRevision;
    private int cancellationRevision;
    private int combatLosses;
    private int recentCasualties;
    private bool hadHeadquarters;
    private bool barracksUnlocked;
    private bool workerGrowthUnlocked;
    private bool secondBarracksUnlocked;
    private BuildingBase threatenedAsset;
    private Vector3 threatenedPosition;
    private float time;
    private float nextDecision;
    private float nextAttack;
    private float nextLaunchAttempt;
    private float recoverUntil;
    private float defendUntil;
    private float resumeManualCancellationAt;
    private float lastLossTime = float.NegativeInfinity;

    internal AIFactionStrategy(AIFactionController controller, GameContext context, AIStrategySettings settings,
        AIStrategyProfile profile)
    {
        this.controller = controller;
        this.context = context;
        this.settings = settings;
        this.profile = profile;
        openingAssault = settings.enableJavaInspiredOpening ? 0 : OpeningAssaultCount;
    }

    internal void Tick(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        time += deltaTime;
        if (time < nextDecision || controller.BaseAnchor is not Vector3 start)
            return;

        nextDecision = time + Mathf.Max(0.5f, settings.decisionInterval);
        economy = Economy.Observe();
        hadHeadquarters |= economy.Headquarters != null;
        Vector3 home = economy.Headquarters != null ? economy.Headquarters.Position : start;
        float radius = Mathf.Max(1f, profile.defenseThreatRadius);
        localThreat = ObserveThreat(home, radius);
        if (localThreat > 0)
            defendUntil = time + DefenseHold;

        ReadMissionOutcome();
        Vector3 guardAnchor = UpdateDefense(home, radius);
        military = Missions.ObserveMilitary(guardAnchor, radius);
        ReadCasualties();
        ExpireOpeningOpportunities();
        UpdateEconomyGoals();
        UpdateAssault(home);
    }

    private Vector3 UpdateDefense(Vector3 home, float radius)
    {
        int capacity = Mathf.Max(HomeReserve, profile.maximumDefenders);
        bool underPressure = time < defendUntil;
        if (localThreat > 0)
            defenseTarget = Mathf.Clamp(Mathf.Max(defenseTarget, HomeReserve + localThreat), HomeReserve, capacity);
        else if (!underPressure)
            defenseTarget = HomeReserve;

        Vector3 anchor = threatenedPosition;
        Vector3? rally = underPressure ? null : FindHomeRally(home, radius, out anchor);
        if (time >= resumeManualCancellationAt)
            Missions.MaintainDefense(anchor, radius, defenseTarget, capacity, rally);
        return anchor;
    }

    private Vector3? FindHomeRally(Vector3 home, float radius, out Vector3 guardAnchor)
    {
        guardAnchor = home;
        Vector3? result = null;
        float radiusSquared = radius * radius;
        float bestDistance = radiusSquared;
        TerrainGrid grid = context.TerrainGrid;
        IReadOnlyList<BuildingBase> buildings = controller.Faction.BuildingManager.BuildingList;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingBase building = buildings[i];
            if (building == null || !building.IsAlive || !building.IsOperational || building.Definition.Type != BuildingType.Barracks ||
                building.UnitProduction?.EffectiveRallyPoint is not Vector3 rally)
                continue;

            Vector3 position = building.Position;
            float distance = AIMissionDirector.DistanceSquared(home, position);
            if (distance < bestDistance && AIMissionDirector.DistanceSquared(position, rally) < radiusSquared &&
                grid.IsInside(grid.WorldToCell(rally)))
            {
                result = rally;
                guardAnchor = position;
                bestDistance = distance;
            }
        }

        return result;
    }

    private int ObserveThreat(Vector3 home, float radius)
    {
        baseAssets.Clear();
        IReadOnlyList<BuildingBase> buildings = controller.Faction.BuildingManager.BuildingList;
        for (int i = 0; i < buildings.Count; i++)
        {
            BuildingBase building = buildings[i];
            if (building != null && building.IsAlive)
                baseAssets.Add((building, 0));
        }

        float radiusSquared = radius * radius;
        int hostiles = 0;
        IReadOnlyList<Faction> factions = context.FactionManager.Factions;
        for (int f = 0; f < factions.Count; f++)
        {
            Faction faction = factions[f];
            if (!controller.Faction.IsEnemy(faction))
                continue;

            IReadOnlyList<UnitBase> units = faction.UnitManager.UnitList;
            for (int u = 0; u < units.Count; u++)
            {
                if (units[u] is not CombatUnit unit || unit == null || !unit.IsAlive || !unit.CanAttack())
                    continue;

                bool nearBase = baseAssets.Count == 0 && AIMissionDirector.DistanceSquared(unit.Position, home) <= radiusSquared;
                for (int i = 0; i < baseAssets.Count; i++)
                {
                    var asset = baseAssets[i];
                    if (AIMissionDirector.DistanceSquared(unit.Position, asset.Building.Position) > radiusSquared)
                        continue;

                    baseAssets[i] = (asset.Building, asset.Hostiles + 1);
                    nearBase = true;
                }

                if (nearBase)
                    hostiles++;
            }
        }

        if (hostiles == 0)
            return 0;

        BuildingBase focus = null;
        int focusStrength = 0;
        int previousStrength = 0;
        foreach (var asset in baseAssets)
        {
            if (asset.Building == threatenedAsset)
                previousStrength = asset.Hostiles;
            if (asset.Hostiles > focusStrength || (asset.Hostiles > 0 && asset.Hostiles == focusStrength &&
                asset.Building == economy.Headquarters))
            {
                focus = asset.Building;
                focusStrength = asset.Hostiles;
            }
        }

        if (previousStrength > 0 && focus != economy.Headquarters && focusStrength <= previousStrength + 1)
            focus = threatenedAsset;
        threatenedAsset = focus;
        threatenedPosition = focus != null ? focus.Position : home;
        return hostiles;
    }

    private bool WorkforceViable => economy.Workers >= 2;
    private bool IsDefending => time < defendUntil;
    private bool CanDevelop => economy.Headquarters != null && WorkforceViable && economy.ActiveGatherers > 0;
    private bool InOpening => settings.enableJavaInspiredOpening && (openingAssault < OpeningAssaultCount || time < SecondAssaultAt);

    private void UpdateEconomyGoals()
    {
        bool useOpening = settings.enableJavaInspiredOpening;
        float barracksAt = useOpening ? Mathf.Max(0f, settings.barracksOpportunity) : 0f;
        // A viable smaller workforce can proceed once the opening has had time to reach its full worker goal.
        barracksUnlocked |= economy.Barracks > 0 || (CanDevelop && time >= barracksAt &&
            (economy.Workers >= InitialWorkers || time >= barracksAt + OpeningRetryWindow));
        workerGrowthUnlocked |= CanDevelop && (!useOpening || time >= ProbeAt) &&
            (economy.OperationalBarracks > 0 || !InOpening);

        bool economyAtRisk = economy.Headquarters == null || !WorkforceViable;
        bool supplyConstrained = economy.OccupiedSupply >= economy.MaxSupply || economy.PendingSupply > 0;
        if (!InOpening && CanDevelop && !IsDefending && !supplyConstrained && economy.OperationalBarracks > 0 &&
            !economy.ProductionExitBlocked && military.Living + economy.QueuedCombat >= DesiredAttack + HomeReserve &&
            profile.maximumBarracksTarget >= 2 && Economy.CanFundBarracksExpansion(Mathf.Max(0, profile.barracksExpansionMineralBuffer)))
            secondBarracksUnlocked = true;

        int barracks = !economyAtRisk && barracksUnlocked && Economy.HasBarracksDefinition ? 1 : 0;
        if (barracks > 0 && !IsDefending && secondBarracksUnlocked && profile.maximumBarracksTarget >= 2)
            barracks = 2;

        int workers = workerGrowthUnlocked && !IsDefending && !economyAtRisk
            ? Mathf.Max(InitialWorkers, profile.developedWorkerGoal) : InitialWorkers;
        int army = DesiredAttack + HomeReserve + 3;
        if (InOpening)
        {
            int stage = time < ProbeAt ? 0 : time < Mathf.Clamp(settings.reinforcementTime, FirstAssaultAt, SecondAssaultAt) ? 1 : 2;
            army = OpeningForce(stage).Maximum + HomeReserve + (stage == 2 ? 2 : 0);
        }
        else if (!supplyConstrained)
            army += (economy.OperationalBarracks >= 2 ? 2 : 0) + Mathf.Min(HomeReserve, military.Recovering);

        if (IsDefending)
            army = Mathf.Max(army, defenseTarget + MinimumAttack);
        if (economyAtRisk)
            army = 0;

        int gasWorkers = !economyAtRisk && workerGrowthUnlocked && workers > 2 && Economy.UsesGasForProduction ? 1 : 0;
        goals = new AIEconomyGoals(workers, gasWorkers, barracks, Mathf.Min(army, Mathf.Max(1, profile.maximumArmyTarget)),
            Mathf.Max(0, profile.supplyBuffer), Mathf.Max(0f, profile.infantryToMechRatio), useOpening && time < ProbeAt);
        Economy.SetStrategicGoals(goals);
    }

    private void UpdateAssault(Vector3 home)
    {
        requiredForAttack = 0;
        if ((hadHeadquarters && economy.Headquarters == null) || (economy.Headquarters != null && !WorkforceViable && !InOpening))
            mode = Mode.EmergencyRecovery;
        else if (IsDefending)
            mode = Mode.Defending;
        else if (military.HasAssault)
            mode = Mode.Attacking;
        else if (time < recoverUntil)
            mode = Mode.Recovering;
        else
            mode = InOpening ? Mode.Opening : Mode.Developing;

        if (economy.Headquarters == null)
            attackStatus = economy.HeadquartersCount > 0 ? "Headquarters under construction" :
                economy.CanReplaceHeadquarters ? "Awaiting headquarters placement" : "Headquarters unavailable";
        else if (!WorkforceViable)
            attackStatus = "Restoring workers";
        else if (IsDefending)
            attackStatus = "Local defense takes priority";
        else if (military.HasAssault)
            attackStatus = "Assault already active";
        else if (economy.OperationalBarracks == 0)
            attackStatus = "Awaiting operational military production";
        else if (openingAssault < OpeningAssaultCount && economy.ActiveGatherers == 0)
            attackStatus = "Awaiting an active gathering economy";
        else if (time < resumeManualCancellationAt)
            attackStatus = "Manual cancellation hold";
        else if (time < Mathf.Max(nextAttack, recoverUntil))
            attackStatus = "Assault cooldown or military recovery";
        else if (time < Grace || (openingAssault < OpeningAssaultCount && time < OpeningOpportunity(openingAssault)))
            attackStatus = "Awaiting attack opportunity";
        else
            TryAuthorizeAssault(home);
    }

    private void TryAuthorizeAssault(Vector3 home)
    {
        bool opening = openingAssault < OpeningAssaultCount;
        (int minimum, int maximum) = opening ? OpeningForce(openingAssault) : (MinimumAttack, MaximumAttack);
        if (!opening && time < Mathf.Max(Grace, nextAttack, recoverUntil) + Mathf.Max(0f, profile.forcePreparationDuration))
            minimum = DesiredAttack;

        int available = military.Available - Missions.GetUnassignedReserve(HomeReserve, home);
        availableForAttack = Mathf.Max(0, available);
        requiredForAttack = minimum;
        if (!opening)
            mode = Mode.PreparingAttack;

        if (available < minimum)
        {
            attackStatus = "Building attack strength";
            return;
        }

        if (time < nextLaunchAttempt)
        {
            attackStatus = lastLaunchStatus;
            return;
        }

        float duration = openingAssault == 0 ? Mathf.Max(1f, settings.probeDuration) : 0f;
        switch (Missions.TryLaunchAssault(minimum, maximum, HomeReserve, duration, home))
        {
            case AIMissionDirector.LaunchResult.Started:
                if (opening)
                    openingAssault++;
                mode = Mode.Attacking;
                attackStatus = "Assault authorized and dispatched";
                break;
            case AIMissionDirector.LaunchResult.NoObjective:
                attackStatus = "No valid enemy objective";
                break;
            case AIMissionDirector.LaunchResult.StagingUnavailable:
                attackStatus = "No usable staging route";
                break;
            default:
                attackStatus = "Assault unavailable";
                break;
        }
        lastLaunchStatus = attackStatus;
        nextLaunchAttempt = time + LaunchRetryDelay;
    }

    private (int Minimum, int Maximum) OpeningForce(int stage)
    {
        if (stage == 0)
            return (1, Mathf.Min(2, MaximumAttack));
        if (stage == 1)
        {
            int maximum = Mathf.Min(4, MaximumAttack);
            return (Mathf.Clamp(MinimumAttack - 1, 1, maximum), maximum);
        }
        return (MinimumAttack, Mathf.Clamp(5, MinimumAttack, MaximumAttack));
    }

    private float OpeningOpportunity(int stage) => stage == 0 ? ProbeAt : stage == 1 ? FirstAssaultAt : SecondAssaultAt;

    private void ExpireOpeningOpportunities()
    {
        while (openingAssault < OpeningAssaultCount)
        {
            float expiresAt = OpeningOpportunity(openingAssault) + OpeningRetryWindow;
            if (openingAssault + 1 < OpeningAssaultCount)
                expiresAt = Mathf.Min(expiresAt, OpeningOpportunity(openingAssault + 1));
            if (time < expiresAt)
                return;

            openingAssault++;
        }
    }

    private void ReadMissionOutcome()
    {
        if (cancellationRevision != Missions.ManualCancellationRevision)
        {
            cancellationRevision = Missions.ManualCancellationRevision;
            resumeManualCancellationAt = time + DefenseHold;
            nextAttack = Mathf.Max(nextAttack, time + Cooldown());
        }

        if (completionRevision == Missions.AssaultCompletionRevision)
            return;

        completionRevision = Missions.AssaultCompletionRevision;
        nextAttack = Mathf.Max(nextAttack, time + Cooldown());
        if (Missions.LastAssaultOutcome is AISquadMissionState.Failed or AISquadMissionState.Withdrawn ||
            Missions.LastAssaultCasualties >= 2)
            recoverUntil = Mathf.Max(recoverUntil, time + RecoveryDuration);
    }

    private void ReadCasualties()
    {
        if (time >= lastLossTime + CasualtyWindow)
            recentCasualties = 0;

        int total = controller.Faction.UnitManager.CombatUnitsLost;
        if (total == combatLosses)
            return;

        recentCasualties += total - combatLosses;
        combatLosses = total;
        lastLossTime = time;
        if (recentCasualties >= 2 && (IsDefending || military.Living - military.Recovering < HomeReserve + MinimumAttack))
            recoverUntil = Mathf.Max(recoverUntil, time + RecoveryDuration);
    }

    private float Cooldown()
    {
        if (InOpening)
            return Mathf.Max(1f, settings.openingAttackCooldown);

        float minimum = Mathf.Max(1f, profile.attackCooldownMin);
        uint seed = unchecked((uint)(completionRevision * 31 + cancellationRevision + (int)controller.Faction.ColorType) * 2654435761u);
        return Mathf.Lerp(minimum, Mathf.Max(minimum, profile.attackCooldownMax), (seed & 0xffff) / 65535f);
    }

    private string OpeningStage()
    {
        if (!InOpening)
            return "Continuous strategy";
        if (economy.Headquarters == null)
            return "Establish headquarters";
        if (economy.Workers < InitialWorkers || economy.ActiveGatherers == 0)
            return "Establish working economy";
        if (economy.OperationalBarracks == 0)
            return "Establish military production";
        if (openingAssault == 0)
            return military.Defending < HomeReserve ? "Prepare initial defense" : "Initial probe";
        if (openingAssault == 1)
            return "First organized assault";
        return time < settings.reinforcementTime ? "Military reinforcement" : "Larger assault";
    }

    public string GetStatus()
    {
        float opportunity = openingAssault < OpeningAssaultCount ? OpeningOpportunity(openingAssault) : Grace;
        float earliest = Mathf.Max(Mathf.Max(opportunity, nextLaunchAttempt),
            Mathf.Max(resumeManualCancellationAt, Mathf.Max(nextAttack, recoverUntil)));
        string attackForce = requiredForAttack > 0 ? $" Attack-ready units after reserve {availableForAttack}/{requiredForAttack}." : "";
        return $"{controller.Faction.Name} ({controller.Faction.ColorType}) AI strategy at {time:F0}s. {mode}, {OpeningStage()}. " +
            $"Workers {economy.Workers}/{goals.Workers} ({economy.ActiveGatherers} gathering, +{economy.QueuedWorkers} queued). " +
            $"Barracks {economy.Barracks}/{goals.Barracks} ({economy.OperationalBarracks} operational), " +
            $"{economy.PendingBuildings} buildings pending. Minerals/gas {economy.Minerals}/{economy.Gas}, " +
            $"supply {economy.OccupiedSupply}/{economy.MaxSupply} (+{economy.PendingSupply} pending). " +
            $"Army {military.Living}/{goals.Army} (+{economy.QueuedCombat} queued), {military.Available} available, " +
            $"{military.Assaulting} assaulting, {military.Recovering} unfit, {recentCasualties} recent losses. " +
            $"Defense {Missions.DefenseState?.ToString() ?? "idle"} ({military.Defending}/{defenseTarget}, {military.NearbyDefenders} on site, " +
            $"{military.NearbyAvailable} available nearby), local hostiles {localThreat}. " +
            $"Assault {Missions.AssaultState?.ToString() ?? "idle"}, last result {Missions.LastAssaultOutcome?.ToString() ?? "none"}. " +
            $"{attackStatus}.{attackForce} Timing gate {Mathf.Max(0f, earliest - time):F0}s remaining.";
    }
}
