using System.Collections.Generic;
using UnityEngine;

public sealed class ProjectileManager
{
    private readonly List<Projectile> projectiles = new List<Projectile>();
    private readonly Transform projectileRoot;

    public IReadOnlyList<Projectile> Projectiles => projectiles;

    public ProjectileManager(Transform projectileRoot)
    {
        this.projectileRoot = projectileRoot;
    }

    public Projectile SpawnProjectile(
    Projectile prefab,
    Vector3 position,
    Quaternion rotation,
    UnitBase source,
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
            collisionMask
        );

        projectiles.Add(projectile);
        return projectile;
    }

    public void Tick(float deltaTime)
    {
        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            Projectile projectile = projectiles[i];

            if (projectile == null || !projectile.Tick(deltaTime))
            {
                projectiles.RemoveAt(i);

                if (projectile != null)
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
    }

}
