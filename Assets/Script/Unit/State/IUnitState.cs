using UnityEngine;

public interface IUnitState
{
    void OnEnter(UnitBase unit);
    void Tick(UnitBase unit);
    void OnExit(UnitBase unit);
}
