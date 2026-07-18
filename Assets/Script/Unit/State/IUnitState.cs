using UnityEngine;

public interface IUnitState
{
    void OnEnter(UnitBase unit);
    void Tick(UnitBase unit, float deltaTime);
    void OnExit(UnitBase unit);
}
