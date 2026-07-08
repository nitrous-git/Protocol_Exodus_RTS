using UnityEngine;

public interface ITargetable
{
    Faction OwnerFaction { get; }
    Vector3 Position { get; }
    Transform AimPoint { get; }
    bool IsAlive { get; }

    void TakeDamage(DamageInfo damageInfo);
}
