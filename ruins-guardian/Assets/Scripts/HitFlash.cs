using System.Collections;
using UnityEngine;

/// <summary>受击反馈：身体材质短暂闪白，配合粒子与音效增强打击感。</summary>
public sealed class HitFlash : MonoBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private Renderer bodyRenderer;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float duration = 0.14f;

    private Material runtimeMaterial;
    private Color originalColor;
    private bool initialized;

    private void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (bodyRenderer != null)
        {
            // 实例化材质，避免改到共享资源
            runtimeMaterial = bodyRenderer.material;
            originalColor = runtimeMaterial.color;
            initialized = true;
        }
    }

    private void OnEnable()
    {
        if (health != null) health.Damaged += HandleDamaged;
        if (health != null) health.ResetHealth += HandleReset;
    }

    private void OnDisable()
    {
        if (health != null) health.Damaged -= HandleDamaged;
        if (health != null) health.ResetHealth -= HandleReset;
    }

    private void HandleDamaged(float amount, Vector3 direction)
    {
        if (!initialized || health.IsDead) return;
        StopAllCoroutines();
        StartCoroutine(FlashRoutine());
    }

    private void HandleReset()
    {
        if (initialized) runtimeMaterial.color = originalColor;
    }

    private IEnumerator FlashRoutine()
    {
        runtimeMaterial.color = flashColor;
        yield return new WaitForSeconds(duration);
        runtimeMaterial.color = originalColor;
    }
}
