using UnityEngine;

public sealed class AIFactionController : IFactionController
{
    public Faction Faction { get; private set; }
    public Vector3? BaseAnchor { get; private set; }
    public AIWaveSettings WaveSettings { get; }
    public AIMissionDirector MissionDirector { get; private set; }
    public AIEconomySettings EconomySettings { get; }
    public AIEconomyManager EconomyManager { get; private set; }
    internal bool EconomyEnabled => EconomySettings.enableEconomyAI;

    public AIFactionController(AIWaveSettings waveSettings = null, AIEconomySettings economySettings = null)
    {
        WaveSettings = waveSettings ?? new AIWaveSettings();
        EconomySettings = economySettings ?? new AIEconomySettings();
    }

    internal void SetBaseAnchor(Vector3 position)
    {
        BaseAnchor = position;
    }

    public void Initialize(Faction faction, GameContext gameContext)
    {
        Faction = faction;
        MissionDirector = new AIMissionDirector(this, gameContext, WaveSettings);
        EconomyManager = new AIEconomyManager(this, gameContext, EconomySettings);
    }

    public void Tick(float deltaTime)
    {
        MissionDirector?.Tick(deltaTime);
        EconomyManager?.Tick(deltaTime);
    }
}
