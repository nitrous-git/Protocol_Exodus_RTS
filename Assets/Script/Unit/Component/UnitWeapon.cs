using UnityEngine;

public class UnitWeapon : MonoBehaviour
{
    [Header("Projectile")]
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 14f; 
    [SerializeField, Min(0.01f)] private float projectileLifetime = 4f;
    [SerializeField, Min(0f)] private float projectileCollisionRadius = 0.1f;
    [SerializeField] private LayerMask projectileCollisionMask = ~0;

    private CombatUnit owner;
    private ProjectileManager projectileManager;
    private float cooldownRemaining;

    public bool IsReady => cooldownRemaining <= 0f;
    public Transform FirePoint => firePoint != null ? firePoint : transform;

    public void Initialize(CombatUnit owner, GameContext gameContext)
    {
        this.owner = owner;
        projectileManager = gameContext != null ? gameContext.ProjectileManager : null;
        cooldownRemaining = 0f;

        if (projectileManager == null)
            Debug.LogError(name + " cannot initialize UnitWeapon because ProjectileManager is missing.");
    }

    public void Tick(float deltaTime)
    {
        if (cooldownRemaining > 0f)
            cooldownRemaining -= deltaTime;
    }

    //public bool TryFire(ITargetable target)
    //{
    //    if (!IsReady || !CanFireAt(target))
    //        return false;

    //    if (projectilePrefab == null || projectileManager == null)
    //        return false;

    //    Vector3 origin = FirePoint.position;
    //    Vector3 targetPosition = target.AimPoint != null ? target.AimPoint.position : target.Position;

    //    Vector3 direction = targetPosition - origin;

    //    if (direction.sqrMagnitude <= 0.0001f)
    //        return false;

    //    direction.Normalize();
    //    Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

    //    //owner.transform.rotation = Quaternion.LookRotation(direction, Vector3.up); 

    //    Projectile projectile = projectileManager.SpawnProjectile(
    //        projectilePrefab,
    //        origin,
    //        rotation,
    //        owner,
    //        direction,
    //        projectileSpeed,
    //        owner.Definition.attackDamage,
    //        projectileLifetime,
    //        projectileCollisionRadius,
    //        projectileCollisionMask
    //    );

    //    if (projectile == null)
    //        return false;

    //    cooldownRemaining = owner.Definition.attackCooldown;

    //    owner.View?.PlayOneShotAnim("Attack");

    //    return true;
    //}

    public bool TryBeginAttack(ITargetable target)
    {
        if (!IsReady || !CanFireAt(target))
            return false;

        if (projectilePrefab == null || projectileManager == null)
            return false;

        cooldownRemaining = owner.Definition.attackCooldown;

        return true;
    }

    public bool TryFireProjectile(ITargetable target)
    {
        if (!CanFireAt(target))
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

        if (target == null || !target.IsAlive)
            return false;

        if (owner.OwnerFaction == null || target.OwnerFaction == null)
            return false;

        if (!owner.OwnerFaction.IsEnemy(target.OwnerFaction))
            return false;

        float range = owner.Definition.attackRange;
        return (target.Position - owner.Position).sqrMagnitude <= range * range;
    }
}
