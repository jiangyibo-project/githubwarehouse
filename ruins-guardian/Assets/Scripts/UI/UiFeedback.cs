using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 反馈层：拾取提示（居中偏下、带图标、淡入淡出）、顶部目标提示条、
/// 世界坐标锁定目标框、右下按键提示。
/// 全部使用不缩放时间，暂停时也能正常播放。
/// </summary>
public sealed class UiFeedback : MonoBehaviour
{
    [Header("拾取提示")]
    [SerializeField] private CanvasGroup pickupGroup;
    [SerializeField] private RectTransform pickupRoot;
    [SerializeField] private Text pickupText;
    [SerializeField] private Image pickupIcon;

    [Header("目标提示条")]
    [SerializeField] private CanvasGroup objectiveGroup;
    [SerializeField] private Text objectiveText;
    [SerializeField] private RectTransform objectiveAccent;
    [SerializeField] private float objectiveHoldSeconds = 4.5f;

    [Header("锁定目标框")]
    [SerializeField] private CanvasGroup lockGroup;
    [SerializeField] private RectTransform lockRoot;
    [SerializeField] private Text lockLabel;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private float lockHeightOffset = 1.15f;

    [Header("按键提示")]
    [SerializeField] private CanvasGroup hintGroup;
    [SerializeField] private Text hintKeyText;
    [SerializeField] private Text hintLabelText;
    [SerializeField] private RectTransform hintRoot;

    private Transform lockTarget;
    private float objectiveHideTime;
    private float hintHideTime = -1f;
    private Coroutine pickupRoutine;
    private Coroutine lockRoutine;
    private Coroutine hintPulseRoutine;
    private Vector2 pickupBasePosition = new Vector2(0f, 96f);

    private void Awake()
    {
        if (pickupRoot != null) pickupBasePosition = pickupRoot.anchoredPosition;
        if (pickupGroup != null) pickupGroup.alpha = 0f;
        if (lockGroup != null) lockGroup.alpha = 0f;
        if (objectiveGroup != null) objectiveGroup.alpha = 0f;
        if (hintGroup != null) hintGroup.alpha = 0f;
        if (worldCamera == null) worldCamera = Camera.main;
    }

    private void LateUpdate()
    {
        TickObjective();
        TickLock();
        TickHint();
    }

    // ==================== 拾取提示 ====================

    /// <summary>弹出拾取提示（居中偏下，带图标）。</summary>
    public void ShowPickup(string message, Color tint)
    {
        if (pickupGroup == null) return;
        if (pickupText != null)
        {
            pickupText.text = message;
            pickupText.color = new Color(1f, 1f, 1f, 0.96f);
        }
        if (pickupIcon != null) pickupIcon.color = tint;
        if (pickupRoutine != null) StopCoroutine(pickupRoutine);
        pickupRoutine = StartCoroutine(PickupRoutine(tint));
    }

    private IEnumerator PickupRoutine(Color tint)
    {
        float duration = 1.5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float alpha = t < 0.15f ? t / 0.15f : (t > 0.72f ? Mathf.Lerp(1f, 0f, (t - 0.72f) / 0.28f) : 1f);
            if (pickupGroup != null) pickupGroup.alpha = alpha;
            if (pickupRoot != null)
            {
                float rise = Mathf.Lerp(0f, 14f, Mathf.Clamp01(t / 0.3f));
                pickupRoot.anchoredPosition = pickupBasePosition + new Vector2(0f, rise);
                float scale = t < 0.2f ? Mathf.Lerp(0.86f, 1.04f, t / 0.2f) : Mathf.Lerp(1.04f, 1f, Mathf.Clamp01((t - 0.2f) / 0.2f));
                pickupRoot.localScale = new Vector3(scale, scale, 1f);
            }
            yield return null;
        }
        if (pickupGroup != null) pickupGroup.alpha = 0f;
        if (pickupRoot != null)
        {
            pickupRoot.anchoredPosition = pickupBasePosition;
            pickupRoot.localScale = Vector3.one;
        }
        pickupRoutine = null;
    }

    // ==================== 目标提示条 ====================

    /// <summary>设置顶部目标提示，淡入后自动淡出。</summary>
    public void SetObjective(string message)
    {
        if (objectiveText != null) objectiveText.text = message;
        if (objectiveGroup != null) objectiveGroup.alpha = 1f;
        if (objectiveAccent != null) objectiveAccent.localScale = new Vector3(1f, 1f, 1f);
        objectiveHideTime = Time.unscaledTime + objectiveHoldSeconds;
    }

    private void TickObjective()
    {
        if (objectiveGroup == null) return;
        if (objectiveGroup.alpha <= 0.001f) return;
        if (Time.unscaledTime < objectiveHideTime) return;
        objectiveGroup.alpha = Mathf.MoveTowards(objectiveGroup.alpha, 0f, Time.unscaledDeltaTime * 1.6f);
    }

    // ==================== 锁定目标框 ====================

    /// <summary>锁定一个世界目标（null 表示取消锁定）。</summary>
    public void SetLockTarget(Transform target)
    {
        if (target == lockTarget) return;
        lockTarget = target;
        if (target == null)
        {
            if (lockRoutine != null) StopCoroutine(lockRoutine);
            lockRoutine = StartCoroutine(LockFadeRoutine(0f));
            return;
        }
        if (lockLabel != null) lockLabel.text = "锁定";
        if (lockRoot != null) lockRoot.localScale = Vector3.one * 1.35f;
        if (lockRoutine != null) StopCoroutine(lockRoutine);
        lockRoutine = StartCoroutine(LockFadeRoutine(1f));
    }

    private IEnumerator LockFadeRoutine(float targetAlpha)
    {
        float duration = 0.2f;
        float elapsed = 0f;
        float start = lockGroup != null ? lockGroup.alpha : 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (lockGroup != null) lockGroup.alpha = Mathf.Lerp(start, targetAlpha, t);
            if (lockRoot != null)
            {
                float scale = Mathf.Lerp(1.35f, 1f, t);
                lockRoot.localScale = new Vector3(scale, scale, 1f);
            }
            yield return null;
        }
        if (lockGroup != null) lockGroup.alpha = targetAlpha;
        if (lockRoot != null) lockRoot.localScale = Vector3.one;
        lockRoutine = null;
    }

    private void TickLock()
    {
        if (lockGroup == null || lockRoot == null) return;
        if (lockTarget == null)
        {
            if (lockGroup.alpha > 0.001f)
            {
                if (lockRoutine != null) StopCoroutine(lockRoutine);
                lockRoutine = StartCoroutine(LockFadeRoutine(0f));
            }
            return;
        }

        Camera camera = worldCamera != null ? worldCamera : Camera.main;
        if (camera == null) return;
        Vector3 world = lockTarget.position + Vector3.up * lockHeightOffset;
        Vector3 screen = camera.WorldToScreenPoint(world);
        if (screen.z <= 0f)
        {
            lockGroup.alpha = 0f;
            return;
        }

        RectTransform parent = lockRoot.parent as RectTransform;
        if (parent == null) return;
        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out local))
            lockRoot.anchoredPosition = local;
    }

    // ==================== 按键提示 ====================

    /// <summary>设置按键提示内容（例如 SPACE / 闪避）。</summary>
    public void SetKeyHint(string key, string description)
    {
        if (hintKeyText != null) hintKeyText.text = key;
        if (hintLabelText != null) hintLabelText.text = description;
        if (hintGroup != null) hintGroup.alpha = 1f;
    }

    /// <summary>让按键提示脉冲一下（闪避就绪时调用）。</summary>
    public void PulseKeyHint()
    {
        if (hintRoot == null) return;
        if (hintGroup != null) hintGroup.alpha = 1f;
        if (hintPulseRoutine != null) StopCoroutine(hintPulseRoutine);
        hintPulseRoutine = StartCoroutine(PulseRoutine());
    }

    /// <summary>按键提示在指定秒数后淡出（传负数则常驻）。</summary>
    public void SetKeyHintHold(float seconds)
    {
        hintHideTime = seconds < 0f ? -1f : Time.unscaledTime + seconds;
    }

    private void TickHint()
    {
        if (hintGroup == null) return;
        if (hintHideTime < 0f) return;
        if (Time.unscaledTime < hintHideTime) return;
        hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, 0f, Time.unscaledDeltaTime * 1.4f);
    }

    private IEnumerator PulseRoutine()
    {
        float duration = 0.28f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.22f;
            hintRoot.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
        hintRoot.localScale = Vector3.one;
        hintPulseRoutine = null;
    }
}
