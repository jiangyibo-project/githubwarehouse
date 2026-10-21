using UnityEngine;

/// <summary>
/// 战斗事件总线（规范 5.4）：UI 与其它系统只通过这些事件读取战斗状态，
/// 禁止直接引用战斗脚本里的具体类。所有触发方法都对 null 委托安全。
/// </summary>
public static class CombatEvents
{
    /// <summary>连击数变化，0 表示连击结束。</summary>
    public static event System.Action<int> ComboChanged;
    /// <summary>闪避冷却就绪度 0..1（1 = 可以闪避）。</summary>
    public static event System.Action<float> DodgeCooldown;
    /// <summary>命中：命中点世界坐标 + 伤害数值。</summary>
    public static event System.Action<Vector3, int> HitLanded;
    /// <summary>锁定目标变化，null 表示没有锁定。</summary>
    public static event System.Action<Transform> TargetChanged;

    /// <summary>广播连击数（0 = 连击结束）。</summary>
    public static void RaiseComboChanged(int combo)
    {
        System.Action<int> handler = ComboChanged;
        if (handler != null) handler(combo);
    }

    /// <summary>广播闪避冷却就绪度，自动钳制到 0..1。</summary>
    public static void RaiseDodgeCooldown(float readiness)
    {
        System.Action<float> handler = DodgeCooldown;
        if (handler != null) handler(Mathf.Clamp01(readiness));
    }

    /// <summary>广播一次命中。</summary>
    public static void RaiseHitLanded(Vector3 hitPoint, int damage)
    {
        System.Action<Vector3, int> handler = HitLanded;
        if (handler != null) handler(hitPoint, damage);
    }

    /// <summary>广播锁定目标变化。</summary>
    public static void RaiseTargetChanged(Transform target)
    {
        System.Action<Transform> handler = TargetChanged;
        if (handler != null) handler(target);
    }

    /// <summary>清空全部订阅，避免场景重建或退出播放模式后残留委托。</summary>
    public static void Clear()
    {
        ComboChanged = null;
        DodgeCooldown = null;
        HitLanded = null;
        TargetChanged = null;
    }

    /// <summary>进入播放模式时清一次，保证静态事件不跨会话累积。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlayModeEnter()
    {
        Clear();
    }
}
