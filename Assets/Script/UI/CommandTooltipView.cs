using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class CommandTooltipView : MonoBehaviour
{
    private const float ScreenPadding = 8f;
    private const float SlotGap = 8f;

    [SerializeField] private RectTransform panel;
    [SerializeField] private RectTransform body;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text description;
    [SerializeField] private RectTransform costRow;
    [SerializeField] private TMP_Text mineralsCost;
    [SerializeField] private TMP_Text gasCost;
    [SerializeField] private TMP_Text supplyCost;

    private CommandSlotView owner;

    internal bool IsShowing => owner != null && panel.gameObject.activeInHierarchy;

    internal void Show(CommandSlotView slot, CommandTooltipContent content)
    {
        owner = slot;
        title.text = content.Title;
        description.text = content.Description;

        Cost cost = content.Cost;
        SetCost(mineralsCost, cost.Minerals, "Minerals");
        SetCost(gasCost, cost.Gas, "Gas");
        SetCost(supplyCost, cost.Supply, "Supply");
        costRow.gameObject.SetActive(cost.Minerals != 0 || cost.Gas != 0 || cost.Supply != 0);

        panel.gameObject.SetActive(true);
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(body);
        float bodyHeight = UnityEngine.UI.LayoutUtility.GetPreferredHeight(body);
        float borderHeight = body.offsetMin.y - body.offsetMax.y;
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bodyHeight + borderHeight);
        RefreshPosition();
    }

    internal bool Hide(CommandSlotView slot = null)
    {
        if (slot != null && slot != owner)
            return false;

        owner = null;
        if (panel != null)
            panel.gameObject.SetActive(false);

        return true;
    }

    internal void RefreshPosition()
    {
        RectTransform viewport = (RectTransform)transform;
        RectTransform target = (RectTransform)owner.transform;
        Rect bounds = viewport.rect;
        Vector2 panelSize = panel.rect.size;
        float padding = Mathf.Min(ScreenPadding, Mathf.Min(bounds.width, bounds.height) * 0.05f);
        float scale = Mathf.Min(1f, Mathf.Min(
            (bounds.width - 2 * padding) / panelSize.x,
            (bounds.height - 2 * padding) / panelSize.y));

        panel.localScale = Vector3.one * scale;
        panelSize *= scale;

        Vector3 topRight = viewport.InverseTransformPoint(target.TransformPoint(target.rect.max));
        Vector3 bottomLeft = viewport.InverseTransformPoint(target.TransformPoint(target.rect.min));
        float y = topRight.y + SlotGap;
        if (y + panelSize.y > bounds.yMax - padding && bottomLeft.y - SlotGap - panelSize.y >= bounds.yMin + padding)
            y = bottomLeft.y - SlotGap - panelSize.y;

        panel.localPosition = new Vector3(
            Mathf.Clamp(topRight.x - panelSize.x, bounds.xMin + padding, bounds.xMax - padding - panelSize.x),
            Mathf.Clamp(y, bounds.yMin + padding, bounds.yMax - padding - panelSize.y),
            0f);
    }

    private void OnDisable()
    {
        Hide();
    }

    private static void SetCost(TMP_Text text, int value, string resource)
    {
        text.transform.parent.gameObject.SetActive(value != 0);
        if (value != 0)
            text.text = $"{value} {resource}";
    }
}
