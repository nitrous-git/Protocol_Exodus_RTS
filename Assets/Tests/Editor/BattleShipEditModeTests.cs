using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class BattleShipEditModeTests
{
    private const string TerranPath = "Assets/Data/Faction/FactionDefinition/FactionDefinition_Terran.asset";
    private const float FlightAltitude = 6f;
    private TerrainData terrainData;
    private TerrainGrid grid;
    private GameContext context;
    private GridNavigationStateSystem navigation;
    private FactionDefinition roster;
    private RecordingPathfinding pathfinding;
    private Transform world;

    [SetUp]
    public void SetUp()
    {
        world = new GameObject("Battle Ship test world") { hideFlags = HideFlags.HideAndDontSave }.transform;
        world.position = new Vector3(1000f, 0f, 1000f);
        terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(96f, 10f, 96f) };
        var terrain = Terrain.CreateTerrainGameObject(terrainData).GetComponent<Terrain>();
        terrain.transform.SetParent(world, false);
        grid = new TerrainGrid(terrain, 1f, 35f, 8f);
        navigation = new GridNavigationStateSystem(grid);
        pathfinding = new RecordingPathfinding();
        context = new GameContext();
        context.SetTerrainGrid(grid);
        context.SetGridNavigationStateSystem(navigation);
        context.SetDestinationAllocationSystem(new DestinationAllocationSystem(grid, navigation));
        context.SetPathfindingService(pathfinding);
        context.SetProjectileManager(new ProjectileManager(world));
        roster = AssetDatabase.LoadAssetAtPath<FactionDefinition>(TerranPath);
        Assert.That(roster, Is.Not.Null);
    }

    [TearDown]
    public void TearDown()
    {
        if (world != null)
            Object.DestroyImmediate(world.gameObject);
        if (terrainData != null)
            Object.DestroyImmediate(terrainData);
    }

    [Test]
    public void SelectionMarkerUsesUniformScaleAndStaysAtFlightAltitude()
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        UnitBase ship = Spawn(faction, UnitType.Aircraft, 10, 17);
        Transform marker = ship.transform.Find("vfx_RoundMarker01_Blue");

        Assert.That(marker, Is.Not.Null);
        Assert.That(marker.localScale, Is.EqualTo(Vector3.one * 0.4f));
        Assert.That(marker.localPosition, Is.EqualTo(Vector3.zero));
        Assert.That(marker.gameObject.activeSelf, Is.False);
        ParticleSystem[] systems = marker.GetComponentsInChildren<ParticleSystem>(true);
        Assert.That(systems, Is.Not.Empty);
        foreach (ParticleSystem system in systems)
            Assert.That(system.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.Local));

        context.SelectUnit(ship, append: false);
        Assert.That(marker.gameObject.activeInHierarchy, Is.True);
        Vector3 destination = At(15, 17);
        ship.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination));
        ship.Tick(1f);
        ship.TickLate(1f);

        Assert.That(ship.Position, Is.EqualTo(new Vector3(destination.x, FlightAltitude, destination.z)));
        Assert.That(marker.position, Is.EqualTo(ship.Position));
        context.ClearSelectedUnits();
        Assert.That(marker.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void MixedTravelUsesGroundFormationAndSeparateAirDestinations()
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        UnitBase worker = Spawn(faction, UnitType.Worker, 10, 10);
        UnitBase mecha = Spawn(faction, UnitType.Mech, 14, 10);
        UnitBase firstShip = Spawn(faction, UnitType.Aircraft, 10, 17);
        UnitBase secondShip = Spawn(faction, UnitType.Aircraft, 16, 17);
        var issuer = CreateIssuer(faction, worker, firstShip, mecha, secondShip);
        Vector3 destination = At(40, 38);

        Assert.That(issuer.TryIssueMoveCommand(destination), Is.True);
        Assert.That(worker.MovementGroupId, Is.GreaterThan(0));
        Assert.That(mecha.MovementGroupId, Is.EqualTo(worker.MovementGroupId));
        Assert.That(firstShip.MovementGroupId, Is.Zero);
        Assert.That(secondShip.MovementGroupId, Is.Zero);
        Assert.That(pathfinding.Requesters, Has.Member(worker));
        Assert.That(pathfinding.Requesters, Has.Member(mecha));
        Assert.That(pathfinding.Requesters, Has.No.Member(firstShip));
        Assert.That(pathfinding.Requesters, Has.No.Member(secondShip));
        Assert.That(navigation.GetOccupiedCell(firstShip), Is.Null);
        Assert.That(navigation.GetOccupiedCell(secondShip), Is.Null);
        Assert.That(navigation.OccupiedCellCount, Is.EqualTo(2));

        Assert.That(issuer.TryIssueAttackMoveCommand(destination), Is.True);
        Assert.That(worker.CurrentCommand, Is.EqualTo(CommandType.Move));
        Assert.That(mecha.CurrentCommand, Is.EqualTo(CommandType.AttackMove));
        Assert.That(firstShip.CurrentCommand, Is.EqualTo(CommandType.AttackMove));
        Assert.That(secondShip.CurrentCommand, Is.EqualTo(CommandType.AttackMove));
        Assert.That(firstShip.MovementGroupId, Is.Zero);
        Assert.That(secondShip.MovementGroupId, Is.Zero);
        for (int i = 0; i < 100; i++)
        {
            firstShip.Tick(0.1f);
            secondShip.Tick(0.1f);
        }

        Assert.That(firstShip.Position.y, Is.EqualTo(FlightAltitude));
        Assert.That(secondShip.Position.y, Is.EqualTo(FlightAltitude));
        Assert.That(firstShip.Motor.HasArrived && secondShip.Motor.HasArrived, Is.True);
        Assert.That(Vector3.Distance(firstShip.Position, secondShip.Position),
            Is.GreaterThanOrEqualTo(firstShip.NavigationRadius + secondShip.NavigationRadius));

        firstShip.IssueCommand(CommandType.Move, CommandContext.MoveTo(At(-100, 38)));
        for (int i = 0; i < 100; i++)
            firstShip.Tick(0.1f);
        Assert.That(firstShip.Position.x, Is.EqualTo(world.position.x + firstShip.NavigationRadius).Within(0.001f));
        Assert.That(firstShip.Position.y, Is.EqualTo(FlightAltitude));
    }

    [Test]
    public void MixedAttackDeploysGroundUnitAndAircraftPursuesDirectly()
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        Faction enemy = CreateFaction(FactionColorType.Red);
        UnitBase mecha = Spawn(faction, UnitType.Mech, 10, 10);
        var ship = (CombatUnit)Spawn(faction, UnitType.Aircraft, 10, 15);
        UnitBase target = Spawn(enemy, UnitType.Combat, 45, 15);
        var issuer = CreateIssuer(faction, ship, mecha);

        Assert.That(issuer.TryIssueAttackCommand(target), Is.True);
        Assert.That(mecha.MovementGroupId, Is.GreaterThan(0));
        Assert.That(navigation.DestinationReservationCount, Is.Positive);
        Assert.That(ship.CurrentTarget, Is.SameAs(target));
        Assert.That(ship.MovementGroupId, Is.Zero);
        int reservations = navigation.DestinationReservationCount;
        for (int i = 0; i < 60; i++)
            ship.Tick(0.1f);

        Assert.That(ship.Motor.HasArrived, Is.True);
        Assert.That(ship.IsWithinAttackRange(target), Is.True);
        Assert.That(ship.Weapon.CanFireAt(target), Is.True);
        Assert.That(context.ProjectileManager.Projectiles, Is.Not.Empty);
        Assert.That(pathfinding.Requesters, Has.No.Member(ship));
        Assert.That(navigation.DestinationReservationCount, Is.EqualTo(reservations));
        Assert.That(navigation.GetOccupiedCell(ship), Is.Null);

        target.transform.position += Vector3.right * 20f;
        ship.Tick(0.1f);
        Assert.That(ship.Motor.HasArrived, Is.False);
        Assert.That(issuer.TryIssueHoldPositionCommand(), Is.True);
        Vector3 heldPosition = ship.Position;
        ship.Tick(1f);
        Assert.That(ship.Position, Is.EqualTo(heldPosition));
        Assert.That(ship.CurrentCommand, Is.EqualTo(CommandType.HoldPosition));
    }

    [Test]
    public void AircraftQueueDeploysWithoutPathfindingAndAccountsForSupplyOnce()
    {
        Faction faction = CreateFaction(FactionColorType.Blue, usePathfinding: false);
        BuildingDefinition facility = roster.GetBuildingDefinition(BuildingType.HeavyProductionFacility);
        BuildingBase building = faction.BuildingManager.Construct(facility, new GridCoord(33, 33), rotationSteps: 1);
        Assert.That(building, Is.Not.Null);
        var production = building.UnitProduction;
        UnitDefinition ship = roster.GetUnitDefinition(UnitType.Aircraft);
        Assert.That(production.Supports(ship), Is.True);
        Assert.That(production.CanEnqueue(ship), Is.False);
        building.Tick(facility.ConstructionDuration);

        int minerals = faction.ResourceManager.Minerals;
        int gas = faction.ResourceManager.Gas;
        Assert.That(production.TryEnqueue(ship), Is.True);
        Assert.That(production.TryEnqueue(ship), Is.True);
        Assert.That(faction.UnitManager.ReservedPopulation, Is.EqualTo(6));
        Assert.That(faction.ResourceManager.Minerals, Is.EqualTo(minerals - 500));
        Assert.That(faction.ResourceManager.Gas, Is.EqualTo(gas - 200));
        production.Tick(7.5f);
        Assert.That(production.ProductionProgress(), Is.EqualTo(0.5f));
        production.Tick(7.5f);
        Assert.That(production.QueueCount, Is.EqualTo(1));
        Assert.That(faction.UnitManager.CurrentPopulation, Is.EqualTo(3));
        Assert.That(faction.UnitManager.ReservedPopulation, Is.EqualTo(3));
        UnitBase first = faction.UnitManager.UnitList[0];
        Transform launch = building.transform.Find("SpawnPoint");
        Assert.That(first.Position, Is.EqualTo(new Vector3(launch.position.x, FlightAltitude, launch.position.z)));
        Assert.That(Vector3.Dot(first.transform.forward, launch.forward), Is.GreaterThan(0.99f));
        Assert.That(first.CurrentCommand, Is.EqualTo(CommandType.Move));
        first.Tick(1f);
        Vector3 rally = production.EffectiveRallyPoint.Value;
        Assert.That(first.Position, Is.EqualTo(new Vector3(rally.x, FlightAltitude, rally.z)));
        first.IssueCommand(CommandType.Move, CommandContext.MoveTo(launch.position));
        first.Tick(1f);

        Assert.That(production.TryCancelAt(0), Is.True);
        Assert.That(faction.UnitManager.ReservedPopulation, Is.Zero);
        Assert.That(faction.ResourceManager.Minerals, Is.EqualTo(minerals - 250));
        Assert.That(faction.ResourceManager.Gas, Is.EqualTo(gas - 100));

        Assert.That(production.TryEnqueue(ship), Is.True);
        production.Tick(ship.ProductionDuration);
        Assert.That(production.QueueCount, Is.Zero);
        Assert.That(faction.UnitManager.CurrentPopulation, Is.EqualTo(6));
        Assert.That(faction.UnitManager.ReservedPopulation, Is.Zero);
        UnitBase second = faction.UnitManager.UnitList[1];
        Assert.That(second.CurrentCommand, Is.EqualTo(CommandType.Move));
        Assert.That(Vector3.Distance(first.Position, second.Position), Is.GreaterThan(2f * ship.NavigationRadius));
        Assert.That(navigation.DestinationReservationCount, Is.Zero);
        Assert.That(navigation.OccupiedCellCount, Is.Zero);
        Assert.That(pathfinding.Requesters, Is.Empty);

        Assert.That(production.TryEnqueue(ship), Is.True);
        building.NotifyRemoved();
        Assert.That(production.QueueCount, Is.Zero);
        Assert.That(faction.UnitManager.ReservedPopulation, Is.Zero);
        Assert.That(faction.UnitManager.CurrentPopulation, Is.EqualTo(6));
    }

    [Test]
    public void AircraftProjectileHitsAirTargetAtItsAimPointAndRespectsCooldown()
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        Faction enemy = CreateFaction(FactionColorType.Green);
        var ship = (CombatUnit)Spawn(faction, UnitType.Aircraft, 18, 18);
        UnitBase target = Spawn(enemy, UnitType.Aircraft, 18, 30);
        UnitBase friendly = Spawn(faction, UnitType.Aircraft, 18, 24);
        float targetHealth = target.Health.CurrentHealth;
        Physics.SyncTransforms();
        ship.IssueCommand(CommandType.Attack, CommandContext.AttackTarget(target));
        ship.Tick(0.1f);
        ship.OnWeaponFireAnimationEvent();
        ship.Tick(1f);

        Assert.That(context.ProjectileManager.Projectiles.Count, Is.EqualTo(1));
        Projectile projectile = context.ProjectileManager.Projectiles[0];
        Assert.That(projectile.Source, Is.SameAs(ship));
        Assert.That(projectile.transform.position, Is.EqualTo(ship.Weapon.FirePoint.position));
        Vector3 expectedDirection = (target.AimPoint.position - projectile.transform.position).normalized;
        Assert.That(Vector3.Dot(projectile.transform.forward, expectedDirection), Is.GreaterThan(0.999f));
        Assert.That(projectile.Tick(1f), Is.False);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(targetHealth - 35f));
        Assert.That(ship.Health.CurrentHealth, Is.EqualTo(650f));
        Assert.That(friendly.Health.CurrentHealth, Is.EqualTo(650f));

        ship.Tick(0.51f);
        Assert.That(context.ProjectileManager.Projectiles.Count, Is.EqualTo(2));
        target.TakeDamage(new DamageInfo(targetHealth, ship, faction));
        ship.Tick(0.1f);
        Assert.That(target.IsAlive, Is.False);
        Assert.That(ship.CurrentTarget, Is.Null);
        Assert.That(ship.Motor.HasArrived, Is.True);
    }

    [TestCase(FactionColorType.Red)]
    [TestCase(FactionColorType.Blue)]
    [TestCase(FactionColorType.Green)]
    public void BombardmentUsesWeaponMountsAndDealsDamageOnceOnFourthImpact(FactionColorType color)
    {
        Faction faction = CreateFaction(color);
        Faction enemy = CreateFaction(color == FactionColorType.Red ? FactionColorType.Blue : FactionColorType.Red);
        var ship = (CombatUnit)Spawn(faction, UnitType.Aircraft, 20, 18);
        UnitBase target = Spawn(enemy, UnitType.Mech, 20, 30);
        target.gameObject.AddComponent<SphereCollider>();
        float health = target.Health.CurrentHealth;
        ship.IssueCommand(CommandType.Attack, CommandContext.AttackTarget(target));
        ship.Tick(0.01f);
        var projectiles = context.ProjectileManager.Projectiles;
        Assert.That(projectiles.Count, Is.EqualTo(1));
        var weapon = new SerializedObject(ship.Weapon);
        var mounts = weapon.FindProperty("bombardmentFirePoints");
        Assert.That(projectiles[0].transform.position, Is.EqualTo(((Transform)mounts.GetArrayElementAtIndex(0).objectReferenceValue).position));
        ship.Tick(0.05f);
        Assert.That(projectiles.Count, Is.EqualTo(1));
        ship.Tick(0.011f);
        Assert.That(projectiles.Count, Is.EqualTo(2));
        Assert.That(projectiles[1].transform.position, Is.EqualTo(((Transform)mounts.GetArrayElementAtIndex(1).objectReferenceValue).position));
        ship.Tick(0.4f);
        Assert.That(projectiles.Count, Is.EqualTo(8));
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(health));
        var firstSalvo = new HashSet<Projectile>(projectiles);

        for (int i = 0; i < 3; i++)
            Assert.That(projectiles[i].Tick(2f), Is.False);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(health));
        Assert.That(projectiles[3].Tick(2f), Is.False);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(health - ship.Definition.attackDamage));
        context.ProjectileManager.Tick(2f);
        Assert.That(projectiles, Is.Empty);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(health - ship.Definition.attackDamage));

        ship.Tick(1f);
        Assert.That(projectiles, Is.Empty);
        ship.Tick(0.05f);
        Assert.That(projectiles.Count, Is.EqualTo(1));
        Assert.That(firstSalvo, Does.Contain(projectiles[0]));
    }

    [Test]
    public void BombardmentUsesFixedTerrainPositionsAndCurrentGroundOccupants()
    {
        Faction faction = CreateFaction(FactionColorType.Green);
        Faction enemy = CreateFaction(FactionColorType.Red);
        var ship = (CombatUnit)Spawn(faction, UnitType.Aircraft, 20, 18);
        UnitBase target = Spawn(enemy, UnitType.Combat, 20, 30);
        UnitBase entering = Spawn(enemy, UnitType.Combat, 14, 32);
        UnitBase nearby = Spawn(enemy, UnitType.Mech, 19, 30);
        UnitBase friendly = Spawn(faction, UnitType.Combat, 20, 28);
        UnitBase aircraft = Spawn(enemy, UnitType.Aircraft, 20, 30);
        BuildingDefinition depot = roster.GetBuildingDefinition(BuildingType.SupplyDepot);
        BuildingBase building = enemy.BuildingManager.Construct(depot, new GridCoord(22, 28));
        Assert.That(building, Is.Not.Null);
        building.Tick(depot.ConstructionDuration);
        BuildingBase friendlyBuilding = faction.BuildingManager.Construct(depot, new GridCoord(14, 28));
        Assert.That(friendlyBuilding, Is.Not.Null);
        friendlyBuilding.Tick(depot.ConstructionDuration);
        Assert.That(building.Position.x - target.Position.x, Is.GreaterThan(3f));

        var heights = new float[33, 33];
        for (int z = 0; z < 33; z++)
        for (int x = 0; x < 33; x++)
            heights[z, x] = 0.1f + x * 0.01f;
        terrainData.SetHeights(0, 0, heights);

        Vector3 center = target.Position;
        float buildingHealth = building.Health.CurrentHealth;
        float friendlyBuildingHealth = friendlyBuilding.Health.CurrentHealth;
        float targetHealth = target.Health.CurrentHealth;
        float nearbyHealth = nearby.Health.CurrentHealth;
        float enteringHealth = entering.Health.CurrentHealth;
        float friendlyHealth = friendly.Health.CurrentHealth;
        float airHealth = aircraft.Health.CurrentHealth;
        ship.IssueCommand(CommandType.Attack, CommandContext.AttackTarget(target));
        ship.Tick(0.01f);
        target.transform.position += Vector3.forward * 7f;
        entering.transform.position = At(19, 32);
        ship.Tick(0.43f);

        var positions = new HashSet<Vector3>();
        foreach (Projectile projectile in context.ProjectileManager.Projectiles)
        {
            Assert.That(projectile.Tick(2f), Is.False);
            Vector3 impact = projectile.transform.position;
            Assert.That(impact.y, Is.EqualTo(grid.SampleHeight(impact)).Within(0.0001f));
            Assert.That(Vector2.Distance(new Vector2(impact.x, impact.z), new Vector2(center.x, center.z)), Is.LessThanOrEqualTo(3.001f));
            positions.Add(impact);
        }
        center.y = grid.SampleHeight(center);
        Assert.That(positions.Count, Is.EqualTo(8));
        Assert.That(positions, Does.Contain(center));
        float damage = ship.Definition.attackDamage;
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(targetHealth));
        Assert.That(nearby.Health.CurrentHealth, Is.EqualTo(nearbyHealth - damage));
        Assert.That(entering.Health.CurrentHealth, Is.EqualTo(enteringHealth - damage));
        Assert.That(friendly.Health.CurrentHealth, Is.EqualTo(friendlyHealth));
        Assert.That(aircraft.Health.CurrentHealth, Is.EqualTo(airHealth));
        Assert.That(building.Health.CurrentHealth, Is.EqualTo(buildingHealth - damage));
        Assert.That(friendlyBuilding.Health.CurrentHealth, Is.EqualTo(friendlyBuildingHealth));
    }

    [Test]
    public void CommittedShotsLandAfterEitherShipOrTargetDiesAndASecondShipHasAnIndependentSalvo()
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        Faction aiFaction = CreateFaction(FactionColorType.Red, controller: new AIFactionController());
        Faction enemy = CreateFaction(FactionColorType.Green);
        var ship = (CombatUnit)Spawn(faction, UnitType.Aircraft, 20, 18);
        var aiShip = (CombatUnit)Spawn(aiFaction, UnitType.Aircraft, 20, 39);
        aiShip.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        UnitBase target = Spawn(enemy, UnitType.Combat, 20, 30);
        UnitBase nearby = Spawn(enemy, UnitType.Mech, 21, 30);
        float health = nearby.Health.CurrentHealth;
        ship.IssueCommand(CommandType.Attack, CommandContext.AttackTarget(target));
        ship.Tick(0.01f);
        // The AI-owned ship uses its normal idle combat awareness.
        var projectiles = context.ProjectileManager.Projectiles;
        for (int i = 0; i < 10 && projectiles.Count < 2; i++)
            aiShip.Tick(0.05f);

        Assert.That(aiShip.CurrentTarget, Is.SameAs(target));
        Assert.That(projectiles.Count, Is.EqualTo(2));
        ship.TakeDamage(new DamageInfo(ship.Health.CurrentHealth, nearby));
        target.TakeDamage(new DamageInfo(target.Health.CurrentHealth, aiShip));
        ship.Tick(0.01f);
        aiShip.Tick(0.01f);
        Assert.That(projectiles.Count, Is.EqualTo(2));

        context.ProjectileManager.Tick(1f);
        Assert.That(projectiles, Is.Empty);
        Assert.That(nearby.Health.CurrentHealth, Is.EqualTo(health - ship.Definition.attackDamage - aiShip.Definition.attackDamage));
    }

    [TestCase(UnitType.Combat)]
    [TestCase(UnitType.Mech)]
    [TestCase(UnitType.Flamer)]
    public void BarracksSpawnsGroundUnitsWithNavigationAndRallyReservation(UnitType type)
    {
        var groundPathfinding = world.gameObject.AddComponent<GridAStarPathfindingService>();
        groundPathfinding.Initialize(grid, navigation);
        Faction faction = CreateFaction(FactionColorType.Blue, service: groundPathfinding);
        BuildingDefinition barracks = roster.GetBuildingDefinition(BuildingType.Barracks);
        BuildingBase building = faction.BuildingManager.Construct(barracks, new GridCoord(33, 33));
        Assert.That(building, Is.Not.Null);
        building.Tick(barracks.ConstructionDuration);
        grid.RebuildStaticClearanceField();
        Physics.SyncTransforms();

        UnitDefinition definition = roster.GetUnitDefinition(type);
        Assert.That(building.UnitProduction.TryEnqueue(definition), Is.True);
        building.UnitProduction.Tick(definition.ProductionDuration);

        Assert.That(building.UnitProduction.QueueCount, Is.Zero);
        Assert.That(faction.UnitManager.CurrentPopulation, Is.EqualTo(definition.Cost.Supply));
        Assert.That(faction.UnitManager.ReservedPopulation, Is.Zero);
        UnitBase unit = faction.UnitManager.UnitList[0];
        Assert.That(unit.Definition, Is.EqualTo(definition));
        Assert.That(unit.IsAirUnit, Is.False);
        Assert.That(unit.CurrentCommand, Is.EqualTo(CommandType.Move));
        Assert.That(unit.Motor.HasPath, Is.True);
        Assert.That(navigation.GetOccupiedCell(unit), Is.Not.Null);
        Assert.That(navigation.DestinationReservationCount, Is.EqualTo(1));
    }

    [Test]
    public void AircraftAttackMoveDamagesBuildingsAndResumesTravel()
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        Faction enemy = CreateFaction(FactionColorType.Red);
        var ship = (CombatUnit)Spawn(faction, UnitType.Aircraft, 21, 24);
        BuildingDefinition depot = roster.GetBuildingDefinition(BuildingType.SupplyDepot);
        BuildingBase target = enemy.BuildingManager.Construct(depot, new GridCoord(20, 36));
        Assert.That(target, Is.Not.Null);
        target.Tick(depot.ConstructionDuration);
        Physics.SyncTransforms();

        float targetHealth = target.Health.CurrentHealth;
        Vector3 destination = At(21, 56);
        ship.IssueCommand(CommandType.AttackMove, CommandContext.AttackMoveTo(destination));
        for (int i = 0; i < 8 && context.ProjectileManager.Projectiles.Count == 0; i++)
            ship.Tick(0.1f);

        Assert.That(ship.CurrentTarget, Is.SameAs(target));
        Assert.That(context.ProjectileManager.Projectiles.Count, Is.EqualTo(1));
        ship.Tick(0.43f);
        Assert.That(context.ProjectileManager.Projectiles.Count, Is.EqualTo(8));
        context.ProjectileManager.Tick(1f);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(targetHealth - 35f));

        target.TakeDamage(new DamageInfo(targetHealth, ship, faction));
        for (int i = 0; i < 60; i++)
            ship.Tick(0.1f);
        Assert.That(ship.CurrentTarget, Is.Null);
        Assert.That(ship.Position, Is.EqualTo(new Vector3(destination.x, FlightAltitude, destination.z)));
    }

    [Test]
    public void TurretStillFiresAndDamagesWithAConventionalProjectile()
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        Faction enemy = CreateFaction(FactionColorType.Red);
        BuildingDefinition definition = roster.GetBuildingDefinition(BuildingType.Turret);
        BuildingBase turret = faction.BuildingManager.Construct(definition, new GridCoord(19, 19));
        Assert.That(turret, Is.Not.Null);
        UnitBase target = Spawn(enemy, UnitType.Mech, 20, 28);
        float health = target.Health.CurrentHealth;
        turret.Tick(definition.ConstructionDuration);
        TurretDefenseComponent defense = turret.GetComponent<TurretDefenseComponent>();
        var projectiles = context.ProjectileManager.Projectiles;
        for (int i = 0; i < 100 && projectiles.Count == 0; i++)
            defense.Tick(0.05f);

        Assert.That(projectiles.Count, Is.EqualTo(1));
        Assert.That(projectiles[0].Source, Is.SameAs(turret));
        Physics.SyncTransforms();
        Assert.That(projectiles[0].Tick(1f), Is.False);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(health - 15f));
    }

    private Faction CreateFaction(FactionColorType color, bool usePathfinding = true, IPathfindingService service = null,
        IFactionController controller = null) =>
        new(roster, color, controller, new UnitManager(context, usePathfinding ? service ?? pathfinding : null, world),
            new BuildingManager(context, grid, world), new ResourceManager(2000, 2000, 30), context);

    private UnitBase Spawn(Faction faction, UnitType type, float x, float z) =>
        faction.UnitManager.SpawnUnit(roster.GetUnitDefinition(type).Prefab, At(x, z), Quaternion.identity);

    private CommandIssuer CreateIssuer(Faction faction, params UnitBase[] selected)
    {
        var issuer = world.gameObject.AddComponent<CommandIssuer>();
        issuer.Initialize(context, faction);
        context.SelectUnits(selected, false);
        return issuer;
    }

    private Vector3 At(float x, float z) => world.position + new Vector3(x, 0f, z);

    private sealed class RecordingPathfinding : IPathfindingService
    {
        public List<UnitBase> Requesters { get; } = new();

        public void Initialize(TerrainGrid terrainGrid, GridNavigationStateSystem navigationState) { }

        public bool TryFindPath(UnitBase requester, Vector3 start, Vector3 end, List<Vector3> result)
        {
            Requesters.Add(requester);
            result.Add(end);
            return true;
        }
    }
}
