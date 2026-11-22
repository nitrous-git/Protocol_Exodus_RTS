using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MatchResultPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private UnityEngine.UI.Button rematchButton;

    private TopBarPanelController sceneNavigation;

    public void Initialize(TopBarPanelController topBarPanel)
    {
        sceneNavigation = topBarPanel;
        gameObject.SetActive(false);
    }

    public void Show(MatchOutcome outcome)
    {
        (titleText.text, descriptionText.text) = outcome switch
        {
            MatchOutcome.Victory => ("VICTORY", "Enemy faction eliminated."),
            MatchOutcome.Defeat => ("DEFEAT", "Your faction has been eliminated."),
            _ => ("DRAW", "All factions have been eliminated.")
        };

        ShowError(string.Empty);
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        rematchButton.Select();
    }

    public void Rematch()
    {
        if (CanNavigate())
            sceneNavigation.ReloadMatch(ShowError);
    }

    public void MainMenu()
    {
        if (CanNavigate())
            sceneNavigation.LoadMainMenu(ShowError);
    }

    private bool CanNavigate()
    {
        if (!isActiveAndEnabled)
            return false;

        if (sceneNavigation == null)
        {
            ShowError("Scene loading is unavailable.");
            return false;
        }

        ShowError(string.Empty);
        return true;
    }

    private void ShowError(string message)
    {
        errorText.text = message;
    }
}
