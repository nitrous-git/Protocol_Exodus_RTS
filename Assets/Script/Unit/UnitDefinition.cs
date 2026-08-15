using UnityEngine;

[CreateAssetMenu(menuName = "Protocol Exodus/Unit/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    [Header("Identity")]
    public UnitType unitType;
    public string displayName = "Unit";
    public Sprite icon;

    [Header("Prefab")]
    public UnitBase prefab;

    [Header("Economy")]
    public Cost cost = Cost.Zero;

    [Header("Production")] 
    [Min(0f)] public float productionDuration = 5f;

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

    [Header("Navigation")]
    [Min(0.05f)] public float navigationRadius = 12.45f;

    public UnitType Type => unitType;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public UnitBase Prefab => prefab;
    public Cost Cost => cost;
    public float ProductionDuration => productionDuration;
    public float NavigationRadius => navigationRadius;

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        visionRange = Mathf.Max(0f, visionRange);

        attackRange = Mathf.Max(0f, attackRange);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackCooldown = Mathf.Max(0.01f, attackCooldown);

        selectionRadius = Mathf.Max(0f, selectionRadius);
        navigationRadius = Mathf.Max(0.05f, NavigationRadius);
        productionDuration = Mathf.Max(0f, productionDuration);
    }
}

