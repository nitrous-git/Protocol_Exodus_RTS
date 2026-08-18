using UnityEngine;

public class IdleState : UnitState<UnitBase>
{
    protected override void OnEnterTyped(UnitBase unit)
    {
        unit.Motor?.Stop();
        unit.View?.PlayAnim("Idle");
    }
}
