using UnityEngine;

/// <summary>
/// 冲击波圆环：由 BruteAI 的第二段重锤触发，自圆心向外扩张后消失。
/// 纯表现件——伤害与击退由施放者判定，这里只负责"看得见的范围提示"。
/// </summary>
public sealed class ShockwaveRing : MonoBehaviour
{
    [SerializeField] private Transform ring;
    [SerializeField] private float duration = 0.45f;
    [SerializeField] private float startRadius = 0.4f;
    [SerializeField] private float thickness = 0.06f;
    [SerializeField] private float restRadius = 0.001f;

    private float elapsed;
    private float targetRadius;
    private bool playing;

    /// <summary>由生成器注入圆环视觉与时长。</summary>
    public void Configure(Transform ringTransform, float ringDuration)
    {
        ring = ringTransform;
        if (ringDuration > 0f) duration = ringDuration;
        if (ring != null) ring.gameObject.SetActive(false);
    }

    private void Awake()
    {
        if (ring != null) ring.gameObject.SetActive(false);
    }

    /// <summary>播一次冲击波，半径由施放者给出。</summary>
    public void Play(float radius)
    {
        if (ring == null) return;
        targetRadius = Mathf.Max(0.2f, radius);
        elapsed = 0f;
        playing = true;
        ring.gameObject.SetActive(true);
        ring.localScale = new Vector3(startRadius, thickness, startRadius);
    }

    private void Update()
    {
        if (!playing || ring == null) return;
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / Mathf.Max(0.05f, duration));
        float radius = Mathf.Lerp(startRadius, targetRadius, t * (2f - t));
        ring.localScale = new Vector3(radius, Mathf.Lerp(thickness, 0.01f, t), radius);
        if (t >= 1f)
        {
            playing = false;
            ring.localScale = new Vector3(restRadius, thickness, restRadius);
            ring.gameObject.SetActive(false);
        }
    }
}
