using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public sealed class ProjectileManager
{
    private const float BombardmentImpactLifetime = 1.5f;
    private readonly List<Projectile> projectiles = new List<Projectile>();
    private readonly Transform projectileRoot;
    private readonly Dictionary<Projectile, ObjectPool<Projectile>> destinationPools = new();
    private readonly Dictionary<ParticleSystem, ObjectPool<ParticleSystem>> impactPools = new();
    private readonly List<(ParticleSystem Effect, ObjectPool<ParticleSystem> Pool, float Remaining)> impacts = new();

    public IReadOnlyList<Projectile> Projectiles => projectiles;

    public ProjectileManager(Transform projectileRoot)
    {
        this.projectileRoot = projectileRoot;
    }

    public Projectile SpawnProjectile(
    Projectile prefab,
    Vector3 position,
    Quaternion rotation,
    ITargetable source,
    Vector3 direction,
    float speed,
    float damage,
    float maxLifetime,
    float collisionRadius,
    LayerMask collisionMask)
    {
        if (prefab == null)
        {
            Debug.LogError("ProjectileManager cannot spawn a projectile because the prefab is missing.");
            return null;
        }

        Projectile projectile = Object.Instantiate(prefab, position, rotation, projectileRoot);
        projectile.Initialize(
            source,
            direction,
            speed,
            damage,
            maxLifetime,
            collisionRadius,
            collisionMask,
            projectileRoot
        );

        projectiles.Add(projectile);
        return projectile;
    }

    internal void SpawnDestinationProjectile(Projectile prefab, Vector3 origin, Vector3 destination, float speed,
        BombardmentSalvo salvo)
    {
        if (!destinationPools.TryGetValue(prefab, out var pool))
        {
            pool = new ObjectPool<Projectile>(
                () => Object.Instantiate(prefab, projectileRoot),
                actionOnRelease: projectile => projectile.ResetDestination(),
                actionOnDestroy: projectile => Object.Destroy(projectile.gameObject), maxSize: 128);
            destinationPools.Add(prefab, pool);
        }

        Projectile shot = pool.Get();
        shot.DestinationPool = pool;
        Vector3 direction = destination - origin;
        Quaternion rotation = direction.sqrMagnitude > 0f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        shot.transform.SetPositionAndRotation(origin, rotation);
        shot.InitializeDestination(destination, speed, salvo);
        projectiles.Add(shot);
    }

    internal void SpawnBombardmentImpact(ParticleSystem prefab, Vector3 position)
    {
        if (!Application.isPlaying || prefab == null)
            return;

        if (!impactPools.TryGetValue(prefab, out var pool))
        {
            pool = new ObjectPool<ParticleSystem>(
                () => Object.Instantiate(prefab, projectileRoot),
                actionOnRelease: effect =>
                {
                    effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    effect.gameObject.SetActive(false);
                },
                actionOnDestroy: effect => Object.Destroy(effect.gameObject), maxSize: 128);
            impactPools.Add(prefab, pool);
        }

        ParticleSystem impact = pool.Get();
        impact.transform.position = position;
        impact.gameObject.SetActive(true);
        impact.Play(true);
        impacts.Add((impact, pool, BombardmentImpactLifetime));
    }

    public void Tick(float deltaTime)
    {
        for (int i = impacts.Count - 1; i >= 0; i--)
        {
            var (effect, pool, remaining) = impacts[i];
            remaining -= deltaTime;
            // Off-screen particle systems can pause their simulation, so also bound their lifetime.
            if (effect != null && remaining > 0f && effect.IsAlive(true))
            {
                impacts[i] = (effect, pool, remaining);
                continue;
            }

            impacts.RemoveAt(i);
            if (effect != null)
                pool.Release(effect);
        }

        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            Projectile projectile = projectiles[i];

            if (projectile == null || !projectile.Tick(deltaTime))
            {
                projectiles.RemoveAt(i);

                if (projectile != null && projectile.DestinationPool != null)
                    projectile.DestinationPool.Release(projectile);
                else if (projectile != null)
                    Object.Destroy(projectile.gameObject);
            }
        }
    }

    public void Clear()
    {
        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            if (projectiles[i] != null)
                Object.Destroy(projectiles[i].gameObject);
        }

        projectiles.Clear();
        foreach (var (effect, _, _) in impacts)
        {
            if (effect != null)
                Object.Destroy(effect.gameObject);
        }
        impacts.Clear();

        foreach (var pool in destinationPools.Values)
            pool.Clear();
        destinationPools.Clear();

        foreach (var pool in impactPools.Values)
            pool.Clear();
        impactPools.Clear();
    }

}
