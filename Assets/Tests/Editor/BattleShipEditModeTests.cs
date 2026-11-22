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

    [TestCase(UnitType.Combat)]
    [TestCase(UnitType.Mech)]
    [TestCase(UnitType.Aircraft)]
    public void AircraftProjectileHitsTargetAtItsAimPointAndRespectsCooldown(UnitType targetType)
    {
        Faction faction = CreateFaction(FactionColorType.Blue);
        Faction enemy = CreateFaction(FactionColorType.Green);
        var ship = (CombatUnit)Spawn(faction, UnitType.Aircraft, 18, 18);
        UnitBase target = Spawn(enemy, targetType, 18, 30);
        UnitBase friendly = targetType == UnitType.Aircraft ? Spawn(faction, UnitType.Aircraft, 18, 24) : null;
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
        if (friendly != null)
            Assert.That(friendly.Health.CurrentHealth, Is.EqualTo(650f));

        ship.Tick(0.51f);
        Assert.That(context.ProjectileManager.Projectiles.Count, Is.EqualTo(2));
        target.TakeDamage(new DamageInfo(targetHealth, ship, faction));
        ship.Tick(0.1f);
        Assert.That(target.IsAlive, Is.False);
        Assert.That(ship.CurrentTarget, Is.Null);
        Assert.That(ship.Motor.HasArrived, Is.True);
    }

    [Test]
    public void BarracksStillSpawnsGroundUnitsWithNavigationAndRallyReservation()
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

        UnitDefinition infantry = roster.GetUnitDefinition(UnitType.Combat);
        Assert.That(building.UnitProduction.TryEnqueue(infantry), Is.True);
        building.UnitProduction.Tick(infantry.ProductionDuration);

        Assert.That(building.UnitProduction.QueueCount, Is.Zero);
        Assert.That(faction.UnitManager.CurrentPopulation, Is.EqualTo(infantry.Cost.Supply));
        Assert.That(faction.UnitManager.ReservedPopulation, Is.Zero);
        UnitBase unit = faction.UnitManager.UnitList[0];
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
        Assert.That(context.ProjectileManager.Projectiles[0].Tick(1f), Is.False);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(targetHealth - 35f));

        target.TakeDamage(new DamageInfo(targetHealth, ship, faction));
        for (int i = 0; i < 60; i++)
            ship.Tick(0.1f);
        Assert.That(ship.CurrentTarget, Is.Null);
        Assert.That(ship.Position, Is.EqualTo(new Vector3(destination.x, FlightAltitude, destination.z)));
    }

    private Faction CreateFaction(FactionColorType color, bool usePathfinding = true, IPathfindingService service = null) =>
        new(roster, color, null, new UnitManager(context, usePathfinding ? service ?? pathfinding : null, world),
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
