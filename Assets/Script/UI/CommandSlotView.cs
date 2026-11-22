using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Visual representation of one reusable Command Panel slot.
/// Gameplay meaning is assigned later by CommandPanelController.
/// </summary>
public sealed class CommandSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("References")]
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text labelText;

    private UnityAction currentClickAction;
    private CommandPanelController tooltipOwner;
    private CommandTooltipContent tooltipContent;

    public Button Button => button;

    public void SetVisual(string label, Sprite icon, bool interactable)
    {
        if (labelText != null)
        {
            bool hasLabel = icon == null && !string.IsNullOrWhiteSpace(label);

            labelText.text = hasLabel ? label : string.Empty;
            labelText.gameObject.SetActive(hasLabel);
        }

        if (iconImage != null)
        {
            bool hasIcon = icon != null;

            iconImage.sprite = icon;
            iconImage.color = interactable ? Color.white : new Color(0.61f, 0.66f, 0.70f, 0.76f);
            iconImage.gameObject.SetActive(hasIcon);
        }

        if (button != null)
            button.interactable = interactable;
    }

    public void SetClickAction(UnityAction clickAction)
    {
        if (button == null)
            return;

        if (currentClickAction != null)
            button.onClick.RemoveListener(currentClickAction);

        currentClickAction = clickAction;

        if (currentClickAction != null)
            button.onClick.AddListener(currentClickAction);
    }

    public void ClearVisual()
    {
        HideTooltip();
        tooltipContent = default;
        SetClickAction(null);
        SetVisual(string.Empty, null, false);
    }

    internal void SetTooltip(CommandPanelController owner, CommandTooltipContent content)
    {
        tooltipOwner = owner;
        tooltipContent = content;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipOwner != null && !string.IsNullOrEmpty(tooltipContent.Title))
            tooltipOwner.ShowTooltip(this, tooltipContent);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        HideTooltip();
    }

    private void OnDisable()
    {
        HideTooltip();
    }

    private void HideTooltip()
    {
        if (tooltipOwner != null)
            tooltipOwner.HideTooltip(this);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        button = GetComponent<Button>();
        labelText = GetComponentInChildren<TMP_Text>(true);
    }
#endif
}
