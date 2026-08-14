public sealed class DestinationAllocationSystem
{
    public RingDestinationAllocator Ring { get; }
    public FormationDestinationAllocator Formation { get; }

    public DestinationAllocationSystem(TerrainGrid terrainGrid, GridReservationSystem reservationSystem)
    {
        Ring = new RingDestinationAllocator(terrainGrid, reservationSystem);

        Formation = new FormationDestinationAllocator(terrainGrid, reservationSystem);
    }
}