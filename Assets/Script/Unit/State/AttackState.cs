public sealed class AttackState : UnitState<CombatUnit>
{
    private ITargetable target;

    public AttackState(ITargetable target)
    {
        this.target = target;
    }

    protected override void OnEnterTyped(CombatUnit unit)
    {
        unit.Motor?.Stop();
        unit.SetCurrentTarget(target);
        unit.View?.PlayAnim("Idle");
    }

    protected override void TickTyped(CombatUnit unit, float deltaTime)
    {
        if (!unit.IsValidAttackTarget(target))
        {
            unit.ClearCurrentTarget();

            if (unit.Sensor == null || !unit.Sensor.IsReady)
                return;

            target = unit.FindClosestAttackTarget();

            if (target == null)
            {
                unit.EnterCombatIdle();
                return;
            }

            unit.SetCurrentTarget(target);
        }

        unit.UpdateAttack(deltaTime);
    }

    protected override void OnExitTyped(CombatUnit unit)
    {
        unit.CancelAttackAnimation();
        unit.ClearCurrentTarget();
    }
}