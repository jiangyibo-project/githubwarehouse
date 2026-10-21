using UnityEngine;

/// <summary>火把火焰：点光源强度与火焰大小随机闪烁。</summary>
public sealed class TorchFlicker : MonoBehaviour
{
    [SerializeField] private Light fireLight;
    [SerializeField] private Transform flame;
    [SerializeField] private Renderer flameRenderer;
    [SerializeField] private float baseIntensity = 1.6f;
    [SerializeField] private float flickerSpeed = 9f;

    private Vector3 baseScale;
    private Color baseColor;

    private void Awake()
    {
        if (flame != null) baseScale = flame.localScale;
        if (fireLight != null) baseColor = fireLight.color;
    }

    private void Update()
    {
        float t = Time.time * flickerSpeed;
        // 多个噪声叠加，火焰跳动更自然
        float noise = Mathf.PerlinNoise(t, 0f) * 0.6f + Mathf.PerlinNoise(0f, t * 1.7f) * 0.4f;
        float variation = 0.75f + noise * 0.5f;

        if (fireLight != null)
        {
            fireLight.intensity = baseIntensity * variation;
            fireLight.color = Color.Lerp(baseColor * 0.8f, new Color(1f, 0.75f, 0.3f), noise);
        }
        if (flame != null)
        {
            float scale = 1f + (variation - 1f) * 0.4f;
            flame.localScale = new Vector3(baseScale.x * scale, baseScale.y * (1.2f - (scale - 1f)), baseScale.z * scale);
        }
    }
}
