using System;
using System.Collections.Generic;
using UnityEngine;

internal readonly struct AIEconomyGoals
{
    public readonly int Workers, GasWorkers, Barracks, Army, SupplyBuffer;
    public readonly float InfantryToMechRatio;
    public readonly bool InfantryFirst;

    public AIEconomyGoals(int workers, int gasWorkers, int barracks, int army, int supplyBuffer,
        float infantryToMechRatio, bool infantryFirst)
    {
        Workers = workers;
        GasWorkers = gasWorkers;
        Barracks = barracks;
        Army = army;
        SupplyBuffer = supplyBuffer;
        InfantryToMechRatio = infantryToMechRatio;
        InfantryFirst = infantryFirst;
    }
}

public sealed class AIEconomyManager
{
    internal struct Situation
    {
        public BuildingBase Headquarters;
        public int Minerals, Gas, OccupiedSupply, MaxSupply, PendingSupply;
        public int Workers, ActiveGatherers, HeadquartersCount, Barracks, OperationalBarracks;
        public int PendingBuildings, QueuedWorkers, QueuedCombat;
        public bool CanReplaceHeadquarters, ProductionExitBlocked;
    }

    [Flags]
    private enum Blocked
    {
        None = 0, NoBaseAnchor = 1, HeadquartersConstruction = 2, WorkersDeploying = 4,
        NoGatheringTarget = 8, Resources = 16, Supply = 32, MissingDefinition = 64,
        Placement = 128, NoProducer = 256, QueueLimit = 512, ProductionExit = 1024, Delivery = 2048
    }

    private readonly AIFactionController controller;
    private readonly GameContext context;
    private readonly AIEconomySettings settings;
    private readonly AIBuildingPlacement placement;
    private readonly BuildingDefinition headquartersDefinition;
    private readonly BuildingDefinition barracksDefinition;
    private readonly BuildingDefinition depotDefinition;
    private readonly UnitDefinition workerDefinition;
    private readonly UnitDefinition infantryDefinition;
    private readonly UnitDefinition mechDefinition;
    private readonly Dictionary<BuildingType, float> constructionRetry = new();
    private readonly List<UnitProductionComponent> headquartersProducers = new();
    private readonly List<UnitProductionComponent> barracksProducers = new();
    private readonly List<WorkerUnit> workers = new();
    private readonly Dictionary<ResourceNode, int> nodeLoads = new();
    private readonly Dictionary<(WorkerUnit Worker, ResourceNode Node), float> gatheringRetry = new();
    private readonly List<(WorkerUnit Worker, ResourceNode Node)> expiredGatheringRetries = new();

    private Faction Faction => controller.Faction;
    private ResourceManager Resources => Faction.ResourceManager;
    private float RetryInterval => Mathf.Max(1f, settings.failedActionRetryInterval);
    private float time;
    private float nextDecision;
    private Blocked blocked;
    private bool economyEstablished;
    private BuildingBase headquarters;
    private int headquartersCount;
    private int barracksCount;
    private int depotCount;
    private int pendingSupply;
    private bool hasWaitingProducer;
    private int queuedWorkers;
    private int infantryCount;
    private int mechCount;
    private int flamerCount;
    private int queuedCombat;
    private int pendingBuildings;
    private AIEconomyGoals? strategicGoals;

    internal bool HasBarracksDefinition => barracksDefinition != null && barracksDefinition.Prefab != null;
    internal bool UsesGasForProduction => (infantryDefinition?.Cost.Gas ?? 0) > 0 || (mechDefinition?.Cost.Gas ?? 0) > 0;

    internal void SetStrategicGoals(AIEconomyGoals goals) => strategicGoals = goals;

    internal Situation Observe()
    {
        ReadFactionState();
        int activeGatherers = 0;
        foreach (WorkerUnit worker in workers)
        {
            WorkerResourceComponent cargo = worker.ResourceComponent;
            if (!worker.IsWaitingForLogistics &&
                ((worker.CurrentCommand == CommandType.Gather && IsAvailable(cargo.AssignedNode)) ||
                 (worker.CurrentCommand == CommandType.Deliver && cargo.HasCargo)))
                activeGatherers++;
        }

        return new Situation
        {
            Headquarters = headquarters,
            HeadquartersCount = headquartersCount,
            Workers = workers.Count,
            ActiveGatherers = activeGatherers,
            Barracks = barracksCount,
            OperationalBarracks = barracksProducers.Count,
            PendingBuildings = pendingBuildings,
            QueuedWorkers = queuedWorkers,
            QueuedCombat = queuedCombat,
            Minerals = Resources.Minerals,
            Gas = Resources.Gas,
            OccupiedSupply = Faction.UnitManager.OccupiedPopulation,
            MaxSupply = Resources.MaxSupply,
            PendingSupply = pendingSupply,
            CanReplaceHeadquarters = headquartersDefinition != null && headquartersDefinition.Prefab != null &&
                Resources.CanAffordResources(headquartersDefinition.Cost),
            ProductionExitBlocked = hasWaitingProducer
        };
    }

    internal bool CanFundBarracksExpansion(int mineralBuffer)
    {
        if (!HasBarracksDefinition || headquartersDefinition == null)
            return false;

        Cost barracks = barracksDefinition.Cost;
        Cost headquartersCost = headquartersDefinition.Cost;
        Cost worker = workerDefinition != null ? workerDefinition.Cost : Cost.Zero;
        return Resources.Minerals >= barracks.Minerals + headquartersCost.Minerals + worker.Minerals + mineralBuffer &&
            Resources.Gas >= barracks.Gas + headquartersCost.Gas + worker.Gas;
    }

    internal AIEconomyManager(AIFactionController controller, GameContext context, AIEconomySettings settings)
    {
        this.controller = controller;
        this.context = context;
        this.settings = settings;
        placement = new AIBuildingPlacement(Faction.BuildingManager, context);
        FactionDefinition roster = Faction.Definition;
        headquartersDefinition = roster.GetBuildingDefinition(BuildingType.CommandCenter);
        barracksDefinition = roster.GetBuildingDefinition(BuildingType.Barracks);
        depotDefinition = roster.GetBuildingDefinition(BuildingType.SupplyDepot);
        workerDefinition = roster.GetUnitDefinition(UnitType.Worker);
        infantryDefinition = roster.GetUnitDefinition(UnitType.Combat);
        mechDefinition = roster.GetUnitDefinition(UnitType.Mech);
    }

    public void Tick(float deltaTime)
    {
        if (!controller.EconomyEnabled || deltaTime <= 0f)
            return;

        time += deltaTime;
        if (time < nextDecision)
            return;

        nextDecision = time + Mathf.Max(0.5f, settings.decisionInterval);
        Evaluate();
    }

    public void RequestReevaluation()
    {
        nextDecision = time;
        constructionRetry.Clear();
        gatheringRetry.Clear();
    }

    public string GetStatus()
    {
        ReadFactionState();
        Blocked status = hasWaitingProducer ? blocked | Blocked.ProductionExit : blocked;
        return $"{Faction.Name} ({Faction.ColorType}) AI economy {(controller.EconomyEnabled ? "enabled" : "disabled")}. " +
            $"Command Centers {headquartersCount}, Barracks {barracksCount}, Supply Depots {depotCount}, " +
            $"workers {workers.Count} (+{queuedWorkers} queued), Infantry/Mechs/Flamers including queued {infantryCount}/{mechCount}/{flamerCount}, " +
            $"supply {Faction.UnitManager.OccupiedPopulation}/{Resources.MaxSupply} (+{pendingSupply} pending), " +
            $"minerals/gas {Resources.Minerals}/{Resources.Gas}. Waiting on: {status}.";
    }

    private void Evaluate()
    {
        blocked = Blocked.None;
        if (controller.BaseAnchor is not Vector3 anchor)
        {
            blocked = Blocked.NoBaseAnchor;
            return;
        }

        ReadFactionState();
        int workerTarget = Mathf.Max(0, strategicGoals?.Workers ?? settings.targetWorkerCount);
        int barracksTarget = Mathf.Max(0, strategicGoals?.Barracks ?? settings.targetBarracksCount);
        int minimumWorkers = Mathf.Min(2, workerTarget);
        int gasTarget = Mathf.Clamp(strategicGoals?.GasWorkers ?? settings.targetGasWorkerCount, 0, workerTarget);
        int mineralWorkers = AssignWorkers(minimumWorkers, gasTarget);
        if (headquarters == null)
        {
            if (headquartersCount == 0)
                TryConstruct(headquartersDefinition, anchor);
            else
                blocked |= Blocked.HeadquartersConstruction;
            return;
        }

        int missingWorkers = Mathf.Max(0, workerTarget - workers.Count - queuedWorkers);
        int workerSupply = workerDefinition != null && missingWorkers > 0 ? workerDefinition.Cost.Supply : 0;
        if (workers.Count < minimumWorkers)
        {
            if (workerSupply > AvailableSupply)
            {
                EnsureSupply(anchor);
                return;
            }

            if (workers.Count + queuedWorkers < minimumWorkers)
                TryQueue(headquartersProducers, workerDefinition);
            blocked |= Blocked.WorkersDeploying;
            return;
        }

        economyEstablished |= minimumWorkers == 0 || mineralWorkers > 0;
        if (economyEstablished && barracksCount < barracksTarget)
        {
            if (strategicGoals != null && !CanFundBarracksExpansion(0))
                blocked |= Blocked.Resources;
            else if (TryConstruct(barracksDefinition, anchor))
                return;
        }

        int armyTarget = Mathf.Max(0, strategicGoals?.Army ?? settings.targetArmySize);
        float ratio = Mathf.Max(0f, strategicGoals?.InfantryToMechRatio ?? settings.infantryToMechRatio);
        float infantryShare = ratio / (1f + ratio);
        if (strategicGoals is AIEconomyGoals goals && barracksProducers.Count > 0)
        {
            if (!CanTrain(barracksProducers, infantryDefinition))
                infantryShare = 0f;
            else if (goals.InfantryFirst || !CanTrain(barracksProducers, mechDefinition))
                infantryShare = 1f;
        }

        int infantryTarget = Mathf.RoundToInt(armyTarget * infantryShare);
        int missingInfantry = Mathf.Max(0, infantryTarget - infantryCount);
        int missingMechs = Mathf.Max(0, armyTarget - infantryTarget - mechCount);
        int armySlots = Mathf.Max(0, armyTarget - infantryCount - mechCount - flamerCount);
        UnitDefinition first = missingInfantry > 0 &&
            (missingMechs == 0 || infantryCount < Mathf.RoundToInt((infantryCount + mechCount + 1) * infantryShare))
            ? infantryDefinition : mechDefinition;
        UnitDefinition second = first == infantryDefinition ? mechDefinition : infantryDefinition;
        bool needsArmy = armySlots > 0 && (barracksCount > 0 || barracksTarget > 0);
        if (strategicGoals != null && barracksProducers.Count > 0 && !CanTrain(barracksProducers, first))
            needsArmy = false;
        if (needsArmy && first == null)
            blocked |= Blocked.MissingDefinition;
        int armySupply = needsArmy && first != null ? first.Cost.Supply : 0;

        Cost headquartersReserve = headquartersDefinition != null ? headquartersDefinition.Cost : Cost.Zero;
        bool preserveHeadquarters = strategicGoals != null;
        if ((workerSupply > AvailableSupply || (barracksProducers.Count > 0 && armySupply > AvailableSupply)) &&
            EnsureSupply(anchor, preserveHeadquarters))
            return;

        Cost growthReserve = strategicGoals != null && workers.Count >= minimumWorkers ? headquartersReserve : Cost.Zero;
        if (missingWorkers > 0 && TryQueue(headquartersProducers, workerDefinition, growthReserve))
        {
            missingWorkers--;
            queuedWorkers++;
        }

        if (needsArmy && barracksCount > 0)
        {
            // Keep enough stock for a headquarters and one replacement worker before spending on the army.
            Cost workerReserve = workerTarget > 0 && workerDefinition != null ? workerDefinition.Cost : Cost.Zero;
            UnitDefinition queued = null;
            if (first != null && TryQueue(barracksProducers, first, headquartersReserve, workerReserve))
                queued = first;
            else if (second != null && (second == infantryDefinition ? missingInfantry : missingMechs) > 0 &&
                TryQueue(barracksProducers, second, headquartersReserve, workerReserve))
                queued = second;

            if (queued != null)
            {
                if (queued == infantryDefinition)
                {
                    infantryCount++;
                    missingInfantry--;
                }
                else
                {
                    mechCount++;
                    missingMechs--;
                }
                armySlots--;
            }
        }

        int remainingSupply = missingWorkers * (workerDefinition != null ? workerDefinition.Cost.Supply : 0);
        if (needsArmy && armySlots > 0)
        {
            remainingSupply += Mathf.Min(armySlots, missingInfantry) * (infantryDefinition != null ? infantryDefinition.Cost.Supply : 0);
            remainingSupply += Mathf.Min(armySlots, missingMechs) * (mechDefinition != null ? mechDefinition.Cost.Supply : 0);
        }

        int nextSupply = Mathf.Max(missingWorkers > 0 ? workerSupply : 0, armySlots > 0 ? armySupply : 0);
        int desiredSupply = Mathf.Min(remainingSupply, nextSupply + Mathf.Max(0, strategicGoals?.SupplyBuffer ?? settings.supplyBuffer));
        if (desiredSupply > AvailableSupply + pendingSupply)
            EnsureSupply(anchor, preserveHeadquarters);
    }

    private static bool CanTrain(List<UnitProductionComponent> producers, UnitDefinition unit)
    {
        if (unit == null || unit.Prefab == null)
            return false;
        foreach (UnitProductionComponent producer in producers)
        {
            if (producer.Supports(unit))
                return true;
        }
        return false;
    }

    private int AvailableSupply => Resources.MaxSupply - Faction.UnitManager.OccupiedPopulation;

    private void ReadFactionState()
    {
        workers.Clear();
        headquartersProducers.Clear();
        barracksProducers.Clear();
        headquarters = null;
        headquartersCount = barracksCount = depotCount = pendingSupply = queuedWorkers = infantryCount = mechCount = flamerCount = 0;
        pendingBuildings = queuedCombat = 0;
        hasWaitingProducer = false;

        foreach (UnitBase unit in Faction.UnitManager.UnitList)
        {
            if (unit == null || !unit.IsAlive)
                continue;

            if (unit is WorkerUnit worker)
                workers.Add(worker);
            else if (unit is CombatUnit)
                CountOrder(unit.Definition);
        }

        foreach (BuildingBase building in Faction.BuildingManager.BuildingList)
        {
            if (building == null || !building.IsAlive)
                continue;

            if (building.Definition.Type == BuildingType.CommandCenter)
            {
                headquartersCount++;
                if (building.IsOperational && building.Headquarters != null)
                    headquarters ??= building;
            }
            else if (building.Definition.Type == BuildingType.Barracks)
                barracksCount++;
            else if (building.Definition.Type == BuildingType.SupplyDepot)
                depotCount++;

            if (building.IsUnderConstruction)
            {
                pendingBuildings++;
                if (building.SupplyProvider != null)
                    pendingSupply += building.SupplyProvider.SupplyProvided;
            }

            UnitProductionComponent producer = building.UnitProduction;
            if (!building.IsOperational || producer == null)
                continue;

            if (building.Definition.Type == BuildingType.CommandCenter)
                headquartersProducers.Add(producer);
            else if (building.Definition.Type == BuildingType.Barracks)
                barracksProducers.Add(producer);

            hasWaitingProducer |= producer.IsWaitingForSpawn;
            foreach (UnitDefinition order in producer.ProductionQueue)
            {
                CountOrder(order);
                if (order.Type is UnitType.Combat or UnitType.Mech or UnitType.Flamer)
                    queuedCombat++;
            }
        }
    }

    private void CountOrder(UnitDefinition definition)
    {
        switch (definition?.Type)
        {
            case UnitType.Worker: queuedWorkers++; break;
            case UnitType.Combat: infantryCount++; break;
            case UnitType.Mech: mechCount++; break;
            case UnitType.Flamer: flamerCount++; break;
        }
    }

    private bool EnsureSupply(Vector3 anchor, bool preserveHeadquarters = false)
    {
        blocked |= Blocked.Supply;
        if (pendingSupply > 0)
            return false;

        if (depotDefinition == null || depotDefinition.Prefab == null ||
            !depotDefinition.Prefab.TryGetComponent(out SupplyProviderComponent provider) || provider.SupplyProvided <= 0)
        {
            blocked |= Blocked.MissingDefinition;
            return false;
        }

        if (preserveHeadquarters && headquartersDefinition != null &&
            (Resources.Minerals < headquartersDefinition.Cost.Minerals + depotDefinition.Cost.Minerals ||
             Resources.Gas < headquartersDefinition.Cost.Gas + depotDefinition.Cost.Gas))
        {
            blocked |= Blocked.Resources;
            return false;
        }

        return TryConstruct(depotDefinition, anchor);
    }

    private bool TryConstruct(BuildingDefinition definition, Vector3 anchor)
    {
        if (definition == null || definition.Prefab == null)
        {
            blocked |= Blocked.MissingDefinition;
            return false;
        }

        if (!Resources.CanAffordResources(definition.Cost))
        {
            blocked |= Blocked.Resources;
            return false;
        }

        if (constructionRetry.TryGetValue(definition.Type, out float retryAt) && time < retryAt)
        {
            blocked |= Blocked.Placement;
            return false;
        }

        if (placement.TryFind(definition, anchor, Mathf.Clamp(settings.buildingSearchRadius, 4, 40),
                Mathf.Clamp(settings.buildingSpacing, 1, AIEconomySettings.MaximumBuildingSpacing),
                out GridCoord origin, out int rotation) &&
            Faction.BuildingManager.Construct(definition, origin, rotation) != null)
            return true;

        constructionRetry[definition.Type] = time + RetryInterval;
        blocked |= Blocked.Placement;
        return false;
    }

    private bool TryQueue(List<UnitProductionComponent> producers, UnitDefinition definition,
        Cost headquartersReserve = default, Cost workerReserve = default)
    {
        if (definition == null || definition.Prefab == null)
        {
            blocked |= Blocked.MissingDefinition;
            return false;
        }

        if (!Faction.UnitManager.CanReservePopulation(definition))
        {
            blocked |= Blocked.Supply;
            return false;
        }

        Cost cost = definition.Cost;
        if (Resources.Minerals < cost.Minerals + headquartersReserve.Minerals + workerReserve.Minerals ||
            Resources.Gas < cost.Gas + headquartersReserve.Gas + workerReserve.Gas)
        {
            blocked |= Blocked.Resources;
            return false;
        }

        Blocked producerStatus = Blocked.NoProducer;
        int queueLimit = Mathf.Clamp(settings.maximumQueuedOrdersPerProducer, 1, UnitProductionComponent.MaximumQueueSize);
        foreach (UnitProductionComponent producer in producers)
        {
            if (!producer.Supports(definition))
                continue;

            if (producer.IsWaitingForSpawn)
                producerStatus = Blocked.ProductionExit;
            else if (producer.QueueCount >= queueLimit)
                producerStatus = Blocked.QueueLimit;
            else if (producer.CanEnqueue(definition) && producer.TryEnqueue(definition))
                return true;
        }

        blocked |= producerStatus;
        return false;
    }

    private int AssignWorkers(int minimumMineralWorkers, int gasTarget)
    {
        ExpireWorkerRetries();
        nodeLoads.Clear();
        int mineralWorkers = 0;
        int gasWorkers = 0;
        foreach (WorkerUnit worker in workers)
        {
            if (worker.CurrentCommand is not (CommandType.Gather or CommandType.Deliver))
                continue;

            if (worker.CurrentCommand == CommandType.Deliver && worker.IsWaitingForLogistics)
                blocked |= Blocked.Delivery;

            ResourceNode node = worker.ResourceComponent.AssignedNode;
            if (IsAvailable(node))
                AddWorkerLoad(node, ref mineralWorkers, ref gasWorkers);
        }

        foreach (WorkerUnit worker in workers)
        {
            if (worker.CurrentCommand != CommandType.Idle || !worker.CanReceiveCommands || worker.Motor == null)
                continue;

            WorkerResourceComponent cargo = worker.ResourceComponent;
            if (cargo.HasCargo)
            {
                worker.IssueCommand(CommandType.Deliver, CommandContext.None());
                if (worker.IsWaitingForLogistics)
                    blocked |= Blocked.Delivery;
                if (IsAvailable(cargo.AssignedNode))
                    AddWorkerLoad(cargo.AssignedNode, ref mineralWorkers, ref gasWorkers);
                continue;
            }

            if (headquarters == null)
                continue;

            if (IsAvailable(cargo.AssignedNode))
            {
                // A gathering order that returned to idle with a live node could not reach it.
                gatheringRetry[(worker, cargo.AssignedNode)] = time + RetryInterval;
                cargo.ClearAssignment();
            }

            ResourceType preferred = mineralWorkers >= minimumMineralWorkers && gasWorkers < gasTarget
                ? ResourceType.Gas : ResourceType.Minerals;
            ResourceNode target = FindGatheringTarget(worker, preferred);
            if (target == null && (preferred == ResourceType.Gas || gasWorkers < gasTarget))
                target = FindGatheringTarget(worker, preferred == ResourceType.Gas ? ResourceType.Minerals : ResourceType.Gas);

            if (target == null)
            {
                blocked |= Blocked.NoGatheringTarget;
                continue;
            }

            worker.IssueCommand(CommandType.Gather, CommandContext.Gather(target));
            if (worker.CurrentCommand == CommandType.Gather)
                AddWorkerLoad(target, ref mineralWorkers, ref gasWorkers);
            else
            {
                gatheringRetry[(worker, target)] = time + RetryInterval;
                cargo.ClearAssignment();
                blocked |= Blocked.NoGatheringTarget;
            }
        }

        return mineralWorkers;
    }

    private ResourceNode FindGatheringTarget(WorkerUnit worker, ResourceType type)
    {
        ResourceNode best = null;
        float bestScore = float.PositiveInfinity;
        float loadPenalty = 5f * Faction.BuildingManager.TerrainGrid.CellSize;
        foreach (ResourceNode node in context.ResourceNodeRepository.ResourceNodeList)
        {
            if (!IsAvailable(node) || node.ResourceType != type || gatheringRetry.ContainsKey((worker, node)))
                continue;

            nodeLoads.TryGetValue(node, out int load);
            float score = (node.Position - headquarters.Position).magnitude +
                0.25f * (node.Position - worker.Position).magnitude + load * loadPenalty;
            if (score < bestScore)
            {
                bestScore = score;
                best = node;
            }
        }

        return best;
    }

    private static bool IsAvailable(ResourceNode node) => node != null && node.IsInitialized && !node.IsDepleted;

    private void AddWorkerLoad(ResourceNode node, ref int mineralWorkers, ref int gasWorkers)
    {
        nodeLoads.TryGetValue(node, out int load);
        nodeLoads[node] = load + 1;
        if (node.ResourceType == ResourceType.Minerals)
            mineralWorkers++;
        else if (node.ResourceType == ResourceType.Gas)
            gasWorkers++;
    }

    private void ExpireWorkerRetries()
    {
        expiredGatheringRetries.Clear();
        foreach (var retry in gatheringRetry)
        {
            if (time >= retry.Value || retry.Key.Worker == null || !retry.Key.Worker.IsAlive || !IsAvailable(retry.Key.Node))
                expiredGatheringRetries.Add(retry.Key);
        }
        foreach (var key in expiredGatheringRetries)
            gatheringRetry.Remove(key);
    }
}
