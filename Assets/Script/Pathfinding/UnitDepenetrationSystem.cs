using System.Collections.Generic;
using UnityEngine;

public sealed class UnitDepenetrationSystem
{
    private const float OverlapTolerance = 0.25f;
    private const float MaxCorrectionPerUnit = 0.45f;
    private const float MovingThreshold = 0.05f;

    // A couple of passes helps dense groups settle without making
    // this into a complicated physics solver.
    private const int SolverIterations = 2;

    private readonly IReadOnlyList<UnitBase> units;

    public UnitDepenetrationSystem(IReadOnlyList<UnitBase> units)
    {
        this.units = units;
    }

    public void Tick()
    {
        if (units == null)
            return;

        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            for (int i = 0; i < units.Count; i++)
            {
                UnitBase a = units[i];

                if (!CanResolve(a))
                    continue;

                for (int j = i + 1; j < units.Count; j++)
                {
                    UnitBase b = units[j];

                    if (!CanResolve(b))
                        continue;

                    ResolvePair(a, b);
                }
            }
        }
    }

    private bool CanResolve(UnitBase unit)
    {
        return unit != null
            && unit.IsInitialized
            && unit.IsAlive
            && unit.Motor != null
            && unit.Definition != null;
    }

    private void ResolvePair(UnitBase a, UnitBase b)
    {
        Vector3 delta = b.Position - a.Position;
        delta.y = 0f;

        float radiusA = a.Definition.NavigationRadius;
        float radiusB = b.Definition.NavigationRadius;

        float minimumDistance = radiusA + radiusB - OverlapTolerance;

        float distanceSqr = delta.sqrMagnitude;

        if (distanceSqr >= minimumDistance * minimumDistance)
            return;

        Vector3 normal;
        float distance;

        if (distanceSqr <= 0.000001f)
        {
            // Exact same position.
            // Give the pair a deterministic direction.
            normal = a.UnitId < b.UnitId ? Vector3.right : Vector3.left;

            distance = 0f;
        }
        else
        {
            distance = Mathf.Sqrt(distanceSqr);
            normal = delta / distance;
        }

        float penetration = minimumDistance - distance;

        //float correctionAmount = Mathf.Min(penetration * 0.5f, MaxCorrectionPerUnit);

        //if (correctionAmount <= 0f)
        //    return;

        //Vector3 correction = normal * correctionAmount;

        //a.Motor.ApplyDepenetration(-correction);
        //b.Motor.ApplyDepenetration(correction);

        bool aMoving = a.Motor.CurrentVelocity.sqrMagnitude > MovingThreshold * MovingThreshold;
        bool bMoving = b.Motor.CurrentVelocity.sqrMagnitude > MovingThreshold * MovingThreshold;

        float aShare = 0.5f;
        float bShare = 0.5f;

        if (aMoving && !bMoving)
        {
            aShare = 0.8f;
            bShare = 0.2f;
        }
        else if (!aMoving && bMoving)
        {
            aShare = 0.2f;
            bShare = 0.8f;
        }

        float aCorrection = Mathf.Min(penetration * aShare, MaxCorrectionPerUnit);
        float bCorrection = Mathf.Min(penetration * bShare, MaxCorrectionPerUnit);

        a.Motor.ApplyDepenetration(-normal * aCorrection);
        b.Motor.ApplyDepenetration(normal * bCorrection);
    }
}