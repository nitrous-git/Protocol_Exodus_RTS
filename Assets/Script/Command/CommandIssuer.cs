using System.Collections.Generic;
using UnityEngine;

public class CommandIssuer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;

    [Header("Move Command")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groupDestinationSpacing = 5f;

    private List<UnitBase> commandableUnits = new List<UnitBase>();

    private GameContext gameContext;
    private Faction issuingFaction;

    private Vector3 currentGroundPosition = Vector3.zero;
    private Vector3 currentGroundNormal = Vector3.up;

    public Vector3 CurrentGroundPosition => currentGroundPosition;
    public Vector3 CurrentGroundNormal => currentGroundNormal;

    public void Initialize(GameContext gameContext, Faction issuingFaction)
    {
        this.gameContext = gameContext;
        this.issuingFaction = issuingFaction;
    }

    private void Awake()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;
    }

    // ---------------------------------------------------------------------
    // Move
    // ---------------------------------------------------------------------

    /// <summary>
    /// Resolves a screen position to a point on commandable ground.
    /// </summary>
    public bool TryResolveGroundPositionFromScreen(Vector2 screenPosition)
    {
        currentGroundPosition = Vector3.zero;
        currentGroundNormal = Vector3.up;

        if (!CanIssueCommands() || worldCamera == null)
            return false;

        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;

        if (!Physics.Raycast(ray, out hit, 10000f, groundMask))
        {
            return false;
        }

        currentGroundPosition = hit.point;
        currentGroundNormal = hit.normal;

        return true;
    }

    public bool TryIssueMoveCommandFromScreen(Vector2 screenPosition)
    {
        if (!TryResolveGroundPositionFromScreen(screenPosition))
        {
            return false;
        }

        return TryIssueMoveCommand(currentGroundPosition);
    }

    public bool TryIssueMoveCommand(Vector3 destinationCenter)
    {
        if (!CanIssueCommands())
            return false;

        CollectCommandableSelectedUnits();

        int commandableCount = commandableUnits.Count;

        if (commandableCount == 0)
            return false;

        bool issuedAnyCommand = false;

        for (int i = 0; i < commandableCount; i++)
        {
            UnitBase unit = commandableUnits[i];

            IControllable controllable = unit as IControllable;
            if (controllable == null)
                continue;

            Vector3 destination = destinationCenter + GetSimpleDestinationOffset(i, commandableCount);
;
            controllable.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination));

            issuedAnyCommand = true;
        }

        return issuedAnyCommand;
    }


    //public bool TryIssueMoveCommandFromScreen(Vector2 screenPosition)
    //{
    //    if (!CanIssueCommands() || worldCamera == null)
    //        return false;

    //    Ray ray = worldCamera.ScreenPointToRay(screenPosition);
    //    RaycastHit hit;

    //    if (!Physics.Raycast(ray, out hit, 10000f, groundMask))
    //    {
    //        return false;
    //    }

    //    IssueMoveCommand(hit.point);
    //    return true;
    //}

    // ---------------------------------------------------------------------
    // Immediate commands
    // ---------------------------------------------------------------------
    public bool TryIssueHoldPositionCommand()
    {
        if (!CanIssueCommands())
            return false;

        CollectCommandableSelectedUnits();

        if (commandableUnits.Count == 0)
            return false;

        bool issuedAnyCommand = false;
        CommandContext context = CommandContext.None();

        for (int i = 0; i < commandableUnits.Count; i++)
        {
            UnitBase unit = commandableUnits[i];

            if (unit is not IControllable controllable)
                continue;

            controllable.IssueCommand(CommandType.HoldPosition, context);

            issuedAnyCommand = true;
        }

        return issuedAnyCommand;
    }

    // ---------------------------------------------------------------------
    // Selection resolution
    // ---------------------------------------------------------------------

    private void CollectCommandableSelectedUnits()
    {
        commandableUnits.Clear();

        if (gameContext == null || issuingFaction == null)
            return;

        IReadOnlyList<UnitBase> selectedUnits = gameContext.SelectedUnits;

        for (int i = 0; i < selectedUnits.Count; i++)
        {
            UnitBase unit = selectedUnits[i];

            if (!issuingFaction.CanIssueCommandsTo(unit))
                continue;

            commandableUnits.Add(unit);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private Vector3 GetSimpleDestinationOffset(int index, int count)
    {
        if (count <= 1)
            return Vector3.zero;

        float angle = index * 137.5f * Mathf.Deg2Rad;
        float radius = Mathf.Sqrt(index + 1) * groupDestinationSpacing;

        return new Vector3(Mathf.Cos(angle) * radius,0f, Mathf.Sin(angle) * radius);
    }

    private bool CanIssueCommands()
    {
        return isActiveAndEnabled && gameContext != null && issuingFaction != null;
    }
}
