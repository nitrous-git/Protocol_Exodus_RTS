using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameLogPanel : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Vector2 padding = new(12f, 8f);

    private readonly StringBuilder text = new();
    private GameLog gameLog;
    private int renderedVersion;

    public void Initialize(GameLog gameLog)
    {
        this.gameLog = gameLog;

        if (background == null || messageText == null)
            return;

        messageText.rectTransform.offsetMin = padding;
        messageText.rectTransform.offsetMax = -padding;
        Refresh();
    }

    public void Tick()
    {
        if (gameLog != null && gameLog.Version != renderedVersion && background != null && messageText != null)
            Refresh();
    }

    private void Refresh()
    {
        renderedVersion = gameLog?.Version ?? 0;
        bool hasMessages = gameLog != null && gameLog.Count > 0;
        background.enabled = hasMessages;
        messageText.enabled = hasMessages;

        if (!hasMessages)
        {
            messageText.text = string.Empty;
            return;
        }

        text.Clear();
        for (int i = 0; i < gameLog.Count; i++)
        {
            if (i > 0)
                text.Append('\n');

            text.Append(gameLog[i]);
        }

        messageText.text = text.ToString();
        RectTransform bounds = background.rectTransform;
        float textWidth = Mathf.Max(1f, bounds.rect.width - 2f * padding.x);
        float textHeight = messageText.GetPreferredValues(messageText.text, textWidth, Mathf.Infinity).y;
        bounds.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight + 2f * padding.y);
    }
}
