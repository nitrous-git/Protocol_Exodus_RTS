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

    private GameContext gameContext;
    private Faction playerFaction;

    public void Initialize(GameContext gameContext, Faction playerFaction)
    {
        this.gameContext = gameContext;
        this.playerFaction = playerFaction;
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
        IReadOnlyList<UnitBase> selectedUnits = gameContext.SelectedUnits;

        for (int i = 0; i < selectedUnits.Count; i++)
        {
            UnitBase unit = selectedUnits[i];

            if (unit == null)
                continue;

            IControllable controllable = unit as IControllable;

            //Debug.Log("controllable is null");
            if (controllable == null || !controllable.CanReceiveCommands)
            {
                //Debug.Log("controllable is null or CanReceiveCommands is false");
                continue;
            }

            Vector3 destination = worldPosition + GetSimpleDestinationOffset(i, selectedUnits.Count);

            //Debug.Log("MoveCommand : "+ unit.name + " at location : " + destination);
            controllable.IssueCommand(CommandType.Move, CommandContext.MoveTo(destination));
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
