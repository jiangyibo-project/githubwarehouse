using UnityEngine;

/// <summary>
/// 第三人称环绕相机：鼠标直接旋转视角（FPS 式，无需按住右键；Z/C 键盘辅助），滚轮缩放，带平滑跟随。
/// 默认取景按「现代动作游戏」调过：距离 5.4m、俯仰 30°、FOV 55（长焦压缩背景，角色占屏更大）、
/// 注视点抬到角色 +1.15m，站立不动时叠加 0.02m / 0.35Hz 的呼吸微抖。
/// 竖直抬升由 verticalOffset 显式控制（旧实现写作 height * 0.35 = 1.925m 的魔法数，
/// 那会把「俯仰 30°」变成实际约 45° 的俯视、角色被压扁更小，故收敛为 0.6m，改回 1.925 即可还原旧观感）。
/// 手感叠加层：trauma² 屏震与轻微 roll、锁定注视偏移与拉近、冲刺 / 闪避 FOV 拉伸。
/// 震动、FOV 与呼吸都用 unscaled 时间，顿帧期间依然在动。
/// 初始化是惰性的（首次 LateUpdate 或重新 OnEnable 时做），
/// 因此 PlayCapture 这类「enabled=false 后直接摆相机」的工具不会把状态搞坏。
/// </summary>
public sealed class OrbitFollowCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float distance = 5.4f;
    [SerializeField] private float verticalOffset = 0.6f;
    [SerializeField] private float minDistance = 3.6f;
    [SerializeField] private float maxDistance = 9f;
    [Header("鼠标转向（FPS 式，无需按住右键）")]
    [SerializeField] private float mouseSensitivity = 3.2f;   // 度/单位，鼠标横向灵敏度
    [SerializeField] private float mousePitchScale = 0.85f;     // 纵向稍慢，避免抬头低头太冲
    [SerializeField] private bool invertMouseY = false;
    [SerializeField] private bool lockCursor = true;            // 锁定光标，像 FPS 一样直接转
    [SerializeField] private float smoothSpeed = 10f;
    [SerializeField] private float pitch = 30f;
    [SerializeField] private float minPitch = 12f;
    [SerializeField] private float maxPitch = 65f;
    [SerializeField] private float lookAtHeight = 1.15f;
    [SerializeField] private float minHeightAboveTarget = 0.4f;
    [SerializeField] private KeyCode rotateLeftKey = KeyCode.Z;
    [SerializeField] private KeyCode rotateRightKey = KeyCode.C;

    [Header("呼吸感")]
    [SerializeField] private float breathAmplitude = 0.02f;
    [SerializeField] private float breathFrequency = 0.35f;

    [Header("屏震")]
    [SerializeField] private float shakeAmplitude = 0.35f;
    [SerializeField] private float shakeRollDegrees = 2.4f;

    [Header("锁定")]
    [SerializeField] private float lockPivotBlend = 0.3f;
    [SerializeField] private float lockDistancePull = 1.6f;
    [SerializeField] private float lockBlendSpeed = 4f;

    [Header("FOV")]
    [SerializeField] private float baseFieldOfView = 55f;
    [SerializeField] private float sprintFieldOfViewBoost = 6f;
    [SerializeField] private float fovLerpSpeed = 6f;

    [Header("相机避障")]
    [SerializeField] private bool enableOcclusion = true;
    [SerializeField] private float occlusionRadius = 0.28f;
    [SerializeField] private float occlusionPadding = 0.25f;
    [SerializeField] private float minOcclusionDistance = 0.5f;
    [SerializeField] private float occlusionShrinkSpeed = 12f;
    [SerializeField] private float occlusionRecoverSpeed = 4f;
    [SerializeField] private float closeUpThreshold = 2f;
    [SerializeField] private float closeUpLift = 1.1f;
    [SerializeField] private LayerMask occlusionMask = ~0;

    private float yaw;
    private float currentPitch;
    private float currentDistance;

    private Camera viewCamera;
    private PlayerDodge dodge;
    private TargetLock targetLock;
    private JuiceDirector juice;
    private Vector3 smoothedPosition;
    private Quaternion smoothedRotation;
    private float lockBlend;
    private float breathWeight;
    private float currentFieldOfView;
    private float nextResolveTime;
    private bool initialized;
    private bool snapNextFrame;

    private float occlusionDistance = -1f;   // 平滑后的可用相机距离，-1 = 未初始化
    private static readonly RaycastHit[] occlusionHits = new RaycastHit[8];

    private void OnEnable()
    {
        // 重新启用（含 PlayCapture 手动摆位后）一律重新取默认值并对齐，避免从旧位置插值过去
        initialized = false;
        snapNextFrame = true;
        nextResolveTime = 0f;
    }

    private void Start()
    {
        EnsureInitialized();
    }

    /// <summary>像 FPS 一样：游戏中锁定光标自由转，菜单/暂停时释放给 UI。</summary>
    private void ApplyCursorLock(bool canLook)
    {
        if (!lockCursor) return;
        if (canLook)
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        else if (Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        EnsureInitialized();
        ResolveReferences();

        float dt = Time.deltaTime;
        float unscaled = Time.unscaledDeltaTime;

        // ---- 输入：像 FPS 一样直接转，不再要求按住右键 ----
        bool canLook = GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.Playing;
        ApplyCursorLock(canLook);

        float drag = canLook ? Input.GetAxis("Mouse X") * mouseSensitivity : 0f;
        // Z / C 旋转视角（Q/E 已让给关卡交互：符文石碑用 E 激活）
        float keys = (Input.GetKey(rotateLeftKey) ? 1f : 0f) - (Input.GetKey(rotateRightKey) ? 1f : 0f);
        yaw += drag + keys * 90f * dt;

        if (canLook)
        {
            float invert = invertMouseY ? -1f : 1f;
            currentPitch = Mathf.Clamp(currentPitch - Input.GetAxis("Mouse Y") * mouseSensitivity * mousePitchScale * invert, minPitch, maxPitch);
        }

        currentDistance = Mathf.Clamp(currentDistance - Input.GetAxis("Mouse ScrollWheel") * 3f, minDistance, maxDistance);

        // ---- 锁定：注视点向目标偏移 + 轻微拉近 ----
        Transform lockTarget = targetLock != null ? targetLock.CurrentTarget : null;
        lockBlend = Mathf.MoveTowards(lockBlend, lockTarget != null ? 1f : 0f, lockBlendSpeed * unscaled);

        Quaternion rotation = Quaternion.Euler(currentPitch, yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * lookAtHeight;
        if (lockTarget != null && lockBlend > 0.001f)
            pivot = Vector3.Lerp(pivot, lockTarget.position + Vector3.up * 1f, lockPivotBlend * lockBlend);

        float effectiveDistance = Mathf.Max(minDistance * 0.6f, currentDistance - lockDistancePull * lockBlend);
        Vector3 desired = pivot - rotation * Vector3.forward * effectiveDistance + Vector3.up * verticalOffset;

        // ---- 避障：只决定"距离"，锁定拉近 / 屏震 / 呼吸 / FOV 全部照旧叠加 ----
        Vector3 eyeOffset = desired - pivot;
        float fullDistance = eyeOffset.magnitude;
        Vector3 eyeDirection = fullDistance > 0.001f ? eyeOffset / fullDistance : Vector3.up;
        float allowedDistance = fullDistance;
        if (enableOcclusion && fullDistance > 0.001f)
            allowedDistance = ResolveOcclusionDistance(pivot, eyeDirection, fullDistance, unscaled);
        Vector3 occludedDesired = pivot + eyeDirection * allowedDistance;

        // ---- 平滑（首帧或重新启用时直接对齐） ----
        if (snapNextFrame)
        {
            smoothedPosition = occludedDesired;
            smoothedRotation = rotation;
            snapNextFrame = false;
        }
        else
        {
            smoothedPosition = Vector3.Lerp(smoothedPosition, occludedDesired, smoothSpeed * dt);
            smoothedRotation = Quaternion.Slerp(smoothedRotation, rotation, smoothSpeed * dt);
        }
        // 相机下限跟着角色走：平地（y=0）得到与旧绝对值 0.4 完全相同的结果，
        // 下到池底（y=-0.6）时下限变 -0.2，镜头不再被硬顶住；同时仍高于池底不会穿地
        float floorY = target.position.y + minHeightAboveTarget;
        if (smoothedPosition.y < floorY) smoothedPosition.y = floorY;

        // ---- 被墙压到贴脸（< 2m）时抬到角色头顶附近俯视，朝向仍锁死在注视点 ----
        Vector3 cameraPosition = smoothedPosition;
        Quaternion cameraRotation = smoothedRotation;
        float lift = CloseUpLift(Vector3.Distance(pivot, smoothedPosition));
        if (lift > 0.001f)
        {
            cameraPosition += Vector3.up * (closeUpLift * lift);
            Vector3 toPivot = pivot - cameraPosition;
            if (toPivot.sqrMagnitude > 0.04f) cameraRotation = Quaternion.LookRotation(toPivot);
        }

        // ---- 屏震（unscaled，顿帧时也抖） + 呼吸微抖 ----
        Vector3 shakeOffset = juice != null ? juice.GetShakeOffset(shakeAmplitude) : Vector3.zero;
        float roll = juice != null ? juice.GetShakeRoll(shakeRollDegrees) : 0f;

        Vector3 finalPosition = cameraPosition + shakeOffset + GetBreathOffset(unscaled);
        // 屏震 / 呼吸也不能把相机压到下限以下
        if (finalPosition.y < floorY) finalPosition.y = floorY;
        transform.position = finalPosition;
        transform.rotation = cameraRotation * Quaternion.Euler(0f, 0f, roll);

        UpdateFieldOfView(unscaled);
    }

    /// <summary>
    /// 从注视点向期望机位做球体投射，返回平滑后的可用相机距离。
    /// 只跳过带 Health 的碰撞体（玩家 / 守卫 / 敌人），因此不会被自己或敌人的胶囊顶开；
    /// Trigger 全部忽略。收缩 12/s、恢复 4/s（指数平滑，帧率无关），用 unscaled 时间，
    /// 顿帧里也能立刻把相机从墙里拉出来。
    /// </summary>
    private float ResolveOcclusionDistance(Vector3 pivot, Vector3 direction, float fullDistance, float dt)
    {
        float target = fullDistance;
        int count = Physics.SphereCastNonAlloc(pivot, occlusionRadius, direction, occlusionHits, fullDistance,
            occlusionMask, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = occlusionHits[i];
            if (hit.collider == null) continue;
            if (hit.collider.GetComponentInParent<Health>() != null) continue;   // 玩家 / 守卫 / 敌人
            if (hit.distance < nearest) nearest = hit.distance;
        }

        if (nearest < float.MaxValue)
            target = Mathf.Clamp(nearest - occlusionPadding, minOcclusionDistance, fullDistance);

        if (occlusionDistance < 0f)
        {
            occlusionDistance = target;   // 首帧直接取，避免从 0 弹进来
        }
        else
        {
            float speed = target < occlusionDistance ? occlusionShrinkSpeed : occlusionRecoverSpeed;
            float k = 1f - Mathf.Exp(-Mathf.Max(0.0001f, speed * Mathf.Max(0f, dt)));
            occlusionDistance = Mathf.Lerp(occlusionDistance, target, k);
        }

        return Mathf.Clamp(occlusionDistance, 0f, fullDistance);
    }

    /// <summary>相机被墙压到 closeUpThreshold 以下时返回 0..1 的抬升权重（越近越高）。</summary>
    private float CloseUpLift(float cameraDistance)
    {
        if (closeUpThreshold <= minOcclusionDistance) return 0f;
        return Mathf.Clamp01(Mathf.InverseLerp(closeUpThreshold, minOcclusionDistance, cameraDistance));
    }

    /// <summary>站立不动时的 0.02m / 0.35Hz 呼吸微抖；走动或闪避时平滑淡出，不抢戏。</summary>
    private Vector3 GetBreathOffset(float unscaled)
    {
        bool idle = IsIdle();
        breathWeight = Mathf.MoveTowards(breathWeight, idle ? 1f : 0f, unscaled * 2.5f);
        if (breathWeight <= 0f || breathAmplitude <= 0f) return Vector3.zero;
        float phase = Time.unscaledTime * breathFrequency * Mathf.PI * 2f;
        return Vector3.up * (Mathf.Sin(phase) * breathAmplitude * breathWeight);
    }

    private bool IsIdle()
    {
        if (dodge != null && dodge.IsDodging) return false;
        float horizontal = Mathf.Abs(Input.GetAxisRaw("Horizontal"));
        float vertical = Mathf.Abs(Input.GetAxisRaw("Vertical"));
        return horizontal + vertical < 0.1f;
    }

    private void UpdateFieldOfView(float dt)
    {
        if (viewCamera == null) return;
        bool moving = !IsIdle();
        bool sprinting = Input.GetKey(KeyCode.LeftShift) && moving;
        bool dodging = dodge != null && dodge.IsDodging;
        float wanted = baseFieldOfView + ((sprinting || dodging) ? sprintFieldOfViewBoost : 0f);
        currentFieldOfView = Mathf.Lerp(currentFieldOfView, wanted, Mathf.Clamp01(fovLerpSpeed * dt));
        viewCamera.fieldOfView = currentFieldOfView;
    }

    /// <summary>惰性初始化：首次 LateUpdate 或重新启用时执行，不依赖 Start。</summary>
    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        viewCamera = GetComponent<Camera>();
        if (viewCamera == null) viewCamera = Camera.main;
        if (baseFieldOfView <= 0f && viewCamera != null) baseFieldOfView = viewCamera.fieldOfView;
        if (baseFieldOfView <= 0f) baseFieldOfView = 55f;

        yaw = target != null ? target.eulerAngles.y : transform.eulerAngles.y;
        currentPitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        currentDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        currentFieldOfView = baseFieldOfView;
        lockBlend = 0f;
        breathWeight = 0f;
        occlusionDistance = -1f;
        snapNextFrame = true;
    }

    private void ResolveReferences()
    {
        if (juice == null) juice = JuiceDirector.Instance;
        if (dodge != null && targetLock != null) return;
        if (Time.unscaledTime < nextResolveTime) return;
        // 目标身上可能还没挂上组件（场景生成顺序不定），每 0.5s 复查一次即可
        nextResolveTime = Time.unscaledTime + 0.5f;
        if (target != null)
        {
            if (dodge == null) dodge = target.GetComponentInParent<PlayerDodge>();
            if (targetLock == null) targetLock = target.GetComponentInParent<TargetLock>();
        }
    }
}
