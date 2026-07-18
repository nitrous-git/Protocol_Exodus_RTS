using UnityEngine;

public abstract class UnitState<TUnit> : IUnitState where TUnit : UnitBase
{
    public void OnEnter(UnitBase unit)
    {
        if (unit is TUnit typedUnit)
        {
            OnEnterTyped(typedUnit);
            return;
        }

        Debug.LogError($"{GetType().Name} cannot run on unit type {unit.GetType().Name}.");
    }

    public void Tick(UnitBase unit, float deltaTime)
    {
        if (unit is TUnit typedUnit)
            TickTyped(typedUnit, deltaTime);
    }

    public void OnExit(UnitBase unit)
    {
        if (unit is TUnit typedUnit)
            OnExitTyped(typedUnit);
    }

    protected virtual void OnEnterTyped(TUnit unit) { }

    // States that do not need deltaTime override this.
    protected virtual void TickTyped(TUnit unit){ }

    // By default, forward to the parameterless version.
    protected virtual void TickTyped(TUnit unit, float deltaTime)
    {
        TickTyped(unit);
    }

    protected virtual void OnExitTyped(TUnit unit) { }
}
