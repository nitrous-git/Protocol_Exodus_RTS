public sealed class PlayerFactionController : IFactionController, IPlayerInputController
{
    private Faction faction;
    private GameContext gameContext;

    private SelectionManager selectionManager;
    private CommandIssuer commandIssuer;
    private CameraController cameraController;

    private PlayerInputBindings inputBindings;

    private KeyInputHandler keyInputHandler;
    private MouseInputHandler mouseInputHandler;

    private bool isPlayerControlInitialized;
    private bool selectionPointerCaptured;

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
    }

    public void Tick(){ }

    public void TickInput(float deltaTime)
    {
        if (!isPlayerControlInitialized)
        {
            cameraController?.ClearMovementInput();
            selectionManager?.CancelSelection();
            selectionPointerCaptured = false;
            return;
        }

        keyInputHandler.TickInput();
        mouseInputHandler.TickInput();

        cameraController?.SetMovementInput(keyInputHandler.CameraMovement);
        HandleSelectionInput();
        HandleCommandInput();
    }

    // Input handle methods 

    private void HandleSelectionInput()
    {
        if (mouseInputHandler.PrimaryPressed)
        {
            bool blockedByUI = inputBindings.IgnoreWorldInputOverUI && mouseInputHandler.PointerOverUI;

            selectionPointerCaptured = !blockedByUI && selectionManager != null && selectionManager.BeginSelection( mouseInputHandler.PointerPosition);
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

    private void HandleCommandInput()
    {
        if (!inputBindings.IssueMoveOnSecondaryPointer)
            return;

        if (!mouseInputHandler.SecondaryPressed)
            return;

        bool blockedByUI = inputBindings.IgnoreWorldInputOverUI && mouseInputHandler.PointerOverUI;

        if (blockedByUI)
            return;

        commandIssuer?.TryIssueMoveCommandFromScreen(mouseInputHandler.PointerPosition);
    }
}