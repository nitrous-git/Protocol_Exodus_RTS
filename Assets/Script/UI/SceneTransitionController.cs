using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SceneTransitionController : MonoBehaviour
{
    [SerializeField] private UnityEngine.UI.Image blackOverlay;
    [SerializeField] private CanvasGroup blackAlpha;
    [SerializeField] private CanvasGroup titleAlpha;
    [SerializeField] private TMP_Text levelTitle;

    [Header("Durations (seconds)")]
    [SerializeField, Min(0)] private float fadeToBlackDuration = 0.35f;
    [SerializeField, Min(0)] private float revealDuration = 0.75f;
    [SerializeField, Min(0)] private float titleHoldDuration = 1.6f;
    [SerializeField, Min(0)] private float titleFadeDuration = 0.65f;

    private static SceneTransitionController instance;

    private bool isLoading;
    private int revealCompletedFrame = -1;
    private EventSystem suspendedEventSystem;
    private Coroutine titleRoutine;

    public static bool BlocksGameplay => instance != null &&
        (instance.isLoading || instance.revealCompletedFrame == Time.frameCount);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => instance = null;

    public static void LoadScene(SceneTransitionController prefab, string scenePath, string displayName,
        Action<string> showError)
    {
        if (instance != null && instance.isLoading)
            return;

        int buildIndex = string.IsNullOrWhiteSpace(scenePath) ? -1 : SceneUtility.GetBuildIndexByScenePath(scenePath);
        if (buildIndex < 0 || !Application.CanStreamedLevelBeLoaded(buildIndex))
        {
            Debug.LogWarning($"Scene transition could not find an enabled build scene for '{scenePath}'.");
            showError?.Invoke("This scene is unavailable.");
            return;
        }

        if (instance == null)
        {
            if (prefab == null || !prefab.HasView || !prefab.enabled || !prefab.gameObject.activeSelf)
            {
                Debug.LogWarning("Scene transition prefab or its UI references are missing.");
                showError?.Invoke("Scene loading is unavailable.");
                return;
            }

            Instantiate(prefab);
        }

        instance.BeginLoad(buildIndex, displayName, showError);
    }

    private bool HasView => blackOverlay != null && blackAlpha != null && titleAlpha != null && levelTitle != null;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance != this)
            return;

        SceneManager.sceneLoaded -= SuspendSceneUI;
        RestoreSceneUI();
        instance = null;
    }

    private void BeginLoad(int buildIndex, string displayName, Action<string> showError)
    {
        isLoading = true;
        if (titleRoutine != null)
        {
            StopCoroutine(titleRoutine);
            titleRoutine = null;
        }

        titleAlpha.alpha = 0f;
        blackOverlay.gameObject.SetActive(true);
        SuspendSceneUI();
        SceneManager.sceneLoaded += SuspendSceneUI;
        StartCoroutine(LoadAndReveal(buildIndex, displayName, showError));
    }

    private IEnumerator LoadAndReveal(int buildIndex, string displayName, Action<string> showError)
    {
        yield return Fade(blackAlpha, 1f, fadeToBlackDuration);
        // Draw one opaque frame before loading or activating the next scene.
        yield return null;

        AsyncOperation operation = null;
        try
        {
            operation = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Scene transition failed for build index {buildIndex}: {exception.Message}", this);
        }

        if (operation != null)
            yield return operation;

        Scene activeScene = SceneManager.GetActiveScene();
        bool loaded = operation != null && activeScene.isLoaded && activeScene.buildIndex == buildIndex;
        bool showTitle = loaded && !string.IsNullOrWhiteSpace(displayName);
        levelTitle.text = showTitle ? displayName : string.Empty;
        // Let the destination render under black before starting the reveal.
        yield return null;
        yield return Fade(blackAlpha, 0f, revealDuration, showTitle ? titleAlpha : null);

        blackOverlay.gameObject.SetActive(false);
        SceneManager.sceneLoaded -= SuspendSceneUI;
        RestoreSceneUI();
        revealCompletedFrame = Time.frameCount;
        isLoading = false;

        if (showTitle)
            titleRoutine = StartCoroutine(DismissTitle());

        if (!loaded)
        {
            Debug.LogWarning($"Scene transition did not reach build index {buildIndex}.", this);
            showError?.Invoke("The scene could not be loaded. Try again.");
        }
    }

    private void SuspendSceneUI(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            SuspendSceneUI();
    }

    private void SuspendSceneUI()
    {
        EventSystem current = EventSystem.current;
        if (current == null)
            return;

        suspendedEventSystem = current;
        current.enabled = false;
    }

    private void RestoreSceneUI()
    {
        if (suspendedEventSystem != null)
            suspendedEventSystem.enabled = true;

        suspendedEventSystem = null;
    }

    private IEnumerator DismissTitle()
    {
        yield return new WaitForSecondsRealtime(titleHoldDuration);
        yield return Fade(titleAlpha, 0f, titleFadeDuration);
        levelTitle.text = string.Empty;
        titleRoutine = null;
    }

    private static IEnumerator Fade(CanvasGroup group, float target, float duration, CanvasGroup enteringTitle = null)
    {
        float start = group.alpha;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            group.alpha = Mathf.Lerp(start, target, progress);
            if (enteringTitle != null)
                enteringTitle.alpha = progress;

            yield return null;
        }

        group.alpha = target;
        if (enteringTitle != null)
            enteringTitle.alpha = 1f;
    }
}
