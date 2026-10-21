using UnityEngine;

/// <summary>
/// 哨兵能量弹：直线飞行，命中玩家造成伤害；撞到墙体 / 地形或超时自毁。
/// 命中判定同时做「点到线段距离」（防高速穿模）与分段射线（防穿墙）。
/// </summary>
public sealed class EnergyBolt : MonoBehaviour
{
    [Header("数值")]
    [SerializeField] private float speed = 11f;
    [SerializeField] private float defaultDamage = 12f;
    [SerializeField] private float hitRadius = 0.55f;
    [SerializeField] private float lifeTime = 4f;

    [Header("表现")]
    [SerializeField] private Transform visual;
    [SerializeField] private Light boltLight;
    [SerializeField] private float pulseSpeed = 18f;

    private Transform target;
    private Health targetHealth;
    private Vector3 direction = Vector3.forward;
    private Vector3 visualBaseScale = Vector3.one;
    private float damage;
    private float elapsed;

    private void Awake()
    {
        if (visual != null) visualBaseScale = visual.localScale;
        if (damage <= 0f) damage = defaultDamage;
    }

    /// <summary>由哨兵在发射瞬间调用。</summary>
    public void Launch(Transform targetTransform, Vector3 flightDirection, float boltDamage)
    {
        target = targetTransform;
        targetHealth = targetTransform != null ? targetTransform.GetComponent<Health>() : null;
        direction = flightDirection.sqrMagnitude > 0.0001f ? flightDirection.normalized : transform.forward;
        damage = boltDamage > 0f ? boltDamage : defaultDamage;
        elapsed = 0f;
        transform.rotation = Quaternion.LookRotation(direction);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;
        if (elapsed > lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 start = transform.position;
        Vector3 end = start + direction * speed * dt;

        // 1) 命中玩家
        if (target != null && targetHealth != null && !targetHealth.IsDead)
        {
            Vector3 chest = target.position + Vector3.up * 1.0f;
            if (DistancePointToSegment(chest, start, end) <= hitRadius)
            {
                if (targetHealth.TakeDamage(damage, direction) && GameManager.Instance != null)
                    GameManager.Instance.NotifyPlayerHurt(chest);
                Destroy(gameObject);
                return;
            }
        }

        // 2) 撞墙 / 地形（角色自身的碰撞体一律不算，避免出生瞬间打在施放者身上）
        if (Physics.Linecast(start, end, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Health>() == null)
            {
                transform.position = hit.point;
                Destroy(gameObject);
                return;
            }
        }

        transform.position = end;

        if (visual != null)
        {
            float pulse = 1f + Mathf.Sin(elapsed * pulseSpeed) * 0.15f;
            visual.localScale = visualBaseScale * pulse;
        }
        if (boltLight != null) boltLight.intensity = 2.2f + Mathf.Sin(elapsed * pulseSpeed) * 0.6f;
    }

    private static float DistancePointToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        if (lengthSq < 0.000001f) return Vector3.Distance(point, a);
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
        return Vector3.Distance(point, a + ab * t);
    }
}
