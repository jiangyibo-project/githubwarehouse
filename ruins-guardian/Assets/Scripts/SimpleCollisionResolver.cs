using UnityEngine;

/// <summary>
/// 轻量碰撞解算（V3 抗卡死版）。本工程移动直接改 transform，没有物理模拟，该组件是唯一的"实体墙"仲裁者。
///
/// 三层防护（对应「角色被卡住出不去」的根因）：
/// 1) **先探再走**：把本帧水平位移拆成 2~3 步，每步用 CapsuleCast 探测；撞到「高过台阶上限」的实心墙
///    就放弃该轴增量（只废被挡的那一轴，另一轴仍可贴墙滑行），不再先冲进墙里再指望推出来。
/// 2) **3D 分离推出**：每帧末尾用 Physics.ComputePenetration 做 5 轮推出，且**不再清零 direction.y**
///    —— 保留竖直分量，角色被夹在窄缝里时能被自然抬到台阶/矮墙顶再落回地面（可接受的"翻过去"）。
///    单帧竖直推出总量限制在 0.3m，避免被弹飞。
/// 3) **脱困兜底**：连续约 0.5s 既在穿透、实际位移又小于 5cm → 沿最近一次推出方向的反方向弹开 0.6m；
///    不行就绕一圈试 8 个水平方向；仍不行则先抬高 0.4m 再各试一次。常见夹缝 1 秒内脱身。
/// 4) **最近安全点回退**：每 0.6s 记录一次"没有穿透、没有被墙挡、脚下有地面、**八向都没有高墙**"的开阔站位
///    （保留最近 4 个）；连续卡住且 2)/3) 全部失败时回退（由新到旧逐个试，路径隔墙则放弃该候选）；
///    回退失败、或回退后 0.5s 内又卡住 → **直接回出生点**。这是任意未知死角的最终保险 —— 平地不会触发。
/// 5) **无穿透卡死**（仅主角）：有输入却连续 1.2s 不动、且 ≥2 个方向被高墙挡住 → 先脱困，再等 0.5s 仍不动 → 回退。
///
/// 分工不变：贴地 / 台阶高度仍由 LevelGround 负责（它只写 y），本组件主要写 x/z，推出时偶尔微调 y。
/// 玩家与所有守卫共用。
/// </summary>
public sealed class SimpleCollisionResolver : MonoBehaviour
{
    [SerializeField] private Collider ownCollider;
    [SerializeField] private float arenaLimit = 9.5f;

    [Header("穿透推出")]
    [SerializeField] private int iterations = 5;
    [SerializeField] private float pushSkin = 0.002f;
    [SerializeField] private float maxVerticalPush = 0.3f;

    [Header("先探再走（把本帧位移拆步）")]
    [SerializeField] private bool sweepMovement = true;
    [SerializeField] private int sweepSteps = 3;
    [SerializeField] private float sweepSkin = 0.01f;
    [SerializeField] private float sweepContactEpsilon = 0.005f;
    [SerializeField] private float sweepRadiusShrink = 0.02f;
    [SerializeField] private float teleportThreshold = 1.2f;

    [Header("脱困兜底")]
    [SerializeField] private float unstickHoldTime = 0.5f;
    [SerializeField] private float unstickMoveEpsilon = 0.05f;
    [SerializeField] private float unstickDistance = 0.6f;
    [SerializeField] private float unstickLift = 0.4f;

    [Header("最近安全点回退（任意死角自救）")]
    [SerializeField] private float safeRecordInterval = 0.6f;
    [SerializeField] private float safeReturnTime = 1.5f;
    [SerializeField] private int safeHistorySize = 4;
    [SerializeField] private float safeReturnRetryWindow = 0.5f;  // 回退后 0.5s 内又卡 → 直接回出生点

    [Header("无穿透卡死（只对主角生效：有输入却动不了）")]
    [SerializeField] private float blockedStuckTime = 1.2f;
    [SerializeField] private float blockedMoveEpsilon = 0.05f;
    [SerializeField] private float blockedProbeDistance = 0.25f;
    [SerializeField] private int blockedDirectionCount = 2;
    [SerializeField] private float blockedReturnDelay = 0.5f;

    [Header("台阶判定（与 PlayerExplorer / EnemyWanderAI 的 stepUp 对齐）")]
    [SerializeField] private float stepUpLimit = LevelGround.DefaultStepUp;

    private Collider[] solids;
    private RaycastHit[] castHits;

    private Vector3 lastPosition;
    private Vector3 lastPushDirection = Vector3.forward;
    private Vector3 stuckReferencePosition;
    private float stuckTimer;
    private float stuckAccum;
    private bool movementClipped;
    private bool initialised;

    // 无穿透卡死（仅主角）：先探再走把移动削掉、胶囊并没有嵌进墙里，穿透恒为 0
    private PlayerExplorer playerExplorer;
    private Vector3 blockedReferencePosition;
    private float blockedStuckTimer;
    private float blockedReturnTimer;
    private bool blockedUnstickTried;
    private readonly RaycastHit[] blockedHits = new RaycastHit[16];

    // 环形缓冲：最近若干个"确认安全"的站位（由旧到新的写入顺序由 safeCursor 标记）
    private Vector3[] safeHistory;
    private int safeCursor;
    private float safeRecordTimer;
    private Vector3 spawnPoint;          // Awake 时记录，最终兜底
    private float lastReturnTime = -999f;

    private void Awake()
    {
        if (ownCollider == null) ownCollider = GetComponent<Collider>();
        playerExplorer = GetComponent<PlayerExplorer>();
        castHits = new RaycastHit[8];
        RebuildSolids();
        lastPosition = transform.position;
        stuckReferencePosition = lastPosition;
        safeHistory = new Vector3[Mathf.Max(1, safeHistorySize)];
        for (int i = 0; i < safeHistory.Length; i++) safeHistory[i] = transform.position;
        safeCursor = 0;
        blockedReferencePosition = transform.position;
        spawnPoint = transform.position;
        initialised = true;
    }

    /// <summary>收集实心墙：排除自身、触发器、以及其它角色（角色之间不互相推挤，守卫之间不卡位）。</summary>
    private void RebuildSolids()
    {
        Collider[] all = FindObjectsOfType<Collider>();
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Collider candidate = all[i];
            if (candidate == null || candidate == ownCollider || candidate.isTrigger) continue;
            if (candidate.transform == transform) continue;
            if (candidate.GetComponentInParent<SimpleCollisionResolver>() != null) continue;
            all[count++] = candidate;
        }
        solids = new Collider[count];
        System.Array.Copy(all, solids, count);
    }

    private void LateUpdate()
    {
        if (!initialised || ownCollider == null || solids == null) return;

        movementClipped = false;

        Vector3 target = transform.position;
        Vector3 delta = target - lastPosition;
        bool teleported = delta.sqrMagnitude > teleportThreshold * teleportThreshold;

        if (sweepMovement && !teleported && delta.sqrMagnitude > 0.0000001f)
        {
            // 只拆水平位移；y 由 LevelGround 的贴地/下落决定，原样保留
            Vector3 start = new Vector3(lastPosition.x, target.y, lastPosition.z);
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            if (horizontal.sqrMagnitude > 0.0000001f)
            {
                Vector3 swept = SweepHorizontal(start, horizontal);
                transform.position = new Vector3(swept.x, target.y, swept.z);
            }
        }

        ClampToArena();
        float penetrationDepth = PushOutOfSolids();
        ClampToArena();

        TrackStuck(penetrationDepth);
        TrackBlockedStuck();
        RecordSafePosition(penetrationDepth);
        lastPosition = transform.position;
    }

    // =====================================================================
    // 一、先探再走
    // =====================================================================

    private Vector3 SweepHorizontal(Vector3 start, Vector3 delta)
    {
        int steps = Mathf.Clamp(sweepSteps, 1, 6);
        Vector3 position = start;
        Vector3 stepDelta = delta / steps;

        for (int i = 0; i < steps; i++)
        {
            Vector3 stepX = new Vector3(stepDelta.x, 0f, 0f);
            Vector3 stepZ = new Vector3(0f, 0f, stepDelta.z);

            // 整步能过就整步走
            if (!SweepBlocked(position, stepDelta))
            {
                position += stepDelta;
                continue;
            }

            // 被挡：按轴拆开，只放弃真正被挡的那一轴（保留贴墙滑行手感）
            bool blockedX = Mathf.Abs(stepX.x) > 0.00001f && SweepBlocked(position, stepX);
            bool blockedZ = Mathf.Abs(stepZ.z) > 0.00001f && SweepBlocked(position, stepZ);
            if (!blockedX) position += stepX;
            else movementClipped = true;
            if (!blockedZ) position += stepZ;
            else movementClipped = true;
        }

        return position;
    }

    /// <summary>沿 dir 从 position 做胶囊扫描：撞到「高过可迈台阶」的实心墙返回 true。</summary>
    private bool SweepBlocked(Vector3 position, Vector3 dir)
    {
        float distance = dir.magnitude;
        if (distance < 0.00001f) return false;
        Vector3 direction = dir / distance;

        GetCapsuleWorld(position, out Vector3 point1, out Vector3 point2, out float radius);
        int count = Physics.CapsuleCastNonAlloc(point1, point2, radius, direction, castHits,
            distance + sweepSkin, ~0, QueryTriggerInteraction.Ignore);

        float steppableTop = position.y + stepUpLimit + 0.02f;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castHits[i];
            Collider collider = hit.collider;
            if (collider == null || collider == ownCollider || collider.isTrigger) continue;
            if (collider.GetComponentInParent<SimpleCollisionResolver>() != null) continue;
            if (hit.distance <= sweepContactEpsilon) continue;      // 已经贴着的交给推出，不算阻挡
            if (collider.bounds.max.y <= steppableTop) continue;    // 台阶 / 地面能走上去，不该挡
            if (Mathf.Abs(hit.normal.y) > 0.7f) continue;           // 地板与天花板不算墙
            return true;
        }
        return false;
    }

    /// <summary>按自身 CapsuleCollider 求出世界空间胶囊两端（用于扫描）。</summary>
    private void GetCapsuleWorld(Vector3 rootPosition, out Vector3 point1, out Vector3 point2, out float radius)
    {
        CapsuleCollider capsule = ownCollider as CapsuleCollider;
        if (capsule == null)
        {
            Bounds bounds = ownCollider.bounds;
            radius = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z) - sweepRadiusShrink);
            point1 = rootPosition + Vector3.up * (bounds.extents.y - radius);
            point2 = rootPosition - Vector3.up * (bounds.extents.y - radius);
            return;
        }

        Vector3 scale = transform.lossyScale;
        float axisScale = capsule.direction == 0 ? scale.x : (capsule.direction == 1 ? scale.y : scale.z);
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        radius = Mathf.Max(0.05f, capsule.radius * radiusScale - sweepRadiusShrink);

        Vector3 center = rootPosition + transform.rotation * Vector3.Scale(capsule.center, scale);
        float halfHeight = Mathf.Max(0f, capsule.height * 0.5f * axisScale - capsule.radius * Mathf.Abs(scale.y));
        Vector3 up = transform.up;
        point1 = center + up * halfHeight;
        point2 = center - up * halfHeight;
    }

    // =====================================================================
    // 二、3D 穿透推出
    // =====================================================================

    /// <summary>把角色从所有实心墙里推出来。返回本帧的最大穿透深度（0 = 没穿透）。</summary>
    private float PushOutOfSolids()
    {
        float maxDepth = 0f;
        float verticalBudget = Mathf.Max(0f, maxVerticalPush);

        for (int iteration = 0; iteration < Mathf.Max(1, iterations); iteration++)
        {
            bool pushed = false;
            Bounds ownBounds = ownCollider.bounds;
            ownBounds.Expand(0.35f);

            for (int i = 0; i < solids.Length; i++)
            {
                Collider solid = solids[i];
                if (solid == null || !solid.enabled) continue;
                if (!solid.bounds.Intersects(ownBounds)) continue;

                if (!Physics.ComputePenetration(
                        ownCollider, transform.position, transform.rotation,
                        solid, solid.transform.position, solid.transform.rotation,
                        out Vector3 direction, out float distance)) continue;

                if (distance <= 0.0001f) continue;

                if (distance > maxDepth) maxDepth = distance;

                Vector3 push = direction.normalized * (distance + pushSkin);
                if (verticalBudget <= 0f) push.y = 0f;
                else if (push.y > verticalBudget) push.y = verticalBudget;
                else if (push.y < -verticalBudget) push.y = -verticalBudget;
                verticalBudget -= Mathf.Abs(push.y);

                transform.position += push;
                lastPushDirection = direction;
                pushed = true;
            }

            if (!pushed) break;
        }

        return maxDepth;
    }

    private void ClampToArena()
    {
        Vector3 position = transform.position;
        position.x = Mathf.Clamp(position.x, -arenaLimit, arenaLimit);
        position.z = Mathf.Clamp(position.z, -arenaLimit, arenaLimit);
        transform.position = position;
    }

    // =====================================================================
    // 三、脱困兜底
    // =====================================================================

    /// <summary>
    /// 卡死判定：只有"真的嵌进墙里"（穿透深度 > 2cm）才算数。
    /// 单纯顶着墙站着不会穿透（先探再走已经拦住了），因此不会被误判成卡死。
    /// 深穿透（> 6cm）按 0.5s 计时，浅穿透（2~6cm）按 1.5s 计时。
    /// </summary>
    private void TrackStuck(float penetrationDepth)
    {
        if (penetrationDepth <= 0.02f)
        {
            stuckTimer = 0f;
            stuckAccum = 0f;
            stuckReferencePosition = transform.position;
            return;
        }

        float moved = Vector3.Distance(transform.position, stuckReferencePosition);
        if (moved > unstickMoveEpsilon)
        {
            stuckReferencePosition = transform.position;
            stuckTimer = 0f;
            stuckAccum = 0f;
            return;
        }

        stuckTimer += Time.deltaTime * (penetrationDepth > 0.06f ? 1f : 0.34f);
        stuckAccum += Time.deltaTime;
        if (stuckTimer < unstickHoldTime) return;

        stuckTimer = 0f;
        if (TryUnstick())
        {
            stuckAccum = 0f;
        }
        else if (stuckAccum >= safeReturnTime && TryReturnToSafePosition())
        {
            stuckAccum = 0f;
        }

        stuckReferencePosition = transform.position;
        lastPosition = transform.position;
    }

    /// <summary>脱离夹缝：先反推、再绕圈、最后抬高，逐级尝试。返回是否成功脱身。</summary>
    private bool TryUnstick()
    {
        Vector3 origin = transform.position;

        Vector3 away = lastPushDirection;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        away.Normalize();
        if (TryEscapeTo(origin, away, unstickDistance)) return true;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            if (TryEscapeTo(origin, dir, unstickDistance)) return true;
        }

        // 抬起 0.4m 再推（被台阶 / 矮墙顶住时最有效）
        Vector3 lifted = origin + Vector3.up * unstickLift;
        transform.position = lifted;
        if (!PenetratesAnySolid(lifted)) return true;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            if (TryEscapeTo(lifted, dir, unstickDistance)) return true;
        }

        // 全部失败：退回原地，等下一轮（至少不会把角色推进墙里）
        transform.position = origin;
        return false;
    }

    // =====================================================================
    // 四、最近安全点回退（任意未知死角的最终保险）
    // =====================================================================

    /// <summary>
    /// 每 safeRecordInterval 秒记录一次安全站位。记录条件（三者同时满足）：
    /// 四个条件同时满足（T6-5 起加强）：
    ///   1) 没有穿透；2) 本帧位移没有被墙削掉；3) 脚下 0.6m 内有地面；
    ///   4) **八向短距胶囊探测都没有被"高过台阶上限的墙"挡住** —— 只记录真正开阔的位置，
    ///      保证回退目标一定是能自由走动的地方（贴墙/夹缝里的位置一律不记）。
    /// 平地正常走位时只是每 0.6s 存一个 Vector3，对移动手感零影响。
    /// </summary>
    private void RecordSafePosition(float penetrationDepth)
    {
        if (penetrationDepth > 0.005f || movementClipped)
        {
            safeRecordTimer = 0f;
            return;
        }

        safeRecordTimer += Time.deltaTime;
        if (safeRecordTimer < safeRecordInterval) return;

        Vector3 feet = transform.position;
        if (!Physics.Raycast(feet + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 0.6f, ~0, QueryTriggerInteraction.Ignore)) return;
        if (hit.collider == ownCollider) return;
        if (CountBlockedDirections() > 0) { safeRecordTimer = 0f; return; }   // 八向里有一向被高墙挡住就不是"开阔点"

        safeRecordTimer = 0f;
        safeHistory[safeCursor] = feet;
        safeCursor = (safeCursor + 1) % safeHistory.Length;
    }

    /// <summary>
    /// 回退自救，三级：
    ///   1) 若距上次回退不到 safeReturnRetryWindow(0.5s) —— 说明刚回退过又卡住，直接回出生点；
    ///   2) 由新到旧试安全点（候选点仍在墙里 / 与当前位置几乎重合 / 路径上隔着高过台阶的墙 → 放弃该候选）；
    ///   3) 全部失败 → 回出生点（Awake 记录的初始位置）。
    /// 这样"回退后还在卡"也有终点，必定能到一个能自由走的地方。
    /// </summary>
    private bool TryReturnToSafePosition()
    {
        if (Time.time - lastReturnTime < safeReturnRetryWindow) return TryReturnToSpawn();

        if (safeHistory != null && safeHistory.Length > 0)
        {
            Vector3 current = transform.position;
            for (int offset = 1; offset <= safeHistory.Length; offset++)
            {
                int index = (safeCursor - offset + safeHistory.Length * 2) % safeHistory.Length;
                Vector3 candidate = safeHistory[index];
                if ((candidate - current).sqrMagnitude < 0.01f) continue;
                if (PenetratesAnySolid(candidate)) continue;
                if (BlockedByWallAlong(current, candidate)) continue;

                ApplyReturn(candidate);
                return true;
            }
        }
        return TryReturnToSpawn();
    }

    /// <summary>最终兜底：回到出生点（不做路径挡墙检查 —— 这是最后手段，只要求落点不嵌墙）。</summary>
    private bool TryReturnToSpawn()
    {
        if (PenetratesAnySolid(spawnPoint)) return false;
        ApplyReturn(spawnPoint);
        return true;
    }

    private void ApplyReturn(Vector3 position)
    {
        transform.position = position;
        lastPushDirection = Vector3.zero;
        stuckTimer = 0f;
        stuckAccum = 0f;
        stuckReferencePosition = position;
        lastPosition = position;
        lastReturnTime = Time.time;
        safeRecordTimer = 0f;
        ResetBlockedStuck();
    }

    private bool TryEscapeTo(Vector3 origin, Vector3 direction, float distance)
    {
        Vector3 candidate = origin + direction * distance;
        candidate.x = Mathf.Clamp(candidate.x, -arenaLimit, arenaLimit);
        candidate.z = Mathf.Clamp(candidate.z, -arenaLimit, arenaLimit);
        if (PenetratesAnySolid(candidate)) return false;
        if (BlockedByWallAlong(origin, candidate)) return false;

        transform.position = candidate;
        lastPushDirection = direction;
        return true;
    }

    /// <summary>脱困路径上是否隔着"真正的墙"（防止 0.6m 的脱困位移把角色瞬移穿墙）。</summary>
    private bool BlockedByWallAlong(Vector3 from, Vector3 to)
    {
        Vector3 eyeFrom = from + Vector3.up * 0.9f;
        Vector3 eyeTo = to + Vector3.up * 0.9f;
        Vector3 direction = eyeTo - eyeFrom;
        float distance = direction.magnitude;
        if (distance < 0.0001f) return false;

        int count = Physics.RaycastNonAlloc(eyeFrom, direction / distance, castHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float wallTop = from.y + stepUpLimit + 0.02f;
        for (int i = 0; i < count; i++)
        {
            Collider collider = castHits[i].collider;
            if (collider == null || collider == ownCollider || collider.isTrigger) continue;
            if (collider.GetComponentInParent<SimpleCollisionResolver>() != null) continue;
            if (collider.bounds.max.y <= wallTop) continue;
            return true;
        }
        return false;
    }

    // =====================================================================
    // 五、无穿透卡死（先探再走把移动削掉、胶囊并未嵌墙 → 穿透恒为 0）
    // =====================================================================

    /// <summary>
    /// 只对主角生效（带 PlayerExplorer 的角色；敌人行为完全不变）。
    /// 触发条件（三条同时满足）：
    ///   1) HasMoveIntent == true（玩家确实按着方向键）；
    ///   2) 连续 blockedStuckTime(1.2s) 内水平位移 < blockedMoveEpsilon(0.05m)；
    ///   3) 短距离 CapsuleCast 探到 ≥ blockedDirectionCount(2) 个方向被"高过台阶上限的墙"挡住
    ///      —— 单面贴墙只会挡住 1 个方向，不会误触发；相邻两向（直角）算 2 个 ✓。
    /// 满足后先调 TryUnstick()；再过 blockedReturnDelay(0.5s) 仍无有效位移则 TryReturnToSafePosition()。
    /// </summary>
    private void TrackBlockedStuck()
    {
        if (playerExplorer == null) return;

        if (!playerExplorer.HasMoveIntent)
        {
            ResetBlockedStuck();
            return;
        }

        Vector3 now = transform.position;
        Vector3 flat = new Vector3(now.x - blockedReferencePosition.x, 0f, now.z - blockedReferencePosition.z);
        if (flat.magnitude > blockedMoveEpsilon)
        {
            blockedReferencePosition = now;
            ResetBlockedStuck();
            return;
        }

        if (CountBlockedDirections() < Mathf.Max(2, blockedDirectionCount))
        {
            ResetBlockedStuck();
            return;
        }

        blockedStuckTimer += Time.deltaTime;
        if (blockedStuckTimer < blockedStuckTime) return;

        if (!blockedUnstickTried)
        {
            blockedUnstickTried = true;
            blockedReturnTimer = Mathf.Max(0.05f, blockedReturnDelay);
            TryUnstick();
            return;
        }

        blockedReturnTimer -= Time.deltaTime;
        if (blockedReturnTimer > 0f) return;

        blockedReturnTimer = Mathf.Max(0.05f, blockedReturnDelay);
        if (TryReturnToSafePosition()) ResetBlockedStuck();
    }

    private void ResetBlockedStuck()
    {
        blockedStuckTimer = 0f;
        blockedReturnTimer = 0f;
        blockedUnstickTried = false;
        blockedReferencePosition = transform.position;
    }

    /// <summary>统计 8 个水平方向里被"高过台阶上限的墙"挡住的方向数（与先探再走同判据）。</summary>
    private int CountBlockedDirections()
    {
        Vector3 feet = transform.position;
        float steppableTop = feet.y + stepUpLimit + 0.02f;
        GetCapsuleWorld(feet, out Vector3 point1, out Vector3 point2, out float radius);
        int blocked = 0;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            int count = Physics.CapsuleCastNonAlloc(point1, point2, radius, direction, blockedHits,
                blockedProbeDistance + sweepSkin, ~0, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
            {
                Collider collider = blockedHits[h].collider;
                if (collider == null || collider == ownCollider || collider.isTrigger) continue;
                if (collider.GetComponentInParent<SimpleCollisionResolver>() != null) continue;
                if (blockedHits[h].distance <= sweepContactEpsilon) continue;
                if (collider.bounds.max.y <= steppableTop) continue;
                if (Mathf.Abs(blockedHits[h].normal.y) > 0.7f) continue;
                blocked++;
                break;
            }
        }
        return blocked;
    }

    /// <summary>候选位置是否还嵌在实心墙里（只在脱困时调用，量小）。</summary>
    private bool PenetratesAnySolid(Vector3 position)
    {
        Bounds ownBounds = ownCollider.bounds;
        ownBounds.center += position - transform.position;
        ownBounds.Expand(0.35f);

        for (int i = 0; i < solids.Length; i++)
        {
            Collider solid = solids[i];
            if (solid == null || !solid.enabled) continue;
            if (!solid.bounds.Intersects(ownBounds)) continue;
            if (!Physics.ComputePenetration(
                    ownCollider, position, transform.rotation,
                    solid, solid.transform.position, solid.transform.rotation,
                    out Vector3 direction, out float distance)) continue;
            if (distance > 0.005f) return true;
        }
        return false;
    }
}
