using UnityEngine;

[DisallowMultipleComponent]
public class SupplyProviderComponent : MonoBehaviour
{
    [SerializeField]
    [Min(0)] private int supplyProvided = 4;

    private BuildingBase building;

    private bool supplyApplied;

    public int SupplyProvided => supplyProvided;

    public void Initialize(BuildingBase building)
    {
        this.building = building;
        ApplySupply();
    }

    public void Tick(float deltaTime){ }

    public void RemoveSupply()
    {
        if (!supplyApplied)
            return;

        ResourceManager resourceManager = building?.OwnerFaction?.ResourceManager;

        if (resourceManager != null)
        {
            resourceManager.DecreaseMaxSupply(supplyProvided);
        }

        supplyApplied = false;
    }

    private void ApplySupply()
    {
        if (supplyApplied)
            return;

        ResourceManager resourceManager = building?.OwnerFaction?.ResourceManager;

        if (resourceManager == null)
            return;

        resourceManager.IncreaseMaxSupply(supplyProvided);
        supplyApplied = true;
    }

    public void NotifyBuildingRemoved()
    {
        RemoveSupply();
    }
}