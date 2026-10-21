using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 敌人头顶血条：世界空间 Canvas，每帧朝向主相机的旋转（billboard），
/// 与 HUD 锁定框共用"看头顶找敌人"的信息通道；死亡后自动隐藏。
/// </summary>
public sealed class EnemyHealthBar : MonoBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image fill;
    [SerializeField] private bool hideWhenFull;

    private Camera mainCamera;

    /// <summary>由生成器注入引用（运行时也能改）。</summary>
    public void Configure(Health ownerHealth, Canvas barCanvas, Image fillImage, bool hideFull)
    {
        health = ownerHealth;
        canvas = barCanvas;
        fill = fillImage;
        hideWhenFull = hideFull;
        if (fill != null)
        {
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 1f;
        }
    }

    private void LateUpdate()
    {
        if (health == null) return;

        if (canvas != null)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera != null) canvas.transform.rotation = mainCamera.transform.rotation;
            bool visible = !health.IsDead && (!hideWhenFull || health.CurrentHealth < health.MaxHealth);
            if (canvas.enabled != visible) canvas.enabled = visible;
        }

        if (fill != null)
        {
            float ratio = health.MaxHealth > 0.001f ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth) : 0f;
            fill.fillAmount = ratio;
        }
    }
}
