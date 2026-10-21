using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 音频面板的折叠开关：点击标题栏按钮在"展开"与"收起"之间平滑过渡，
/// 内容整体淡出并禁用射线，箭头翻转 180°。全部使用不缩放时间。
/// </summary>
public sealed class UiPanelFold : MonoBehaviour
{
    [SerializeField] private Button foldButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private RectTransform content;
    [SerializeField] private CanvasGroup contentGroup;
    [SerializeField] private RectTransform chevron;
    [SerializeField] private Vector2 expandedSize = new Vector2(316f, 318f);
    [SerializeField] private Vector2 collapsedSize = new Vector2(316f, 58f);
    [SerializeField] private float duration = 0.18f;

    private bool expanded = true;
    private float progress = 1f;
    private float fromHeight;
    private float toHeight;

    /// <summary>当前是否展开。</summary>
    public bool IsExpanded { get { return expanded; } }

    private void Awake()
    {
        if (panel == null) panel = transform as RectTransform;
        if (foldButton != null) foldButton.onClick.AddListener(Toggle);
        toHeight = expanded ? expandedSize.y : collapsedSize.y;
        progress = 1f;
        Apply(1f);
    }

    private void OnDestroy()
    {
        if (foldButton != null) foldButton.onClick.RemoveListener(Toggle);
    }

    /// <summary>切换展开状态。</summary>
    public void Toggle()
    {
        expanded = !expanded;
        fromHeight = panel != null ? panel.sizeDelta.y : 0f;
        toHeight = expanded ? expandedSize.y : collapsedSize.y;
        progress = 0f;
        if (expanded && content != null) content.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (progress >= 1f) return;
        progress = Mathf.Min(1f, progress + Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
        Apply(Mathf.SmoothStep(0f, 1f, progress));
    }

    private void Apply(float t)
    {
        if (panel != null)
        {
            float height = progress >= 1f ? toHeight : Mathf.Lerp(fromHeight, toHeight, t);
            panel.sizeDelta = new Vector2(expandedSize.x, height);
        }
        if (contentGroup != null)
        {
            contentGroup.alpha = expanded ? t : 1f - t;
            contentGroup.blocksRaycasts = expanded && t > 0.6f;
        }
        if (chevron != null)
        {
            float angle = expanded ? 180f : 0f;
            chevron.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpAngle(expanded ? 0f : 180f, angle, t));
        }
        if (!expanded && progress >= 1f && content != null) content.gameObject.SetActive(false);
    }
}
