using UnityEngine;

public class SelectionBox : MonoBehaviour
{
    [SerializeField] private RectTransform selectionRectTransform;
    [SerializeField] private Canvas canvas;

    private RectTransform canvasRectTransform;

    private void Awake()
    {
        if (selectionRectTransform == null)
            selectionRectTransform = GetComponent<RectTransform>();

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>();

        if (canvas != null)
            canvasRectTransform = canvas.transform as RectTransform;

        Hide();
    }

    public void Show()
    {
        if (selectionRectTransform != null)
            selectionRectTransform.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (selectionRectTransform != null)
            selectionRectTransform.gameObject.SetActive(false);
    }

    public void UpdateVisual(Vector2 screenStart, Vector2 screenEnd)
    {
        if (selectionRectTransform == null)
            return;

        if (canvasRectTransform != null)
        {
            Vector2 localStart;
            Vector2 localEnd;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRectTransform,
                screenStart,
                canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                out localStart
            );

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRectTransform,
                screenEnd,
                canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                out localEnd
            );

            Vector2 center = (localStart + localEnd) * 0.5f;
            Vector2 size = new Vector2(
                Mathf.Abs(localStart.x - localEnd.x),
                Mathf.Abs(localStart.y - localEnd.y)
            );

            selectionRectTransform.anchoredPosition = center;
            selectionRectTransform.sizeDelta = size;
            return;
        }

        Vector2 directCenter = (screenStart + screenEnd) * 0.5f;
        Vector2 directSize = new Vector2(
            Mathf.Abs(screenStart.x - screenEnd.x),
            Mathf.Abs(screenStart.y - screenEnd.y)
        );

        selectionRectTransform.anchoredPosition = directCenter;
        selectionRectTransform.sizeDelta = directSize;
    }
}
