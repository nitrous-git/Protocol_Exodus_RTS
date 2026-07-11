using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class SelectionManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private SelectionBox selectionBox;

    [Header("Selection")]
    [SerializeField] private LayerMask selectableMask = ~0;
    [SerializeField] private float dragThresholdPixels = 8f;
    [SerializeField] private bool shiftAddsToSelection = true;
    [SerializeField] private bool ignoreInputOverUI = true;

    private GameContext gameContext;

    private Vector2 dragStartScreenPosition;
    private Vector2 currentScreenPosition;
    private bool isDragging;
    private bool mouseDownStartedOverUI;

    public void Initialize(GameContext gameContext)
    {
        this.gameContext = gameContext;
    }

    private void Awake()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;
    }

    public void TickInput(float deltaTime)
    {
        if (worldCamera == null || gameContext == null)
            return;

        HandleSelectionInput();
    }

    private void HandleSelectionInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            mouseDownStartedOverUI = IsPointerOverUI();

            if (ignoreInputOverUI && mouseDownStartedOverUI)
                return;

            dragStartScreenPosition = Input.mousePosition;
            currentScreenPosition = dragStartScreenPosition;
            isDragging = false;

            if (selectionBox != null)
                selectionBox.Hide();
        }

        if (Input.GetMouseButton(0))
        {
            if (ignoreInputOverUI && mouseDownStartedOverUI)
                return;

            currentScreenPosition = Input.mousePosition;

            if (!isDragging)
            {
                float dragDistance = Vector2.Distance(dragStartScreenPosition, currentScreenPosition);

                if (dragDistance >= dragThresholdPixels)
                {
                    isDragging = true;

                    if (selectionBox != null)
                        selectionBox.Show();
                }
            }

            if (isDragging && selectionBox != null)
                selectionBox.UpdateVisual(dragStartScreenPosition, currentScreenPosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            if (ignoreInputOverUI && mouseDownStartedOverUI)
                return;

            currentScreenPosition = Input.mousePosition;

            bool append = shiftAddsToSelection &&
                (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

            if (isDragging)
                SelectUnitsInBox(append);
            else
                SelectSingleUnitUnderMouse(append);

            isDragging = false;
            mouseDownStartedOverUI = false;

            if (selectionBox != null)
                selectionBox.Hide();
        }
    }

    private void SelectSingleUnitUnderMouse(bool append)
    {
        UnitBase unit = FindUnitUnderMouse();

        if (unit != null)
        {
            gameContext.SelectUnit(unit, append);
            return;
        }

        if (!append)
            gameContext.ClearSelectedUnits();
    }

    private UnitBase FindUnitUnderMouse()
    {
        Ray ray = worldCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (!Physics.Raycast(ray, out hit, 10000f, selectableMask))
            return null;

        UnitBase unit = hit.collider.GetComponentInParent<UnitBase>();

        if (unit == null || !unit.CanBeSelected)
            return null;

        return unit;
    }

    private void SelectUnitsInBox(bool append)
    {
        Rect selectionRect = GetScreenRect(dragStartScreenPosition, currentScreenPosition);
        List<UnitBase> unitsInBox = new List<UnitBase>();

        IReadOnlyList<UnitBase> allUnits = gameContext.AllUnits;

        //Debug.Log("SelectUnitsInBox");

        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitBase unit = allUnits[i];
            //Debug.Log("Added : " + unit.name);

            if (unit == null || !unit.CanBeSelected)
                continue;

            Vector3 screenPosition = worldCamera.WorldToScreenPoint(unit.SelectionPosition);

            if (screenPosition.z < 0f)
                continue;

            if (selectionRect.Contains(screenPosition, true))
            {
                //Debug.Log("Added : " + unit.name);
                unitsInBox.Add(unit);
            }
        }

        gameContext.SelectUnits(unitsInBox, append);
    }

    private Rect GetScreenRect(Vector2 start, Vector2 end)
    {
        float xMin = Mathf.Min(start.x, end.x);
        float xMax = Mathf.Max(start.x, end.x);
        float yMin = Mathf.Min(start.y, end.y);
        float yMax = Mathf.Max(start.y, end.y);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
