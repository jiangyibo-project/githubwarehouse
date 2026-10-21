using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 动效驱动：分段血条的延迟掉血层、受击抖动与闪红、数值滚动，
/// 宝石弹跳、闪避冷却环、连击数字放大淡出、准心命中收缩。
/// 所有动画一律使用 unscaledDeltaTime，暂停（timeScale = 0）时依然正常。
/// </summary>
public sealed class HudAnimator : MonoBehaviour
{
    [Header("血条")]
    [SerializeField] private RectTransform shakeRoot;
    [SerializeField] private Image instantFill;
    [SerializeField] private Image delayedFill;
    [SerializeField] private Image flashFill;
    [SerializeField] private Image frameImage;
    [SerializeField] private Text healthText;
    [SerializeField] private Color healthColorHigh = new Color(1f, 0.72f, 0.4f, 1f);
    [SerializeField] private Color healthColorLow = new Color(0.85f, 0.29f, 0.23f, 1f);

    [Header("宝石")]
    [SerializeField] private RectTransform gemRoot;
    [SerializeField] private Text gemText;
    [SerializeField] private Image gemIcon;

    [Header("闪避冷却")]
    [SerializeField] private Image cooldownFill;
    [SerializeField] private Text cooldownText;
    [SerializeField] private RectTransform cooldownRoot;

    [Header("连击")]
    [SerializeField] private CanvasGroup comboGroup;
    [SerializeField] private RectTransform comboRoot;
    [SerializeField] private Text comboValue;

    [Header("准心")]
    [SerializeField] private RectTransform crosshairRoot;
    [SerializeField] private Image crosshairRing;
    [SerializeField] private Image crosshairDot;

    private float displayedHealth = -1f;
    private float targetHealth = 100f;
    private float maximumHealth = 100f;
    private float instantRatio = 1f;
    private float delayedRatio = 1f;
    private float drainDelay;
    private Coroutine shakeRoutine;
    private Coroutine gemRoutine;
    private Coroutine crosshairRoutine;
    private Coroutine comboRoutine;
    private float comboHideTime;
    private bool comboVisible;
    private float previousReadiness = 1f;
    private int lastGemCount = -1;
    private Vector2 shakeBasePosition;

    private void Awake()
    {
        if (shakeRoot != null) shakeBasePosition = shakeRoot.anchoredPosition;
        if (comboGroup != null)
        {
            comboGroup.alpha = 0f;
            comboGroup.blocksRaycasts = false;
            comboGroup.interactable = false;
        }
        if (flashFill != null) flashFill.color = UiFactory.Alpha(Color.white, 0f);
        if (crosshairRoot != null) crosshairRoot.localScale = Vector3.one;
    }

    private void Update()
    {
        TickDrain();
        ScrollHealthNumber();
        TickComboFade();
    }

    // ==================== 血条 ====================

    /// <summary>刷新血量（立即层立刻到位，延迟层稍后追上）。</summary>
    public void ApplyHealth(float current, float max)
    {
        maximumHealth = Mathf.Max(1f, max);
        targetHealth = Mathf.Clamp(current, 0f, maximumHealth);
        if (displayedHealth < 0f) displayedHealth = targetHealth;

        float ratio = targetHealth / maximumHealth;
        instantRatio = ratio;
        if (instantFill != null) instantFill.fillAmount = ratio;
        if (frameImage != null)
            frameImage.color = Color.Lerp(healthColorLow, UiFactory.Alpha(UiFactory.Gold, 0.9f), Mathf.Clamp01(ratio * 1.6f));

        if (ratio >= delayedRatio - 0.001f)
        {
            // 回血或首次赋值：延迟层直接对齐
            delayedRatio = ratio;
            if (delayedFill != null) delayedFill.fillAmount = ratio;
        }
        else
        {
            // 掉血：先停顿再追上，用 Update 驱动（HUD 被隐藏时不会卡住协程）
            drainDelay = 0.22f;
        }
    }

    private void TickDrain()
    {
        if (delayedRatio <= instantRatio + 0.0005f) return;
        drainDelay -= Time.unscaledDeltaTime;
        if (drainDelay > 0f) return;
        delayedRatio = Mathf.MoveTowards(delayedRatio, instantRatio, Time.unscaledDeltaTime * 0.55f);
        if (delayedFill != null) delayedFill.fillAmount = delayedRatio;
    }

    private void ScrollHealthNumber()
    {
        if (healthText == null) return;
        if (displayedHealth < 0f) displayedHealth = targetHealth;
        float difference = targetHealth - displayedHealth;
        if (Mathf.Abs(difference) > 0.05f)
        {
            float step = Mathf.Max(1.2f, Mathf.Abs(difference) * 7f) * Time.unscaledDeltaTime;
            displayedHealth = Mathf.MoveTowards(displayedHealth, targetHealth, step);
        }
        else
        {
            displayedHealth = targetHealth;
        }
        healthText.text = Mathf.CeilToInt(displayedHealth) + " / " + Mathf.CeilToInt(maximumHealth);
    }

    /// <summary>受击：血条抖动 + 瞬时闪红。</summary>
    public void PlayDamageShake()
    {
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(ShakeRoutine());
    }

    private IEnumerator ShakeRoutine()
    {
        float duration = 0.32f;
        float elapsed = 0f;
        if (flashFill != null) flashFill.color = UiFactory.Alpha(UiFactory.Blood, 0.7f);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            float amplitude = Mathf.Lerp(9f, 0f, t);
            if (shakeRoot != null)
                shakeRoot.anchoredPosition = shakeBasePosition + new Vector2(Random.Range(-amplitude, amplitude), Random.Range(-amplitude, amplitude) * 0.6f);
            if (flashFill != null) flashFill.color = UiFactory.Alpha(UiFactory.Blood, Mathf.Lerp(0.7f, 0f, t));
            yield return null;
        }
        if (shakeRoot != null) shakeRoot.anchoredPosition = shakeBasePosition;
        if (flashFill != null) flashFill.color = UiFactory.Alpha(UiFactory.Blood, 0f);
        shakeRoutine = null;
    }

    // ==================== 宝石 ====================

    /// <summary>刷新宝石计数，并在数值变化时弹跳。</summary>
    public void ApplyGems(int collected, int total)
    {
        bool changed = collected != lastGemCount;
        lastGemCount = collected;
        if (gemText != null) gemText.text = collected + " / " + total;
        if (!changed) return;
        if (gemRoutine != null) StopCoroutine(gemRoutine);
        gemRoutine = StartCoroutine(GemBounceRoutine());
    }

    private IEnumerator GemBounceRoutine()
    {
        float duration = 0.34f;
        float elapsed = 0f;
        if (gemIcon != null) gemIcon.color = Color.white;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float punch = 1f + Mathf.Sin(t * Mathf.PI) * 0.35f;
            if (gemRoot != null) gemRoot.localScale = new Vector3(punch, punch, 1f);
            if (gemIcon != null) gemIcon.color = Color.Lerp(Color.white, UiFactory.Gem, t);
            yield return null;
        }
        if (gemRoot != null) gemRoot.localScale = Vector3.one;
        if (gemIcon != null) gemIcon.color = UiFactory.Gem;
        gemRoutine = null;
    }

    // ==================== 闪避冷却环 ====================

    /// <summary>设置闪避冷却就绪度（0 = 刚用掉，1 = 就绪）。</summary>
    public void SetDodgeReadiness(float readiness)
    {
        readiness = Mathf.Clamp01(readiness);
        if (cooldownFill != null)
        {
            cooldownFill.fillAmount = readiness;
            cooldownFill.color = Color.Lerp(UiFactory.Blood, UiFactory.Teal, readiness);
        }
        if (cooldownText != null)
            cooldownText.text = readiness >= 0.999f ? "READY" : Mathf.RoundToInt(readiness * 100f) + "%";
        if (cooldownRoot != null && readiness >= 0.999f && previousReadiness < 0.999f)
            StartCoroutine(PulseRoutine(cooldownRoot, 1.18f, 0.2f));
        previousReadiness = readiness;
    }

    // ==================== 连击 ====================

    /// <summary>设置连击数，0 表示结束。数字放大后缓慢淡出。</summary>
    public void SetCombo(int count)
    {
        if (comboGroup == null) return;
        if (count <= 1)
        {
            if (comboVisible && comboRoutine == null) comboRoutine = StartCoroutine(ComboFadeRoutine());
            return;
        }

        if (comboValue != null) comboValue.text = "x" + count;
        comboGroup.alpha = 1f;
        comboVisible = true;
        comboHideTime = Time.unscaledTime + 2.4f;
        if (comboRoot != null)
        {
            if (comboRoutine != null) StopCoroutine(comboRoutine);
            comboRoutine = StartCoroutine(PulseRoutine(comboRoot, 1.3f, 0.26f));
        }
    }

    private void TickComboFade()
    {
        if (!comboVisible || comboGroup == null) return;
        if (comboRoutine != null) return;
        if (Time.unscaledTime < comboHideTime) return;
        comboRoutine = StartCoroutine(ComboFadeRoutine());
    }

    private IEnumerator ComboFadeRoutine()
    {
        float duration = 0.34f;
        float elapsed = 0f;
        float startAlpha = comboGroup != null ? comboGroup.alpha : 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (comboGroup != null) comboGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            if (comboRoot != null)
            {
                float scale = Mathf.Lerp(1f, 1.25f, t);
                comboRoot.localScale = new Vector3(scale, scale, 1f);
            }
            yield return null;
        }
        if (comboGroup != null) comboGroup.alpha = 0f;
        if (comboRoot != null) comboRoot.localScale = Vector3.one;
        comboVisible = false;
        comboRoutine = null;
    }

    // ==================== 准心 ====================

    /// <summary>命中反馈：准心收缩并闪红。</summary>
    public void PlayCrosshairHit()
    {
        if (crosshairRoot == null) return;
        if (crosshairRoutine != null) StopCoroutine(crosshairRoutine);
        crosshairRoutine = StartCoroutine(CrosshairRoutine());
    }

    private IEnumerator CrosshairRoutine()
    {
        float duration = 0.26f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float punch = t < 0.3f ? Mathf.Lerp(1f, 0.66f, t / 0.3f) : Mathf.Lerp(0.66f, 1f, (t - 0.3f) / 0.7f);
            if (crosshairRoot != null) crosshairRoot.localScale = new Vector3(punch, punch, 1f);
            if (crosshairRing != null) crosshairRing.color = Color.Lerp(UiFactory.Blood, UiFactory.Alpha(Color.white, 0.55f), t);
            if (crosshairDot != null) crosshairDot.color = Color.Lerp(UiFactory.Blood, UiFactory.Alpha(Color.white, 0.9f), t);
            yield return null;
        }
        if (crosshairRoot != null) crosshairRoot.localScale = Vector3.one;
        if (crosshairRing != null) crosshairRing.color = UiFactory.Alpha(Color.white, 0.55f);
        if (crosshairDot != null) crosshairDot.color = UiFactory.Alpha(Color.white, 0.9f);
        crosshairRoutine = null;
    }

    // ==================== 通用弹跳 ====================

    private IEnumerator PulseRoutine(RectTransform target, float peak, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = 1f + Mathf.Sin(t * Mathf.PI) * (peak - 1f);
            target.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
        target.localScale = Vector3.one;
        if (target == comboRoot) comboRoutine = null;
    }
}
