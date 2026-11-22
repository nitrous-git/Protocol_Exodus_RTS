using UnityEngine;

public static class PlayerOptions
{
    public const int DefaultFrameRate = 60;

    private const string MasterVolumeKey = "Options.MasterVolume";
    private const string FullscreenKey = "Options.Fullscreen";
    private const string FrameRateKey = "Options.FrameRate";

    public static float MasterVolume => Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
    public static bool Fullscreen => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedOptions()
    {
        AudioListener.volume = MasterVolume;
        if (PlayerPrefs.HasKey(FullscreenKey))
            ApplyFullscreen(Fullscreen);

        ApplyFrameRate(DefaultFrameRate);
    }

    public static void SetMasterVolume(float volume)
    {
        volume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MasterVolumeKey, volume);
        AudioListener.volume = volume;
    }

    public static void SetFullscreen(bool fullscreen)
    {
        PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
        ApplyFullscreen(fullscreen);
    }

    public static int GetFrameRate(int fallback = DefaultFrameRate)
    {
        int frameRate = PlayerPrefs.GetInt(FrameRateKey, fallback);
        return frameRate > 0 || frameRate == -1 ? frameRate : fallback;
    }

    public static void SetFrameRate(int frameRate)
    {
        PlayerPrefs.SetInt(FrameRateKey, frameRate);
        ApplyFrameRate(DefaultFrameRate);
    }

    public static void ApplyFrameRate(int fallback)
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = GetFrameRate(fallback);
    }

    private static void ApplyFullscreen(bool fullscreen)
    {
        Screen.fullScreenMode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }
}
