using UnityEngine;

public sealed class BuildingView : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private GameObject selectionIndicator;

    private BuildingBase owner;

    public void Initialize(BuildingBase owner)
    {
        this.owner = owner;

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectionIndicator != null)
            selectionIndicator.SetActive(selected);
    }
}