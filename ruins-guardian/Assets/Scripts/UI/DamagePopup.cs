using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 世界坐标伤害数字：描边 + 投影、随机散开、上浮缓出、暴击放大变色。
/// 受击方（玩家掉血）为血红，玩家输出为白色描金，暴击为金色加大。
/// 实例由 HUDController 通过 Spawn 创建，自己销毁自己。
/// </summary>
public sealed class DamagePopup : MonoBehaviour
{
    private const float LifeSeconds = 0.85f;
    private const float RiseSpeed = 118f;
    private const float Gravity = -230f;
    private const float ScatterSpeed = 62f;
    private const float PunchDuration = 0.18f;

    private CanvasGroup group;
    private RectTransform rect;
    private Vector2 velocity;
    private float elapsed;
    private float punchFrom = 1.15f;

    /// <summary>
    /// 在浮层上生成一个伤害数字。
    /// hostile 为 true 表示挨打的是玩家（红色），false 表示玩家打出的伤害（白色 / 金色）。
    /// </summary>
    public static DamagePopup Spawn(RectTransform layer, Camera worldCamera, Vector3 worldPosition,
        string value, bool hostile, bool critical)
    {
        if (layer == null) return null;

        GameObject go = new GameObject("Damage Popup", typeof(RectTransform), typeof(CanvasGroup), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(layer, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(240f, 70f);

        Camera camera = worldCamera != null ? worldCamera : Camera.main;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, worldPosition);
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screenPoint, null, out localPoint);
        rect.anchoredPosition = localPoint;

        Text text = go.GetComponent<Text>();
        text.font = UiFactory.DefaultFont;
        text.fontSize = critical ? 46 : 30;
        text.fontStyle = critical ? FontStyle.Bold : FontStyle.Normal;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.text = value;

        Color main;
        Color stroke;
        if (critical)
        {
            main = UiFactory.Gold;
            stroke = UiFactory.Alpha(UiFactory.Hex("3A1E08"), 0.95f);
        }
        else if (hostile)
        {
            main = UiFactory.Blood;
            stroke = UiFactory.Alpha(UiFactory.Hex("200508"), 0.95f);
        }
        else
        {
            main = Color.white;
            stroke = UiFactory.Alpha(UiFactory.Hex("2A1503"), 0.9f);
        }
        text.color = main;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = stroke;
        outline.effectDistance = new Vector2(critical ? 2.5f : 2f, critical ? -2.5f : -2f);
        outline.useGraphicAlpha = true;

        Shadow shadow = go.AddComponent<Shadow>();
        shadow.effectColor = UiFactory.Alpha(Color.black, 0.55f);
        shadow.effectDistance = new Vector2(1.5f, -3f);
        shadow.useGraphicAlpha = true;

        DamagePopup popup = go.AddComponent<DamagePopup>();
        popup.rect = rect;
        popup.group = go.GetComponent<CanvasGroup>();
        popup.group.alpha = 1f;
        popup.group.blocksRaycasts = false;
        popup.group.interactable = false;
        popup.punchFrom = critical ? 1.45f : 1.15f;
        popup.velocity = new Vector2(Random.Range(-ScatterSpeed, ScatterSpeed), Random.Range(RiseSpeed * 0.55f, RiseSpeed));
        rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-7f, 7f));
        rect.localScale = Vector3.one * popup.punchFrom;
        return popup;
    }

    private void Update()
    {
        float delta = Time.unscaledDeltaTime;
        elapsed += delta;
        float t = Mathf.Clamp01(elapsed / LifeSeconds);

        velocity.y += Gravity * delta * 0.35f;
        if (rect != null) rect.anchoredPosition += velocity * delta;

        if (rect != null)
        {
            float punch = t < PunchDuration ? Mathf.Lerp(punchFrom, 1f, t / PunchDuration) : 1f;
            float shrink = t > 0.72f ? Mathf.Lerp(1f, 0.82f, (t - 0.72f) / 0.28f) : 1f;
            rect.localScale = Vector3.one * (punch * shrink);
        }

        if (group != null)
            group.alpha = t < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);

        if (elapsed >= LifeSeconds) Destroy(gameObject);
    }
}
