using UnityEngine;

public sealed class AttackState : UnitState<CombatUnit>
{
    private ITargetable target;

    private GridCoord? reservedAttackCell;
    private bool pathRequested;

    public AttackState(ITargetable target)
    {
        this.target = target;
    }

    protected override void OnEnterTyped(CombatUnit unit)
    {
        unit.Motor?.Stop();

        unit.SetCurrentTarget(target);
        unit.View?.PlayAnim("Idle");

        RequestAttackPosition(unit);
    }

    protected override void TickTyped(CombatUnit unit, float deltaTime)
    {
        // ---------------------------------------------------------
        // Validate / reacquire target
        // ---------------------------------------------------------

        if (!unit.IsValidAttackTarget(target))
        {
            ReleaseAttackPosition(unit);

            unit.ClearCurrentTarget();

            if (unit.Sensor == null || !unit.Sensor.IsReady)
            {
                return;
            }

            target = unit.FindClosestAttackTarget();

            if (target == null)
            {
                unit.EnterCombatIdle();
                return;
            }

            unit.SetCurrentTarget(target);

            RequestAttackPosition(unit);
        }

        // ---------------------------------------------------------
        // Tactical movement
        // ---------------------------------------------------------

        if (pathRequested)
        {
            if (unit.Motor != null && !unit.Motor.HasArrived)
            {
                // Stage A:
                // finish tactical positioning before firing.
                return;
            }

            pathRequested = false;

            unit.View?.PlayAnim("Idle");
        }

        // ---------------------------------------------------------
        // Attack
        // ---------------------------------------------------------

        if (unit.IsWithinAttackRange(target))
        {
            unit.UpdateAttack(deltaTime);
        }
    }

    protected override void OnExitTyped(CombatUnit unit)
    {
        unit.Motor?.Stop();

        ReleaseAttackPosition(unit);

        unit.CancelAttackAnimation();
        unit.ClearCurrentTarget();
    }

    private void RequestAttackPosition(CombatUnit unit)
    {
        pathRequested = false;

        if (unit.Motor == null ||
            unit.TerrainGrid == null ||
            unit.DestinationAllocationSystem == null)
        {
            return;
        }

        reservedAttackCell = unit.DestinationAllocationSystem.Attack.TryAllocate(unit, target);

        if (!reservedAttackCell.HasValue)
        {
            // No tactical position currently available.
            //
            // If already in weapon range, Tick() can still
            // attack from the current position.
            return;
        }

        Vector3 destination = unit.TerrainGrid.CellToWorld(reservedAttackCell.Value);

        pathRequested = unit.Motor.MoveTo(destination);

        if (!pathRequested)
        {
            ReleaseAttackPosition(unit);
            return;
        }

        unit.View?.PlayAnim("Walk");
    }

    private void ReleaseAttackPosition(CombatUnit unit)
    {
        if (!reservedAttackCell.HasValue)
            return;

        unit.ReleaseDestination(reservedAttackCell.Value);

        reservedAttackCell = null;
    }
}