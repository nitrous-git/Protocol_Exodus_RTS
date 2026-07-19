public sealed class PlayerFactionController : IFactionController, IPlayerInputController
{
    private Faction faction;
    private GameContext gameContext;

    private SelectionManager selectionManager;
    private CommandIssuer commandIssuer;
    private CameraController cameraController;

    private bool isPlayerControlInitialized;

    public void Initialize(Faction faction, GameContext gameContext)
    {
        this.faction = faction;
        this.gameContext = gameContext;
    }

    public void InitializePlayerControl(
    SelectionManager selectionManager,
    CommandIssuer commandIssuer,
    CameraController cameraController)
    {
        this.selectionManager = selectionManager;
        this.commandIssuer = commandIssuer;
        this.cameraController = cameraController;

        isPlayerControlInitialized = true;
    }


    public void Tick(){ }

    public void TickInput(float deltaTime)
    {
        if (!isPlayerControlInitialized)
            return;

        selectionManager?.TickInput(deltaTime);
        commandIssuer?.TickInput(deltaTime);
        cameraController?.TickInput(deltaTime);
    }
}