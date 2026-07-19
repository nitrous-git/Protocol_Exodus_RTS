using System.Collections.Generic;
using UnityEngine;

public class SelectionManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private SelectionBox selectionBox;

    [Header("Selection")]
    [SerializeField] private LayerMask selectableMask = ~0;
    [SerializeField] private float dragThresholdPixels = 8f;

    private GameContext gameContext;

    private Vector2 dragStartScreenPosition;
    private Vector2 currentScreenPosition;

    private bool isSelectionActive;
    private bool isDragging;

    public void Initialize(GameContext gameContext)
    {
        this.gameContext = gameContext;
    }

    private void Awake()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;
    }

    /// <summary>
    /// Begins a potential click or drag selection at the supplied
    /// screen-space position.
    /// </summary>
    public bool BeginSelection(Vector2 screenPosition)
    {
        if (!CanProcessSelection())
            return false;

        isSelectionActive = true;
        isDragging = false;

        dragStartScreenPosition = screenPosition;
        currentScreenPosition = screenPosition;

        selectionBox?.Hide();

        return true;
    }

    /// <summary>
    /// Updates the active selection gesture.
    ///
    /// The manager determines when the gesture crosses the configured
    /// drag threshold and becomes a box selection.
    /// </summary>
    public void UpdateSelection(Vector2 screenPosition)
    {
        if (!isSelectionActive)
            return;

        currentScreenPosition = screenPosition;

        if (!isDragging)
        {
            float dragDistance = Vector2.Distance(dragStartScreenPosition, currentScreenPosition);

            if (dragDistance >= dragThresholdPixels)
            {
                isDragging = true;
                selectionBox?.Show();
            }
        }

        if (isDragging)
        {
            selectionBox?.UpdateVisual(dragStartScreenPosition, currentScreenPosition);
        }
    }

    /// <summary>
    /// Completes the active selection gesture.
    ///
    /// A gesture below the drag threshold becomes a single selection.
    /// A gesture above the threshold becomes a box selection.
    /// </summary>
    public void EndSelection(
        Vector2 screenPosition,
        bool append)
    {
        if (!isSelectionActive)
            return;

        currentScreenPosition = screenPosition;

        if (isDragging)
            SelectUnitsInBox(append);
        else
            SelectSingleUnitAtScreenPosition(screenPosition, append);

        ResetSelectionGesture();
    }

    /// <summary>
    /// Cancels the current selection gesture without changing the
    /// selected-unit collection.
    /// </summary>
    public void CancelSelection()
    {
        ResetSelectionGesture();
    }

    // Helpers method
    private bool CanProcessSelection()
    {
        return isActiveAndEnabled && worldCamera != null && gameContext != null;
    }

    private void ResetSelectionGesture()
    {
        isSelectionActive = false;
        isDragging = false;

        selectionBox?.Hide();
    }

    private void SelectSingleUnitAtScreenPosition(Vector2 screenPosition, bool append)
    {
        UnitBase unit = FindUnitAtScreenPosition(screenPosition);

        if (unit != null)
        {
            gameContext.SelectUnit(unit, append);
            return;
        }

        if (!append)
            gameContext.ClearSelectedUnits();
    }

    private UnitBase FindUnitAtScreenPosition(Vector2 screenPosition)
    {
        Ray ray = worldCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 10000f, selectableMask))
        {
            return null;
        }

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

    // Cleanup 
    private void OnDisable()
    {
        ResetSelectionGesture();
    }
}
