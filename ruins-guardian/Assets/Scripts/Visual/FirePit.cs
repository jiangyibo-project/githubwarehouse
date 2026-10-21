using UnityEngine;

/// <summary>
/// 遗迹火盆：程序化生成火焰与火星粒子，点光源强度按多频噪声闪烁。
/// 只依赖 UnityEngine，不引用任何音频模块（音效由 T7 统一接入）。
/// 粒子与光源全部在 EnsureBuilt 中创建，因此既能在编辑期（生成器调用 Configure）搭好，
/// 也能在运行时（Awake）自愈补建。
/// </summary>
[DisallowMultipleComponent]
public sealed class FirePit : MonoBehaviour
{
    [SerializeField] private Light fireLight;
    [SerializeField] private ParticleSystem flameParticles;
    [SerializeField] private ParticleSystem sparkParticles;
    [SerializeField] private Material flameMaterial;
    [SerializeField] private float baseIntensity = 2.4f;
    [SerializeField] private float lightRange = 7.5f;
    [SerializeField] private float flameScale = 1f;
    [SerializeField] private int seed = 1;

    /// <summary>火焰基色（规范 5.2 火光 #FFB066）与内焰色 #FF7A3C。</summary>
    private static readonly Color FlameOuter = new Color(1f, 0.427f, 0.235f);
    private static readonly Color FlameInner = new Color(1f, 0.690f, 0.400f);
    private static readonly Color LightColor = new Color(1f, 0.62f, 0.28f);

    /// <summary>
    /// 由场景生成器在编辑期调用：写入外观参数并立即搭好光、火焰与火星。
    /// </summary>
    /// <param name="material">加性火焰材质（生成器统一创建，避免运行时 Shader.Find）。</param>
    /// <param name="intensity">点光基础强度，规范推荐 2.0-3.0。</param>
    /// <param name="range">点光范围，规范推荐 6-9。</param>
    /// <param name="scale">火焰整体缩放。</param>
    /// <param name="randomSeed">相位与粒子随机种子。</param>
    public void Configure(Material material, float intensity, float range, float scale, int randomSeed)
    {
        flameMaterial = material;
        baseIntensity = Mathf.Max(0f, intensity);
        lightRange = Mathf.Max(0.5f, range);
        flameScale = Mathf.Max(0.1f, scale);
        seed = randomSeed;
        EnsureBuilt();
    }

    /// <summary>运行时调整点光基础强度（例如关卡氛围变化）。</summary>
    public void SetBaseIntensity(float intensity)
    {
        baseIntensity = Mathf.Max(0f, intensity);
    }

    private void Awake()
    {
        EnsureBuilt();
    }

    private void Update()
    {
        if (fireLight == null) return;

        // 三层不同频率的噪声叠加，避免火焰节奏机械
        float time = Time.time * 6.5f + seed * 1.37f;
        float slow = Mathf.PerlinNoise(time * 0.6f, 0.31f);
        float fast = Mathf.PerlinNoise(time * 2.3f, 7.71f);
        float micro = Mathf.PerlinNoise(time * 5.7f, 13.19f);
        float noise = slow * 0.5f + fast * 0.32f + micro * 0.18f;

        fireLight.intensity = baseIntensity * (0.78f + noise * 0.45f);
        fireLight.color = Color.Lerp(LightColor * 0.82f, new Color(1f, 0.76f, 0.42f), noise);

        // 光源轻微游走，让影子边缘也活起来
        fireLight.transform.localPosition = new Vector3(
            (Mathf.PerlinNoise(time * 1.7f, 3.3f) - 0.5f) * 0.06f,
            0.02f + (Mathf.PerlinNoise(time * 2.1f, 5.9f) - 0.5f) * 0.04f,
            (Mathf.PerlinNoise(time * 1.3f, 9.1f) - 0.5f) * 0.06f);
    }

    /// <summary>幂等的搭建：缺少哪一个就补哪一个，供编辑期与运行时共用。</summary>
    private void EnsureBuilt()
    {
        Material material = ResolveFlameMaterial();

        if (fireLight == null)
        {
            Light existing = GetComponentInChildren<Light>(true);
            if (existing != null)
            {
                fireLight = existing;
            }
            else
            {
                GameObject lightObject = new GameObject("火焰光");
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 0.35f * flameScale, 0f);
                Light created = lightObject.AddComponent<Light>();
                created.type = LightType.Point;
                created.color = LightColor;
                created.range = lightRange;
                created.intensity = baseIntensity;
                // 点光阴影开销高，六盏火盆一律不开阴影，明暗层次交给主方向光与 AO
                created.shadows = LightShadows.None;
                fireLight = created;
            }
        }

        fireLight.range = lightRange;
        fireLight.intensity = baseIntensity;

        if (flameParticles == null)
        {
            flameParticles = BuildFlameParticles(material);
        }

        if (sparkParticles == null)
        {
            sparkParticles = BuildSparkParticles(material);
        }
    }

    private Material ResolveFlameMaterial()
    {
        if (flameMaterial != null) return flameMaterial;

        // 兜底：编辑器生成时应始终传入材质；这里只在缺失时退到内置加性粒子着色器
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        flameMaterial = new Material(shader);
        flameMaterial.name = "FirePitRuntimeFlame";
        return flameMaterial;
    }

    private ParticleSystem BuildFlameParticles(Material material)
    {
        GameObject go = new GameObject("火焰粒子");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.28f * flameScale, 0f);
        go.transform.localScale = Vector3.one * flameScale;

        ParticleSystem particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.34f, 0.62f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(FlameInner, FlameOuter);
        main.gravityModifier = -0.06f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 90;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(28f);

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = 0.12f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, FadeCurve(0.55f, 1f, 0.08f));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FlameGradient());

        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = particles.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-1.1f, 1.1f);

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.22f;
        noise.frequency = 1.1f;
        noise.scrollSpeed = 0.5f;
        noise.damping = true;

        ConfigureRenderer(particles, material, 1f);
        particles.Play();
        return particles;
    }

    private ParticleSystem BuildSparkParticles(Material material)
    {
        GameObject go = new GameObject("火星粒子");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.34f * flameScale, 0f);

        ParticleSystem particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 1f;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
        main.startColor = new ParticleSystem.MinMaxGradient(FlameInner, FlameOuter);
        main.gravityModifier = 0.28f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        // 偶尔爆一小簇火星，而不是稳定喷发
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0.0f, 1, 3, 1, 1.35f),
            new ParticleSystem.Burst(0.45f, 0, 2, 1, 1.6f)
        });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 14f;
        shape.radius = 0.1f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, FadeCurve(1f, 0.8f, 0.1f));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FlameGradient());

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.8f;

        ConfigureRenderer(particles, material, 1.6f);
        particles.Play();
        return particles;
    }

    private static void ConfigureRenderer(ParticleSystem particles, Material material, float sortingFudge)
    {
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) return;

        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = material;
        renderer.sortingFudge = sortingFudge;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>颜色曲线：白 → 白（颜色由 startColor 决定），alpha 先亮后灭。</summary>
    private static Gradient FlameGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.92f, 0.72f), 0.45f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.12f),
                new GradientAlphaKey(0.55f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    private static AnimationCurve FadeCurve(float start, float peak, float end)
    {
        AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, start),
            new Keyframe(0.3f, peak),
            new Keyframe(1f, end));
        curve.SmoothTangents(1, 0f);
        return curve;
    }
}
