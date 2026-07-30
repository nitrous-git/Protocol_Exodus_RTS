using UnityEngine;

/// <summary>
/// Interprets input for a human-controlled faction.
///
/// Raw keyboard and pointer state is read through the input handlers.
/// This controller decides what that input means according to the
/// current PlayerInteractionMode.
/// </summary>
public sealed class PlayerFactionController : IFactionController, IPlayerInputController
{
    private Faction faction;
    private GameContext gameContext;

    private PlayerInteractionMode currentInteractionMode = PlayerInteractionMode.Default;

    private SelectionManager selectionManager;
    private CommandIssuer commandIssuer;
    private CameraController cameraController;

    private PlayerInputBindings inputBindings;

    private KeyInputHandler keyInputHandler;
    private MouseInputHandler mouseInputHandler;

    private bool isPlayerControlInitialized;
    private bool selectionPointerCaptured;

    private GameObject targetMarkerPrefab;
    private Transform interactionMarkersRoot;
    private GameObject activeMoveTargetMarker;
    private const float markerHeightOffset = 0.05f;

    private BuildingPlacementController buildingPlacementController;
    private BuildingDefinition pendingBuildingDefinition;

    public PlayerInteractionMode CurrentInteractionMode => currentInteractionMode;

    // ---------------------------------------------------------------------
    // Initialization
    // ---------------------------------------------------------------------

    public void Initialize(Faction faction, GameContext gameContext)
    {
        this.faction = faction;
        this.gameContext = gameContext;
    }

    /// <summary>
    /// Initializes dependencies used only by a human-controlled faction.
    ///
    /// This is deliberately separate from the generic IFactionController
    /// initialization because AI controllers do not require these systems.
    /// </summary>
    public void InitializePlayerControl(
    PlayerInputBindings inputBindings,
    SelectionManager selectionManager,
    CommandIssuer commandIssuer,
    CameraController cameraController,
    TerrainGrid terrainGrid,
    GameObject targetMarkerPrefab,
    BuildingPlacementPreview buildingPlacementPreviewPrefab,
    Transform interactionMarkersRoot)
    {
        this.inputBindings = inputBindings;
        this.selectionManager = selectionManager;
        this.commandIssuer = commandIssuer;
        this.cameraController = cameraController;

        this.targetMarkerPrefab = targetMarkerPrefab;
        this.interactionMarkersRoot = interactionMarkersRoot;

        keyInputHandler = new KeyInputHandler(inputBindings);
        mouseInputHandler = new MouseInputHandler(inputBindings);

        isPlayerControlInitialized = true;
        selectionPointerCaptured = false;

        buildingPlacementController = 
            new BuildingPlacementController(
                terrainGrid, 
                faction.BuildingManager, 
                commandIssuer, 
                buildingPlacementPreviewPrefab, 
                interactionMarkersRoot);

        currentInteractionMode = PlayerInteractionMode.Default;
        EnterInteractionMode(currentInteractionMode);
    }

    // ---------------------------------------------------------------------
    // Tick
    // ---------------------------------------------------------------------

    public void Tick(){ }

    public void TickInput(float deltaTime)
    {
        if (!isPlayerControlInitialized)
        {
            ResetPlayerInputState();
            return;
        }

        keyInputHandler.TickInput();
        mouseInputHandler.TickInput();

        // Global input
        cameraController?.SetMovementInput(keyInputHandler.CameraMovement);

        // Interaction-dependent input
        HandleCurrentInteraction();
    }

    // ---------------------------------------------------------------------
    // Command Panel requests
    // ---------------------------------------------------------------------

    /// <summary>
    /// Receives an action requested by the Command Panel.
    ///
    /// The panel does not need to know whether the action executes
    /// immediately or changes the current interaction mode.
    /// </summary>
    public void HandleCommandPanelAction(CommandPanelAction action)
    {
        if (!isPlayerControlInitialized)
            return;

        switch (action.Type)
        {
            case CommandPanelActionType.None:
                break;

            case CommandPanelActionType.HoldPosition:
                IssueHoldPosition();
                break;

            case CommandPanelActionType.BeginMoveTargeting:
                BeginMoveTargeting();
                break;

            case CommandPanelActionType.BeginAttackTargeting:
                //BeginAttackTargeting();
                break;

            case CommandPanelActionType.BeginBuildingPlacement:
                BeginBuildingPlacement(action.BuildingDefinition);
                break;

            case CommandPanelActionType.TrainUnit:
                TryTrainUnit(action.UnitDefinition);
                break;

            case CommandPanelActionType.BeginRallyPointTargeting:
                //BeginRallyPointTargeting();
                break;

            case CommandPanelActionType.CancelProduction:
                //TryCancelProduction();
                break;

            case CommandPanelActionType.CancelInteraction:
                CancelCurrentInteraction();
                break;

            default:
                Debug.LogWarning($"Unsupported Command Panel action: {action.Type}");
                break;
        }
    }

    private void IssueHoldPosition()
    {
        if (currentInteractionMode != PlayerInteractionMode.Default)
        {
            return;
        }

        commandIssuer?.TryIssueHoldPositionCommand();
    }

    private void BeginMoveTargeting()
    {
        if (currentInteractionMode != PlayerInteractionMode.Default)
        {
            return;
        }

        SetInteractionMode(PlayerInteractionMode.MoveTargeting);
    }

    private void BeginBuildingPlacement(BuildingDefinition definition)
    {
        if (definition == null)
            return;

        if (buildingPlacementController == null)
            return;

        // The player may choose another building while already in placement mode.
        if (currentInteractionMode == PlayerInteractionMode.BuildPlacement)
        {
            buildingPlacementController.Begin(definition);
            return;
        }

        pendingBuildingDefinition = definition;

        SetInteractionMode(PlayerInteractionMode.BuildPlacement);
    }

    private void TryTrainUnit(UnitDefinition unitDefinition)
    {
        if (unitDefinition == null)
            return;

        // Later:
        // 1. Resolve the selected player-owned production building.
        // 2. Verify it supports this UnitDefinition.
        // 3. Ask the building's production component to enqueue it.
    }

    // ---------------------------------------------------------------------
    // Interaction routing
    // ---------------------------------------------------------------------

    private void HandleCurrentInteraction()
    {
        switch (currentInteractionMode)
        {
            case PlayerInteractionMode.Default:
                HandleDefaultInteraction();
                break;

            case PlayerInteractionMode.MoveTargeting:
                HandleMoveTargetingInteraction();
                break;

            case PlayerInteractionMode.BuildPlacement:
                HandleBuildingPlacementInteraction();
                break;

            default:
                Debug.LogWarning($"Unsupported player interaction mode: {currentInteractionMode}. Returning to Default.");
                CancelCurrentInteraction();
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Default interaction
    // ---------------------------------------------------------------------

    private void HandleSelectionInput()
    {
        if (mouseInputHandler.PrimaryPressed)
        {
            selectionPointerCaptured = !IsWorldPointerBlocked() && selectionManager != null && selectionManager.BeginSelection( mouseInputHandler.PointerPosition);
        }

        if (selectionPointerCaptured && mouseInputHandler.PrimaryHeld)
        {
            selectionManager.UpdateSelection(mouseInputHandler.PointerPosition);
        }

        if (mouseInputHandler.PrimaryReleased)
        {
            if (selectionPointerCaptured)
            {
                selectionManager.EndSelection(mouseInputHandler.PointerPosition, keyInputHandler.AddToSelectionHeld);
            }

            selectionPointerCaptured = false;
        }
    }

    private void HandleDefaultCommandInput()
    {
        if (!inputBindings.IssueMoveOnSecondaryPointer)
            return;

        if (!mouseInputHandler.SecondaryPressed)
            return;

        if (IsWorldPointerBlocked())
            return;

        commandIssuer?.TryIssueMoveCommandFromScreen(mouseInputHandler.PointerPosition);
    }

    private void HandleDefaultInteraction()
    {
        HandleSelectionInput();
        HandleDefaultCommandInput();
    }

    // ---------------------------------------------------------------------
    // Move Targeting interaction
    // ---------------------------------------------------------------------

    private void HandleMoveTargetingInteraction()
    {
        bool hasGroundPosition = UpdateMoveTargetMarker();

        if (mouseInputHandler.SecondaryPressed)
        {
            CancelCurrentInteraction();
            return;
        }

        if (!mouseInputHandler.PrimaryPressed)
            return;

        if (IsWorldPointerBlocked() || !hasGroundPosition)
            return;

        Vector3 destination = commandIssuer.CurrentGroundPosition;
        bool commandIssued = commandIssuer.TryIssueMoveCommand(destination);

        if (commandIssued)
        {
            SetInteractionMode(PlayerInteractionMode.Default);
        }
    }

    private bool UpdateMoveTargetMarker()
    { 
        if (activeMoveTargetMarker == null || commandIssuer == null)
            return false;

        bool hasGroundPosition = 
            !IsWorldPointerBlocked() 
            && commandIssuer != null 
            && commandIssuer.TryResolveGroundPositionFromScreen(mouseInputHandler.PointerPosition);

        activeMoveTargetMarker.SetActive(hasGroundPosition);

        if (!hasGroundPosition)
            return false;

        Vector3 groundPosition = commandIssuer.CurrentGroundPosition;
        Vector3 groundNormal = commandIssuer.CurrentGroundNormal;

        Vector3 markerPosition = groundPosition + groundNormal * markerHeightOffset;
        Quaternion markerRotation = Quaternion.FromToRotation(Vector3.up, groundNormal);

        activeMoveTargetMarker.transform.SetPositionAndRotation(markerPosition, markerRotation);

        return true;
    }

    private void SpawnMoveTargetMarker()
    {
        if (activeMoveTargetMarker != null)
            return;

        activeMoveTargetMarker = Object.Instantiate(targetMarkerPrefab, interactionMarkersRoot);
        activeMoveTargetMarker.SetActive(false);
    }

    private void DestroyMoveTargetMarker()
    {
        if (activeMoveTargetMarker == null)
            return;

        Object.Destroy(activeMoveTargetMarker);
        activeMoveTargetMarker = null;
    }

    // ---------------------------------------------------------------------
    // Building Placement interaction
    // ---------------------------------------------------------------------

    private void HandleBuildingPlacementInteraction()
    {
        if (buildingPlacementController == null)
        {
            CancelCurrentInteraction();
            return;
        }

        bool pointerBlocked = IsWorldPointerBlocked();

        buildingPlacementController.UpdatePlacement(mouseInputHandler.PointerPosition, pointerBlocked);

        if (mouseInputHandler.SecondaryPressed)
        {
            CancelCurrentInteraction();
            return;
        }

        if (!mouseInputHandler.PrimaryPressed)
            return;

        if (pointerBlocked)
            return;

        bool constructed = buildingPlacementController.TryConfirm();

        if (!constructed)
            return;

        // One successful placement exits placement mode.
        SetInteractionMode(PlayerInteractionMode.Default);
    }

    // ---------------------------------------------------------------------
    // Interaction-mode lifecycle
    // ---------------------------------------------------------------------

    private void SetInteractionMode(PlayerInteractionMode newMode)
    {
        if (currentInteractionMode == newMode)
            return;

        ExitInteractionMode(currentInteractionMode);
        currentInteractionMode = newMode;
        EnterInteractionMode(currentInteractionMode);
    }

    public void CancelCurrentInteraction()
    {
        SetInteractionMode(PlayerInteractionMode.Default);
    }

    private void EnterInteractionMode(PlayerInteractionMode mode)
    {
        switch (mode)
        {
            case PlayerInteractionMode.Default:
                EnterDefaultInteraction();
                break;

            case PlayerInteractionMode.MoveTargeting:
                EnterMoveTargeting();
                break;

            case PlayerInteractionMode.BuildPlacement:
                EnterBuildingPlacement();
                break;
        }
    }

    private void ExitInteractionMode(PlayerInteractionMode mode)
    {
        switch (mode)
        {
            case PlayerInteractionMode.Default:
                ExitDefaultInteraction();
                break;

            case PlayerInteractionMode.MoveTargeting:
                ExitMoveTargeting();
                break;

            case PlayerInteractionMode.BuildPlacement:
                ExitBuildingPlacement();
                break;
        }
    }

    // ---------------------------------------------------------------------
    // Specific interaction lifecycle
    // ---------------------------------------------------------------------

    // Default
    private void EnterDefaultInteraction() { }

    private void ExitDefaultInteraction()
    {
        CancelSelectionGesture();
    }
    
    // Move
    private void EnterMoveTargeting()
    {
        CancelSelectionGesture();
        SpawnMoveTargetMarker();
    }

    private void ExitMoveTargeting()
    {
        DestroyMoveTargetMarker();
    }

    // Building placement
    private void EnterBuildingPlacement()
    {
        if (buildingPlacementController == null)
            return;

        buildingPlacementController.Begin(pendingBuildingDefinition);
        pendingBuildingDefinition = null;
    }

    private void ExitBuildingPlacement()
    {
        pendingBuildingDefinition = null;
        buildingPlacementController?.Cancel();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private bool IsWorldPointerBlocked()
    {
        bool blockedByUI = inputBindings.IgnoreWorldInputOverUI && mouseInputHandler.PointerOverUI;
        return blockedByUI;
    }

    private void ResetPlayerInputState()
    {
        cameraController?.ClearMovementInput();

        CancelSelectionGesture();

        keyInputHandler?.Reset();
        mouseInputHandler?.Reset();

        currentInteractionMode = PlayerInteractionMode.Default;
    }

    private void CancelSelectionGesture()
    {
        selectionManager?.CancelSelection();
        selectionPointerCaptured = false;
    }
}