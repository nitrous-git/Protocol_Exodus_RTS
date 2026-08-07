using UnityEngine;

/// <summary>
/// Moves a worker beside the closest operational headquarters,
/// deposits its cargo, and resumes its assigned gathering task.
/// </summary>
public class WorkerDeliverState : UnitState<WorkerUnit>
{
    private BuildingBase dropOffBuilding;

    private bool pathRequested;

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

        dropOffBuilding = unit.FindClosestResourceDropOff();

        if (dropOffBuilding == null)
        {
            Debug.LogWarning($"{unit.name} has cargo but no operational headquarters is available.");
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        Vector3? interactionPosition = unit.GetInteractionPosition(dropOffBuilding);

        if (!interactionPosition.HasValue || unit.Motor == null)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        pathRequested = unit.Motor.MoveTo(interactionPosition.Value);

        if (!pathRequested)
        {
            Debug.LogWarning($"{unit.name} could not reach {dropOffBuilding.name}.");
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
        }
    }

    protected override void TickTyped(WorkerUnit unit, float deltaTime)
    {
        if (!IsDropOffAvailable(dropOffBuilding))
        {
            // Re-enter delivery so another headquarters can be selected.
            unit.IssueCommand(CommandType.Deliver, CommandContext.None());
            return;
        }

        if (!pathRequested || unit.Motor == null)
            return;

        if (!unit.Motor.HasArrived)
            return;

        ResourceManager factionResources = unit.OwnerFaction?.ResourceManager;

        int deliveredAmount = unit.ResourceComponent.DeliverCargo(factionResources);

        if (deliveredAmount <= 0)
        {
            Debug.LogWarning($"{unit.name} could not deliver its cargo.");
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        Debug.Log($"{unit.name} delivered {deliveredAmount} resources.");

        ResumeGatheringOrIdle(unit);
    }

    // ---------------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------------

    private static bool IsDropOffAvailable(BuildingBase building)
    {
        return building != null &&
               building.IsInitialized &&
               building.IsAlive &&
               building.IsOperational &&
               building.Headquarters != null;
    }

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

}
