using UnityEngine;

/// <summary>
/// 守卫有限状态机：巡逻、调查、追击、攻击、返回五个状态。
/// 带视锥视野、近距离感知、攻击前摇判定、受击记忆和头顶警觉图标。
/// V2 起同时充当敌人基类：状态处理全部拆成 protected virtual 方法，SentinelAI / BruteAI 覆写即可复用整套 FSM；
/// 并加入向下射线贴地（LevelGround），让守卫也能迈上石阶、走下蓄水池，不再被三层地形卡住。
/// 公有 API（State 枚举 / CurrentState / ResetGuard / SetAIEnabled）保持不变 —— AudioPanelController 依赖它们。
/// </summary>
[RequireComponent(typeof(Health))]
public class EnemyWanderAI : MonoBehaviour
{
    public enum State { Patrol, Investigate, Chase, Attack, Return }

    [Header("移动与视野")]
    [SerializeField] protected Transform player;
    [SerializeField] protected Transform[] patrolPoints;
    [SerializeField] protected float patrolSpeed = 1.8f;
    [SerializeField] protected float chaseSpeed = 3.4f;
    [SerializeField] protected float detectionRange = 8f;
    [SerializeField] protected float viewAngle = 120f;
    [SerializeField] protected float proximityRange = 3f;
    [SerializeField] protected float attackRange = 1.9f;
    [SerializeField] protected float turnSpeed = 9f;
    [SerializeField] protected float investigateDuration = 3.5f;

    [Header("攻击节奏")]
    [SerializeField] protected float attackInterval = 1.7f;
    [SerializeField] protected float windup = 0.7f;
    [SerializeField] protected float damage = 18f;

    [Header("引用")]
    [SerializeField] protected Animator animator;
    [SerializeField] protected Canvas alertCanvas;
    [SerializeField] protected GameObject questionMark;
    [SerializeField] protected GameObject exclamationMark;

    [Header("地形贴地")]
    [SerializeField] protected bool groundSnap = true;
    [SerializeField] protected float groundStepUp = 0.36f;
    [SerializeField] protected float groundStepDown = 0.68f;
    [SerializeField] protected float gravity = -25f;
    [SerializeField] protected float probeUp = 0.55f;
    [SerializeField] protected float probeLength = 2f;
    [SerializeField] protected float groundOffset = 0.02f;
    [SerializeField] protected LayerMask groundMask = ~0;

    protected Health health;
    protected Health playerHealth;
    protected int patrolIndex;
    protected State state = State.Patrol;
    protected Vector3 home;
    protected Vector3 lastKnownPosition;
    protected float investigateTimer;
    protected float attackTimer;
    protected bool hasStruck;
    protected float deadTimer;

    private Collider[] ownColliders;
    private LevelGround.Profile groundProfile;
    private float verticalVelocity;
    private bool grounded = true;
    private Vector3 spawnPoint;

    public State CurrentState { get { return state; } }

    protected virtual void Awake()
    {
        home = transform.position;
        spawnPoint = home;
        health = GetComponent<Health>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (player != null) playerHealth = player.GetComponent<Health>();
        if (alertCanvas != null) alertCanvas.enabled = false;

        ownColliders = LevelGround.CollectOwnColliders(gameObject);
        groundProfile = LevelGround.Profile.CreateDefault(groundStepUp, groundStepDown);
        groundProfile.mask = groundMask;
        groundProfile.gravity = gravity;
        groundProfile.probeUp = probeUp;
        groundProfile.probeLength = probeLength;
        groundProfile.groundOffset = groundOffset;

        OnAwake();
    }

    private void OnEnable()
    {
        health.Damaged += HandleDamaged;
        health.Died += HandleDied;
    }

    private void OnDisable()
    {
        health.Damaged -= HandleDamaged;
        health.Died -= HandleDied;
    }

    /// <summary>Awake 末尾的扩展点（派生类在这里改数值 / 缓存引用）。</summary>
    protected virtual void OnAwake() { }

    private void HandleDamaged(float amount, Vector3 direction)
    {
        if (health.IsDead) return;
        if (animator != null) animator.SetTrigger("HitTrigger");
        // 被打中立刻知道玩家位置
        if (player != null)
        {
            lastKnownPosition = player.position;
            ChangeState(State.Chase);
        }
        OnDamaged(amount, direction);
    }

    private void HandleDied()
    {
        if (animator != null)
        {
            animator.SetBool("IsDead", true);
            animator.SetFloat("Speed", 0f);
        }
        if (alertCanvas != null) alertCanvas.enabled = false;
        if (GameManager.Instance != null) GameManager.Instance.NotifyGuardDied(transform.position);
        OnDied();
        enabled = false;
    }

    /// <summary>受击扩展点（派生类做霸体、护盾等）。</summary>
    protected virtual void OnDamaged(float amount, Vector3 direction) { }

    /// <summary>死亡扩展点（派生类做爆炸粒子、掉落治疗等）。</summary>
    protected virtual void OnDied() { }

    public void ResetGuard()
    {
        transform.position = home;
        ChangeState(State.Patrol);
        patrolIndex = 0;
        investigateTimer = 0f;
        attackTimer = 0f;
        hasStruck = false;
        deadTimer = 0f;
        verticalVelocity = 0f;
        grounded = true;
        if (animator != null) animator.SetBool("IsDead", false);
        health.Restore();
        enabled = true;
        OnReset();
    }

    /// <summary>重置扩展点。</summary>
    protected virtual void OnReset() { }

    public void SetAIEnabled(bool value)
    {
        enabled = value;
        if (!value)
        {
            if (alertCanvas != null) alertCanvas.enabled = false;
            if (animator != null) animator.SetFloat("Speed", 0f);
        }
    }

    protected virtual void Update()
    {
        if (player == null || playerHealth == null || playerHealth.IsDead)
        {
            // 玩家死亡后停止追击，回到巡逻
            if (state == State.Chase || state == State.Attack || state == State.Investigate)
                ChangeState(State.Return);
            UpdateAlertIcon();

            if (state == State.Return)
            {
                MoveTo(home, patrolSpeed);
                if (FlatDistance(home) < 0.25f) ChangeState(State.Patrol);
            }
            else if (state == State.Patrol)
            {
                TickPatrol();
            }

            UpdateAnimatorSpeed();
            TickExtra();
            FollowGround();
            return;
        }

        bool canSee = CanSeePlayer();
        switch (state)
        {
            case State.Patrol:
                TickPatrol();
                if (canSee) ChangeState(State.Chase);
                break;

            case State.Investigate:
                TickInvestigate(canSee);
                break;

            case State.Chase:
                TickChase(canSee);
                break;

            case State.Attack:
                TickAttack();
                break;

            case State.Return:
                TickReturn(canSee);
                break;
        }

        UpdateAnimatorSpeed();
        UpdateAlertIcon();
        TickExtra();
        FollowGround();
    }

    // ================= 状态处理（派生类覆写这五个方法） =================

    /// <summary>巡逻：沿巡逻点循环移动。</summary>
    protected virtual void TickPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        Transform target = patrolPoints[patrolIndex];
        MoveTo(target.position, patrolSpeed);
        if (FlatDistance(target.position) < 0.35f)
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
    }

    /// <summary>调查：走向最后已知位置，原地扫视，超时后返回。</summary>
    protected virtual void TickInvestigate(bool canSee)
    {
        investigateTimer -= Time.deltaTime;
        if (FlatDistance(lastKnownPosition) > 0.6f)
        {
            MoveTo(lastKnownPosition, chaseSpeed * 0.7f);
        }
        else
        {
            Face(lastKnownPosition + transform.forward * 2f);
            transform.Rotate(0f, Mathf.Sin(Time.time * 2f) * 40f * Time.deltaTime, 0f);
        }

        if (canSee) ChangeState(State.Chase);
        else if (investigateTimer <= 0f) ChangeState(State.Return);
    }

    /// <summary>追击：贴近到攻击距离后进入攻击。</summary>
    protected virtual void TickChase(bool canSee)
    {
        if (!canSee)
        {
            ChangeState(State.Investigate);
            return;
        }

        lastKnownPosition = player.position;
        float distance = FlatDistance(player.position);
        if (distance <= attackRange) ChangeState(State.Attack);
        else MoveTo(player.position, chaseSpeed);
    }

    /// <summary>攻击：近战挥击，前摇结束瞬间判定命中。</summary>
    protected virtual void TickAttack()
    {
        float distance = FlatDistance(player.position);
        if (distance > attackRange + 0.5f)
        {
            ChangeState(State.Chase);
            return;
        }

        Face(player.position);
        attackTimer -= Time.deltaTime;

        if (!hasStruck && attackTimer <= attackInterval - windup)
        {
            // 前摇结束，判定命中
            hasStruck = true;
            if (distance <= attackRange)
            {
                Vector3 direction = player.position - transform.position;
                direction.y = 0f;
                if (playerHealth.TakeDamage(damage, direction.normalized))
                {
                    if (GameManager.Instance != null)
                        GameManager.Instance.NotifyPlayerHurt(player.position + Vector3.up);
                }
            }
        }

        if (attackTimer <= 0f)
        {
            hasStruck = false;
            attackTimer = attackInterval;
            if (animator != null) animator.SetTrigger("AttackTrigger");
            if (GameManager.Instance != null) GameManager.Instance.NotifyGuardSwing(transform.position);
        }
    }

    /// <summary>返回：回到出生点，途中若再次发现玩家则重新追击。</summary>
    protected virtual void TickReturn(bool canSee)
    {
        MoveTo(home, patrolSpeed);
        if (canSee) ChangeState(State.Chase);
        else if (FlatDistance(home) < 0.3f) ChangeState(State.Patrol);
    }

    /// <summary>每帧末尾的扩展点（派生类做蓄力发光、两段连招等）。</summary>
    protected virtual void TickExtra() { }

    protected virtual void ChangeState(State next)
    {
        State previous = state;
        state = next;
        switch (next)
        {
            case State.Investigate:
                investigateTimer = investigateDuration;
                break;
            case State.Attack:
                EnterAttack();
                break;
            case State.Patrol:
                patrolIndex = 0;
                break;
        }
        OnStateChanged(previous, next);
    }

    /// <summary>进入攻击状态时的初始化（重锤 / 远程蓄力的节奏由派生类覆写）。</summary>
    protected virtual void EnterAttack()
    {
        attackTimer = attackInterval;
        hasStruck = false;
        if (animator != null) animator.SetTrigger("AttackTrigger");
        if (GameManager.Instance != null) GameManager.Instance.NotifyGuardSwing(transform.position);
    }

    protected virtual void OnStateChanged(State previous, State next) { }

    // ================= 感知与移动 =================

    protected virtual bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance <= proximityRange) return true;
        if (distance > detectionRange) return false;
        if (Vector3.Angle(transform.forward, toPlayer) > viewAngle * 0.5f) return false;

        // 视线遮挡检测（围墙和断墙会挡住视野）
        Vector3 eye = transform.position + Vector3.up * 1.4f;
        Vector3 target = player.position + Vector3.up * 1.2f;
        if (Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Health>() == null) return false;
        }
        return true;
    }

    protected void UpdateAlertIcon()
    {
        if (alertCanvas == null) return;
        bool show = state == State.Chase || state == State.Attack || state == State.Investigate;
        alertCanvas.enabled = show;
        if (questionMark != null) questionMark.SetActive(state == State.Investigate);
        if (exclamationMark != null) exclamationMark.SetActive(state == State.Chase || state == State.Attack);
    }

    protected void UpdateAnimatorSpeed()
    {
        if (animator == null) return;
        float speed = 0f;
        if (state == State.Patrol || state == State.Return) speed = patrolSpeed;
        else if (state == State.Chase || state == State.Investigate) speed = chaseSpeed;
        animator.SetFloat("Speed", speed);
    }

    /// <summary>朝目标行走（只改 x/z，y 由 FollowGround 统一贴地）。</summary>
    protected void MoveTo(Vector3 target, float speed)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        Face(target);
        transform.position += direction.normalized * speed * Time.deltaTime;
    }

    /// <summary>远离目标（哨兵后撤保持射击距离用）。</summary>
    protected void MoveAway(Vector3 from, float speed)
    {
        Vector3 direction = transform.position - from;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;
        transform.position += direction.normalized * speed * Time.deltaTime;
    }

    protected void Face(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
    }

    protected float FlatDistance(Vector3 other)
    {
        Vector3 d = other - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>每帧末尾贴地：迈上石阶、走下蓄水池、离开平台自然下落。</summary>
    protected void FollowGround()
    {
        if (!groundSnap) return;
        LevelGround.Follow(transform, groundProfile, ownColliders,
            ref verticalVelocity, ref grounded, spawnPoint, Time.deltaTime);
    }

    /// <summary>当前是否站在地面上。</summary>
    protected bool IsGrounded { get { return grounded; } }
}
