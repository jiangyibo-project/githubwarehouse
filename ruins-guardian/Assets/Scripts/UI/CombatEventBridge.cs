using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 战斗事件桥（规范 5.4 的 UI 侧接收端）。
/// T4 在 Assets/Scripts/Combat/CombatEvents.cs 里定义的是一组静态事件；本桥用反射在运行时
/// 找到该类型并把四个事件转发到自己的同名事件上，因此 UI 代码在任何装配顺序下都能编译，
/// 也不会直接引用战斗脚本里的任何具体类（规范 5.4 要求）。
/// 找不到类型时只打印一次提示，UI 退化为纯本地状态（不报错、不崩溃）。
/// </summary>
public static class CombatEventBridge
{
    /// <summary>连击数变化，0 表示连击结束。</summary>
    public static event Action<int> ComboChanged;
    /// <summary>闪避冷却就绪度 0..1。</summary>
    public static event Action<float> DodgeCooldown;
    /// <summary>命中落地：命中点 + 伤害。</summary>
    public static event Action<Vector3, int> HitLanded;
    /// <summary>锁定目标变化，null 表示无目标。</summary>
    public static event Action<Transform> TargetChanged;

    private const string SourceTypeName = "CombatEvents";

    private static bool initialized;
    private static bool available;
    private static bool warned;

    /// <summary>战斗事件源是否已就位。</summary>
    public static bool Available { get { EnsureInitialized(); return available; } }

    /// <summary>幂等初始化：必要时把反射订阅接上。</summary>
    public static void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;

        Type source = ResolveType(SourceTypeName);
        if (source == null)
        {
            if (!warned)
            {
                warned = true;
                Debug.Log("[UI] 尚未找到 CombatEvents（T4 未落地），HUD 先按本地状态运行。");
            }
            return;
        }

        available |= Bind<int>(source, "ComboChanged", HandleComboChanged);
        available |= Bind<float>(source, "DodgeCooldown", HandleDodgeCooldown);
        available |= Bind<Vector3, int>(source, "HitLanded", HandleHitLanded);
        available |= Bind<Transform>(source, "TargetChanged", HandleTargetChanged);
    }

    private static Type ResolveType(string name)
    {
        Type direct = Type.GetType(name);
        if (direct != null) return direct;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type found = assemblies[i].GetType(name, false);
            if (found != null) return found;
        }
        return null;
    }

    private static bool Bind<T1>(Type source, string eventName, Action<T1> handler)
    {
        return BindEvent(source, eventName, handler, typeof(Action<T1>));
    }

    private static bool Bind<T1, T2>(Type source, string eventName, Action<T1, T2> handler)
    {
        return BindEvent(source, eventName, handler, typeof(Action<T1, T2>));
    }

    private static bool BindEvent(Type source, string eventName, Delegate handler, Type expected)
    {
        try
        {
            EventInfo info = source.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
            if (info == null)
            {
                Debug.LogWarning("[UI] CombatEvents 缺少事件：" + eventName);
                return false;
            }
            if (info.EventHandlerType != expected)
            {
                Debug.LogWarning("[UI] CombatEvents." + eventName + " 的签名与规范 5.4 不一致");
                return false;
            }
            MethodInfo method = handler.Method;
            Delegate typed = Delegate.CreateDelegate(expected, method);
            info.AddEventHandler(null, typed);
            return true;
        }
        catch (Exception error)
        {
            Debug.LogWarning("[UI] 订阅 CombatEvents." + eventName + " 失败：" + error.Message);
            return false;
        }
    }

    private static void HandleComboChanged(int combo)
    {
        Action<int> handler = ComboChanged;
        if (handler != null) handler(combo);
    }

    private static void HandleDodgeCooldown(float readiness)
    {
        Action<float> handler = DodgeCooldown;
        if (handler != null) handler(readiness);
    }

    private static void HandleHitLanded(Vector3 point, int damage)
    {
        Action<Vector3, int> handler = HitLanded;
        if (handler != null) handler(point, damage);
    }

    private static void HandleTargetChanged(Transform target)
    {
        Action<Transform> handler = TargetChanged;
        if (handler != null) handler(target);
    }
}
