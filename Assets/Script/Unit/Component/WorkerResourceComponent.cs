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

    [Header("Resource Action")]
    [SerializeField, Min(1)] private int resourceAmountPerCycle = 1;
    [SerializeField, Min(0.05f)] private float resourceActionInterval = 0.5f;

    private float actionElapsed;

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
        actionElapsed = 0f;

        if (node == null)
            return;

        assignedResourceType = node.ResourceType;
        hasAssignedResourceType = true;
    }

    public void ClearAssignment()
    {
        AssignedNode = null;
        hasAssignedResourceType = false;
        actionElapsed = 0f;
    }

    public void ResetActionCycle()
    {
        actionElapsed = 0f;
    }

    /// <summary>
    /// Advances gathering and extracts resources whenever one or more
    /// gather intervals have completed.
    ///
    /// Returns the amount extracted during this tick.
    /// </summary>
    public int TickGather(ResourceNode node, float deltaTime)
    {
        if (node == null || !node.IsInitialized || node.IsDepleted || IsFull)
        {
            return 0;
        }

        // One cargo load cannot contain mixed resource types.
        if (HasCargo && CargoType != node.ResourceType)
            return 0;

        int completedCycles = AdvanceActionTimer(deltaTime);

        int requestedAmount = Mathf.Min(FreeCapacity, completedCycles * resourceAmountPerCycle);

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
    /// Advances the delivery timer and deposits resources
    /// for each completed action cycle.
    ///
    /// Returns the amount delivered during this tick.
    /// </summary>
    public int TickDeliver(ResourceManager resourceManager, float deltaTime)
    {
        if (resourceManager == null || !HasCargo)
            return 0;

        int completedCycles = AdvanceActionTimer(deltaTime);

        if (completedCycles <= 0)
            return 0;

        int deliveredAmount = Mathf.Min(CargoAmount, completedCycles * resourceAmountPerCycle);
        resourceManager.AddResources(CargoType, deliveredAmount);
        CargoAmount -= deliveredAmount;

        return deliveredAmount;
    }

    private int AdvanceActionTimer(float deltaTime)
    {
        actionElapsed += Mathf.Max(0f, deltaTime);

        if (actionElapsed < resourceActionInterval)
            return 0;

        int completedCycles = Mathf.FloorToInt(actionElapsed / resourceActionInterval);
        actionElapsed -= completedCycles * resourceActionInterval;
        return completedCycles;
    }

    private void OnValidate()
    {
        carryCapacity = Mathf.Max(1, carryCapacity);
        resourceAmountPerCycle = Mathf.Max(1, resourceAmountPerCycle);
        resourceActionInterval = Mathf.Max(0.05f, resourceActionInterval);
    }
}