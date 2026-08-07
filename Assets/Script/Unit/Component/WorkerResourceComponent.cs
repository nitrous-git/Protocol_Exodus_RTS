using UnityEngine;

/// <summary>
/// Owns the resource-gathering data of one WorkerUnit.
///
/// This component stores cargo, controls extraction timing,
/// and deposits carried resources into a faction ResourceManager.
/// It does not move the worker or choose targets.
/// </summary>
public sealed class WorkerResourceComponent : MonoBehaviour
{
    [Header("Cargo")] 
    [SerializeField, Min(1)] private int carryCapacity = 10;

    [Header("Gathering")]
    [SerializeField, Min(1)] private int gatherAmountPerCycle = 1;
    [SerializeField, Min(0.05f)] private float gatherInterval = 0.5f;

    private float gatherElapsed;

    private bool hasAssignedResourceType;
    private ResourceType assignedResourceType;

    public int CarryCapacity => carryCapacity;
    public int CargoAmount { get; private set; }
    public ResourceType CargoType { get; private set; }

    public ResourceNode AssignedNode { get; private set; }

    public bool HasCargo => CargoAmount > 0;
    public bool IsFull => CargoAmount >= carryCapacity;
    public int FreeCapacity => Mathf.Max(0, carryCapacity - CargoAmount);

    public bool HasAssignedResourceType => hasAssignedResourceType;
    public ResourceType AssignedResourceType => assignedResourceType;

    /// <summary>
    /// Assigns the node that this worker should gather from.
    ///
    /// The resource type is retained even if the node is later destroyed,
    /// allowing the worker to find another node of the same type.
    /// </summary>
    public void AssignNode(ResourceNode node)
    {
        AssignedNode = node;
        gatherElapsed = 0f;

        if (node == null)
            return;

        assignedResourceType = node.ResourceType;
        hasAssignedResourceType = true;
    }

    public void ClearAssignment()
    {
        AssignedNode = null;
        hasAssignedResourceType = false;
        gatherElapsed = 0f;
    }

    public void ResetGatherCycle()
    {
        gatherElapsed = 0f;
    }

    /// <summary>
    /// Advances gathering and extracts resources whenever one or more
    /// gather intervals have completed.
    ///
    /// Returns the amount extracted during this tick.
    /// </summary>
    public int TickGather(ResourceNode node, float deltaTime)
    {
        if (node == null)
            return 0;

        if (!node.IsInitialized || node.IsDepleted)
            return 0;

        if (IsFull)
            return 0;

        // One cargo load cannot contain mixed resource types.
        if (HasCargo && CargoType != node.ResourceType)
            return 0;

        gatherElapsed += Mathf.Max(0f, deltaTime);

        if (gatherElapsed < gatherInterval)
            return 0;

        int completedCycles = Mathf.FloorToInt(gatherElapsed / gatherInterval);

        gatherElapsed -= completedCycles * gatherInterval;

        int requestedAmount = Mathf.Min(FreeCapacity, completedCycles * gatherAmountPerCycle);

        if (requestedAmount <= 0)
            return 0;

        int extractedAmount = node.Extract(requestedAmount);

        if (extractedAmount <= 0)
            return 0;

        if (!HasCargo)
            CargoType = node.ResourceType;

        CargoAmount += extractedAmount;

        return extractedAmount;
    }

    /// <summary>
    /// Deposits the entire cargo load into the faction economy.
    ///
    /// Returns the delivered amount.
    /// </summary>
    public int DeliverCargo(ResourceManager resourceManager)
    {
        if (resourceManager == null || !HasCargo)
            return 0;

        int deliveredAmount = CargoAmount;

        resourceManager.AddResources(CargoType, deliveredAmount);

        CargoAmount = 0;
        gatherElapsed = 0f;

        return deliveredAmount;
    }

    private void OnValidate()
    {
        carryCapacity = Mathf.Max(1, carryCapacity);
        gatherAmountPerCycle = Mathf.Max(1, gatherAmountPerCycle);
        gatherInterval = Mathf.Max(0.05f, gatherInterval);
    }
}