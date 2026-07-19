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

    /// <summary>
    /// Resolves a screen-space position into a valid ground position
    /// and issues a move command to the currently selected,
    /// commandable units.
    /// </summary>
    public bool TryIssueMoveCommandFromScreen(Vector2 screenPosition)
    {
        if (!CanIssueCommands())
            return false;

        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;

        if (!Physics.Raycast(ray, out hit, 10000f, groundMask))
        {
            return false;
        }

        IssueMoveCommand(hit.point);
        return true;
    }

    /// <summary>
    /// Issues a move command directly from a world-space position.
    ///
    /// This entry point can also be used later by the minimap or
    /// other input surfaces that already know the world position.
    /// </summary>
    public void IssueMoveCommand(Vector3 worldPosition)
    {
        if (gameContext == null || issuingFaction == null)
            return;

        CollectCommandableSelectedUnits();

        int commandableCount = commandableUnits.Count;

        for (int i = 0; i < commandableCount; i++)
        {
            UnitBase unit = commandableUnits[i];

            IControllable controllable = unit as IControllable;

            if (controllable == null)
                continue;

            Vector3 destination = worldPosition + GetSimpleDestinationOffset(i, commandableCount);

            //Debug.Log("Success issueCommand");
            controllable.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination));
        }
    }

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

    // Helpers method 
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
        return isActiveAndEnabled && worldCamera != null && gameContext != null && issuingFaction != null;
    }
}
