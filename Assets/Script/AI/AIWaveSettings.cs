using System;
using UnityEngine;

[Serializable]
public sealed class AIWaveSettings
{
    public bool automaticWaves;
    [Min(0f)] public float initialDelay = 20f;
    [Min(0.5f)] public float waveCooldown = 25f;
    [Min(1)] public int minimumParticipants = 3;
    [Min(1)] public int maximumParticipants = 8;
    [Min(0)] public int homeDefenders = 2;
    [Min(0f)] public float stagingDistance = 10f;
    [Range(0.5f, 1f)] public float assemblyReadiness = 0.75f;
    [Min(1f)] public float assemblyTimeout = 15f;
    [Tooltip("Fraction of the original squad lost or no longer fit for the operation before it withdraws.")]
    [Range(0.1f, 1f)] public float withdrawalThreshold = 0.5f;
    [Min(1f)] public float defenseGuardRadius = 12f;
}
