using System;
using UnityEngine;

[Serializable]
public sealed class AIStrategySettings
{
    [Tooltip("Enable A4 in Development Sandbox. Standard Match always uses A4.")]
    public bool enableStrategyAI;
    [Min(0.5f)] public float decisionInterval = 1.5f;

    [Header("Shared Opening")]
    public bool enableJavaInspiredOpening = true;
    [Min(2)] public int initialWorkerGoal = 3;
    [Min(0f)] public float barracksOpportunity = 24f;
    [Min(0f)] public float baseBuildingGrace = 60f;
    [Min(0f)] public float openingProbeTime = 70f;
    [Min(0f)] public float firstAssaultTime = 120f;
    [Min(0f)] public float reinforcementTime = 145f;
    [Min(0f)] public float secondAssaultTime = 180f;
    [Min(1f)] public float openingRetryWindow = 30f;
    [Min(1f)] public float probeDuration = 35f;
    [Min(1f)] public float openingAttackCooldown = 20f;
    [Min(1f)] public float openingRecoveryDuration = 40f;

    public AIStrategyProfile mediumProfile = new AIStrategyProfile();
}

[Serializable]
public sealed class AIStrategyProfile
{
    [Min(2)] public int developedWorkerGoal = 5;
    [Min(0)] public int homeDefenseReserve = 2;
    [Min(1)] public int maximumDefenders = 6;
    [Min(1f)] public float defenseThreatRadius = 20f;
    [Min(1f)] public float defenseStandDownDelay = 8f;
    [Min(1)] public int minimumAttackForce = 4;
    [Min(1)] public int desiredAttackForce = 7;
    [Min(1)] public int maximumAttackForce = 10;
    [Min(0f)] public float forcePreparationDuration = 7f;
    [Min(1f)] public float attackCooldownMin = 9f;
    [Min(1f)] public float attackCooldownMax = 15f;
    [Min(1f)] public float recoveryDuration = 30f;
    [Min(1)] public int maximumArmyTarget = 16;
    [Range(1, 2)] public int maximumBarracksTarget = 2;
    [Min(0)] public int barracksExpansionMineralBuffer = 150;
    [Min(0f)] public float infantryToMechRatio = 2f;
    [Min(0)] public int supplyBuffer = 3;
}
