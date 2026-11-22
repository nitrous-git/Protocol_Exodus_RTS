using System.Collections.Generic;

/// <summary>
/// Moves a worker beside an operational friendly headquarters,
/// deposits its cargo, and resumes its assigned gathering task.
/// </summary>
public class WorkerDeliverState : WorkerInteractionState
{
    private readonly BuildingBase requestedDropOff;
    private readonly HashSet<int> triedDropOffs = new();
    private readonly Dictionary<int, HashSet<GridCoord>> rejectedCellsByDropOff = new();

    private BuildingBase dropOffBuilding;

    public WorkerDeliverState(BuildingBase requestedDropOff = null)
    {
        this.requestedDropOff = requestedDropOff;
    }

    protected override void OnEnterTyped(WorkerUnit unit)
    {
        WorkerResourceComponent resourceComponent = unit.ResourceComponent;

        if (resourceComponent == null)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        if (!resourceComponent.HasCargo)
        {
            ResumeGatheringOrIdle(unit);
            return;
        }

        resourceComponent.ResetActionCycle();
        FindApproach(unit);
    }

    protected override void FindApproach(WorkerUnit unit)
    {
        if (unit.OwnerFaction?.ResourceManager == null)
        {
            WaitForRetry(unit, "faction resource manager unavailable");
            return;
        }

        dropOffBuilding = FindNextDropOff(unit);
        if (dropOffBuilding == null && triedDropOffs.Count > 0)
        {
            // Every eligible headquarters had a turn. Start again with the requested or nearest one.
            triedDropOffs.Clear();
            dropOffBuilding = FindNextDropOff(unit);
        }

        if (dropOffBuilding == null)
        {
            rejectedCellsByDropOff.Clear();
            WaitForRetry(unit, "no operational friendly headquarters");
            return;
        }

        int buildingId = dropOffBuilding.BuildingId;
        triedDropOffs.Add(buildingId);
        if (!rejectedCellsByDropOff.TryGetValue(buildingId, out HashSet<GridCoord> rejectedCells))
        {
            rejectedCells = new HashSet<GridCoord>();
            rejectedCellsByDropOff.Add(buildingId, rejectedCells);
        }

        StartApproach(unit, dropOffBuilding.FootprintOrigin, dropOffBuilding.PlacedFootprintSize, rejectedCells, dropOffBuilding);
    }

    protected override void TickTyped(WorkerUnit unit, float deltaTime)
    {
        if (!unit.IsAlive)
            return;

        if (HasApproach && !unit.IsResourceDropOffAvailable(dropOffBuilding))
        {
            WaitForRetry(unit, "selected headquarters unavailable", rejectCell: false);
            return;
        }

        if (!TickApproach(unit, deltaTime))
            return;

        unit.ResourceComponent.TickDeliver(unit.OwnerFaction.ResourceManager, deltaTime);
        if (!unit.ResourceComponent.HasCargo)
            ResumeGatheringOrIdle(unit);
    }

    private BuildingBase FindNextDropOff(WorkerUnit unit)
    {
        if (unit.IsResourceDropOffAvailable(requestedDropOff) && !triedDropOffs.Contains(requestedDropOff.BuildingId))
            return requestedDropOff;

        return unit.FindClosestResourceDropOff(triedDropOffs);
    }

    private static void ResumeGatheringOrIdle(WorkerUnit unit)
    {
        ResourceNode resourceNode = unit.ResourceComponent.AssignedNode;

        if (resourceNode == null || !resourceNode.IsInitialized || resourceNode.IsDepleted)
            resourceNode = unit.FindReplacementResourceNode();

        if (resourceNode != null)
        {
            unit.IssueCommand(CommandType.Gather, CommandContext.Gather(resourceNode));
            return;
        }

        unit.IssueCommand(CommandType.Idle, CommandContext.None());
    }
}
