using UnityEngine;

/// <summary>
/// 玩家探索移动：相机相对的 WASD 移动、Shift 冲刺、空格跳跃、攻击时减速、围墙边界限制，
/// 并在地形层（LevelGround）完成向下射线贴地、0.32m 石阶自动迈上、池底落差与离开平台后的自然下落。
/// 水平方向的穿墙阻挡仍由 SimpleCollisionResolver 负责；本组件只写 y，两者不冲突。
/// </summary>
[RequireComponent(typeof(Health))]
public sealed class PlayerExplorer : MonoBehaviour
{
    [Header("移动")]
    [SerializeField] private float walkSpeed = 3.6f;
    [SerializeField] private float runSpeed = 6.5f;
    [SerializeField] private float turnSpeed = 14f;
    [SerializeField] private float arenaLimit = 9.2f;
    [SerializeField] private Animator animator;

    [Header("地形行走（向下射线贴地）")]
    [SerializeField] private float jumpSpeed = 6.8f;        // 空格跳跃，跳高约 0.92m
    [SerializeField] private float stepUpHeight = 0.36f;    // 北侧高台石阶每级 0.32m
    [SerializeField] private float stepDownSnap = 0.68f;    // 蓄水池落差 0.6m 可直接走下
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float probeUp = 0.55f;
    [SerializeField] private float probeLength = 2f;
    [SerializeField] private float groundOffset = 0.02f;
    [SerializeField] private LayerMask groundMask = ~0;

    private Health health;
    private PlayerCombat combat;
    private Transform cameraTransform;

    private Collider[] ownColliders;
    private LevelGround.Profile groundProfile;
    private float verticalVelocity;
    private bool grounded = true;
    private bool inputEnabled = true;
    private Vector3 spawnPoint;

    /// <summary>是否站在地面上（供敌人 / 波次系统查询）。</summary>
    public bool IsGrounded { get { return grounded; } }

    /// <summary>
    /// 只读输入意图：本帧是否有移动输入（用于碰撞解算识别"有输入却动不了"的无穿透卡死）。
    /// 死亡或输入被关闭时为 false。
    /// </summary>
    public bool HasMoveIntent { get; private set; }

    /// <summary>复活符文倒地等状态下关闭输入：位置不再平移，但仍然受贴地 / 重力影响。</summary>
    public void SetInputEnabled(bool value)
    {
        inputEnabled = value;
        if (!value && animator != null) animator.SetFloat("Speed", 0f);
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        combat = GetComponent<PlayerCombat>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (Camera.main != null) cameraTransform = Camera.main.transform;

        spawnPoint = transform.position;
        ownColliders = LevelGround.CollectOwnColliders(gameObject);
        groundProfile = LevelGround.Profile.CreateDefault(stepUpHeight, stepDownSnap);
        groundProfile.mask = groundMask;
        groundProfile.gravity = gravity;
        groundProfile.probeUp = probeUp;
        groundProfile.probeLength = probeLength;
        groundProfile.groundOffset = groundOffset;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    private void Update()
    {
        if (health.IsDead)
        {
            HasMoveIntent = false;
            if (animator != null) animator.SetFloat("Speed", 0f);
            return;
        }

        float horizontal = 0f;
        float vertical = 0f;
        if (inputEnabled)
        {
            horizontal = Input.GetAxisRaw("Horizontal");
            vertical = Input.GetAxisRaw("Vertical");
        }

        // 只读意图：只看原始轴输入，不参与任何移动计算
        HasMoveIntent = Mathf.Abs(horizontal) + Mathf.Abs(vertical) > 0.01f;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = cameraTransform.forward;
            forward.y = 0f;
            forward.Normalize();
            right = cameraTransform.right;
            right.y = 0f;
            right.Normalize();
        }

        Vector3 move = (forward * vertical + right * horizontal);
        if (move.sqrMagnitude > 1f) move.Normalize();

        bool running = Input.GetKey(KeyCode.LeftShift) && move.sqrMagnitude > 0.01f;
        float speed = running ? runSpeed : walkSpeed;
        if (combat != null) speed *= combat.MovementScale;

        Vector3 position = transform.position + move * speed * Time.deltaTime;
        position.x = Mathf.Clamp(position.x, -arenaLimit, arenaLimit);
        position.z = Mathf.Clamp(position.z, -arenaLimit, arenaLimit);
        transform.position = position;

        // 跳跃：只在站立时起跳，起跳后交给 LevelGround 的重力/落地逻辑
        if (inputEnabled && grounded && Input.GetKeyDown(KeyCode.Space))
        {
            verticalVelocity = jumpSpeed;
            grounded = false;
        }

        // 垂直：贴地 / 迈上石阶 / 走下蓄水池 / 离开平台自然下落
        LevelGround.Follow(transform, groundProfile, ownColliders,
            ref verticalVelocity, ref grounded, spawnPoint, Time.deltaTime);

        if (move.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
            if (animator != null) animator.SetFloat("Speed", running ? 6f : 2f);
        }
        else if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
        }
    }
}
