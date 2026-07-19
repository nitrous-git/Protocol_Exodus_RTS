using UnityEngine;

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

    private void HandleCurrentInteraction()
    {
        switch (currentInteractionMode)
        {
            case PlayerInteractionMode.Default:
                HandleDefaultInteraction();
                break;

            default:
                Debug.LogWarning($"Unsupported player interaction mode: " + $"{currentInteractionMode}. Returning to Default.");
                CancelCurrentInteraction();
                break;
        }
    }

    // Input handle methods 
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

    // Interaction Mode methods
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
    
    // Specific Interaction Enter/Exit methods
    private void EnterDefaultInteraction() { }

    private void ExitDefaultInteraction()
    {
        CancelSelectionGesture();
    }


    // Helpers method 
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

        currentInteractionMode =
            PlayerInteractionMode.Default;
    }

    private void CancelSelectionGesture()
    {
        selectionManager?.CancelSelection();
        selectionPointerCaptured = false;
    }
}