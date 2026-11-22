using UnityEngine;

public sealed class AIFactionController : IFactionController
{
    public Faction Faction { get; private set; }
    public Vector3? BaseAnchor { get; private set; }
    public AIWaveSettings WaveSettings { get; }
    public AIMissionDirector MissionDirector { get; private set; }
    public AIEconomySettings EconomySettings { get; }
    public AIEconomyManager EconomyManager { get; private set; }
    public AIFactionStrategy Strategy { get; private set; }
    internal bool EconomyEnabled => Strategy != null || EconomySettings.enableEconomyAI;
    private readonly AIStrategySettings strategySettings;

    public AIFactionController(AIWaveSettings waveSettings = null, AIEconomySettings economySettings = null,
        AIStrategySettings strategySettings = null)
    {
        WaveSettings = waveSettings ?? new AIWaveSettings();
        EconomySettings = economySettings ?? new AIEconomySettings();
        this.strategySettings = strategySettings;
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
        if (strategySettings != null)
            Strategy = new AIFactionStrategy(this, gameContext, strategySettings,
                strategySettings.mediumProfile ?? new AIStrategyProfile());
    }

    public void Tick(float deltaTime)
    {
        MissionDirector?.Tick(deltaTime);
        Strategy?.Tick(deltaTime);
        EconomyManager?.Tick(deltaTime);
    }
}
