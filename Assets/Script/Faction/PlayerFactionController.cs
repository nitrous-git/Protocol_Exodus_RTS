public sealed class PlayerFactionController : IFactionController, IPlayerInputController
{
    private Faction faction;
    private GameContext gameContext;

    private SelectionManager selectionManager;
    private CommandIssuer commandIssuer;
    private CameraController cameraController;

    private KeyInputHandler keyInputHandler;

    private bool isPlayerControlInitialized;

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
        this.selectionManager = selectionManager;
        this.commandIssuer = commandIssuer;
        this.cameraController = cameraController;

        keyInputHandler = new KeyInputHandler(inputBindings);

        isPlayerControlInitialized = true;
    }


    public void Tick(){ }

    public void TickInput(float deltaTime)
    {
        if (!isPlayerControlInitialized)
        {
            cameraController?.ClearMovementInput();
            return;
        }

        // Read centralized keyboard state once for this input frame.
        keyInputHandler.TickInput();

        // Route movement intent to the camera.
        cameraController?.SetMovementInput(keyInputHandler.CameraMovement);

        // Mouse input remains inside these systems temporarily.
        selectionManager?.TickInput(deltaTime);
        commandIssuer?.TickInput(deltaTime);
    }
}