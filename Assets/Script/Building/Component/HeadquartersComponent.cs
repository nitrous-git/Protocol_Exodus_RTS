using UnityEngine;

public class HeadquartersComponent : MonoBehaviour
{
    private BuildingBase building;

    public BuildingBase Building => building;

    public void Initialize(BuildingBase building)
    {
        this.building = building;
    }

    public void Tick(float deltaTime){ }

    public void NotifyBuildingRemoved() { }
}