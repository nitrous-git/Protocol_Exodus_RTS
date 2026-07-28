using UnityEngine;

/// <summary>
/// Temporary world-space visual used while choosing a target destination.
/// </summary>
public sealed class TargetMarkerView : MonoBehaviour
{
    public void ShowAt(Vector3 worldPosition)
    {
        transform.position = worldPosition;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}