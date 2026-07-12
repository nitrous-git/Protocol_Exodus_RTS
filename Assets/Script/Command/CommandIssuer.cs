using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CommandIssuer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;

    [Header("Move Command")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groupDestinationSpacing = 50.25f;
    [SerializeField] private bool issueMoveOnRightClick = true;
    [SerializeField] private bool ignoreInputOverUI = true;

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

    public void TickInput(float deltaTime)
    {
        if (!issueMoveOnRightClick)
            return;

        if (gameContext == null || worldCamera == null)
            return;

        if (!Input.GetMouseButtonDown(1))
            return;

        if (ignoreInputOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        TryIssueMoveCommandFromScreen(Input.mousePosition);
    }

    public bool TryIssueMoveCommandFromScreen(Vector2 screenPosition)
    {
        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        RaycastHit hit;

        if (!Physics.Raycast(ray, out hit, 10000f, groundMask))
            return false;

        IssueMoveCommand(hit.point);
        return true;
    }

    public void IssueMoveCommand(Vector3 worldPosition)
    {
        CollectCommandableSelectedUnits();

        int commandableCount = commandableUnits.Count;

        for (int i = 0; i < commandableCount; i++)
        {
            UnitBase unit = commandableUnits[i];

            IControllable controllable = unit as IControllable;

            if (controllable == null)
                continue;

            Vector3 destination = worldPosition + GetSimpleDestinationOffset(i + 1, commandableCount);

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

    private Vector3 GetSimpleDestinationOffset(int index, int count)
    {
        if (count <= 1)
            return Vector3.zero;

        float angle = index * 137.5f * Mathf.Deg2Rad;
        float radius = Mathf.Sqrt(index + 1) * groupDestinationSpacing;

        return new Vector3(Mathf.Cos(angle) * radius,0f, Mathf.Sin(angle) * radius);
    }
}
