using System.Data;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;

public class UnitBase : MonoBehaviour, IControllable
{
    [Header("Definition")]
    [SerializeField] protected UnitDefinition definition;

    [Header("Selection / Control")]
    [SerializeField] private bool canBeSelected = true;
    [SerializeField] private bool canReceiveCommands = true;
    [SerializeField] private Transform selectionPoint;

    protected Faction ownerFaction;
    protected IUnitState currentState;
    protected CommandContext currentContext;

    protected UnitHealth health;
    protected UnitMotor motor;
    protected UnitSensor sensor;
    protected UnitView view;

    public UnitDefinition Definition => definition;
    public Faction OwnerFaction => ownerFaction;
    public CommandType CurrentCommand { get; protected set; } = CommandType.Idle;
    public bool IsSelected { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool CanReceiveCommands => canReceiveCommands;
    public bool CanBeSelected { get { return canBeSelected; } }


    public UnitHealth Health => health;
    public UnitMotor Motor => motor;
    public UnitSensor Sensor => sensor;
    public UnitView View => view;

    public Vector3 Position => transform.position;
    public bool IsAlive => health != null && health.IsAlive;

    public Vector3 SelectionPosition
    {
        get
        {
            if (selectionPoint != null)
                return selectionPoint.position;

            return transform.position;
        }
    }


    protected virtual void Awake()
    {
        CacheComponents();
    }

    protected virtual void Start()
    {
        Initialize();
    }

    protected virtual void CacheComponents()
    {
        health = GetComponent<UnitHealth>();
        motor = GetComponent<UnitMotor>();
        sensor = GetComponent<UnitSensor>();
        view = GetComponent<UnitView>();
    }

    public virtual void Initialize()
    {
        CacheComponents();

        if (definition == null)
        {
            Debug.LogError(name + " cannot initialize because UnitDefinition is missing.");
            return;
        }

        if (GameContext.Instance == null)
        {
            Debug.LogError(name + " cannot initialize because GameContext is missing.");
            return;
        }

        if (motor != null)
        {
            motor.Initialize(
                this,
                GameContext.Instance.PathfindingService,
                definition.moveSpeed
            );
        }

        if (view != null)
            view.Initialize(this);

        GameContext.Instance.RegisterUnit(this);

        IsInitialized = true;

        IssueCommand(CommandType.Idle, CommandContext.None());
    }

    protected virtual void Update()
    {
        if (!IsInitialized)
            return;

        if (currentState != null)
            currentState.Tick(this);
    }

    public virtual void IssueCommand(CommandType commandType, CommandContext context)
    {
        if (!CanReceiveCommands)
            return;

        CurrentCommand = commandType;
        currentContext = context;

        IUnitState nextState = CreateStateForCommand(commandType, context);
        SetState(nextState);
    }

    protected virtual IUnitState CreateStateForCommand(CommandType commandType, CommandContext context)
    {
        switch (commandType)
        {
            case CommandType.Move:
                return new MoveState(context.WorldPosition);

            case CommandType.Idle:
            default:
                return new IdleState();
        }
    }

    protected void SetState(IUnitState nextState)
    {
        if (currentState != null)
            currentState.OnExit(this);

        currentState = nextState;

        if (currentState != null)
            currentState.OnEnter(this);
    }

    public virtual void SetSelected(bool selected)
    {
        IsSelected = selected;

        if (view != null)
            view.SetSelected(selected);
    }

    protected virtual void OnDestroy()
    {
        if (GameContext.Instance != null)
            GameContext.Instance.UnregisterUnit(this);
    }


}
