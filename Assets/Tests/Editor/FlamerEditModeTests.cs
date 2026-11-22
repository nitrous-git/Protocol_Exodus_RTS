using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class FlamerEditModeTests
{
    private GameObject world;
    private GameContext context;
    private Faction friendly;
    private Faction enemy;

    [SetUp]
    public void SetUp()
    {
        var roster = AssetDatabase.LoadAssetAtPath<FactionDefinition>(
            "Assets/Data/Faction/FactionDefinition/FactionDefinition_Terran.asset");
        Assert.That(roster, Is.Not.Null);

        world = new GameObject("Flamer test world") { hideFlags = HideFlags.HideAndDontSave };
        world.transform.position = new Vector3(1000f, 0f, 1000f);
        context = new GameContext();
        friendly = CreateFaction(roster, FactionColorType.Blue);
        enemy = CreateFaction(roster, FactionColorType.Red);
    }

    [TearDown]
    public void TearDown()
    {
        if (world != null)
            Object.DestroyImmediate(world);
    }

    [Test]
    public void BurstHitsEachLivingGroundEnemyInsideTheConeOnce()
    {
        // The Flamer initializes and begins its attack without a ProjectileManager.
        var flamer = (CombatUnit)Spawn(friendly, UnitType.Flamer, 0f, 0f);
        UnitBase target = Spawn(enemy, UnitType.Worker, 0f, 3f);
        UnitBase secondTarget = Spawn(enemy, UnitType.Worker, 2.1f, 4.7f);
        UnitBase outsideRange = Spawn(enemy, UnitType.Worker, 0f, 5.6f);
        UnitBase outsideAngle = Spawn(enemy, UnitType.Worker, 3f, 3f);
        UnitBase behind = Spawn(enemy, UnitType.Worker, 0f, -2f);
        UnitBase ally = Spawn(friendly, UnitType.Worker, -0.4f, 3f);
        UnitBase dead = Spawn(enemy, UnitType.Worker, 0.3f, 3.4f);
        dead.TakeDamage(new DamageInfo(dead.Health.MaxHealth, flamer));

        var child = new GameObject("Additional target collider") { layer = target.gameObject.layer };
        child.transform.SetParent(target.transform, false);
        child.AddComponent<SphereCollider>().radius = 0.3f;

        var buildingObject = new GameObject("Target building", typeof(BuildingBase), typeof(BoxCollider))
        {
            layer = LayerMask.NameToLayer("Building")
        };
        buildingObject.transform.SetParent(world.transform, false);
        buildingObject.transform.localPosition = new Vector3(-1.2f, 0f, 4f);
        var building = buildingObject.GetComponent<BuildingBase>();
        building.Initialize(enemy.Definition.GetBuildingDefinition(BuildingType.SupplyDepot),
            enemy, context, enemy.BuildingManager, default);

        context.SetProjectileManager(new ProjectileManager(world.transform));
        UnitBase aircraft = Spawn(enemy, UnitType.Aircraft, 0.6f, 3.5f);
        // Put an aircraft's collider inside the overlap volume to exercise the target filter.
        aircraft.transform.position = world.transform.position + new Vector3(0.6f, 0f, 3.5f);
        Physics.SyncTransforms();

        Assert.That(flamer.IsValidAttackTarget(aircraft), Is.False);
        StartAttack(flamer, target);
        flamer.OnWeaponFireAnimationEvent();
        flamer.OnWeaponFireAnimationEvent();
        flamer.OnAttackAnimationFinished();

        float damage = flamer.Definition.attackDamage;
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth - damage));
        Assert.That(secondTarget.Health.CurrentHealth, Is.EqualTo(secondTarget.Health.MaxHealth - damage));
        Assert.That(building.Health.CurrentHealth, Is.EqualTo(building.Health.MaxHealth - damage));
        foreach (UnitBase untouched in new[] { flamer, outsideRange, outsideAngle, behind, ally, aircraft })
            Assert.That(untouched.Health.CurrentHealth, Is.EqualTo(untouched.Health.MaxHealth), untouched.name);
    }

    [Test]
    public void CancelledAttackDiscardsItsFireEventAndCanAttackAgainAfterCooldown()
    {
        var flamer = (CombatUnit)Spawn(friendly, UnitType.Flamer, 0f, 0f);
        UnitBase target = Spawn(enemy, UnitType.Worker, 0f, 3f);
        Physics.SyncTransforms();

        StartAttack(flamer, target);
        flamer.IssueCommand(CommandType.HoldPosition, CommandContext.None());
        flamer.OnWeaponFireAnimationEvent();
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth));
        Assert.That(flamer.Weapon.IsReady, Is.False);

        flamer.Weapon.Tick(flamer.Definition.attackCooldown);
        StartAttack(flamer, target);
        flamer.OnWeaponFireAnimationEvent();
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth - flamer.Definition.attackDamage));
    }

    [TestCase(UnitType.Combat)]
    [TestCase(UnitType.Mech)]
    public void ExistingGroundWeaponStillFiresAProjectileFromItsAnimationEvent(UnitType type)
    {
        context.SetProjectileManager(new ProjectileManager(world.transform));
        var attacker = (CombatUnit)Spawn(friendly, type, 0f, 0f);
        UnitBase target = Spawn(enemy, UnitType.Worker, 0f, 5f);
        Physics.SyncTransforms();

        StartAttack(attacker, target);
        attacker.OnWeaponFireAnimationEvent();

        Assert.That(context.ProjectileManager.Projectiles.Count, Is.EqualTo(1));
        Assert.That(context.ProjectileManager.Projectiles[0].Tick(1f), Is.False);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth - attacker.Definition.attackDamage));
    }

    private Faction CreateFaction(FactionDefinition roster, FactionColorType color) =>
        new(roster, color, null, new UnitManager(context, null, world.transform),
            new BuildingManager(context, null, world.transform), null, context);

    private UnitBase Spawn(Faction faction, UnitType type, float x, float z)
    {
        UnitBase prefab = faction.Definition.GetUnitDefinition(type).Prefab;
        Vector3 position = world.transform.position + new Vector3(x, 0f, z);
        return faction.UnitManager.SpawnUnit(prefab, position, Quaternion.identity, preserveSpawnPosition: true);
    }

    private static void StartAttack(CombatUnit flamer, ITargetable target)
    {
        flamer.IssueCommand(CommandType.Attack, CommandContext.AttackTarget(target));
        flamer.UpdateAttack(0.1f);
    }
}
