using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class TopBarPanelController : MonoBehaviour
{
    [Header("Counters")]
    [SerializeField] private TMP_Text mineralsText;
    [SerializeField] private TMP_Text gasText;
    [SerializeField] private TMP_Text populationText;
    [SerializeField] private Color populationColor = new(0.91f, 0.94f, 0.97f);
    [SerializeField] private Color populationWarningColor = new(1f, 0.74f, 0.47f);

    [Header("Pause Menu")]
    [SerializeField] private UnityEngine.UI.Button menuButton;
    [SerializeField] private GameObject pauseMenu;
    [SerializeField] private UnityEngine.UI.Button resumeButton;
    [SerializeField] private UnityEngine.UI.Button quitButton;

    private GameLoop gameLoop;
    private ResourceManager resources;
    private UnitManager units;
    private int? previousMinerals;
    private int? previousGas;
    private int? previousPopulation;
    private int? previousSupply;

    public bool CanOpenPauseMenu => isActiveAndEnabled && pauseMenu != null;

    private void Awake()
    {
        if (menuButton != null)
            menuButton.onClick.AddListener(OpenPauseMenu);

        if (resumeButton != null)
            resumeButton.onClick.AddListener(ResumeGame);

        if (quitButton != null)
        {
#if UNITY_EDITOR
            quitButton.interactable = false;
#else
            quitButton.onClick.AddListener(QuitGame);
#endif
        }
    }

    private void OnDestroy()
    {
        if (menuButton != null)
            menuButton.onClick.RemoveListener(OpenPauseMenu);

        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(ResumeGame);

#if !UNITY_EDITOR
        if (quitButton != null)
            quitButton.onClick.RemoveListener(QuitGame);
#endif
    }

    public void Initialize(Faction playerFaction, GameLoop gameLoop)
    {
        this.gameLoop = gameLoop;
        resources = playerFaction?.ResourceManager;
        units = playerFaction?.UnitManager;
        previousMinerals = previousGas = previousPopulation = previousSupply = null;

        SetPauseMenuVisible(false);
        RefreshCounters();
    }

    public void Tick()
    {
        RefreshCounters();
    }

    private void RefreshCounters()
    {
        if (resources == null || units == null)
            return;

        int minerals = resources.Minerals;
        int gas = resources.Gas;
        int occupied = units.OccupiedPopulation;
        int supply = resources.MaxSupply;

        SetCounter(mineralsText, minerals, ref previousMinerals);
        SetCounter(gasText, gas, ref previousGas);

        if (populationText != null && (occupied != previousPopulation || supply != previousSupply))
        {
            populationText.text = $"{occupied} / {supply}";
            populationText.color = occupied >= supply ? populationWarningColor : populationColor;
            previousPopulation = occupied;
            previousSupply = supply;
        }
    }

    private static void SetCounter(TMP_Text text, int value, ref int? previousValue)
    {
        if (text == null || value == previousValue)
            return;

        text.text = value.ToString();
        previousValue = value;
    }

    public void SetPauseMenuVisible(bool visible)
    {
        if (pauseMenu == null || pauseMenu.activeSelf == visible)
            return;

        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        if (!visible && selected != null && selected.transform.IsChildOf(pauseMenu.transform))
            eventSystem.SetSelectedGameObject(null);

        pauseMenu.SetActive(visible);
        if (visible && resumeButton != null)
            resumeButton.Select();
    }

    private void OpenPauseMenu()
    {
        if (CanOpenPauseMenu)
            gameLoop?.SetPaused(true);
    }

    private void ResumeGame()
    {
        gameLoop?.SetPaused(false);
    }

#if !UNITY_EDITOR
    private static void QuitGame()
    {
        Application.Quit();
    }
#endif
}
