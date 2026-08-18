using UnityEngine;

public class UnitAnimationEventRelay : MonoBehaviour
{
    private CombatUnit combatUnit;

    private void Awake()
    {
        combatUnit = GetComponentInParent<CombatUnit>();
    }

    public void FireWeapon()
    {
        combatUnit?.OnWeaponFireAnimationEvent();
    }

    public void AttackAnimationFinished()
    {
        combatUnit?.OnAttackAnimationFinished();
    }
}