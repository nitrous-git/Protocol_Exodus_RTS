using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Visual representation of one reusable Command Panel slot.
/// Gameplay meaning is assigned later by CommandPanelController.
/// </summary>
public sealed class CommandSlotView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text labelText;

    public Button Button => button;

    public void SetVisual(string label, Sprite icon, bool interactable)
    {
        if (labelText != null)
        {
            bool hasLabel = !string.IsNullOrWhiteSpace(label);

            labelText.text = hasLabel ? label : string.Empty;
            labelText.gameObject.SetActive(hasLabel);
        }

        if (iconImage != null)
        {
            bool hasIcon = icon != null;

            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(hasIcon);
        }

        if (button != null)
            button.interactable = interactable;
    }

    public void ClearVisual()
    {
        SetVisual(string.Empty, null, false);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        button = GetComponent<Button>();
        labelText = GetComponentInChildren<TMP_Text>(true);
    }
#endif
}