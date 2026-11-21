using UnityEngine;

public sealed class AIFactionController : IFactionController
{
    public Faction Faction { get; private set; }
    public Vector3? BaseAnchor { get; private set; }
    public AIWaveSettings WaveSettings { get; }
    public AIMissionDirector MissionDirector { get; private set; }

    public AIFactionController(AIWaveSettings waveSettings = null)
    {
        WaveSettings = waveSettings ?? new AIWaveSettings();
    }

    internal void SetBaseAnchor(Vector3 position)
    {
        BaseAnchor = position;
    }

    public void Initialize(Faction faction, GameContext gameContext)
    {
        Faction = faction;
        MissionDirector = new AIMissionDirector(this, gameContext, WaveSettings);
    }

    public void Tick(float deltaTime)
    {
        MissionDirector?.Tick(deltaTime);
    }
}
