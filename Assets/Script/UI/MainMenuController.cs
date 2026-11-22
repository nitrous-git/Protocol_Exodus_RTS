using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MainMenuController : MonoBehaviour
{
    [SerializeField] private MatchLaunchDefinition defaultMatch;
    [SerializeField] private SceneTransitionController transitionPrefab;

    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private UnityEngine.UI.Button optionsButton;
    [SerializeField] private UnityEngine.UI.Slider masterVolumeSlider;
    [SerializeField] private UnityEngine.UI.Toggle fullscreenToggle;
    [SerializeField] private TMP_Text volumeValue;
    [SerializeField] private TMP_Text frameRateValue;
    [SerializeField] private TMP_Text statusText;

    private static readonly int[] FrameRates = { 30, 60, 120, 144, 240, -1 };

    public void StartMatch()
    {
        statusText.text = string.Empty;
        if (defaultMatch == null)
        {
            ShowError("No map is configured.");
            return;
        }

        SceneTransitionController.LoadScene(transitionPrefab, defaultMatch.ScenePath, defaultMatch.DisplayName, ShowError);
    }

    public void OpenOptions()
    {
        statusText.text = string.Empty;
        masterVolumeSlider.SetValueWithoutNotify(PlayerOptions.MasterVolume);
        UpdateVolumeLabel(PlayerOptions.MasterVolume);
        fullscreenToggle.SetIsOnWithoutNotify(PlayerOptions.Fullscreen);
        UpdateFrameRateLabel(PlayerOptions.GetFrameRate());

        mainPanel.SetActive(false);
        optionsPanel.SetActive(true);
        masterVolumeSlider.Select();
    }

    public void CloseOptions()
    {
        PlayerPrefs.Save();
        optionsPanel.SetActive(false);
        mainPanel.SetActive(true);
        optionsButton.Select();
    }

    public void SetMasterVolume(float volume)
    {
        PlayerOptions.SetMasterVolume(volume);
        UpdateVolumeLabel(volume);
    }

    public void SetFullscreen(bool fullscreen) => PlayerOptions.SetFullscreen(fullscreen);

    public void CycleFrameRate()
    {
        int currentIndex = Array.IndexOf(FrameRates, PlayerOptions.GetFrameRate());
        int frameRate = FrameRates[(currentIndex + 1) % FrameRates.Length];
        PlayerOptions.SetFrameRate(frameRate);
        UpdateFrameRateLabel(frameRate);
    }

    public void Exit()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnDisable()
    {
        if (optionsPanel != null && optionsPanel.activeSelf)
            PlayerPrefs.Save();
    }

    private void ShowError(string message) => statusText.text = message;

    private void UpdateVolumeLabel(float volume) => volumeValue.text = $"{volume:P0}";

    private void UpdateFrameRateLabel(int frameRate) =>
        frameRateValue.text = frameRate == -1 ? "Uncapped  >" : $"{frameRate} FPS  >";
}
