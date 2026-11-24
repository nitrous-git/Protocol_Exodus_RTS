using UnityEngine;
using UnityEngine.Pool;

public class Projectile : MonoBehaviour
{
    private const int HitBufferSize = 8;

    [Header("Selection")]
    [SerializeField] private GameObject hitCollisionFx;
    [SerializeField] private ParticleSystem destinationEffect;

    private readonly RaycastHit[] hitBuffer = new RaycastHit[HitBufferSize];

    private ITargetable source;
    private Transform sourceTransform;
    private Faction sourceFaction;
    private Vector3 direction;
    private float speed;
    private float damage;
    private float remainingLifetime;
    private float collisionRadius;
    private LayerMask collisionMask;
    private bool initialized;
    private Transform projectileRoot;
    private BombardmentSalvo salvo;
    private Vector3 destination;

    internal ObjectPool<Projectile> DestinationPool { get; set; }

    public ITargetable Source => source;

    internal void InitializeDestination(Vector3 point, float projectileSpeed, BombardmentSalvo bombardment)
    {
        destination = point;
        speed = projectileSpeed;
        salvo = bombardment;
        source = salvo.Source;
        initialized = true;
        gameObject.SetActive(true);
        if (Application.isPlaying && destinationEffect != null)
            destinationEffect.Play(true);
    }

    internal void ResetDestination()
    {
        if (destinationEffect != null)
            destinationEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        salvo = null;
        source = null;
        initialized = false;
        gameObject.SetActive(false);
    }

    public void Initialize(
        ITargetable source,
        Vector3 direction,
        float speed,
        float damage,
        float maxLifetime,
        float collisionRadius,
        LayerMask collisionMask,
        Transform projectileRoot)
    {
        this.source = source;
        sourceFaction = source?.OwnerFaction;
        sourceTransform = source is Component component && component != null ? component.transform : null;
        this.direction = direction.normalized;
        this.speed = Mathf.Max(0f, speed);
        this.damage = Mathf.Max(0f, damage);
        remainingLifetime = Mathf.Max(0.01f, maxLifetime);
        this.collisionRadius = Mathf.Max(0f, collisionRadius);
        this.collisionMask = collisionMask;
        initialized = true;
        this.projectileRoot = projectileRoot;
    }

    // Returns false when the projectile should be removed by ProjectileManager.
    public bool Tick(float deltaTime)
    {
        if (!initialized) return false;
        if (salvo != null)
            return TickDestination(deltaTime);

        remainingLifetime -= deltaTime;
        if (remainingLifetime <= 0.0f) return false;

        float distance = speed * deltaTime;
        if (distance <= 0.0f) return true;

        if (TryGetFirstCollision(distance, out RaycastHit hit))
        {
            transform.position = hit.point;
            ResolveHit(hit.collider);
            return false;
        }

        transform.position += direction * distance;
        return true;
    }


    private bool TickDestination(float deltaTime)
    {
        Vector3 position = Vector3.MoveTowards(transform.position, destination, speed * deltaTime);
        transform.position = position;
        if (position != destination)
            return true;

        BombardmentSalvo impactSalvo = salvo;
        salvo = null;
        initialized = false;
        impactSalvo.Impact(destination);
        return false;
    }

    private bool TryGetFirstCollision(float distance, out RaycastHit closestHit)
    {
        int hitCount = Physics.SphereCastNonAlloc(transform.position,
                                                collisionRadius,          // zero radius works like a raycast
                                                direction,
                                                hitBuffer,
                                                distance,
                                                collisionMask,
                                                QueryTriggerInteraction.Ignore);

        float closestDistance = float.MaxValue;
        closestHit = default;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = hitBuffer[i];
            if (IsOwnCollider(candidate.collider)) continue;

            ITargetable targetable = ResolveTargetable(candidate.collider);
            if (targetable != null && targetable.OwnerFaction == sourceFaction) continue;

            if (candidate.distance < closestDistance)
            {
                closestDistance = candidate.distance;
                closestHit = candidate;
                found = true;
            }
        }

        return found;
    }

    private void ResolveHit(Collider hitCollider)
    {
        ITargetable targetable = ResolveTargetable(hitCollider);
        if (targetable == null || !targetable.IsAlive) return;

        SpawnHitColisionFx();
        targetable.TakeDamage(new DamageInfo(damage, source, sourceFaction));
    }

    private void SpawnHitColisionFx()
    {
        if (!Application.isPlaying || hitCollisionFx == null)
            return;

        GameObject hitFx = GameObject.Instantiate(hitCollisionFx, this.transform.position, this.transform.rotation, projectileRoot);
        Object.Destroy(hitFx, 1.3f);
    }

    private bool IsOwnCollider(Collider candidate)
    {
        if (candidate == null) return false;

        Transform t = candidate.transform;
        Transform self = transform;

        return t == self || t.IsChildOf(self) ||
               (sourceTransform != null && (t == sourceTransform || t.IsChildOf(sourceTransform)));
    }

    private static ITargetable ResolveTargetable(Collider candidate)
    {
        return candidate?.GetComponentInParent<ITargetable>(true);
    }
}
