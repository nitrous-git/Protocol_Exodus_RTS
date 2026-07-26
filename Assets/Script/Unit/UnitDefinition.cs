using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Unit/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    [Header("Identity")]
    public UnitType unitType;
    public string displayName = "Unit";
    public GameObject prefab;

    [Header("Economy")]
    public Cost cost = Cost.Zero;

    [Header("Core Stats")]
    [Min(1f)] public float maxHealth = 100f;
    [Min(0f)] public float moveSpeed = 4f;
    [Min(0f)] public float visionRange = 12f;

    [Header("Combat Stats")]
    public bool canAttack;
    [Min(0f)] public float attackRange = 7f;
    [Min(0f)] public float attackDamage = 10f;
    [Min(0.01f)] public float attackCooldown = 1f;

    [Header("Selection")]
    public float selectionRadius = 0.5f;

    public UnitType Type => unitType;
    public string DisplayName => displayName;
    public Cost Cost => cost;
}

