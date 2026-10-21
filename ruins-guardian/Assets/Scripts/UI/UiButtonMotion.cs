using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 按钮交互反馈：悬停放大增亮、按下回缩、离开复位。
/// 全部使用不缩放时间（unscaledDeltaTime），暂停菜单里也有反馈。
/// </summary>
public sealed class UiButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private float hoverScale = 1.045f;
    [SerializeField] private float pressScale = 0.965f;
    [SerializeField] private float speed = 16f;
    [SerializeField] private RectTransform target;
    [SerializeField] private Graphic glow;

    private float currentScale = 1f;
    private float wantedScale = 1f;
    private float glowCurrent;
    private float glowWanted;
    private Color glowColor = Color.white;
    private bool hovering;
    private bool pressing;

    /// <summary>生成器配置缩放倍率。</summary>
    public void Configure(float hover, float press)
    {
        hoverScale = hover;
        pressScale = press;
    }

    private void Awake()
    {
        if (target == null) target = transform as RectTransform;
        if (glow != null) glowColor = glow.color;
        if (target != null) currentScale = target.localScale.x;
        wantedScale = currentScale;
    }

    private void OnDisable()
    {
        hovering = false;
        pressing = false;
        wantedScale = 1f;
        glowWanted = 0f;
        if (target != null) target.localScale = Vector3.one;
        if (glow != null) glow.color = UiFactory.Alpha(glowColor, 0f);
        glowCurrent = 0f;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        wantedScale = hoverScale;
        glowWanted = 1f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        pressing = false;
        wantedScale = 1f;
        glowWanted = 0f;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressing = true;
        wantedScale = pressScale;
        glowWanted = 1.6f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressing = false;
        wantedScale = hovering ? hoverScale : 1f;
        glowWanted = hovering ? 1f : 0f;
    }

    private void Update()
    {
        float delta = Time.unscaledDeltaTime * speed;
        currentScale = Mathf.Lerp(currentScale, wantedScale, delta);
        if (target != null) target.localScale = new Vector3(currentScale, currentScale, 1f);

        glowCurrent = Mathf.Lerp(glowCurrent, glowWanted, delta);
        if (glow != null) glow.color = UiFactory.Alpha(glowColor, Mathf.Clamp01(glowColor.a * 0.35f + glowCurrent * 0.45f));
    }
}
