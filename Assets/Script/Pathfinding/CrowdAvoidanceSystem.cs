using System.Collections.Generic;
using UnityEngine;

public sealed class CrowdAvoidanceSystem
{
    private const float Epsilon = 0.00001f;

    // Initial experimental values.
    private const float NeighborDistance = 6f;
    private const float TimeHorizon = 2f;
    private const float RadiusPadding = 0.05f;

    private readonly GameContext gameContext;
    private readonly GridNavigationStateSystem navigationState;

    private readonly List<UnitBase> neighbors = new List<UnitBase>();

    private readonly List<OrcaLine> lines = new List<OrcaLine>();

    private readonly List<OrcaLine> projectedLines = new List<OrcaLine>();

    private readonly Dictionary<UnitBase, Vector3> solvedVelocities = new Dictionary<UnitBase, Vector3>();

    private struct OrcaLine
    {
        public Vector2 Point;
        public Vector2 Direction;
    }

    public CrowdAvoidanceSystem(GameContext gameContext)
    {
        this.gameContext = gameContext;
        navigationState =
            gameContext != null
                ? gameContext.GridNavigationStateSystem
                : null;
    }

    // ---------------------------------------------------------------------
    // Tick
    // ---------------------------------------------------------------------

    public void Tick(float deltaTime)
    {
        if (gameContext == null)
            return;

        IReadOnlyList<UnitBase> units =
            gameContext.AllUnits;

        solvedVelocities.Clear();

        // -------------------------------------------------------------
        // Phase 1:
        // Solve everybody from the same position / velocity snapshot.
        // Do NOT move units in this loop.
        // -------------------------------------------------------------

        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit = units[i];

            if (!CanSolve(unit))
                continue;

            Vector3 safeVelocity =
                SolveVelocity(
                    unit,
                    deltaTime);

            solvedVelocities[unit] =
                safeVelocity;
        }

        // -------------------------------------------------------------
        // Phase 2:
        // Only after all solutions are known do we move the units.
        // -------------------------------------------------------------

        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit = units[i];

            if (unit == null)
                continue;

            if (!solvedVelocities.TryGetValue(
                    unit,
                    out Vector3 safeVelocity))
            {
                continue;
            }

            unit.Motor?.ApplyCrowdVelocity(
                safeVelocity);
        }
    }

    private static bool CanSolve(UnitBase unit)
    {
        return unit != null &&
               unit.IsAlive &&
               unit.Motor != null &&
               unit.Motor.HasPreparedMovement;
    }

    // ---------------------------------------------------------------------
    // ORCA
    // ---------------------------------------------------------------------

    private Vector3 SolveVelocity(
        UnitBase unit,
        float deltaTime)
    {
        UnitMotor motor = unit.Motor;

        lines.Clear();

        if (navigationState != null)
        {
            navigationState.GetNearbyUnits(
                unit,
                NeighborDistance,
                neighbors);

            for (int i = 0; i < neighbors.Count; i++)
            {
                UnitBase other =
                    neighbors[i];

                if (other == null ||
                    other.Motor == null)
                {
                    continue;
                }

                // C3:
                // only reciprocal Moving <-> Moving for now.
                //
                // Hold / Arrived responsibility is deliberately C6.
                if (!other.Motor.HasPreparedMovement)
                    continue;

                OrcaLine line =
                    BuildAgentLine(
                        unit,
                        other,
                        deltaTime);

                lines.Add(line);
            }
        }

        Vector2 preferredVelocity =
            To2D(motor.PreferredVelocity);

        float maxSpeed =
            motor.MaxSpeed;

        if (preferredVelocity.sqrMagnitude >
            maxSpeed * maxSpeed)
        {
            preferredVelocity =
                preferredVelocity.normalized *
                maxSpeed;
        }

        Vector2 result =
            Vector2.zero;

        int failedLine =
            LinearProgram2(
                lines,
                maxSpeed,
                preferredVelocity,
                false,
                ref result);

        if (failedLine < lines.Count)
        {
            LinearProgram3(
                lines,
                failedLine,
                maxSpeed,
                ref result);
        }

        return To3D(result);
    }

    private OrcaLine BuildAgentLine(
        UnitBase self,
        UnitBase other,
        float deltaTime)
    {
        Vector2 selfPosition =
            To2D(self.Position);

        Vector2 otherPosition =
            To2D(other.Position);

        Vector2 selfVelocity =
            To2D(self.Motor.CurrentVelocity);

        Vector2 otherVelocity =
            To2D(other.Motor.CurrentVelocity);

        Vector2 relativePosition =
            otherPosition -
            selfPosition;

        Vector2 relativeVelocity =
            selfVelocity -
            otherVelocity;

        float distanceSq =
            relativePosition.sqrMagnitude;

        float combinedRadius =
            self.NavigationRadius +
            other.NavigationRadius +
            RadiusPadding;

        float combinedRadiusSq =
            combinedRadius *
            combinedRadius;

        OrcaLine line =
            new OrcaLine();

        Vector2 correction;

        // -------------------------------------------------------------
        // Not currently overlapping.
        // Predict collision using the time horizon.
        // -------------------------------------------------------------

        if (distanceSq > combinedRadiusSq)
        {
            float inverseTimeHorizon =
                1f /
                Mathf.Max(
                    TimeHorizon,
                    0.001f);

            Vector2 w =
                relativeVelocity -
                inverseTimeHorizon *
                relativePosition;

            float wLengthSq =
                w.sqrMagnitude;

            float dot =
                Vector2.Dot(
                    w,
                    relativePosition);

            // ---------------------------------------------------------
            // Closest point lies on the cutoff circle.
            // ---------------------------------------------------------

            if (dot < 0f &&
                dot * dot >
                combinedRadiusSq *
                wLengthSq)
            {
                float wLength =
                    Mathf.Sqrt(wLengthSq);

                Vector2 unitW;

                if (wLength > Epsilon)
                {
                    unitW =
                        w / wLength;
                }
                else
                {
                    unitW =
                        GetFallbackDirection(
                            self,
                            other,
                            relativePosition);
                }

                line.Direction =
                    new Vector2(
                        unitW.y,
                        -unitW.x);

                correction =
                    (
                        combinedRadius *
                        inverseTimeHorizon -
                        wLength
                    ) *
                    unitW;
            }

            // ---------------------------------------------------------
            // Closest point lies on one of the VO legs.
            // ---------------------------------------------------------

            else
            {
                float leg =
                    Mathf.Sqrt(
                        Mathf.Max(
                            0f,
                            distanceSq -
                            combinedRadiusSq));

                if (Det(
                        relativePosition,
                        w) > 0f)
                {
                    // Left leg.
                    line.Direction =
                        new Vector2(
                            relativePosition.x *
                                leg -
                            relativePosition.y *
                                combinedRadius,

                            relativePosition.x *
                                combinedRadius +
                            relativePosition.y *
                                leg)
                        / distanceSq;
                }
                else
                {
                    // Right leg.
                    Vector2 direction =
                        new Vector2(
                            relativePosition.x *
                                leg +
                            relativePosition.y *
                                combinedRadius,

                            -relativePosition.x *
                                combinedRadius +
                            relativePosition.y *
                                leg)
                        / distanceSq;

                    line.Direction =
                        -direction;
                }

                correction =
                    Vector2.Dot(
                        relativeVelocity,
                        line.Direction) *
                    line.Direction -
                    relativeVelocity;
            }
        }

        // -------------------------------------------------------------
        // Already overlapping.
        //
        // Use this frame's timestep rather than the longer horizon so
        // the solver immediately requests separation.
        // -------------------------------------------------------------

        else
        {
            float inverseTimeStep =
                1f /
                Mathf.Max(
                    deltaTime,
                    0.001f);

            Vector2 w =
                relativeVelocity -
                inverseTimeStep *
                relativePosition;

            float wLength =
                w.magnitude;

            Vector2 unitW;

            if (wLength > Epsilon)
            {
                unitW =
                    w / wLength;
            }
            else
            {
                unitW =
                    GetFallbackDirection(
                        self,
                        other,
                        relativePosition);
            }

            line.Direction =
                new Vector2(
                    unitW.y,
                    -unitW.x);

            correction =
                (
                    combinedRadius *
                    inverseTimeStep -
                    wLength
                ) *
                unitW;
        }

        // Reciprocal part:
        // each moving unit assumes half of the correction.
        line.Point =
            selfVelocity +
            0.5f *
            correction;

        return line;
    }

    // ---------------------------------------------------------------------
    // Linear Program
    // ---------------------------------------------------------------------

    private static bool LinearProgram1(
        List<OrcaLine> constraints,
        int lineIndex,
        float maxSpeed,
        Vector2 optimalVelocity,
        bool directionOnly,
        ref Vector2 result)
    {
        OrcaLine line =
            constraints[lineIndex];

        float dot =
            Vector2.Dot(
                line.Point,
                line.Direction);

        float discriminant =
            dot * dot +
            maxSpeed * maxSpeed -
            line.Point.sqrMagnitude;

        if (discriminant < 0f)
            return false;

        float sqrtDiscriminant =
            Mathf.Sqrt(discriminant);

        float tLeft =
            -dot -
            sqrtDiscriminant;

        float tRight =
            -dot +
            sqrtDiscriminant;

        for (int i = 0; i < lineIndex; i++)
        {
            OrcaLine other =
                constraints[i];

            float denominator =
                Det(
                    line.Direction,
                    other.Direction);

            float numerator =
                Det(
                    other.Direction,
                    line.Point -
                    other.Point);

            if (Mathf.Abs(denominator) <= Epsilon)
            {
                if (numerator < 0f)
                    return false;

                continue;
            }

            float t =
                numerator /
                denominator;

            if (denominator >= 0f)
            {
                tRight =
                    Mathf.Min(
                        tRight,
                        t);
            }
            else
            {
                tLeft =
                    Mathf.Max(
                        tLeft,
                        t);
            }

            if (tLeft > tRight)
                return false;
        }

        if (directionOnly)
        {
            if (Vector2.Dot(
                    optimalVelocity,
                    line.Direction) > 0f)
            {
                result =
                    line.Point +
                    tRight *
                    line.Direction;
            }
            else
            {
                result =
                    line.Point +
                    tLeft *
                    line.Direction;
            }
        }
        else
        {
            float t =
                Vector2.Dot(
                    line.Direction,
                    optimalVelocity -
                    line.Point);

            t =
                Mathf.Clamp(
                    t,
                    tLeft,
                    tRight);

            result =
                line.Point +
                t *
                line.Direction;
        }

        return true;
    }

    private static int LinearProgram2(
        List<OrcaLine> constraints,
        float maxSpeed,
        Vector2 optimalVelocity,
        bool directionOnly,
        ref Vector2 result)
    {
        if (directionOnly)
        {
            result =
                optimalVelocity *
                maxSpeed;
        }
        else if (
            optimalVelocity.sqrMagnitude >
            maxSpeed * maxSpeed)
        {
            result =
                optimalVelocity.normalized *
                maxSpeed;
        }
        else
        {
            result =
                optimalVelocity;
        }

        for (int i = 0;
             i < constraints.Count;
             i++)
        {
            OrcaLine line =
                constraints[i];

            if (Det(
                    line.Direction,
                    line.Point -
                    result) <= 0f)
            {
                continue;
            }

            Vector2 previousResult =
                result;

            if (!LinearProgram1(
                    constraints,
                    i,
                    maxSpeed,
                    optimalVelocity,
                    directionOnly,
                    ref result))
            {
                result =
                    previousResult;

                return i;
            }
        }

        return constraints.Count;
    }

    private void LinearProgram3(
        List<OrcaLine> constraints,
        int beginLine,
        float maxSpeed,
        ref Vector2 result)
    {
        float distance =
            0f;

        for (int i = beginLine;
             i < constraints.Count;
             i++)
        {
            OrcaLine current =
                constraints[i];

            float violation =
                Det(
                    current.Direction,
                    current.Point -
                    result);

            if (violation <= distance)
                continue;

            projectedLines.Clear();

            for (int j = 0; j < i; j++)
            {
                OrcaLine previous =
                    constraints[j];

                OrcaLine projected =
                    new OrcaLine();

                float determinant =
                    Det(
                        current.Direction,
                        previous.Direction);

                if (Mathf.Abs(determinant) <= Epsilon)
                {
                    // Same direction:
                    // previous constraint adds nothing.
                    if (Vector2.Dot(
                            current.Direction,
                            previous.Direction) > 0f)
                    {
                        continue;
                    }

                    // Opposing parallel constraints.
                    projected.Point =
                        0.5f *
                        (
                            current.Point +
                            previous.Point
                        );
                }
                else
                {
                    projected.Point =
                        current.Point +
                        (
                            Det(
                                previous.Direction,
                                current.Point -
                                previous.Point)
                            /
                            determinant
                        ) *
                        current.Direction;
                }

                Vector2 direction =
                    previous.Direction -
                    current.Direction;

                if (direction.sqrMagnitude <=
                    Epsilon * Epsilon)
                {
                    continue;
                }

                projected.Direction =
                    direction.normalized;

                projectedLines.Add(
                    projected);
            }

            Vector2 previousResult =
                result;

            Vector2 optimizationDirection =
                new Vector2(
                    -current.Direction.y,
                    current.Direction.x);

            int failed =
                LinearProgram2(
                    projectedLines,
                    maxSpeed,
                    optimizationDirection,
                    true,
                    ref result);

            if (failed <
                projectedLines.Count)
            {
                // Numerical degeneracy.
                // Keep the previous feasible result.
                result =
                    previousResult;
            }

            distance =
                Det(
                    current.Direction,
                    current.Point -
                    result);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static float Det(
        Vector2 a,
        Vector2 b)
    {
        return
            a.x * b.y -
            a.y * b.x;
    }

    private static Vector2 To2D(
        Vector3 vector)
    {
        return new Vector2(
            vector.x,
            vector.z);
    }

    private static Vector3 To3D(
        Vector2 vector)
    {
        return new Vector3(
            vector.x,
            0f,
            vector.y);
    }

    private static Vector2 GetFallbackDirection(
        UnitBase self,
        UnitBase other,
        Vector2 relativePosition)
    {
        if (relativePosition.sqrMagnitude >
            Epsilon * Epsilon)
        {
            // Move away from the other agent.
            return
                -relativePosition.normalized;
        }

        // Extremely rare exact same-position case.
        // UnitId makes both units choose opposite directions.
        return
            self.UnitId < other.UnitId
                ? Vector2.right
                : Vector2.left;
    }
}