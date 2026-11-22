using System.Collections.Generic;
using UnityEngine;

public class UnitWeapon : MonoBehaviour
{
    private enum AttackMode
    {
        Projectile,
        FlameCone
    }

    [SerializeField] private AttackMode attackMode;
    [SerializeField] private Transform firePoint;

    [Header("Projectile")]
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 14f; 
    [SerializeField, Min(0.01f)] private float projectileLifetime = 4f;
    [SerializeField, Min(0f)] private float projectileCollisionRadius = 0.1f;
    [SerializeField] private LayerMask projectileCollisionMask = ~0;

    [Header("Flame Cone")]
    [SerializeField, Range(1f, 180f)] private float flameConeAngle = 70f;
    [SerializeField] private LayerMask flameHitMask = ~0;
    [SerializeField] private ParticleSystem flameEffect;

    private CombatUnit owner;
    private ProjectileManager projectileManager;
    private float cooldownRemaining;
    private bool flameBurstPending;
    private Collider[] flameHits;
    private HashSet<ITargetable> flameTargets;

    public bool IsReady => cooldownRemaining <= 0f;
    public Transform FirePoint => firePoint != null ? firePoint : transform;
    internal bool UsesFlameCone => attackMode == AttackMode.FlameCone;

    public void Initialize(CombatUnit owner, GameContext gameContext)
    {
        this.owner = owner;
        projectileManager = gameContext != null ? gameContext.ProjectileManager : null;
        cooldownRemaining = 0f;

        if (UsesFlameCone)
        {
            flameHits ??= new Collider[64];
            flameTargets ??= new HashSet<ITargetable>(64);
        }
        else if (projectileManager == null)
            Debug.LogError(name + " cannot initialize UnitWeapon because ProjectileManager is missing.");
    }

    public void Tick(float deltaTime)
    {
        if (cooldownRemaining > 0f)
            cooldownRemaining -= deltaTime;
    }

    internal bool TryFireImmediate(ITargetable target)
    {
        if (!IsReady || !TryFireProjectile(target))
            return false;

        cooldownRemaining = owner.Definition.attackCooldown;
        return true;
    }

    public bool TryBeginAttack(ITargetable target)
    {
        if (!IsReady || !CanFireAt(target))
            return false;

        if (!UsesFlameCone && (projectilePrefab == null || projectileManager == null))
            return false;

        cooldownRemaining = owner.Definition.attackCooldown;
        flameBurstPending = UsesFlameCone;

        return true;
    }

    internal void FireFromAnimation(ITargetable target)
    {
        if (!UsesFlameCone)
        {
            TryFireProjectile(target);
            return;
        }

        if (!flameBurstPending || !owner.IsAlive)
            return;

        flameBurstPending = false;
        if (flameEffect != null)
        {
            flameEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            flameEffect.Play(true);
        }

        FireFlameCone();
    }

    internal void StopFlameBurst()
    {
        flameBurstPending = false;
        if (flameEffect != null && flameEffect.isEmitting)
            flameEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    private void OnDisable()
    {
        flameBurstPending = false;
        if (flameEffect != null)
            flameEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void FireFlameCone()
    {
        // Use the same ground origin and range as AttackState.
        Vector3 origin = owner.Position;
        Vector3 forward = owner.transform.forward;
        forward.y = 0f;
        forward.Normalize();
        float range = owner.GetAttackRange();
        float rangeSquared = range * range;
        float minDot = Mathf.Cos(flameConeAngle * 0.5f * Mathf.Deg2Rad);
        var damage = new DamageInfo(owner.Definition.attackDamage, owner);

        int count;
        // Grow only when a dense cluster fills the buffer, then reuse the larger buffer.
        while ((count = Physics.OverlapSphereNonAlloc(origin, range, flameHits, flameHitMask,
                   QueryTriggerInteraction.Ignore)) == flameHits.Length)
        {
            flameHits = new Collider[flameHits.Length * 2];
        }

        for (int i = 0; i < count; i++)
        {
            Collider hit = flameHits[i];
            flameHits[i] = null;
            ITargetable target = hit != null ? hit.GetComponentInParent<ITargetable>() : null;
            if (!owner.IsValidAttackTarget(target))
                continue;

            Vector3 offset = target.Position - origin;
            offset.y = 0f;
            float distanceSquared = offset.sqrMagnitude;
            if (distanceSquared <= rangeSquared &&
                Vector3.Dot(forward, offset) >= minDot * Mathf.Sqrt(distanceSquared) &&
                flameTargets.Add(target))
            {
                target.TakeDamage(damage);
            }
        }

        flameTargets.Clear();
    }

    public bool TryFireProjectile(ITargetable target)
    {
        if (UsesFlameCone || !CanFireAt(target))
            return false;

        if (projectilePrefab == null || projectileManager == null)
            return false;

        Vector3 origin = FirePoint.position;
        Vector3 targetPosition = target.AimPoint != null ? target.AimPoint.position : target.Position;
        Vector3 direction = targetPosition - origin;

        if (direction.sqrMagnitude <= 0.0001f)
            return false;

        direction.Normalize();

        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

        Projectile projectile = projectileManager.SpawnProjectile(
            projectilePrefab,
            origin,
            rotation,
            owner,
            direction,
            projectileSpeed,
            owner.Definition.attackDamage,
            projectileLifetime,
            projectileCollisionRadius,
            projectileCollisionMask
        );

        return projectile != null;
    }

    public bool CanFireAt(ITargetable target)
    {
        if (owner == null || owner.Definition == null || !owner.Definition.canAttack)
            return false;

        if (UsesFlameCone)
            return owner.IsValidAttackTarget(target) && owner.IsWithinAttackRange(target);

        if (target == null || !target.IsAlive)
            return false;

        if (owner.OwnerFaction == null || target.OwnerFaction == null)
            return false;

        if (!owner.OwnerFaction.IsEnemy(target.OwnerFaction))
            return false;

        if (owner.IsAirUnit)
            return owner.IsWithinAttackRange(target);

        float range = owner.Definition.attackRange;
        return (target.Position - owner.Position).sqrMagnitude <= range * range;
    }
}
