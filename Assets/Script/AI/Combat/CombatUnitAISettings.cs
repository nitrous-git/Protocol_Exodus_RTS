using System;
using UnityEngine;

[Serializable]
public sealed class CombatUnitAISettings
{
    internal const float StalledTravelTimeout = 3f;
    internal const float ProgressDistance = 0.2f;

    [Range(0f, 1f)] public float retreatHealthThreshold = 0.30f;
    [Min(0.1f)] public float recentDamageWindow = 1.2f;
    [Min(0.05f)] public float decisionInterval = 0.15f;
    [Min(0.1f)] public float threatRadius = 18f;
    [Min(0.1f)] public float pursuitDistance = 28f;
    [Min(0.05f)] public float repathInterval = 0.6f;
    [Min(0.1f)] public float repathDistance = 2f;
    [Min(0f)] public float threatExitPadding = 2f;
    [Min(0.1f)] public float threatReengageDelay = 1.2f;
}
