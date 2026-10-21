using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 开始菜单 / 暂停菜单 / 胜负结算面板（视觉 V2）。
/// 对外签名保持不变：ShowStartMenu / ShowPauseMenu / ShowWinMenu / ShowLoseMenu / HideAllPanels。
/// 内部过渡：遮罩淡入 + 卡片从 0.92 倍弹入（Back 缓动）+ 一道斜向扫光掠过卡片。
/// 全部使用不缩放时间，暂停（timeScale = 0）时依然正常。
/// </summary>
public sealed class GameMenuController : MonoBehaviour
{
    [Header("面板")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject endPanel;
    [SerializeField] private Text endTitleText;
    [SerializeField] private Text endStatsText;

    [Header("按钮")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button pauseRestartButton;
    [SerializeField] private Button endRestartButton;

    [Header("V2 过渡")]
    [SerializeField] private RectTransform startCard;
    [SerializeField] private RectTransform pauseCard;
    [SerializeField] private RectTransform endCard;
    [SerializeField] private RectTransform startShine;
    [SerializeField] private RectTransform pauseShine;
    [SerializeField] private RectTransform endShine;
    [SerializeField] private Text endTimeValue;
    [SerializeField] private Text endKillValue;
    [SerializeField] private Text endGemValue;
    [SerializeField] private float fadeDuration = 0.22f;
    [SerializeField] private float cardDuration = 0.32f;
    [SerializeField] private float sweepDuration = 0.62f;

    private Coroutine startRoutine;
    private Coroutine pauseRoutine;
    private Coroutine endRoutine;

    private void Awake()
    {
        EnsureGroup(startPanel);
        EnsureGroup(pausePanel);
        EnsureGroup(endPanel);

        if (startButton != null) startButton.onClick.AddListener(() => GameManager.Instance.StartGame());
        if (resumeButton != null) resumeButton.onClick.AddListener(() => GameManager.Instance.ResumeGame());
        if (pauseRestartButton != null) pauseRestartButton.onClick.AddListener(() => GameManager.Instance.RestartGame());
        if (endRestartButton != null) endRestartButton.onClick.AddListener(() => GameManager.Instance.RestartGame());
    }

    public void ShowStartMenu()
    {
        ShowPanel(startPanel, startCard, startShine, 0);
    }

    public void ShowPauseMenu()
    {
        ShowPanel(pausePanel, pauseCard, pauseShine, 1);
    }

    public void ShowWinMenu(float time, int killedGuards)
    {
        int gems = GameManager.Instance != null ? GameManager.Instance.CollectedGems : 0;
        int total = GameManager.Instance != null ? GameManager.Instance.TotalGems : 0;
        if (endTitleText != null)
        {
            endTitleText.text = "遗迹试炼通过";
            endTitleText.color = UiFactory.Gold;
        }
        FillEndStats(FormatTime(time), killedGuards + " 名", gems + " / " + total,
            "符文石门已经开启，你带着遗迹的宝藏全身而退。");
        ShowPanel(endPanel, endCard, endShine, 2);
    }

    public void ShowLoseMenu(float time, int gems, int totalGems)
    {
        if (endTitleText != null)
        {
            endTitleText.text = "你倒在了遗迹中";
            endTitleText.color = UiFactory.Blood;
        }
        int kills = GameManager.Instance != null ? GameManager.Instance.KilledGuards : 0;
        FillEndStats(FormatTime(time), kills + " 名", gems + " / " + totalGems,
            "复活符文已经耗尽，遗迹守卫仍在巡视走廊。");
        ShowPanel(endPanel, endCard, endShine, 2);
    }

    public void HideAllPanels()
    {
        HidePanel(startPanel, startCard, 0);
        HidePanel(pausePanel, pauseCard, 1);
        HidePanel(endPanel, endCard, 2);
    }

    private void FillEndStats(string timeText, string killText, string gemText, string tail)
    {
        bool hasColumns = endTimeValue != null || endKillValue != null || endGemValue != null;
        if (endTimeValue != null) endTimeValue.text = timeText;
        if (endKillValue != null) endKillValue.text = killText;
        if (endGemValue != null) endGemValue.text = gemText;
        if (endStatsText == null) return;
        // 面板没有数据列时把三项统计回落进正文，避免信息丢失
        endStatsText.text = hasColumns ? tail : "用时：" + timeText + "\n击杀：" + killText + "\n宝石：" + gemText + "\n\n" + tail;
    }

    // ==================== 过渡动画 ====================

    private void ShowPanel(GameObject panel, RectTransform card, RectTransform shine, int slot)
    {
        if (panel == null) return;
        // 先让其它面板淡出
        if (slot != 0) HidePanel(startPanel, startCard, 0);
        if (slot != 1) HidePanel(pausePanel, pauseCard, 1);
        if (slot != 2) HidePanel(endPanel, endCard, 2);

        panel.SetActive(true);
        SetAlpha(panel, 0f);
        Coroutine routine = StartCoroutine(ShowRoutine(panel, card, shine));
        if (slot == 0) startRoutine = routine;
        else if (slot == 1) pauseRoutine = routine;
        else endRoutine = routine;
    }

    private void HidePanel(GameObject panel, RectTransform card, int slot)
    {
        if (panel == null || !panel.activeSelf) return;
        Coroutine routine = StartCoroutine(HideRoutine(panel));
        if (slot == 0) startRoutine = routine;
        else if (slot == 1) pauseRoutine = routine;
        else endRoutine = routine;
    }

    private IEnumerator ShowRoutine(GameObject panel, RectTransform card, RectTransform shine)
    {
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        float duration = Mathf.Max(fadeDuration, cardDuration);
        float elapsed = 0f;
        Vector2 cardHome = card != null ? card.anchoredPosition : Vector2.zero;
        Vector2 shineHome = shine != null ? shine.anchoredPosition : Vector2.zero;
        float cardWidth = card != null ? card.sizeDelta.x : 640f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (group != null) group.alpha = Mathf.Clamp01(elapsed / fadeDuration);
            if (card != null)
            {
                float ct = Mathf.Clamp01(elapsed / cardDuration);
                float scale = Mathf.Lerp(0.92f, 1f, EaseOutBack(ct));
                card.localScale = new Vector3(scale, scale, 1f);
                card.anchoredPosition = cardHome + new Vector2(0f, Mathf.Lerp(-18f, 0f, ct));
            }
            if (shine != null)
            {
                float st = Mathf.Clamp01(elapsed / sweepDuration);
                shine.anchoredPosition = shineHome + new Vector2(Mathf.Lerp(-cardWidth * 0.85f, cardWidth * 0.85f, st), 0f);
            }
            yield return null;
        }

        if (group != null) group.alpha = 1f;
        if (card != null)
        {
            card.localScale = Vector3.one;
            card.anchoredPosition = cardHome;
        }
        if (shine != null) shine.anchoredPosition = shineHome;
    }

    private IEnumerator HideRoutine(GameObject panel)
    {
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group == null)
        {
            panel.SetActive(false);
            yield break;
        }
        float start = group.alpha;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, 0f, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }
        group.alpha = 0f;
        panel.SetActive(false);
    }

    private static void EnsureGroup(GameObject panel)
    {
        if (panel == null) return;
        if (panel.GetComponent<CanvasGroup>() == null) panel.AddComponent<CanvasGroup>();
    }

    private static void SetAlpha(GameObject panel, float alpha)
    {
        if (panel == null) return;
        CanvasGroup group = panel.GetComponent<CanvasGroup>();
        if (group != null) group.alpha = alpha;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int secs = Mathf.FloorToInt(seconds % 60f);
        return minutes.ToString("00") + ":" + secs.ToString("00");
    }
}
