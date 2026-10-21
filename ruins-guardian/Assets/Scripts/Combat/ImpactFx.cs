using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 命中特效工厂：火花粒子、命中点闪光、地面灰尘环。
/// 击退与受击闪白由 Health / HitFlash 承担（Health.Damaged 已经驱动 HitFlash），这里不重复实现，
/// ImpactFx 只负责视觉层。组件式单例：场景生成器 AddComponent 后直接用，
/// 没挂也能在第一次需要时自动创建。
/// </summary>
public sealed class ImpactFx : MonoBehaviour
{
    [Header("火花")]
    [SerializeField] private Color sparkColor = new Color(1f, 0.69f, 0.4f);
    [SerializeField] private int sparkCount = 14;
    [SerializeField] private float sparkSpeed = 4.5f;
    [SerializeField] private float sparkSize = 0.09f;
    [SerializeField] private float sparkLife = 0.35f;
    [SerializeField] private float sparkRadius = 0.12f;

    [Header("命中闪光")]
    [SerializeField] private float flashDuration = 0.09f;
    [SerializeField] private float flashIntensity = 6f;
    [SerializeField] private float flashRange = 5f;

    [Header("地面灰尘环")]
    [SerializeField] private Color dustColor = new Color(0.78f, 0.75f, 0.68f);
    [SerializeField] private float dustDuration = 0.4f;
    [SerializeField] private float dustRadius = 1.7f;
    [SerializeField] private float dustHeight = 0.05f;
    [SerializeField] private int dustDamageThreshold = 2;

    private static ImpactFx instance;

    private ParticleSystem sparks;
    private Light flashLight;
    private float flashTimer;
    private float flashPeak;
    private readonly List<DustRing> rings = new List<DustRing>();

    /// <summary>全局实例；不存在时在播放模式下自动创建一个。</summary>
    public static ImpactFx Instance
    {
        get
        {
            if (instance == null && Application.isPlaying) instance = CreateInstance();
            return instance;
        }
    }

    private sealed class DustRing
    {
        public Transform Tr;
        public Material Mat;
        public float Life;
        public float Duration;
        public Color Color;
    }

    private static ImpactFx CreateInstance()
    {
        GameObject go = new GameObject("Impact Fx");
        return go.AddComponent<ImpactFx>();
    }

    private void Awake()
    {
        if (instance == null) instance = this;
        if (sparks == null) sparks = CreateSparks();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (flashLight != null && flashTimer > 0f)
        {
            flashTimer -= dt;
            float t = Mathf.Clamp01(flashTimer / Mathf.Max(0.01f, flashDuration));
            flashLight.intensity = flashPeak * t;
            if (flashTimer <= 0f) flashLight.enabled = false;
        }

        for (int i = rings.Count - 1; i >= 0; i--)
        {
            DustRing ring = rings[i];
            ring.Life -= dt;
            if (ring.Tr == null || ring.Life <= 0f)
            {
                DestroyRing(ring);
                rings.RemoveAt(i);
                continue;
            }
            float t = 1f - Mathf.Clamp01(ring.Life / ring.Duration);
            float radius = Mathf.Lerp(0.3f, dustRadius, t);
            ring.Tr.localScale = new Vector3(radius, Mathf.Max(0.01f, dustHeight), radius);
            if (ring.Mat != null)
            {
                Color color = ring.Color;
                color.a = ring.Color.a * (1f - t);
                ring.Mat.color = color;
            }
        }
    }

    /// <summary>播一次命中特效；damage 用于决定是否追加地面灰尘环。</summary>
    public static void Spawn(Vector3 hitPoint, Vector3 hitDirection, int damage, Health target)
    {
        ImpactFx fx = Instance;
        if (fx == null) return;
        fx.PlaySparks(hitPoint);
        fx.PlayFlash(hitPoint);
        if (damage >= fx.dustDamageThreshold) fx.PlayDustRing(hitPoint);
    }

    private ParticleSystem CreateSparks()
    {
        GameObject go = new GameObject("Impact Sparks");
        go.transform.SetParent(transform, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop();

        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startColor = sparkColor;
        main.startSpeed = sparkSpeed;
        main.startSize = sparkSize;
        main.startLifetime = sparkLife;
        main.gravityModifier = 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.burstCount = 1;
        int count = Mathf.Max(1, sparkCount);
        emission.SetBurst(0, new ParticleSystem.Burst(0f, count));

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = sparkRadius;

        ParticleSystem.ColorOverLifetimeModule colorOverLife = system.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(sparkColor, 0f), new GradientColorKey(sparkColor * 0.4f, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;

        return system;
    }

    private void PlaySparks(Vector3 point)
    {
        if (sparks == null) sparks = CreateSparks();
        if (sparks == null) return;
        sparks.transform.position = point;
        sparks.Play();
    }

    private void PlayFlash(Vector3 point)
    {
        if (flashLight == null)
        {
            GameObject go = new GameObject("Impact Flash");
            go.transform.SetParent(transform, false);
            flashLight = go.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = sparkColor;
            flashLight.shadows = LightShadows.None;
            flashLight.range = flashRange;
        }

        flashLight.transform.position = point;
        flashLight.range = flashRange;
        flashLight.color = sparkColor;
        flashLight.intensity = flashIntensity;
        flashLight.enabled = true;
        flashPeak = flashIntensity;
        flashTimer = Mathf.Max(0.01f, flashDuration);
    }

    private void PlayDustRing(Vector3 point)
    {
        Vector3 center = point;
        if (Physics.Raycast(point + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            center = hit.point;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        go.name = "Impact Dust Ring";
        go.transform.position = center + Vector3.up * (dustHeight * 0.5f);

        Material material = CreateTransparentMaterial(dustColor);
        if (material == null)
        {
            Destroy(go);
            return;
        }
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.transform.localScale = new Vector3(0.3f, Mathf.Max(0.01f, dustHeight), 0.3f);

        DustRing ring = new DustRing();
        ring.Tr = go.transform;
        ring.Mat = material;
        ring.Color = dustColor;
        ring.Duration = Mathf.Max(0.05f, dustDuration);
        ring.Life = ring.Duration;
        rings.Add(ring);
    }

    private void DestroyRing(DustRing ring)
    {
        if (ring.Tr != null) Destroy(ring.Tr.gameObject);
        if (ring.Mat != null) Destroy(ring.Mat);
    }

    private static Material CreateTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        Material material = new Material(shader);
        material.color = color;
        material.renderQueue = 3000;
        return material;
    }
}
