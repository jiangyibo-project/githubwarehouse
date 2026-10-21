using UnityEngine;

/// <summary>
/// HUD 只读桥：关卡层需要更新目标提示 / 血量显示，但 HUDController 归 UI 模块所有（禁止改），
/// 因此这里只调用它已有的公有方法，绝不改 HUDController 本体。
/// GameManager 没有暴露 SetObjective，所以用 FindObjectOfType 拿一次引用后缓存。
/// </summary>
public static class HudBridge
{
    private static HUDController hud;

    public static void SetObjective(string text)
    {
        HUDController target = Resolve();
        if (target != null) target.SetObjective(text);
    }

    public static void RefreshHealth(Health health)
    {
        if (health == null) return;
        HUDController target = Resolve();
        if (target != null) target.RefreshHealth(health);
    }

    private static HUDController Resolve()
    {
        if (hud == null) hud = Object.FindObjectOfType<HUDController>();
        return hud;
    }
}
