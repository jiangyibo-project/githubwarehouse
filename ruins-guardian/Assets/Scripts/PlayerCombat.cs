using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家近战攻击：三段连击 + 顿帧 + 屏震 + 命中特效。
/// 左键出招（场景里没有 PlayerDodge 时空格兼作出招，兼容旧场景，避免和闪避抢键）；
/// 每段拥有独立的前摇 / 命中窗口 / 收招 / 扇形范围 / 伤害 / 前冲位移；
/// 连打可以在 comboWindow 内接续下一段，超时自动重置连击。
/// </summary>
[RequireComponent(typeof(Health))]
public sealed class PlayerCombat : MonoBehaviour
{
    private enum Phase { Idle, Windup, HitWindow, Recovery }

    [Header("引用")]
    [SerializeField] private Animator animator;
    [SerializeField] private LayerMask enemyMask = ~0;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private TargetLock targetLock;
    [SerializeField] private PlayerDodge dodge;

    [Header("连击节奏")]
    [SerializeField] private float comboWindow = 0.55f;
    [SerializeField] private float attackMoveScale = 0.3f;

    [Header("三段：伤害")]
    [SerializeField] private float[] stageDamage = { 1.2f, 1.5f, 2.4f };
    [Header("三段：前摇秒数")]
    [SerializeField] private float[] stageWindup = { 0.12f, 0.11f, 0.17f };
    [Header("三段：命中窗口秒数")]
    [SerializeField] private float[] stageHitWindow = { 0.09f, 0.09f, 0.12f };
    [Header("三段：收招秒数")]
    [SerializeField] private float[] stageRecovery = { 0.18f, 0.2f, 0.34f };
    [Header("三段：扇形范围")]
    [SerializeField] private float[] stageRange = { 2.3f, 2.4f, 2.9f };
    [SerializeField] private float[] stageHalfAngle = { 60f, 65f, 80f };
    [Header("三段：前冲位移")]
    [SerializeField] private float[] stageLunge = { 0.6f, 0.7f, 1.1f };

    [Header("打击反馈")]
    [SerializeField] private float hitStopScale = 0.05f;
    [SerializeField] private float hitStopDuration = 0.07f;
    [SerializeField] private float heavyHitStopDuration = 0.1f;
    [SerializeField] private float hitTrauma = 0.32f;
    [SerializeField] private float heavyHitTrauma = 0.55f;
    [SerializeField] private float hitPointHeight = 0.9f;

    private Health health;
    private Phase phase = Phase.Idle;
    private float phaseTimer;
    private int stageIndex;         // 当前正在播放的段数 1..3，0 = 未出招
    private int nextStage = 1;      // 下一段要播的段数（连击接续）
    private float chainExpireTime;  // 连击窗口到期时间
    private int comboShown;         // 已广播的连击数
    private bool hitResolved;
    private float lungeRemaining;
    private readonly List<Health> damagedThisSwing = new List<Health>();

    /// <summary>是否处于出招中（前摇 / 命中窗口 / 收招）。</summary>
    public bool IsAttacking { get { return phase != Phase.Idle; } }

    /// <summary>当前连击数（0 = 连击结束）。</summary>
    public int ComboCount { get { return comboShown; } }

    /// <summary>移动速度倍率：出招减速、受击硬直减速、闪避时压到最低，1 = 正常。</summary>
    public float MovementScale
    {
        get
        {
            float scale = 1f;
            if (phase == Phase.Windup) scale = attackMoveScale * 0.6f;
            else if (phase == Phase.HitWindow) scale = attackMoveScale;
            else if (phase == Phase.Recovery) scale = Mathf.Min(0.75f, attackMoveScale + 0.45f);
            if (dodge != null && dodge.IsDodging) scale *= 0.15f;
            if (health != null) scale *= health.StunMoveScale;
            return scale;
        }
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (targetLock == null) targetLock = GetComponent<TargetLock>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (dodge == null) dodge = GetComponentInParent<PlayerDodge>();
        if (dodge == null) dodge = GetComponentInChildren<PlayerDodge>();
        if (cameraTransform == null)
        {
            Camera main = Camera.main;
            if (main != null) cameraTransform = main.transform;
        }
    }

    private void OnEnable()
    {
        if (health != null) health.Died += HandleDied;
    }

    private void OnDisable()
    {
        if (health != null) health.Died -= HandleDied;
        StopAttack();
    }

    private void HandleDied()
    {
        StopAttack();
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            if (phase != Phase.Idle) StopAttack();
            return;
        }

        // 顿帧期间 Time.deltaTime 极小，出招节奏被自然冻结，手感统一
        float dt = Time.deltaTime;
        TickPhase(dt);

        if (phase == Phase.Idle && chainExpireTime > 0f && Time.time >= chainExpireTime) ResetChain();
        if (phase != Phase.Idle) return;
        if (dodge != null && dodge.IsDodging) return;
        HandleInput();
    }

    private void HandleInput()
    {
        if (!IsGameplayActive()) return;
        bool pressed = Input.GetMouseButtonDown(0);
        if (!pressed && dodge == null) pressed = Input.GetKeyDown(KeyCode.Space);
        if (pressed) StartStage();
    }

    private void StartStage()
    {
        stageIndex = Mathf.Clamp(nextStage <= 0 ? 1 : nextStage, 1, 3);
        phase = Phase.Windup;
        phaseTimer = StageValue(stageWindup, stageIndex - 1, 0.12f);
        hitResolved = false;
        chainExpireTime = 0f;
        lungeRemaining = Mathf.Max(0f, StageValue(stageLunge, stageIndex - 1, 0f));

        FaceAimDirection();

        if (animator != null) animator.SetTrigger("AttackTrigger");
        GameManager manager = GameManager.Instance;
        if (manager != null) manager.NotifyPlayerSwing();

        comboShown = stageIndex;
        CombatEvents.RaiseComboChanged(comboShown);
    }

    private void TickPhase(float dt)
    {
        switch (phase)
        {
            case Phase.Windup:
                ApplyLunge(dt);
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    phase = Phase.HitWindow;
                    phaseTimer = StageValue(stageHitWindow, stageIndex - 1, 0.09f);
                }
                break;

            case Phase.HitWindow:
                if (!hitResolved) hitResolved = TryHit();
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    if (!hitResolved) hitResolved = TryHit();
                    phase = Phase.Recovery;
                    phaseTimer = StageValue(stageRecovery, stageIndex - 1, 0.18f);
                }
                break;

            case Phase.Recovery:
                phaseTimer -= dt;
                if (phaseTimer <= 0f)
                {
                    phase = Phase.Idle;
                    nextStage = stageIndex >= 3 ? 1 : stageIndex + 1;
                    chainExpireTime = Time.time + Mathf.Max(0.05f, comboWindow);
                }
                break;
        }
    }

    private void ApplyLunge(float dt)
    {
        if (lungeRemaining <= 0f) return;
        float windup = Mathf.Max(0.02f, StageValue(stageWindup, stageIndex - 1, 0.12f));
        float distance = lungeRemaining * Mathf.Clamp01(dt / windup);
        if (distance <= 0f) return;
        lungeRemaining = Mathf.Max(0f, lungeRemaining - distance);
        transform.position += transform.forward * distance;
    }

    private void FaceAimDirection()
    {
        Vector3 direction = Vector3.zero;
        if (targetLock != null && targetLock.HasTarget) direction = targetLock.AimDirection;
        if (direction.sqrMagnitude < 0.01f)
        {
            if (cameraTransform == null)
            {
                Camera main = Camera.main;
                if (main != null) cameraTransform = main.transform;
            }
            if (cameraTransform != null) direction = cameraTransform.forward;
        }
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.LookRotation(direction.normalized);
    }

    /// <summary>
    /// 按当前段的扇形参数结算一次命中。保持无参数私有签名，
    /// 因为 Assets/Editor/PlayModeSmokeTest.cs 会反射调用它。
    /// </summary>
    private bool TryHit()
    {
        int index = Mathf.Clamp(stageIndex - 1, 0, 2);
        float damage = Mathf.Max(0.01f, StageValue(stageDamage, index, 1f));
        float range = Mathf.Max(0.5f, StageValue(stageRange, index, 2.3f));
        float halfAngle = Mathf.Max(10f, StageValue(stageHalfAngle, index, 60f));

        Collider[] hits = Physics.OverlapSphere(transform.position + Vector3.up * 0.5f, range, enemyMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return false;

        damagedThisSwing.Clear();
        bool any = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Health enemy = hits[i] != null ? hits[i].GetComponentInParent<Health>() : null;
            if (enemy == null || enemy == health || enemy.IsPlayer || enemy.IsDead) continue;
            if (damagedThisSwing.Contains(enemy)) continue;

            Vector3 toEnemy = enemy.transform.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude < 0.01f) continue;
            if (Vector3.Angle(transform.forward, toEnemy) > halfAngle) continue;

            Vector3 hitDirection = -toEnemy.normalized;
            if (!enemy.TakeDamage(damage, hitDirection)) continue;

            damagedThisSwing.Add(enemy);
            any = true;

            int damageInt = Mathf.Max(1, Mathf.RoundToInt(damage));
            Vector3 hitPoint = enemy.transform.position + Vector3.up * hitPointHeight;
            GameManager manager = GameManager.Instance;
            if (manager != null) manager.NotifyHit(hitPoint, enemy, damageInt);
            CombatEvents.RaiseHitLanded(hitPoint, damageInt);
            ImpactFx.Spawn(hitPoint, hitDirection, damageInt, enemy);
        }

        if (any) PlayHitFeedback();
        return any;
    }

    private void PlayHitFeedback()
    {
        bool heavy = stageIndex >= 3;
        JuiceDirector juice = JuiceDirector.Instance;
        if (juice != null)
        {
            juice.HitStop(hitStopScale, heavy ? heavyHitStopDuration : hitStopDuration);
            float trauma = heavy ? heavyHitTrauma : hitTrauma * (1f + 0.15f * Mathf.Max(0, stageIndex - 1));
            juice.AddTrauma(trauma);
        }
    }

    private void StopAttack()
    {
        phase = Phase.Idle;
        phaseTimer = 0f;
        stageIndex = 0;
        lungeRemaining = 0f;
        ResetChain();
    }

    private void ResetChain()
    {
        nextStage = 1;
        chainExpireTime = 0f;
        if (comboShown != 0)
        {
            comboShown = 0;
            CombatEvents.RaiseComboChanged(0);
        }
    }

    /// <summary>菜单 / 暂停 / 结算期间不接受出招输入（这些状态下 Time.timeScale = 0）。</summary>
    private static bool IsGameplayActive()
    {
        GameManager manager = GameManager.Instance;
        return manager == null || manager.State == GameManager.GameState.Playing;
    }

    private static float StageValue(float[] values, int index, float fallback)
    {
        if (values == null || values.Length == 0) return fallback;
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
    }
}
