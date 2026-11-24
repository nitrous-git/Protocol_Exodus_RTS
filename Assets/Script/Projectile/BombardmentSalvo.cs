using UnityEngine;
using UnityEngine.Pool;

internal sealed class BombardmentSalvo
{
    private const float GoldenAngle = 2.39996323f;

    private readonly GameContext context;
    private readonly DamageInfo damage;
    private readonly float radius;
    private readonly ParticleSystem impactPrefab;
    private int launchLimit;
    private int impacts;
    private bool damageResolved;

    internal Vector3 Center { get; }
    internal Vector3[] Destinations { get; }
    internal int Launched { get; private set; }
    internal bool IsLaunching => Launched < launchLimit;
    internal bool IsActive => IsLaunching || impacts < Launched;
    internal ITargetable Source => damage.Source;

    internal BombardmentSalvo(GameContext context, CombatUnit owner, Vector3 center, float radius,
        int projectileCount, ParticleSystem impactPrefab)
    {
        this.context = context;
        damage = new DamageInfo(owner.Definition.attackDamage, owner);
        this.radius = radius;
        this.impactPrefab = impactPrefab;
        launchLimit = projectileCount;

        TerrainGrid grid = context.TerrainGrid;
        center.y = grid.SampleHeight(center);
        Center = center;
        Destinations = new Vector3[projectileCount];
        Destinations[projectileCount / 2] = center;

        // Rotate the disk pattern with the attacker and sample increasingly wide rings.
        float heading = owner.transform.eulerAngles.y * Mathf.Deg2Rad;
        for (int shot = 0, point = 0; shot < projectileCount; shot++)
        {
            if (shot == projectileCount / 2)
                continue;

            float distance = radius * Mathf.Sqrt((point + 1f) / (projectileCount - 1));
            float angle = heading + point * GoldenAngle;
            Vector3 position = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * distance;
            // Reflect points at the terrain edges so even a salvo at the map border lands on terrain.
            position = grid.ReflectIntoTerrainBounds(position);
            position.y = grid.SampleHeight(position);
            Destinations[shot] = position;
            point++;
        }
    }

    internal void Launch(Projectile prefab, Vector3 origin, float speed)
    {
        context.ProjectileManager.SpawnDestinationProjectile(prefab, origin, Destinations[Launched], speed, this);
        Launched++;
    }

    internal void StopLaunching() => launchLimit = Launched;

    internal void Impact(Vector3 position)
    {
        context.ProjectileManager.SpawnBombardmentImpact(impactPrefab, position);
        impacts++;
        if (damageResolved || impacts < Mathf.Min((Destinations.Length + 1) / 2, launchLimit))
            return;

        damageResolved = true;
        ApplyDamage();
    }

    private void ApplyDamage()
    {
        // The match registry contains each entity once, regardless of its collider count.
        // Gather the hits before applying damage in case death callbacks change the registry.
        using var pooled = ListPool<ITargetable>.Get(out var hits);
        foreach (ITargetable target in context.AllTargetables)
        {
            if (target is not Object entity || entity == null || !target.IsAlive ||
                !damage.SourceFaction.IsEnemy(target.OwnerFaction))
                continue;

            Vector3 offset = target.Position - Center;
            if (target is UnitBase { IsAirUnit: false } unit)
            {
                float reach = radius + unit.NavigationRadius;
                if (offset.x * offset.x + offset.z * offset.z <= reach * reach)
                    hits.Add(target);
            }
            else if (target is BuildingBase building)
            {
                Vector2 halfSize = (Vector2)building.PlacedFootprintSize * (0.5f * context.TerrainGrid.CellSize);
                float x = Mathf.Max(0f, Mathf.Abs(offset.x) - halfSize.x);
                float z = Mathf.Max(0f, Mathf.Abs(offset.z) - halfSize.y);
                if (x * x + z * z <= radius * radius)
                    hits.Add(target);
            }
        }

        foreach (ITargetable target in hits)
            target.TakeDamage(damage);
    }
}
