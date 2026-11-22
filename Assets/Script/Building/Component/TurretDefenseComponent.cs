using UnityEngine;

[DisallowMultipleComponent]
public sealed class TurretDefenseComponent : MonoBehaviour
{
    [Header("Targeting")]
    [SerializeField, Min(0.01f)] private float range = 12f;
    [SerializeField, Min(0.01f)] private float scanInterval = 0.25f;

    [Header("Aiming")]
    [SerializeField] private Transform towerPivot;
    [SerializeField] private Transform gunPivot;
    [SerializeField] private Transform firePoint;
    [SerializeField] private Transform secondaryFirePoint;
    [SerializeField, Min(1f)] private float towerRotationSpeed = 180f;
    [SerializeField] private bool aimPitch = true;
    [SerializeField, Min(1f)] private float gunPitchSpeed = 120f;
    [Tooltip("Local X angle limits from the deployed pose. Positive angles lower the barrel.")]
    [SerializeField] private Vector2 gunPitchLimits = new(-55f, 75f);
    [SerializeField, Range(0.1f, 15f)] private float firingTolerance = 5f;

    [Header("Projectile")]
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0f)] private float damage = 15f;
    [SerializeField, Min(0.01f)] private float fireCooldown = 1.2f;
    [SerializeField, Min(0.01f)] private float projectileSpeed = 18f;
    [SerializeField, Min(0.01f)] private float projectileLifetime = 4f;
    [SerializeField, Min(0f)] private float projectileCollisionRadius = 0.1f;
    [SerializeField] private LayerMask projectileCollisionMask = ~0;

    [Header("Deployment")]
    [SerializeField] private Animation deploymentAnimation;

    private BuildingBase owner;
    private GameContext gameContext;
    private AnimationClip deploymentClip;
    private bool deploymentStarted;
    private bool isDeployed;
    private float deploymentTime;
    private Quaternion deployedGunRotation;
    private float gunPitch;
    private ITargetable target;
    private float scanRemaining;
    private float cooldownRemaining;
    private bool fireFromSecondary;

    public void Initialize(BuildingBase building, GameContext context)
    {
        owner = building;
        gameContext = context;

        if (deploymentAnimation != null)
        {
            deploymentAnimation.playAutomatically = false;
            deploymentAnimation.Stop();
            deploymentAnimation.enabled = false;
            deploymentClip = deploymentAnimation.clip;
        }
    }

    public void BeginDeployment()
    {
        if (deploymentStarted)
            return;

        if (deploymentClip == null || towerPivot == null || firePoint == null ||
            (aimPitch && gunPivot == null) || projectilePrefab == null || gameContext.ProjectileManager == null)
        {
            Debug.LogError("Turret defense is missing a deployment clip, aiming transform, or projectile dependency.", this);
            return;
        }

        deploymentStarted = true;
        deploymentClip.SampleAnimation(deploymentAnimation.gameObject, 0f);
    }

    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || !deploymentStarted)
            return;

        if (!isDeployed)
        {
            TickDeployment(deltaTime);
            return;
        }

        if (cooldownRemaining > 0f)
            cooldownRemaining -= deltaTime;
        if (scanRemaining > 0f)
            scanRemaining -= deltaTime;

        float rangeSquared = range * range;
        Vector3 origin = owner.Position;
        if (EnemyDistanceSquared(target, origin) > rangeSquared)
        {
            target = null;
            if (scanRemaining <= 0f)
            {
                target = FindNearestEnemy(origin, rangeSquared);
                scanRemaining = scanInterval;
            }
        }

        if (target == null)
            return;

        Transform aimPoint = target.AimPoint;
        Vector3 targetPosition = aimPoint != null ? aimPoint.position : target.Position;
        Transform muzzle = fireFromSecondary && secondaryFirePoint != null ? secondaryFirePoint : firePoint;
        AimAt(muzzle, targetPosition, deltaTime);

        if (cooldownRemaining <= 0f)
            TryFire(muzzle, targetPosition);
    }

    public void NotifyBuildingRemoved()
    {
        deploymentStarted = false;
        target = null;
    }

    private void TickDeployment(float deltaTime)
    {
        // Sample only during deployment so both pivots retain their final pose and are free to aim.
        deploymentTime = Mathf.Min(deploymentTime + deltaTime, deploymentClip.length);
        deploymentClip.SampleAnimation(deploymentAnimation.gameObject, deploymentTime);
        if (deploymentTime < deploymentClip.length)
            return;

        if (aimPitch)
            deployedGunRotation = gunPivot.localRotation;

        // Stagger initial scans when several turrets finish deploying together.
        uint hash = unchecked((uint)owner.BuildingId * 2654435761u);
        scanRemaining = (hash & 0xFFFFu) / 65535f * scanInterval;
        isDeployed = true;
    }

    private ITargetable FindNearestEnemy(Vector3 origin, float rangeSquared)
    {
        ITargetable nearest = null;
        float nearestDistance = float.PositiveInfinity;
        var targetables = gameContext.AllTargetables;
        for (int i = 0; i < targetables.Count; i++)
        {
            ITargetable candidate = targetables[i];
            float distance = EnemyDistanceSquared(candidate, origin);
            if (distance <= rangeSquared && distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private float EnemyDistanceSquared(ITargetable candidate, Vector3 origin)
    {
        if (candidate == null || (candidate is Object unityObject && unityObject == null) ||
            !candidate.IsAlive || owner.OwnerFaction?.IsEnemy(candidate.OwnerFaction) != true)
            return float.PositiveInfinity;

        Vector3 offset = candidate.Position - origin;
        if (candidate is not UnitBase { IsAirUnit: true })
            offset.y = 0f;
        return offset.sqrMagnitude;
    }

    private void AimAt(Transform muzzle, Vector3 position, float deltaTime)
    {
        float yawError = GetAimError(towerPivot, towerPivot.up, muzzle, position);
        float yawStep = towerRotationSpeed * deltaTime;
        towerPivot.Rotate(Vector3.up, Mathf.Clamp(yawError, -yawStep, yawStep), Space.Self);

        if (!aimPitch)
            return;

        float pitchError = GetAimError(gunPivot, gunPivot.right, muzzle, position);
        float desiredPitch = Mathf.Clamp(gunPitch + pitchError, gunPitchLimits.x, gunPitchLimits.y);
        gunPitch = Mathf.MoveTowards(gunPitch, desiredPitch, gunPitchSpeed * deltaTime);
        gunPivot.localRotation = deployedGunRotation * Quaternion.AngleAxis(gunPitch, Vector3.right);
    }

    private static float GetAimError(Transform pivot, Vector3 axis, Transform muzzle, Vector3 position)
    {
        Vector3 toTarget = Vector3.ProjectOnPlane(position - pivot.position, axis);
        Vector3 forward = Vector3.ProjectOnPlane(muzzle.forward, axis).normalized;
        float distance = toTarget.magnitude;
        if (distance < 0.001f || forward == Vector3.zero)
            return 0f;

        // Account for a muzzle that sits to one side of the pivot's aiming plane.
        float offset = Vector3.Dot(muzzle.position - pivot.position, Vector3.Cross(axis, forward));
        float correction = Mathf.Asin(Mathf.Clamp(offset / distance, -1f, 1f)) * Mathf.Rad2Deg;
        return Mathf.DeltaAngle(0f, Vector3.SignedAngle(forward, toTarget, axis) - correction);
    }

    private void TryFire(Transform muzzle, Vector3 position)
    {
        Vector3 direction = position - muzzle.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Vector3 barrelForward = muzzle.forward;
        Vector3 alignmentDirection = direction;
        if (!aimPitch)
        {
            barrelForward = Vector3.ProjectOnPlane(barrelForward, towerPivot.up);
            alignmentDirection = Vector3.ProjectOnPlane(alignmentDirection, towerPivot.up);
        }

        if (Vector3.Angle(barrelForward, alignmentDirection) > firingTolerance)
            return;

        direction.Normalize();
        Projectile projectile = gameContext.ProjectileManager.SpawnProjectile(
            projectilePrefab, muzzle.position, Quaternion.LookRotation(direction), owner, direction,
            projectileSpeed, damage, projectileLifetime, projectileCollisionRadius, projectileCollisionMask);
        if (projectile != null)
        {
            cooldownRemaining = fireCooldown;
            fireFromSecondary = secondaryFirePoint != null && !fireFromSecondary;
        }
    }
}
