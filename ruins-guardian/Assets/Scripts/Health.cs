using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 生命值组件：受伤、无敌帧、硬直、击退、死亡反馈与重置，玩家和守卫共用。
/// 硬直只提供倍率（StunMoveScale），由 PlayerCombat.MovementScale 乘进玩家移动速度；
/// 死亡反馈做「慢动作 + 倒地 + 几何坍缩溶解」（不改 shader，因此不用透明淡出）。
/// </summary>
public sealed class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private bool isPlayer;
    [SerializeField] private float invulnerableTime = 0f;
    [SerializeField] private float regenerationPerSecond = 0f;

    [Header("受击手感")]
    [SerializeField] private float hitStunDuration = 0.12f;
    [SerializeField] private float hitStunMoveScale = 0.25f;
    [SerializeField] private float knockbackDistance = 0.55f;
    [SerializeField] private float knockbackDuration = 0.16f;
    [SerializeField] private float playerKnockbackScale = 0.6f;

    [Header("死亡反馈")]
    [SerializeField] private float deathSlowScale = 0.25f;
    [SerializeField] private float deathSlowDuration = 0.2f;
    [SerializeField] private float deathTrauma = 0.8f;
    [SerializeField] private float fallDuration = 0.35f;
    [SerializeField] private float dissolveDuration = 0.75f;

    private Vector3 lastHitDirection;
    private float invulnerableUntil;
    private float stunUntil;
    private float knockbackTimer;
    private float knockbackSpeed;
    private Vector3 knockbackDirection = Vector3.forward;
    private Renderer[] renderers;
    private Collider bodyCollider;
    private Coroutine deathRoutine;
    private Quaternion deathBaseRotation;

    public float MaxHealth { get { return maxHealth; } }
    public float CurrentHealth { get; private set; }
    public bool IsPlayer { get { return isPlayer; } }
    public bool IsDead { get; private set; }
    public Vector3 LastHitDirection { get { return lastHitDirection; } }

    /// <summary>是否处于受击硬直中（0.12s）。</summary>
    public bool IsStunned { get { return Time.time < stunUntil; } }

    /// <summary>硬直期间的移动速度倍率，1 = 正常。</summary>
    public float StunMoveScale { get { return IsStunned ? Mathf.Clamp01(hitStunMoveScale) : 1f; } }

    public event Action<float, Vector3> Damaged;
    public event Action Died;
    public event Action ResetHealth;

    private void Awake()
    {
        CurrentHealth = maxHealth;
        renderers = GetComponentsInChildren<Renderer>(true);
        bodyCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        if (IsDead || regenerationPerSecond <= 0f) return;
        if (CurrentHealth < maxHealth)
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + regenerationPerSecond * Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (knockbackTimer <= 0f) return;
        // 放在 LateUpdate：晚于 AI 与玩家移动，击退才不会被同一帧的位移覆盖
        float dt = Mathf.Min(Time.deltaTime, knockbackTimer);
        knockbackTimer = Mathf.Max(0f, knockbackTimer - dt);
        transform.position += knockbackDirection * (knockbackSpeed * dt);
    }

    /// <summary>返回是否真正造成了伤害（无敌帧或已死亡会返回 false）。</summary>
    public bool TakeDamage(float amount, Vector3 hitDirection)
    {
        if (IsDead || amount <= 0f) return false;
        if (Time.time < invulnerableUntil) return false;

        lastHitDirection = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : Vector3.forward;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        stunUntil = Time.time + Mathf.Max(0f, hitStunDuration);
        StartKnockback(lastHitDirection);
        if (invulnerableTime > 0f) invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + invulnerableTime);
        Damaged?.Invoke(amount, lastHitDirection);

        if (CurrentHealth <= 0f)
        {
            IsDead = true;
            Died?.Invoke();
            PlayDeathFeedback();
        }
        return true;
    }

    /// <summary>临时无敌（闪避用）。</summary>
    public void GrantInvulnerability(float seconds)
    {
        if (seconds <= 0f) return;
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);
    }

    /// <summary>额外施加一次击退位移（外部系统可调用）。</summary>
    public void ApplyKnockback(Vector3 direction, float distance)
    {
        if (IsDead || distance <= 0f) return;
        Vector3 flat = direction;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) return;
        knockbackDirection = flat.normalized;
        knockbackSpeed = distance / Mathf.Max(0.02f, knockbackDuration);
        knockbackTimer = Mathf.Max(0.02f, knockbackDuration);
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
    }

    public void Restore()
    {
        IsDead = false;
        CurrentHealth = maxHealth;
        invulnerableUntil = 0f;
        stunUntil = 0f;
        knockbackTimer = 0f;

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }
        if (deathBaseRotation != Quaternion.identity) transform.rotation = deathBaseRotation;
        transform.localScale = Vector3.one;
        SetRenderersEnabled(true);
        if (bodyCollider != null) bodyCollider.enabled = true;
        ResetHealth?.Invoke();
    }

    private void StartKnockback(Vector3 direction)
    {
        if (knockbackDistance <= 0f || knockbackDuration <= 0f) return;
        Vector3 flat = direction;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) return;
        float distance = isPlayer ? knockbackDistance * Mathf.Clamp01(playerKnockbackScale) : knockbackDistance;
        ApplyKnockback(flat, distance);
    }

    private void PlayDeathFeedback()
    {
        // 玩家死亡交给 GameManager 走结算流程，不做倒地溶解
        if (isPlayer) return;

        JuiceDirector juice = JuiceDirector.Instance;
        if (juice != null)
        {
            juice.AddTrauma(deathTrauma);
            juice.SlowMotion(deathSlowScale, deathSlowDuration);
        }

        deathBaseRotation = transform.rotation;
        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        // 倒地
        Quaternion fall = deathBaseRotation * Quaternion.Euler(80f, 0f, 0f);
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, fallDuration);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.rotation = Quaternion.Slerp(deathBaseRotation, fall, t * t);
            yield return null;
        }
        transform.rotation = fall;

        // 溶解：垂直压缩 + 下沉，最后隐藏渲染
        elapsed = 0f;
        duration = Mathf.Max(0.05f, dissolveDuration);
        Vector3 startPosition = transform.position;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = new Vector3(1f, Mathf.Lerp(1f, 0.12f, t), 1f);
            transform.position = startPosition + Vector3.down * (0.45f * t);
            yield return null;
        }

        SetRenderersEnabled(false);
        // 尸体不再阻挡移动
        if (bodyCollider != null) bodyCollider.enabled = false;
        deathRoutine = null;
    }

    private void SetRenderersEnabled(bool value)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].enabled = value;
        }
    }
}
