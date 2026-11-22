using System.Collections.Generic;

/// <summary>
/// Moves a worker beside a ResourceNode and gathers until its cargo
/// is full or the node is depleted.
/// </summary>
public class WorkerGatherState : WorkerInteractionState
{
    private readonly ResourceNode targetNode;
    private readonly HashSet<GridCoord> rejectedCells = new();

    public WorkerGatherState(ResourceNode targetNode)
    {
        this.targetNode = targetNode;
    }

    protected override void OnEnterTyped(WorkerUnit unit)
    {
        WorkerResourceComponent resourceComponent = unit.ResourceComponent;

        if (resourceComponent == null)
        {
            unit.IssueCommand(CommandType.Idle, CommandContext.None());
            return;
        }

        if (!IsNodeAvailable(targetNode))
        {
            ContinueWithReplacementOrIdle(unit);
            return;
        }

        // Assign before checking cargo type. This allows a command such as
        // "gather gas" while carrying minerals to deposit the minerals
        // first and then resume at the newly assigned gas node.
        resourceComponent.AssignNode(targetNode);

        bool mustDeliverExistingCargo = 
            resourceComponent.HasCargo && 
            (resourceComponent.IsFull || resourceComponent.CargoType != targetNode.ResourceType);

        if (mustDeliverExistingCargo)
        {
            unit.IssueCommand(CommandType.Deliver, CommandContext.None());
            return;
        }

        FindApproach(unit);
    }

    protected override void FindApproach(WorkerUnit unit) =>
        StartApproach(unit, targetNode.OriginCell, targetNode.Footprint, rejectedCells, targetNode);

    protected override void TickTyped(WorkerUnit unit, float deltaTime)
    {
        if (!unit.IsAlive)
            return;

        if (!IsNodeAvailable(targetNode))
        {
            ContinueWithReplacementOrIdle(unit);
            return;
        }

        if (!TickApproach(unit, deltaTime))
            return;

        WorkerResourceComponent resourceComponent = unit.ResourceComponent;

        resourceComponent.TickGather(targetNode, deltaTime);

        if (resourceComponent.IsFull || targetNode.IsDepleted)
        {
            if (resourceComponent.HasCargo)
            {
                unit.IssueCommand(CommandType.Deliver, CommandContext.None());
                return;
            }

            ContinueWithReplacementOrIdle(unit);
        }
    }

    // ---------------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------------

    private static bool IsNodeAvailable(ResourceNode resourceNode)
    {
        return resourceNode != null && resourceNode.IsInitialized && !resourceNode.IsDepleted;
    }

    private static void ContinueWithReplacementOrIdle(WorkerUnit unit)
    {
        WorkerResourceComponent resourceComponent = unit.ResourceComponent;

        if (resourceComponent != null && resourceComponent.HasCargo)
        {
            unit.IssueCommand(CommandType.Deliver, CommandContext.None());
            return;
        }

        ResourceNode replacementNode = unit.FindReplacementResourceNode();

        if (replacementNode != null)
        {
            unit.IssueCommand(CommandType.Gather, CommandContext.Gather(replacementNode));

            return;
        }

        unit.IssueCommand(CommandType.Idle, CommandContext.None());
    }

}
