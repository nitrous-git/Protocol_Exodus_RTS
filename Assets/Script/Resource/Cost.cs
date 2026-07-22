using System;
using UnityEngine;

[Serializable]
public struct Cost
{
    [Min(0)] public int minerals;
    [Min(0)] public int gas;
    [Min(0)] public int supply;

    public int Minerals => minerals;
    public int Gas => gas;
    public int Supply => supply;

    public Cost(int minerals, int gas, int supply)
    {
        if (minerals < 0 || gas < 0 || supply < 0)
            throw new Exception("Cost values must be ¡İ 0");

        this.minerals = minerals;
        this.gas = gas;
        this.supply = supply;
    }

    public static Cost Zero => new(0, 0, 0);

    public override string ToString()
    {
        return $"[{minerals}M, {gas}G, {supply}S]";
    }
}