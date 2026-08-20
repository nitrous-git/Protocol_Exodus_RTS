using System;
using UnityEngine;

[RequireComponent(typeof(UnitSensor))]
[RequireComponent(typeof(UnitWeapon))]
public class CombatUnit : UnitBase
{
    [Header("Combat")]
    [SerializeField] private UnitWeapon weapon;

    [Tooltip("How far the explicit target must move before AttackState rebuilds its path.")]
    [SerializeField, Min(0.05f)] private float chaseRepathDistance = 0.75f;

    [Header("Visual")]
    [SerializeField] private float combatRotationSpeed = 720f;


    private ITargetable currentTarget;
    private bool attackAnimationActive;
    private ITargetable attackTarget;

    public float ChaseRepathDistance => chaseRepathDistance;
    public ITargetable CurrentTarget => currentTarget;
    public UnitWeapon Weapon => weapon;

    protected override void CacheComponents()
    {
        base.CacheComponents();
        weapon = GetComponent<UnitWeapon>();
    }

    public override void Initialize(Faction ownerFaction, GameContext gameContext, IPathfindingService pathfindingService, UnitManager owningUnitManager)
    {
        base.Initialize(ownerFaction, gameContext, pathfindingService, owningUnitManager);

        if (!IsInitialized)
            return;

        if (definition == null || !definition.canAttack)
        {
            Debug.LogWarning(name + " is a CombatUnit whose UnitDefinition cannot attack.");
            return;
        }

        if (weapon == null)
        {
            Debug.LogError(name + " cannot initialize because UnitWeapon is missing.");
            return;
        }

        weapon.Initialize(this, gameContext);
    }

    public override void Tick(float deltaTime)
    {
        weapon?.Tick(deltaTime);

        UpdateCombatAwareness();

        base.Tick(deltaTime);
    }

    public override void IssueCommand(CommandType commandType, CommandContext context)
    {
        if (!CanReceiveCommands)
            return;

        CurrentCommand = commandType;
        currentContext = context;

        switch (commandType)
        {
            case CommandType.Move:
                SetState(new MoveState(context.WorldPosition, context.FormationSlotIndex, context.FormationUnitCount));
                break;

            case CommandType.Attack:
                SetState(new AttackState(context.Target));
                break;

            case CommandType.HoldPosition:
                EnterCombatIdle();
                break;
            
            case CommandType.Idle:
            default:
                SetState(new CombatIdleState());
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Attack Methods
    // ---------------------------------------------------------------------

    private void UpdateCombatAwareness()
    {
        if (!IsInitialized || !IsAlive || !CanAttack())
            return;

        if (currentState is AttackState)
            return;

        ITargetable target = FindClosestAttackTarget();

        if (target == null)
            return;

        SetState(new AttackState(target));
    }

    public void UpdateAttack(float deltaTime)
    {
        if (!IsValidAttackTarget(currentTarget))
            return;

        FaceTarget(currentTarget, deltaTime);

        if (attackAnimationActive)
            return;

        if (!weapon.TryBeginAttack(currentTarget))
            return;

        attackTarget = currentTarget;
        attackAnimationActive = true;

        view?.PlayAnim("Attack");
    }

    public void OnWeaponFireAnimationEvent()
    {
        if (!attackAnimationActive)
            return;

        if (attackTarget == null || !attackTarget.IsAlive)
            return;

        weapon.TryFireProjectile(attackTarget);
    }

    public void OnAttackAnimationFinished()
    {
        attackAnimationActive = false;
        attackTarget = null;

        view?.PlayAnim("Idle");
    }

    public void CancelAttackAnimation()
    {
        attackAnimationActive = false;
        attackTarget = null;
    }

    // ---------------------------------------------------------------------
    // Helper methods
    // ---------------------------------------------------------------------

    public void EnterCombatIdle()
    {
        CurrentCommand = CommandType.Idle;
        currentContext = CommandContext.None();

        SetState(new CombatIdleState());
    }

    public void ClearCurrentTarget()
    {
        currentTarget = null;
    }

    public bool CanAttack()
    {
        return definition != null && definition.canAttack && sensor != null && weapon != null;
    }

    public bool IsWithinAttackRange(ITargetable target)
    {
        if (target == null)
            return false;

        Vector3 difference = target.Position - Position;

        difference.y = 0f;

        float attackRange = GetAttackRange();

        return difference.sqrMagnitude <= attackRange * attackRange;
    }

    public bool IsValidAttackTarget(ITargetable target)
    {
        if (sensor == null)
            return false;

        return sensor.IsValidEnemyTarget(target) && IsWithinAttackRange(target);
    }

    public ITargetable FindClosestAttackTarget()
    {
        if (!CanAttack())
            return null;

        return sensor.FindClosestEnemy(GetAttackRange());
    }

    public void FaceTarget(ITargetable target, float deltaTime)
    {
        if (target == null)
            return;

        Vector3 direction = target.Position - transform.position;

        // The unit rotates only around the vertical axis.
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, combatRotationSpeed * deltaTime);
    }

    // ---------------------------------------------------------------------
    // Getter & Setter 
    // ---------------------------------------------------------------------

    public float GetAttackRange()
    {
        if (definition == null)
            return 0.0f;

        return definition.attackRange;
    }

    public void SetCurrentTarget(ITargetable target)
    {
        currentTarget = target;
    }

    // Gizmos 
    private void OnDrawGizmosSelected()
    {
        if (definition == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, definition.visionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, definition.attackRange);

        if (CurrentTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(
                transform.position + Vector3.up * 0.25f,
                CurrentTarget.Position + Vector3.up * 0.25f
            );
        }
    }

}
