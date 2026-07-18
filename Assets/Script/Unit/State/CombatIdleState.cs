public sealed class CombatIdleState : UnitState<CombatUnit>
{
    protected override void OnEnterTyped(CombatUnit unit)
    {
        unit.Motor?.Stop();
        unit.ClearCurrentTarget();
    }

    protected override void TickTyped(CombatUnit unit, float deltaTime)
    {
        unit.UpdateAutomaticCombat(deltaTime);
    }

    protected override void OnExitTyped(CombatUnit unit)
    {
        unit.ClearCurrentTarget();
    }
}