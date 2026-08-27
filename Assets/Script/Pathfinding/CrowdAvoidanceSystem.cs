using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local reciprocal collision avoidance for moving units.
///
/// Architecture:
///
/// A* / path following
///      ¡ý
/// PreferredVelocity
///      ¡ý
/// CrowdAvoidanceSystem
///      ¡ý
/// ORCA safe velocity
///      ¡ý
/// UnitMotor.ApplyCrowdVelocity()
///
/// C3 baseline:
/// - Agent-agent ORCA only.
/// - Moving <-> Moving only.
/// - Reciprocal 50 / 50 responsibility.
/// - No radius padding.
/// - No passing bias.
/// - No custom fallback direction.
/// - No stationary-unit responsibility policy.
/// - Static obstacles remain handled by radius-aware A*.
///
/// ORCA math follows the standard RVO2 agent-agent formulation.
/// </summary>
public sealed class CrowdAvoidanceSystem
{
    private const float Epsilon = 0.00001f;

    // C3 baseline parameters.
    //
    // At moveSpeed = 2.5:
    // two head-on units have a relative closing speed of 5.
    // With a 2 second horizon, 10 world units is a sensible
    // initial neighbor distance.
    private const float NeighborDistance = 10f;
    private const float TimeHorizon = 2f;
    private const float StationaryPassingBias = 0.50f;

    private readonly GameContext gameContext;
    private readonly GridNavigationStateSystem navigationState;

    private readonly List<UnitBase> neighbors = new List<UnitBase>();

    private readonly List<OrcaLine> lines = new List<OrcaLine>();

    private readonly List<OrcaLine> projectedLines = new List<OrcaLine>();

    private readonly Dictionary<UnitBase, Vector3> solvedVelocities = new Dictionary<UnitBase, Vector3>();

    private readonly HashSet<UnitBase> movingLastFrame =
    new HashSet<UnitBase>();

    private readonly HashSet<UnitBase> movingThisFrame =
        new HashSet<UnitBase>();

    private struct OrcaLine
    {
        public Vector2 Point;
        public Vector2 Direction;

        public OrcaLine(
            Vector2 point,
            Vector2 direction)
        {
            Point = point;
            Direction = direction;
        }
    }

    public CrowdAvoidanceSystem(
        GameContext gameContext)
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
        if (gameContext == null ||
            deltaTime <= Epsilon)
        {
            return;
        }

        IReadOnlyList<UnitBase> units =
            gameContext.AllUnits;

        solvedVelocities.Clear();
        movingThisFrame.Clear();

        // Determine the complete moving set before solving anybody.
        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit =
                units[i];

            if (CanSolve(unit))
            {
                movingThisFrame.Add(unit);
            }
        }

        // -------------------------------------------------------------
        // Solve
        // -------------------------------------------------------------

        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit =
                units[i];

            if (!movingThisFrame.Contains(unit))
                continue;

            Vector3 solvedVelocity =
                SolveVelocity(
                    unit,
                    deltaTime);

            solvedVelocities[unit] =
                solvedVelocity;
        }

        // -------------------------------------------------------------
        // Apply
        // -------------------------------------------------------------

        for (int i = 0; i < units.Count; i++)
        {
            UnitBase unit =
                units[i];

            if (unit == null)
                continue;

            if (!solvedVelocities.TryGetValue(
                    unit,
                    out Vector3 solvedVelocity))
            {
                continue;
            }

            unit.Motor?.ApplyCrowdVelocity(
                solvedVelocity);
        }

        // -------------------------------------------------------------
        // Save movement state for next frame.
        // -------------------------------------------------------------

        movingLastFrame.Clear();

        foreach (UnitBase unit in movingThisFrame)
        {
            movingLastFrame.Add(unit);
        }
    }

    private static bool CanSolve(
        UnitBase unit)
    {
        return
            unit != null &&
            unit.IsAlive &&
            unit.Motor != null &&
            unit.Motor.HasPreparedMovement;
    }

    private Vector2 GetSolverVelocity(
        UnitBase unit)
    {
        UnitMotor motor =
            unit.Motor;

        if (!motor.HasPreparedMovement)
        {
            return Vector2.zero;
        }

        if (!movingLastFrame.Contains(unit))
        {
            Vector2 preferred =
                To2D(
                    motor.PreferredVelocity);

            return Vector2.ClampMagnitude(
                preferred,
                motor.MaxSpeed);
        }

        return To2D(
            motor.CurrentVelocity);
    }

    // ---------------------------------------------------------------------
    // ORCA
    // ---------------------------------------------------------------------

    private Vector3 SolveVelocity(
        UnitBase unit,
        float deltaTime)
    {
        UnitMotor motor =
            unit.Motor;

        lines.Clear();

        // We currently have no ORCA obstacle constraints.
        // Static world navigation remains the responsibility of A*.
        const int numObstacleLines = 0;

        // -------------------------------------------------------------
        // Agent neighbors
        // -------------------------------------------------------------

        if (navigationState != null)
        {
            navigationState.GetNearbyUnits(
                unit,
                NeighborDistance,
                neighbors);

            // RVO2 keeps agent neighbors ordered from closest to
            // furthest. It is irrelevant for the 1v1 tests, but this
            // keeps our input ordering closer to the reference solver.
            SortNeighborsByDistance(
                unit);

            for (int i = 0;
                 i < neighbors.Count;
                 i++)
            {
                UnitBase other =
                    neighbors[i];

                if (other == null ||
                    !other.IsAlive ||
                    other.Motor == null)
                {
                    continue;
                }

                bool otherIsMoving = other.Motor.HasPreparedMovement;

                float responsibility =
                    otherIsMoving
                        ? 0.5f
                        : 1.0f;

                OrcaLine line =
                    BuildAgentLine(
                        unit,
                        other,
                        deltaTime,
                        responsibility);

                lines.Add(line);
            }
        }

        // -------------------------------------------------------------
        // Preferred velocity
        // -------------------------------------------------------------

        Vector2 preferredVelocity =
            To2D(
                motor.PreferredVelocity);

        // before LP2  -> ApplyStationaryPassingBias
        preferredVelocity = ApplyStationaryPassingBias(unit, preferredVelocity);

        float maxSpeed =
            motor.MaxSpeed;

        // PreferredVelocity should already respect moveSpeed,
        // but keep the solver input valid regardless.
        if (preferredVelocity.sqrMagnitude >
            maxSpeed * maxSpeed)
        {
            preferredVelocity =
                preferredVelocity.normalized *
                maxSpeed;
        }

        // -------------------------------------------------------------
        // Solve velocity closest to PreferredVelocity while satisfying
        // all ORCA half-plane constraints.
        // -------------------------------------------------------------

        Vector2 result =
            Vector2.zero;

        int failedLine =
            LinearProgram2(
                lines,
                maxSpeed,
                preferredVelocity,
                false,
                ref result);

        if (failedLine <
            lines.Count)
        {
            LinearProgram3(
                lines,
                numObstacleLines,
                failedLine,
                maxSpeed,
                ref result);
        }

        return To3D(result);
    }

    // ---------------------------------------------------------------------
    // Agent ORCA Constraint
    // ---------------------------------------------------------------------

    private OrcaLine BuildAgentLine(
        UnitBase self,
        UnitBase other,
        float deltaTime,
        float responsability)
    {
        Vector2 selfPosition =
            To2D(
                self.Position);

        Vector2 otherPosition =
            To2D(
                other.Position);

        Vector2 selfVelocity = GetSolverVelocity(self);

        Vector2 otherVelocity = GetSolverVelocity(other);

        Vector2 relativePosition =
            otherPosition -
            selfPosition;

        Vector2 relativeVelocity =
            selfVelocity -
            otherVelocity;

        float distanceSq =
            relativePosition.sqrMagnitude;

        // IMPORTANT:
        //
        // No arbitrary padding in the C3 reference baseline.
        // NavigationRadius is the actual ORCA agent radius.
        float combinedRadius =
            self.NavigationRadius +
            other.NavigationRadius;

        float combinedRadiusSq =
            combinedRadius *
            combinedRadius;

        Vector2 direction;
        Vector2 correction;

        // -------------------------------------------------------------
        // No current collision.
        //
        // Construct the velocity obstacle using TimeHorizon.
        // -------------------------------------------------------------

        if (distanceSq >
            combinedRadiusSq)
        {
            float inverseTimeHorizon =
                1f /
                TimeHorizon;

            // Vector from the cutoff-circle center to the current
            // relative velocity.
            Vector2 w =
                relativeVelocity -
                inverseTimeHorizon *
                relativePosition;

            float wLengthSq =
                w.sqrMagnitude;

            float dotProduct1 =
                Vector2.Dot(
                    w,
                    relativePosition);

            // ---------------------------------------------------------
            // Project on cutoff circle.
            // ---------------------------------------------------------

            if (dotProduct1 < 0f &&
                dotProduct1 * dotProduct1 >
                combinedRadiusSq *
                wLengthSq)
            {
                float wLength =
                    Mathf.Sqrt(
                        wLengthSq);

                Vector2 unitW =
                    w /
                    wLength;

                direction =
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
            // Project on velocity-obstacle legs.
            // ---------------------------------------------------------

            else
            {
                float leg =
                    Mathf.Sqrt(
                        distanceSq -
                        combinedRadiusSq);

                if (Det(
                        relativePosition,
                        w) > 0f)
                {
                    // Left leg.
                    direction =
                        new Vector2(
                            relativePosition.x *
                                leg -
                            relativePosition.y *
                                combinedRadius,

                            relativePosition.x *
                                combinedRadius +
                            relativePosition.y *
                                leg)
                        /
                        distanceSq;
                }
                else
                {
                    // Right leg.
                    direction =
                        -new Vector2(
                            relativePosition.x *
                                leg +
                            relativePosition.y *
                                combinedRadius,

                            -relativePosition.x *
                                combinedRadius +
                            relativePosition.y *
                                leg)
                        /
                        distanceSq;
                }

                float dotProduct2 =
                    Vector2.Dot(
                        relativeVelocity,
                        direction);

                correction =
                    dotProduct2 *
                    direction -
                    relativeVelocity;
            }
        }

        // -------------------------------------------------------------
        // Already colliding / overlapping.
        //
        // ORCA uses the current simulation timestep instead of the
        // longer prediction horizon.
        // -------------------------------------------------------------

        else
        {
            float inverseTimeStep =
                1f /
                deltaTime;

            Vector2 w =
                relativeVelocity -
                inverseTimeStep *
                relativePosition;

            float wLength =
                w.magnitude;

            Vector2 unitW =
                w /
                wLength;

            direction =
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

        // -------------------------------------------------------------
        // Reciprocal responsibility.
        //
        // Standard ORCA:
        // each moving agent takes exactly half the required correction.
        // -------------------------------------------------------------

        Vector2 point =
            selfVelocity +
            responsability *
            correction;

        return new OrcaLine(
            point,
            direction);
    }

    // ---------------------------------------------------------------------
    // Linear Program 1
    // ---------------------------------------------------------------------

    private static bool LinearProgram1(
        List<OrcaLine> constraints,
        int lineIndex,
        float radius,
        Vector2 optimalVelocity,
        bool directionOnly,
        ref Vector2 result)
    {
        OrcaLine line =
            constraints[lineIndex];

        float dotProduct =
            Vector2.Dot(
                line.Point,
                line.Direction);

        float discriminant =
            dotProduct *
            dotProduct +
            radius *
            radius -
            line.Point.sqrMagnitude;

        // The speed circle does not intersect this constraint.
        if (discriminant < 0f)
            return false;

        float sqrtDiscriminant =
            Mathf.Sqrt(
                discriminant);

        float tLeft =
            -dotProduct -
            sqrtDiscriminant;

        float tRight =
            -dotProduct +
            sqrtDiscriminant;

        // Restrict the valid interval according to all previous
        // constraints.
        for (int i = 0;
             i < lineIndex;
             i++)
        {
            OrcaLine previous =
                constraints[i];

            float denominator =
                Det(
                    line.Direction,
                    previous.Direction);

            float numerator =
                Det(
                    previous.Direction,
                    line.Point -
                    previous.Point);

            // Nearly parallel constraints.
            if (Mathf.Abs(denominator) <=
                Epsilon)
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

            if (tLeft >
                tRight)
            {
                return false;
            }
        }

        if (directionOnly)
        {
            // Optimize only the direction.
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
            // Find the point on this valid interval closest to the
            // desired velocity.
            float t =
                Vector2.Dot(
                    line.Direction,
                    optimalVelocity -
                    line.Point);

            if (t <
                tLeft)
            {
                result =
                    line.Point +
                    tLeft *
                    line.Direction;
            }
            else if (t >
                     tRight)
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
                    t *
                    line.Direction;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------------
    // Linear Program 2
    // ---------------------------------------------------------------------

    private static int LinearProgram2(
        List<OrcaLine> constraints,
        float radius,
        Vector2 optimalVelocity,
        bool directionOnly,
        ref Vector2 result)
    {
        // Initial candidate velocity.
        if (directionOnly)
        {
            result =
                optimalVelocity *
                radius;
        }
        else if (
            optimalVelocity.sqrMagnitude >
            radius *
            radius)
        {
            result =
                optimalVelocity.normalized *
                radius;
        }
        else
        {
            result =
                optimalVelocity;
        }

        // Enforce each ORCA constraint.
        for (int i = 0;
             i < constraints.Count;
             i++)
        {
            OrcaLine line =
                constraints[i];

            // Positive determinant means the current result violates
            // this half-plane.
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
                    radius,
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

    // ---------------------------------------------------------------------
    // Linear Program 3
    // ---------------------------------------------------------------------

    private void LinearProgram3(
        List<OrcaLine> constraints,
        int numObstacleLines,
        int beginLine,
        float radius,
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

            if (violation <=
                distance)
            {
                continue;
            }

            // ---------------------------------------------------------
            // Current result violates constraint i.
            //
            // Build a new projected linear program.
            // ---------------------------------------------------------

            projectedLines.Clear();

            // We currently have no obstacle ORCA lines, but retain the
            // same structure as standard RVO2 so obstacle constraints
            // could be supported later without changing the LP.
            for (int obstacleIndex = 0;
                 obstacleIndex < numObstacleLines;
                 obstacleIndex++)
            {
                projectedLines.Add(
                    constraints[obstacleIndex]);
            }

            for (int j = numObstacleLines;
                 j < i;
                 j++)
            {
                OrcaLine previous =
                    constraints[j];

                Vector2 point;

                float determinant =
                    Det(
                        current.Direction,
                        previous.Direction);

                if (Mathf.Abs(determinant) <=
                    Epsilon)
                {
                    // Parallel lines.
                    if (Vector2.Dot(
                            current.Direction,
                            previous.Direction) > 0f)
                    {
                        // Same direction:
                        // previous line adds no new restriction.
                        continue;
                    }

                    // Opposite directions.
                    point =
                        0.5f *
                        (
                            current.Point +
                            previous.Point
                        );
                }
                else
                {
                    point =
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
                    (
                        previous.Direction -
                        current.Direction
                    ).normalized;

                projectedLines.Add(
                    new OrcaLine(
                        point,
                        direction));
            }

            Vector2 previousResult =
                result;

            Vector2 optimizationDirection =
                new Vector2(
                    -current.Direction.y,
                    current.Direction.x);

            int failedLine =
                LinearProgram2(
                    projectedLines,
                    radius,
                    optimizationDirection,
                    true,
                    ref result);

            if (failedLine <
                projectedLines.Count)
            {
                // The previous result was already feasible in theory.
                // Failure here can only come from floating-point
                // precision, so retain it.
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
    // Neighbor Ordering
    // ---------------------------------------------------------------------

    private void SortNeighborsByDistance(
        UnitBase unit)
    {
        Vector3 origin =
            unit.Position;

        // Small insertion sort.
        //
        // Neighbor counts are expected to be low because
        // GridNavigationStateSystem has already spatially filtered the
        // candidate set.
        for (int i = 1;
             i < neighbors.Count;
             i++)
        {
            UnitBase candidate =
                neighbors[i];

            float candidateDistanceSq =
                XZDistanceSquared(
                    origin,
                    candidate.Position);

            int j =
                i - 1;

            while (j >= 0)
            {
                UnitBase previous =
                    neighbors[j];

                float previousDistanceSq =
                    XZDistanceSquared(
                        origin,
                        previous.Position);

                if (previousDistanceSq <=
                    candidateDistanceSq)
                {
                    break;
                }

                neighbors[j + 1] =
                    previous;

                j--;
            }

            neighbors[j + 1] =
                candidate;
        }
    }

    // ---------------------------------------------------------------------
    // Stationary Bias
    // ---------------------------------------------------------------------

    private Vector2 ApplyStationaryPassingBias(
    UnitBase self,
    Vector2 preferredVelocity)
    {
        if (preferredVelocity.sqrMagnitude <= Epsilon)
            return preferredVelocity;

        Vector2 forward =
            preferredVelocity.normalized;

        for (int i = 0; i < neighbors.Count; i++)
        {
            UnitBase other =
                neighbors[i];

            if (other == null ||
                other.Motor == null ||
                other.Motor.HasPreparedMovement)
            {
                continue;
            }

            Vector2 toOther =
                To2D(
                    other.Position -
                    self.Position);

            float distance =
                toOther.magnitude;

            if (distance <= Epsilon)
                continue;

            Vector2 directionToOther =
                toOther / distance;

            // Only care about stationary units substantially ahead.
            if (Vector2.Dot(
                    forward,
                    directionToOther) < 0.75f)
            {
                continue;
            }

            Vector2 right =
                new Vector2(
                    forward.y,
                    -forward.x);

            Vector2 biased =
                preferredVelocity +
                right *
                self.Motor.MaxSpeed *
                StationaryPassingBias;

            return Vector2.ClampMagnitude(
                biased,
                self.Motor.MaxSpeed);
        }

        return preferredVelocity;
    }

    // ---------------------------------------------------------------------
    // Math Helpers
    // ---------------------------------------------------------------------

    private static float Det(
        Vector2 first,
        Vector2 second)
    {
        return
            first.x *
            second.y -
            first.y *
            second.x;
    }

    private static float XZDistanceSquared(
        Vector3 first,
        Vector3 second)
    {
        float deltaX =
            first.x -
            second.x;

        float deltaZ =
            first.z -
            second.z;

        return
            deltaX *
            deltaX +
            deltaZ *
            deltaZ;
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
}