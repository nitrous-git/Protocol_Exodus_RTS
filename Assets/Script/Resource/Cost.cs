using System;
using UnityEngine;

[Serializable]
public struct Cost
{
    [Min(0)] public int minerals;
    [Min(0)] public int gas;
    [Min(0)] public int supply;

    public Cost(int minerals, int gas, int supply)
    {
        if (minerals < 0 || gas < 0 || supply < 0)
            throw new Exception("Cost values must be ¡Ý 0");

        this.minerals = Mathf.Max(0, minerals);
        this.gas = Mathf.Max(0, gas);
        this.supply = Mathf.Max(0, supply);
    }

    public static Cost Zero => new(0, 0, 0);

    public String toString()
    {
        return String.Format("Cost: [%dM, %dG, %dS]", minerals, gas, supply);
    }
}