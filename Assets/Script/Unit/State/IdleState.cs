using UnityEngine;

public class IdleState : UnitState<UnitBase>
{
    protected override void OnEnterTyped(UnitBase unit)
    {
        if (unit.Motor != null)
            unit.Motor.Stop();
    }
}
