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
    CameraController cameraController)
    {
        this.inputBindings = inputBindings;
        this.selectionManager = selectionManager;
        this.commandIssuer = commandIssuer;
        this.cameraController = cameraController;

        keyInputHandler = new KeyInputHandler(inputBindings);
        mouseInputHandler = new MouseInputHandler(inputBindings);

        isPlayerControlInitialized = true;
        selectionPointerCaptured = false;

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

    private void BeginBuildingPlacement(BuildingDefinition buildingDefinition)
    {
        if (buildingDefinition == null)
            return;

        //pendingBuildingDefinition = buildingDefinition;
        //SetInteractionMode(PlayerInteractionMode.BuildingPlacement);
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

            default:
                Debug.LogWarning($"Unsupported player interaction mode: ${currentInteractionMode}. Returning to Default.");
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
    // Interaction-mode lifecycle
    // ---------------------------------------------------------------------

    public void SetInteractionMode(PlayerInteractionMode newMode)
    {
        if (currentInteractionMode == newMode)
            return;

        ExitInteractionMode(currentInteractionMode);
        currentInteractionMode = newMode;
        EnterInteractionMode(currentInteractionMode);
    }

    public void CancelCurrentInteraction()
    {
        ExitInteractionMode(currentInteractionMode);
        currentInteractionMode = PlayerInteractionMode.Default;
        EnterInteractionMode(currentInteractionMode);
    }

    private void EnterInteractionMode(PlayerInteractionMode mode)
    {
        switch (mode)
        {
            case PlayerInteractionMode.Default:
                EnterDefaultInteraction();
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
        }
    }

    // ---------------------------------------------------------------------
    // Specific interaction lifecycle
    // ---------------------------------------------------------------------

    private void EnterDefaultInteraction() { }

    private void ExitDefaultInteraction()
    {
        CancelSelectionGesture();
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