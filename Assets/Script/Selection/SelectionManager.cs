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

    private readonly List<ISelectionHandler> selectionHandlers = new();

    private GameContext gameContext;

    private Vector2 dragStartScreenPosition;
    private Vector2 currentScreenPosition;

    private bool isSelectionActive;
    private bool isDragging;

    public void Initialize(GameContext gameContext)
    {
        this.gameContext = gameContext;

        BuildSelectionChain();
    }

    private void Awake()
    {
        if (worldCamera == null)
            worldCamera = Camera.main;
    }

    private void BuildSelectionChain()
    {
        selectionHandlers.Clear();

        selectionHandlers.Add(new UnitSelectionHandler(this));
        selectionHandlers.Add(new BuildingSelectionHandler(this));
        selectionHandlers.Add(new ResourceSelectionHandler(this));
        selectionHandlers.Add(new NoSelectionHandler(this));
    }

    // ---------------------------------------------------------------------
    // Selection gesture
    // ---------------------------------------------------------------------

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
    public void EndSelection(Vector2 screenPosition, bool append)
    {
        if (!isSelectionActive)
            return;

        currentScreenPosition = screenPosition;

        //if (isDragging)
        //    SelectUnitsInBox(append);
        //else
        //    SelectSingleUnitAtScreenPosition(screenPosition, append);

        SelectionQuery query;

        if (isDragging)
        {
            query = SelectionQuery.CreateBox(GetScreenRect(dragStartScreenPosition, currentScreenPosition));
        }
        else
        {
            query = SelectionQuery.CreateClick(screenPosition);
        }

        for (int i = 0; i < selectionHandlers.Count; i++)
        {
            if (selectionHandlers[i].TryHandle(query, append))
                break;
        }

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

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

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

    private Rect GetScreenRect(Vector2 start, Vector2 end)
    {
        float xMin = Mathf.Min(start.x, end.x);
        float xMax = Mathf.Max(start.x, end.x);
        float yMin = Mathf.Min(start.y, end.y);
        float yMax = Mathf.Max(start.y, end.y);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void OnDisable()
    {
        ResetSelectionGesture();
    }

    // ---------------------------------------------------------------------
    // Candidate resolution
    // ---------------------------------------------------------------------

    //private void SelectSingleUnitAtScreenPosition(Vector2 screenPosition, bool append)
    //{
    //    UnitBase unit = FindUnitAtScreenPosition(screenPosition);

    //    if (unit != null)
    //    {
    //        gameContext.SelectUnit(unit, append);
    //        return;
    //    }

    //    if (!append)
    //        gameContext.ClearSelectedUnits();
    //}

    //private UnitBase FindUnitAtScreenPosition(Vector2 screenPosition)
    //{
    //    Ray ray = worldCamera.ScreenPointToRay(screenPosition);

    //    if (!Physics.Raycast(ray, out RaycastHit hit, 10000f, selectableMask))
    //    {
    //        return null;
    //    }

    //    UnitBase unit = hit.collider.GetComponentInParent<UnitBase>();

    //    if (unit == null || !unit.CanBeSelected)
    //        return null;

    //    return unit;
    //}

    //private void SelectUnitsInBox(bool append)
    //{
    //    Rect selectionRect = GetScreenRect(dragStartScreenPosition, currentScreenPosition);
    //    List<UnitBase> unitsInBox = new List<UnitBase>();

    //    IReadOnlyList<UnitBase> allUnits = gameContext.AllUnits;

    //    //Debug.Log("SelectUnitsInBox");

    //    for (int i = 0; i < allUnits.Count; i++)
    //    {
    //        UnitBase unit = allUnits[i];
    //        //Debug.Log("Added : " + unit.name);

    //        if (unit == null || !unit.CanBeSelected)
    //            continue;

    //        Vector3 screenPosition = worldCamera.WorldToScreenPoint(unit.SelectionPosition);

    //        if (screenPosition.z < 0f)
    //            continue;

    //        if (selectionRect.Contains(screenPosition, true))
    //        {
    //            //Debug.Log("Added : " + unit.name);
    //            unitsInBox.Add(unit);
    //        }
    //    }

    //    gameContext.SelectUnits(unitsInBox, append);
    //}

    private T FindSelectableAtScreenPosition<T>(Vector2 screenPosition) where T : Component, ISelectable
    {
        Ray ray = worldCamera.ScreenPointToRay(screenPosition);

        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, selectableMask, QueryTriggerInteraction.Ignore);

        T closestCandidate = null;
        float closestDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            T candidate = hit.collider.GetComponentInParent<T>();

            if (candidate == null || !candidate.CanBeSelected)
                continue;

            if (hit.distance >= closestDistance)
                continue;

            closestCandidate = candidate;
            closestDistance = hit.distance;
        }

        return closestCandidate;
    }

    private List<UnitBase> FindUnitsInBox(Rect selectionRect)
    {
        List<UnitBase> result = new();

        IReadOnlyList<UnitBase> allUnits = gameContext.AllUnits;

        for (int i = 0; i < allUnits.Count; i++)
        {
            UnitBase unit = allUnits[i];

            if (unit == null || !unit.CanBeSelected)
                continue;

            if (IsInsideSelectionRect(unit.SelectionPosition, selectionRect))
                result.Add(unit);
        }

        return result;
    }

    private BuildingBase FindBuildingInBox(Rect selectionRect)
    {
        IReadOnlyList<BuildingBase> allBuildings = gameContext.AllBuildings;

        // Only one building may be selected.
        for (int i = 0; i < allBuildings.Count; i++)
        {
            BuildingBase building = allBuildings[i];

            if (building == null || !building.CanBeSelected)
                continue;

            if (IsInsideSelectionRect(building.SelectionPosition, selectionRect))
            {
                return building;
            }
        }

        return null;
    }

    private ResourceNode FindResourceNodeInBox(Rect selectionRect)
    {
        IReadOnlyList<ResourceNode> allResourceNodes = gameContext.AllResourceNodes;

        // Only one building may be selected.
        for (int i = 0; i < allResourceNodes.Count; i++)
        {
            ResourceNode resourceNode = allResourceNodes[i];

            if (resourceNode == null || !resourceNode.CanBeSelected)
                continue;

            if (IsInsideSelectionRect(resourceNode.SelectionPosition, selectionRect))
            {
                return resourceNode;
            }
        }

        return null;
    }

    private bool IsInsideSelectionRect(Vector3 worldPosition, Rect selectionRect)
    {
        Vector3 screenPosition = worldCamera.WorldToScreenPoint(worldPosition);

        if (screenPosition.z < 0f)
            return false;

        return selectionRect.Contains(screenPosition, true);
    }

    // ---------------------------------------------------------------------
    // Selection chain
    // ---------------------------------------------------------------------

    private interface ISelectionHandler
    {
        bool TryHandle(SelectionQuery query, bool append);
    }

    private sealed class UnitSelectionHandler : ISelectionHandler
    {
        private readonly SelectionManager owner;

        public UnitSelectionHandler(SelectionManager owner)
        {
            this.owner = owner;
        }

        public bool TryHandle(SelectionQuery query, bool append)
        {
            if (query.IsBoxSelection)
            {
                List<UnitBase> units = owner.FindUnitsInBox(query.ScreenRect);

                if (units.Count == 0)
                    return false;

                owner.gameContext.SelectUnits(units, append);
                return true;
            }

            UnitBase unit = owner.FindSelectableAtScreenPosition<UnitBase>(query.ScreenPosition);

            if (unit == null)
                return false;

            owner.gameContext.SelectUnit(unit, append);
            return true;
        }
    }

    private sealed class BuildingSelectionHandler : ISelectionHandler
    {
        private readonly SelectionManager owner;

        public BuildingSelectionHandler(SelectionManager owner)
        {
            this.owner = owner;
        }

        public bool TryHandle(SelectionQuery query, bool append)
        {
            BuildingBase building;

            if (query.IsBoxSelection)
            {
                building = owner.FindBuildingInBox(query.ScreenRect);
            }
            else
            {
                building = owner.FindSelectableAtScreenPosition<BuildingBase>(query.ScreenPosition);
            }

            if (building == null)
                return false;

            // Append is intentionally ignored.
            // Buildings are always a single exclusive selection.
            owner.gameContext.SelectBuilding(building);

            return true;
        }
    }

    private sealed class ResourceSelectionHandler : ISelectionHandler
    {
        private readonly SelectionManager owner;

        public ResourceSelectionHandler(SelectionManager owner)
        {
            this.owner = owner;
        }

        public bool TryHandle(SelectionQuery query, bool append)
        {
            ResourceNode resourceNode;

            if (query.IsBoxSelection)
            {
                resourceNode = owner.FindResourceNodeInBox(query.ScreenRect);
            }
            else
            {
                resourceNode = owner.FindSelectableAtScreenPosition<ResourceNode>(query.ScreenPosition);
            }

            if (resourceNode == null)
                return false;

            // Append is intentionally ignored.
            // ResourceNode are always a single exclusive selection.
            owner.gameContext.SelectResourceNode(resourceNode);

            return true;
        }
    }

    private sealed class NoSelectionHandler : ISelectionHandler
    {
        private readonly SelectionManager owner;

        public NoSelectionHandler(SelectionManager owner)
        {
            this.owner = owner;
        }

        public bool TryHandle(SelectionQuery query, bool append)
        {
            // Shift-clicking empty ground preserves the current selection.
            if (!append)
                owner.gameContext.ClearSelection();

            return true;
        }
    }

    private readonly struct SelectionQuery
    {
        public bool IsBoxSelection { get; }
        public Vector2 ScreenPosition { get; }
        public Rect ScreenRect { get; }

        private SelectionQuery(bool isBoxSelection, Vector2 screenPosition, Rect screenRect)
        {
            IsBoxSelection = isBoxSelection;
            ScreenPosition = screenPosition;
            ScreenRect = screenRect;
        }

        public static SelectionQuery CreateClick(Vector2 screenPosition)
        {
            return new SelectionQuery(false, screenPosition, default);
        }

        public static SelectionQuery CreateBox(Rect screenRect)
        {
            return new SelectionQuery(true, default, screenRect);
        }
    }

}
