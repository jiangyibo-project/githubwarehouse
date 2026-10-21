using UnityEngine;

/// <summary>
/// 地形贴地工具：向下射线采样脚下地面，并给出统一的「上台阶 / 下台阶 / 走出平台自然下落」垂直解算。
/// 只写 transform.position.y —— 水平方向的墙面阻挡交给 SimpleCollisionResolver（它主要写 x/z，
/// 只在穿透推出时微调 y）。玩家（PlayerExplorer）与所有敌人（EnemyWanderAI 及其派生类）共用同一套实现。
/// </summary>
public static class LevelGround
{
    /// <summary>默认可迈上的台阶高度（玩家/守卫的 Profile.stepUp 与碰撞解算的"能不能走上去"判定共用一个值）。</summary>
    public const float DefaultStepUp = 0.36f;

    /// <summary>默认落地抬升：站在地面上时脚底比地面高 0.02m。</summary>
    public const float DefaultGroundOffset = 0.02f;

    /// <summary>可站立表面的法线阈值：低于该值视为墙面，不当作地面（避免贴墙时被抬到墙顶）。</summary>
    private const float WalkableNormalY = 0.5f;

    private static readonly RaycastHit[] Hits = new RaycastHit[24];

    /// <summary>贴地参数。玩家与各级守卫各自持有一份（由序列化字段拼出来）。</summary>
    [System.Serializable]
    public struct Profile
    {
        public LayerMask mask;      // 参与贴地的层
        public float probeUp;       // 射线起点抬高（必须大于 stepUp，否则看不到要上的台阶）
        public float probeLength;   // 射线总长（需求：2m）
        public float stepUp;        // 可直接迈上去的最大台阶高度（石阶每级 0.32m）
        public float stepDown;      // 可直接走下去的最大落差，超过则进入自由下落
        public float gravity;       // 重力加速度（负值，需求：-25 m/s²）
        public float groundOffset;  // 落地后脚底相对地面的抬升（需求：+0.02）
        public float safetyY;       // 低于该高度视为掉出世界，回到 respawn

        /// <summary>构造一份默认参数：-25 重力、2m 射线、0.02 落地抬升。</summary>
        public static Profile CreateDefault(float stepUpHeight, float stepDownHeight)
        {
            Profile profile;
            profile.mask = ~0;
            profile.probeUp = 0.55f;
            profile.probeLength = 2f;
            profile.stepUp = stepUpHeight;
            profile.stepDown = stepDownHeight;
            profile.gravity = -25f;
            profile.groundOffset = DefaultGroundOffset;
            profile.safetyY = -20f;
            return profile;
        }
    }

    /// <summary>向下射线找脚下地面。返回 false 表示脚下空无一物（该下落）。</summary>
    public static bool SampleGround(Vector3 feet, Profile profile, Collider[] ignore, out float groundY)
    {
        Vector3 origin = new Vector3(feet.x, feet.y + profile.probeUp, feet.z);
        float distance = profile.probeUp + profile.probeLength;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, distance, profile.mask,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        float highest = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = Hits[i];
            Collider collider = hit.collider;
            if (collider == null || collider.isTrigger) continue;
            if (hit.normal.y < WalkableNormalY) continue;   // 竖直墙面不算地面
            if (IsIgnored(collider, ignore)) continue;      // 忽略自身胶囊，避免贴着自己
            if (collider.GetComponentInParent<Health>() != null) continue;   // 角色不算地形（不站到别人头上）
            if (hit.point.y > highest)
            {
                highest = hit.point.y;
                found = true;
            }
        }

        groundY = found ? highest : feet.y;
        return found;
    }

    /// <summary>
    /// 垂直解算：把 target 的 y 贴到地面，或按重力下落。水平位置不动。
    /// 返回解算后是否站在地面上。
    /// </summary>
    public static bool Follow(Transform target, Profile profile, Collider[] ignore,
        ref float verticalVelocity, ref bool grounded, Vector3 respawnPoint, float deltaTime)
    {
        if (deltaTime <= 0f) deltaTime = Time.deltaTime;
        Vector3 position = target.position;

        float groundY;
        bool hasGround = SampleGround(position, profile, ignore, out groundY);
        float standY = groundY + profile.groundOffset;

        if (grounded)
        {
            if (!hasGround)
            {
                grounded = false;
            }
            else
            {
                float rise = standY - position.y;
                if (rise > profile.stepUp)
                {
                    // 台阶太高（例如高台侧面）：保持当前高度，交给水平碰撞把角色挡住
                }
                else if (rise >= -profile.stepDown)
                {
                    // 同一平面 / 上台阶 / 走下石阶与蓄水池：直接贴合
                    position.y = standY;
                    verticalVelocity = 0f;
                }
                else
                {
                    grounded = false;   // 走出平台边缘，开始自然下落
                }
            }
        }

        if (!grounded)
        {
            verticalVelocity += profile.gravity * deltaTime;
            position.y += verticalVelocity * deltaTime;
            if (hasGround && verticalVelocity <= 0f && position.y <= standY)
            {
                position.y = standY;
                verticalVelocity = 0f;
                grounded = true;
            }
            else if (position.y < profile.safetyY)
            {
                // 保底：极端情况（被挤出地形）不让角色掉出世界
                position = new Vector3(respawnPoint.x, respawnPoint.y + profile.groundOffset, respawnPoint.z);
                verticalVelocity = 0f;
                grounded = false;
            }
        }

        target.position = position;
        return grounded;
    }

    private static bool IsIgnored(Collider collider, Collider[] ignore)
    {
        if (ignore == null) return false;
        for (int i = 0; i < ignore.Length; i++)
        {
            if (ignore[i] == collider) return true;
        }
        return false;
    }

    /// <summary>收集自身与子物体的碰撞体，供射线过滤「忽略玩家自身 Collider」。</summary>
    public static Collider[] CollectOwnColliders(GameObject owner)
    {
        if (owner == null) return new Collider[0];
        return owner.GetComponentsInChildren<Collider>(true);
    }
}
