using UnityEngine;

public class UnitProductionComponent : MonoBehaviour
{
    private BuildingBase building;

    public BuildingBase Building => building;

    public void Initialize(BuildingBase building)
    {
        this.building = building;
    }

    public void Tick(float deltaTime)
    {
        if (building == null || !building.IsAlive)
        {
            return;
        }

        // Production queue comes later.
    }

    public void NotifyBuildingRemoved() { }
}