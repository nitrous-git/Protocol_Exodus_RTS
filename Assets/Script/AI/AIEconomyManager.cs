using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class AIEconomyManager
{
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
    private readonly Dictionary<WorkerUnit, float> deliveryRetry = new();
    private readonly List<WorkerUnit> expiredDeliveryRetries = new();

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
        if (!settings.enableEconomyAI || deltaTime <= 0f)
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
        deliveryRetry.Clear();
    }

    public string GetStatus()
    {
        ReadFactionState();
        Blocked status = hasWaitingProducer ? blocked | Blocked.ProductionExit : blocked;
        return $"{Faction.Name} ({Faction.ColorType}) AI economy {(settings.enableEconomyAI ? "enabled" : "disabled")}. " +
            $"Command Centers {headquartersCount}, Barracks {barracksCount}, Supply Depots {depotCount}, " +
            $"workers {workers.Count} (+{queuedWorkers} queued), Infantry/Mechs including queued {infantryCount}/{mechCount}, " +
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
        int workerTarget = Mathf.Max(0, settings.targetWorkerCount);
        int minimumWorkers = Mathf.Min(2, workerTarget);
        int mineralWorkers = AssignWorkers(minimumWorkers);
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
        if (economyEstablished && barracksCount < settings.targetBarracksCount &&
            TryConstruct(barracksDefinition, anchor))
            return;

        int armyTarget = Mathf.Max(0, settings.targetArmySize);
        float ratio = Mathf.Max(0f, settings.infantryToMechRatio);
        float infantryShare = ratio / (1f + ratio);
        int infantryTarget = Mathf.RoundToInt(armyTarget * infantryShare);
        int missingInfantry = Mathf.Max(0, infantryTarget - infantryCount);
        int missingMechs = Mathf.Max(0, armyTarget - infantryTarget - mechCount);
        int armySlots = Mathf.Max(0, armyTarget - infantryCount - mechCount);
        UnitDefinition first = missingInfantry > 0 &&
            (missingMechs == 0 || infantryCount < Mathf.RoundToInt((infantryCount + mechCount + 1) * infantryShare))
            ? infantryDefinition : mechDefinition;
        UnitDefinition second = first == infantryDefinition ? mechDefinition : infantryDefinition;
        bool needsArmy = armySlots > 0 && (barracksCount > 0 || settings.targetBarracksCount > 0);
        if (needsArmy && first == null)
            blocked |= Blocked.MissingDefinition;
        int armySupply = needsArmy && first != null ? first.Cost.Supply : 0;

        if ((workerSupply > AvailableSupply || (barracksProducers.Count > 0 && armySupply > AvailableSupply)) &&
            EnsureSupply(anchor))
            return;

        if (missingWorkers > 0 && TryQueue(headquartersProducers, workerDefinition))
        {
            missingWorkers--;
            queuedWorkers++;
        }

        if (needsArmy && barracksCount > 0)
        {
            // Keep enough stock for a headquarters and one replacement worker before spending on the army.
            Cost headquartersReserve = headquartersDefinition != null ? headquartersDefinition.Cost : Cost.Zero;
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
        int desiredSupply = Mathf.Min(remainingSupply, nextSupply + Mathf.Max(0, settings.supplyBuffer));
        if (desiredSupply > AvailableSupply + pendingSupply)
            EnsureSupply(anchor);
    }

    private int AvailableSupply => Resources.MaxSupply - Faction.UnitManager.OccupiedPopulation;

    private void ReadFactionState()
    {
        workers.Clear();
        headquartersProducers.Clear();
        barracksProducers.Clear();
        headquarters = null;
        headquartersCount = barracksCount = depotCount = pendingSupply = queuedWorkers = infantryCount = mechCount = 0;
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

            if (building.IsUnderConstruction && building.SupplyProvider != null)
                pendingSupply += building.SupplyProvider.SupplyProvided;

            UnitProductionComponent producer = building.UnitProduction;
            if (!building.IsOperational || producer == null)
                continue;

            if (building.Definition.Type == BuildingType.CommandCenter)
                headquartersProducers.Add(producer);
            else if (building.Definition.Type == BuildingType.Barracks)
                barracksProducers.Add(producer);

            hasWaitingProducer |= producer.IsWaitingForSpawn;
            foreach (UnitDefinition order in producer.ProductionQueue)
                CountOrder(order);
        }
    }

    private void CountOrder(UnitDefinition definition)
    {
        switch (definition?.Type)
        {
            case UnitType.Worker: queuedWorkers++; break;
            case UnitType.Combat: infantryCount++; break;
            case UnitType.Mech: mechCount++; break;
        }
    }

    private bool EnsureSupply(Vector3 anchor)
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

    private int AssignWorkers(int minimumMineralWorkers)
    {
        ExpireWorkerRetries();
        nodeLoads.Clear();
        int mineralWorkers = 0;
        int gasWorkers = 0;
        foreach (WorkerUnit worker in workers)
        {
            if (worker.CurrentCommand is not (CommandType.Gather or CommandType.Deliver))
                continue;

            if (headquarters == null)
            {
                worker.IssueCommand(CommandType.Idle, CommandContext.None());
                continue;
            }

            ResourceNode node = worker.ResourceComponent.AssignedNode;
            if (IsAvailable(node))
                AddWorkerLoad(node, ref mineralWorkers, ref gasWorkers);
        }

        if (headquarters == null)
            return 0;

        int gasTarget = Mathf.Clamp(settings.targetGasWorkerCount, 0, Mathf.Max(0, settings.targetWorkerCount));
        foreach (WorkerUnit worker in workers)
        {
            if (worker.CurrentCommand != CommandType.Idle || !worker.CanReceiveCommands || worker.Motor == null)
                continue;

            WorkerResourceComponent cargo = worker.ResourceComponent;
            if (cargo.HasCargo)
            {
                if (deliveryRetry.ContainsKey(worker))
                {
                    blocked |= Blocked.Delivery;
                    continue;
                }

                worker.IssueCommand(CommandType.Deliver, CommandContext.None());
                if (worker.CurrentCommand == CommandType.Idle)
                {
                    deliveryRetry[worker] = time + RetryInterval;
                    blocked |= Blocked.Delivery;
                }
                else if (IsAvailable(cargo.AssignedNode))
                    AddWorkerLoad(cargo.AssignedNode, ref mineralWorkers, ref gasWorkers);
                continue;
            }

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

        expiredDeliveryRetries.Clear();
        foreach (var retry in deliveryRetry)
        {
            if (time >= retry.Value || retry.Key == null || !retry.Key.IsAlive)
                expiredDeliveryRetries.Add(retry.Key);
        }
        foreach (WorkerUnit worker in expiredDeliveryRetries)
            deliveryRetry.Remove(worker);
    }
}
