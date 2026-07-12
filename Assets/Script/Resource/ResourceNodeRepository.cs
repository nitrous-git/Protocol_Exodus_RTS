using System;
using UnityEngine;

public class ResourceNodeRepository
{
    private GameContext gameContext;

    public ResourceNodeRepository(GameContext gameContext)
    {
        this.gameContext = gameContext;
    }

    internal void Tick(float deltaTime)
    {
        throw new NotImplementedException();
    }
}
