public sealed class DestinationAllocationSystem
{
    public RingDestinationAllocator Ring { get; }
    public FormationDestinationAllocator Formation { get; }
    public AttackPositionAllocator Attack { get; }

    public DestinationAllocationSystem(TerrainGrid terrainGrid, GridNavigationStateSystem navigationState)
    {
        Ring = new RingDestinationAllocator(terrainGrid, navigationState);

        Formation = new FormationDestinationAllocator(terrainGrid, navigationState);

        Attack = new AttackPositionAllocator(terrainGrid, navigationState);
    }

}