using UnityEngine;

/// <summary>
/// Moves a worker beside the closest operational headquarters,
/// deposits its cargo, and resumes its assigned gathering task.
/// </summary>
public class WorkerDeliverState : UnitState<WorkerUnit>
{
    private readonly BuildingBase requestedDropOff;

    private BuildingBase dropOffBuilding;
    private GridCoord? reservedInteractionCell;
    private bool pathRequested;

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

        if (IsDropOffAvailable(requestedDropOff, unit.OwnerFaction))
        {
            dropOffBuilding = requestedDropOff;
        }
        else
        {
            dropOffBuilding = unit.FindClosestResourceDropOff();
        }

        if (dropOffBuilding == null)
        {
            Debug.LogWarning($"{unit.name} has cargo but no operational headquarters is available.");
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        reservedInteractionCell = unit.ReserveInteractionCell(dropOffBuilding);

        if (!reservedInteractionCell.HasValue || unit.Motor == null || unit.TerrainGrid == null)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        Vector3 interactionPosition = unit.TerrainGrid.CellToWorld(reservedInteractionCell.Value);
        pathRequested = unit.Motor.MoveTo(interactionPosition);

        if (!pathRequested)
        {
            Debug.LogWarning($"{unit.name} could not reach {dropOffBuilding.name}.");
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
        }
    }

    protected override void OnExitTyped(WorkerUnit unit)
    {
        if (!reservedInteractionCell.HasValue)
            return;

        unit.ReleaseInteractionCell(reservedInteractionCell.Value);
        reservedInteractionCell = null;
    }

    protected override void TickTyped(WorkerUnit unit, float deltaTime)
    {
        if (!IsDropOffAvailable(dropOffBuilding, unit.OwnerFaction))
        {
            // Re-enter delivery so another headquarters can be selected.
            unit.IssueCommand(CommandType.Deliver, CommandContext.None());
            return;
        }

        if (!pathRequested || unit.Motor == null)
            return;

        if (!unit.Motor.HasArrived)
            return;

        ResourceManager resourceManager = unit.OwnerFaction?.ResourceManager;

        unit.ResourceComponent.TickDeliver(resourceManager, deltaTime);

        if (unit.ResourceComponent.HasCargo)
            return;

        ResumeGatheringOrIdle(unit);
    }

    // ---------------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------------

    //private static bool IsDropOffAvailable(BuildingBase building)
    //{
    //    return building != null &&
    //           building.IsInitialized &&
    //           building.IsAlive &&
    //           building.IsOperational &&
    //           building.Headquarters != null;
    //}

    private static void ResumeGatheringOrIdle(WorkerUnit unit)
    {
        ResourceNode resourceNode = unit.ResourceComponent?.AssignedNode;

        if (resourceNode == null || !resourceNode.IsInitialized || resourceNode.IsDepleted)
        {
            resourceNode = unit.FindReplacementResourceNode();
        }

        if (resourceNode != null)
        {
            unit.IssueCommand(CommandType.Gather, CommandContext.Gather(resourceNode));
            return;
        }

        unit.IssueCommand(CommandType.Idle, CommandContext.None());
    }

    private static bool IsDropOffAvailable(BuildingBase building, Faction faction)
    {
        return building != null &&
               building.IsInitialized &&
               building.IsAlive &&
               building.IsOperational &&
               building.OwnerFaction == faction &&
               building.Headquarters != null;
    }
}
