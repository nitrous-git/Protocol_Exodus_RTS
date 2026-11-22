using UnityEngine;

public class FrameRateLimiter : MonoBehaviour
{
    [SerializeField] private int targetFPS = PlayerOptions.DefaultFrameRate;

    private void Awake()
    {
        PlayerOptions.ApplyFrameRate(targetFPS);
    }
}
