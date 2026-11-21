using System.Collections.Generic;
using UnityEngine;

public sealed class GameLog
{
    public const int DefaultCapacity = 8;
    public const float DefaultDisplayDuration = 3f;

    private readonly GameContext gameContext;
    private readonly int capacity;
    private readonly float displayDuration;
    private readonly List<(string Message, double ExpiresAt)> entries;

    public int Count => entries.Count;
    public int Version { get; private set; }
    public string this[int index] => entries[index].Message;

    internal GameLog(GameContext gameContext, int capacity, float displayDuration)
    {
        this.gameContext = gameContext;
        this.capacity = Mathf.Max(1, capacity);
        this.displayDuration = Mathf.Max(0.01f, displayDuration);
        entries = new(this.capacity);
    }

    public void Log(string message, Faction faction)
    {
        if (faction == null || faction != gameContext.PlayerFaction || string.IsNullOrWhiteSpace(message))
            return;

        double now = Time.realtimeSinceStartupAsDouble;
        RemoveExpired(now);

        if (entries.Count == capacity)
            entries.RemoveAt(0);

        entries.Add((message, now + displayDuration));
        Version++;
    }

    internal void Tick()
    {
        if (entries.Count > 0)
            RemoveExpired(Time.realtimeSinceStartupAsDouble);
    }

    internal void Clear()
    {
        entries.Clear();
        Version++;
    }

    private void RemoveExpired(double now)
    {
        int expiredCount = 0;
        while (expiredCount < entries.Count && entries[expiredCount].ExpiresAt <= now)
            expiredCount++;

        if (expiredCount == 0)
            return;

        entries.RemoveRange(0, expiredCount);
        Version++;
    }
}
