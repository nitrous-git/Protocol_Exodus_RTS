public sealed class CombatIdleState : UnitState<CombatUnit>
{
    protected override void OnEnterTyped(CombatUnit unit)
    {
        unit.Motor?.Stop();
        unit.View?.PlayAnim("Idle");
        unit.ClearCurrentTarget();
    }

    protected override void TickTyped(CombatUnit unit, float deltaTime){ }

    protected override void OnExitTyped(CombatUnit unit)
    {
        unit.ClearCurrentTarget();
    }
}