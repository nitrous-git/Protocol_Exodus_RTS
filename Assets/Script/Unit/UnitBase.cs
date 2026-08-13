using System.Data;
using System.Reflection.Metadata;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;

public class UnitBase : MonoBehaviour, IControllable, ISelectable, ITargetable
{
    [Header("Definition")]
    [SerializeField] protected UnitDefinition definition;

    [Header("Selection / Control")]
    [SerializeField] private bool canBeSelected = true;
    [SerializeField] private bool canReceiveCommands = true;
    [SerializeField] private Transform selectionPoint;

    [Header("Targeting")]
    [SerializeField] private Transform aimPoint;

    protected Faction ownerFaction;
    protected GameContext gameContext;
    protected UnitManager owningUnitManager;

    protected IUnitState currentState;
    protected CommandContext currentContext;

    protected Health health;
    protected UnitMotor motor;
    protected UnitSensor sensor;
    protected UnitView view;


    public UnitDefinition Definition => definition;
    public Faction OwnerFaction => ownerFaction;

    public CommandType CurrentCommand { get; protected set; } = CommandType.Idle;
    public string CurrentStateName => currentState != null ? currentState.GetType().Name : "None";


    public bool IsSelected { get; private set; }
    public bool IsInitialized { get; private set; }
    public int UnitId { get; private set; } = -1;

    public bool CanReceiveCommands => canReceiveCommands;
    public bool CanBeSelected => canBeSelected;


    public Health Health => health;
    public UnitMotor Motor => motor;
    public UnitSensor Sensor => sensor;
    public UnitView View => view;

    public Vector3 Position => transform.position;
    public bool IsAlive => health != null && health.IsAlive;
    public Transform AimPoint => aimPoint != null ? aimPoint : transform;

    public Vector3 SelectionPosition => selectionPoint != null ? selectionPoint.position : transform.position;

    public TerrainGrid TerrainGrid => gameContext?.TerrainGrid;

    protected virtual void Awake()
    {
        CacheComponents();
    }

    protected virtual void CacheComponents()
    {
        health = GetComponent<Health>();
        motor = GetComponent<UnitMotor>();
        sensor = GetComponent<UnitSensor>();
        view = GetComponent<UnitView>();
    }

    public virtual void Initialize(Faction ownerFaction, GameContext gameContext, IPathfindingService pathfindingService, UnitManager owningUnitManager)
    {
        if (IsInitialized)
            return;

        UnitId = gameContext.AllocateUnitId();

        CacheComponents();

        this.ownerFaction = ownerFaction;
        this.gameContext = gameContext;
        this.owningUnitManager = owningUnitManager;


        if (definition == null)
        {
            Debug.LogError(name + " cannot initialize because UnitDefinition is missing.");
            return;
        }

        if (gameContext == null)
        {
            Debug.LogError(name + " cannot initialize because GameContext is missing.");
            return;
        }

        if (health == null)
        {
            Debug.LogError(name + " cannot initialize because UnitHealth is missing.");
            return;
        }

        health.Initialize(definition.maxHealth);
        health.OnDied += HandleDied;

        motor?.Initialize(this, pathfindingService, definition.moveSpeed);
        sensor?.Initialize(this, gameContext);
        view?.Initialize(this);

        owningUnitManager.RegisterUnit(this);

        IsInitialized = true;
        IssueCommand(CommandType.Idle, CommandContext.None());
    }

    public virtual void Tick(float deltaTime)
    {
        if (!IsInitialized) return;

        sensor?.Tick(deltaTime);
        currentState?.Tick(this, deltaTime);
        motor?.Tick();
    }

    public virtual void TickLate(float deltaTime)
    {
        if (!IsInitialized) return; 

        view?.TickLate();
    }

    public virtual void IssueCommand(CommandType commandType, CommandContext context)
    {
        if (!CanReceiveCommands) return;

        CurrentCommand = commandType;
        currentContext = context;

        //IUnitState nextState = CreateStateForCommand(commandType, context);
        //SetState(nextState);

        switch (commandType)
        {
            case CommandType.Move:
                SetState(new MoveState(context.WorldPosition));
                break;

            case CommandType.HoldPosition:
                motor?.Stop();
                SetState(new IdleState());
                break;

            case CommandType.Idle:
            default:
                //SetState(new IdleState()); 
                break;  
        }
    }

    //protected virtual IUnitState CreateStateForCommand(CommandType commandType, CommandContext context)
    //{
    //    switch (commandType)
    //    {
    //        case CommandType.Move:
    //            return new MoveState(context.WorldPosition);

    //        case CommandType.Idle:
    //        default:
    //            return new IdleState();
    //    }
    //}

    // Selection methods

    protected void SetState(IUnitState nextState)
    {
        currentState?.OnExit(this);
        currentState = nextState;
        currentState?.OnEnter(this);
    }

    public virtual void SetSelected(bool selected)
    {
        IsSelected = selected;
        view?.SetSelected(selected);
    }

    // Health & Damage methods

    public virtual void TakeDamage(DamageInfo damageInfo)
    {
        health?.ApplyDamage(damageInfo);
    }

    protected virtual void HandleDied()
    {
        motor?.Stop();
        SetSelected(false);
        owningUnitManager?.RequestRemoveUnit(this);
    }

    protected virtual void OnDestroy()
    {
        if (health != null)
            health.OnDied -= HandleDied;

        owningUnitManager?.UnregisterUnit(this);
    }

}
