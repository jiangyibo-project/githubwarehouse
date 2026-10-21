using UnityEngine;

/// <summary>
/// Tab 锁定：在 12m 内挑最近的存活敌人作为锁定目标。
/// 锁定期间角色朝向由「相机 → 目标」的水平方向决定（在 LateUpdate 覆盖普通移动朝向），
/// 再按 Tab、目标死亡或超出脱锁距离即解除，并通过 CombatEvents.TargetChanged 广播。
/// </summary>
public sealed class TargetLock : MonoBehaviour
{
    [Header("锁定参数")]
    [SerializeField] private KeyCode lockKey = KeyCode.Tab;
    [SerializeField] private float lockRange = 12f;
    [SerializeField] private float releaseRange = 16f;
    [SerializeField] private float faceTurnSpeed = 16f;

    [Header("引用")]
    [SerializeField] private Transform cameraTransform;

    private Health targetHealth;

    /// <summary>当前锁定目标，null 表示没有锁定。</summary>
    public Transform CurrentTarget { get; private set; }

    /// <summary>是否处于锁定状态。</summary>
    public bool HasTarget { get { return CurrentTarget != null; } }

    /// <summary>锁定时的瞄准方向（水平单位向量），供攻击起手对齐朝向。</summary>
    public Vector3 AimDirection { get; private set; }

    private void Update()
    {
        if (!IsGameplayActive()) return;
        if (Input.GetKeyDown(lockKey)) Toggle();

        if (CurrentTarget == null) return;
        if (targetHealth == null || targetHealth.IsDead)
        {
            SetTarget(null);
            return;
        }
        if (Vector3.Distance(transform.position, CurrentTarget.position) > releaseRange) SetTarget(null);
    }

    private void LateUpdate()
    {
        if (CurrentTarget == null) return;

        Transform cam = ResolveCamera();
        Vector3 origin = cam != null ? cam.position : transform.position + Vector3.up * 1.5f;
        Vector3 direction = CurrentTarget.position - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;

        direction.Normalize();
        AimDirection = direction;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), faceTurnSpeed * Time.deltaTime);
    }

    /// <summary>切换锁定状态（Tab 的等价调用）。</summary>
    public void Toggle()
    {
        if (CurrentTarget != null)
        {
            SetTarget(null);
            return;
        }
        SetTarget(FindNearestEnemy());
    }

    /// <summary>强制解除锁定（玩家死亡、重开局等）。</summary>
    public void Release()
    {
        SetTarget(null);
    }

    private Transform FindNearestEnemy()
    {
        Health[] candidates = FindObjectsOfType<Health>();   // 只在按键时扫描，不进 Update
        Transform best = null;
        float bestSqr = lockRange * lockRange;
        for (int i = 0; i < candidates.Length; i++)
        {
            Health candidate = candidates[i];
            if (candidate == null || candidate.IsPlayer || candidate.IsDead) continue;
            Vector3 delta = candidate.transform.position - transform.position;
            delta.y = 0f;
            float sqr = delta.sqrMagnitude;
            if (sqr > bestSqr) continue;
            bestSqr = sqr;
            best = candidate.transform;
        }
        return best;
    }

    private void SetTarget(Transform target)
    {
        if (CurrentTarget == target) return;
        CurrentTarget = target;
        targetHealth = target != null ? target.GetComponentInParent<Health>() : null;
        if (target == null) AimDirection = transform.forward;
        CombatEvents.RaiseTargetChanged(target);
    }

    /// <summary>菜单 / 暂停 / 结算期间不响应锁定输入。</summary>
    private static bool IsGameplayActive()
    {
        GameManager manager = GameManager.Instance;
        return manager == null || manager.State == GameManager.GameState.Playing;
    }

    private Transform ResolveCamera()
    {
        if (cameraTransform == null)
        {
            Camera main = Camera.main;
            if (main != null) cameraTransform = main.transform;
        }
        return cameraTransform;
    }
}
