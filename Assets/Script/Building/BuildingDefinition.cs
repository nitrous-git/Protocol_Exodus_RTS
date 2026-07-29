using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Building/Building Definition")]
public sealed class BuildingDefinition : ScriptableObject
{
    [Header("Identity")]
    public BuildingType buildingType;
    public string displayName = "Building";

    [Header("Prefab")]
    public BuildingBase prefab;

    [Header("Economy")]
    public Cost cost = Cost.Zero;

    [Header("Placement")]
    public Vector2Int footprintSize = Vector2Int.one;

    [Header("Core Stats")]
    [Min(1f)] public float maxHealth = 100f;

    public BuildingType Type => buildingType;
    public string DisplayName => displayName;
    public Vector2Int FootprintSize => footprintSize;
    public BuildingBase Prefab => prefab;
    public Cost Cost => cost;

    private void OnValidate()
    {
        footprintSize.x = Mathf.Max(1, footprintSize.x);
        footprintSize.y = Mathf.Max(1, footprintSize.y);

        maxHealth = Mathf.Max(1f, maxHealth);
    }
}