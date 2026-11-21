using System;
using UnityEngine;

[Serializable]
public sealed class AIEconomySettings
{
    internal const int MaximumBuildingSpacing = 16;

    public bool enableEconomyAI = true;
    [Min(0.5f)] public float decisionInterval = 0.75f;
    [Min(0)] public int targetWorkerCount = 5;
    [Min(0)] public int targetGasWorkerCount;
    [Min(0)] public int targetBarracksCount = 1;
    [Min(0)] public int targetArmySize = 12;
    [Min(0f)] public float infantryToMechRatio = 2f;
    [Min(0)] public int supplyBuffer = 3;
    [Range(1, UnitProductionComponent.MaximumQueueSize)] public int maximumQueuedOrdersPerProducer = 2;
    [Range(4, 40), Tooltip("Maximum placement search radius from the base anchor, in grid cells.")]
    public int buildingSearchRadius = 24;
    [Range(1, MaximumBuildingSpacing)]
    [Tooltip("Clear cells kept between buildings. Resource nodes get at least two cells. Wider gaps may require a larger building search radius.")]
    public int buildingSpacing = 4;
    [Min(1f)] public float failedActionRetryInterval = 5f;
}
