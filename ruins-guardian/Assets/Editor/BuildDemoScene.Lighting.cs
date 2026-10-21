using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// T2 光照与后处理：暖主光 + 冷补光 + 三层环境光 + 指数雾，
/// 后处理为 ACES / Bloom / 色分级 / 暗角 / 颗粒 / 色差 / AO 组合。
/// 数值取向：高饱和 + 强对比 + 暖冷对撞（哈迪斯 / 暖雪 式），不是"明亮发灰"。
/// 注：工程当前是 Gamma 色彩空间，所有数值都按 Gamma 观感标定。
/// </summary>
public static partial class BuildDemoScene
{
    // 环境基调：1.2 → 0.9。tint 修复后石材反照率已提高约 47%，不再需要叠环境光
    private const float V2AmbientIntensity = 0.9f;
    // 雾加厚到 0.027：远景 30m 外迅速褪成雾色，40m 外基本只剩轮廓，70m 外完全没入雾里
    private const float V2FogDensity = 0.027f;

    // 冷色补光：只把朝向镜头的背光面从"死黑"提到"看得清"，不再参与整体提亮
    private const float V2FillIntensity = 0.85f;

    // 补光只负责把敌人与背光面照亮，颜色仍取冷蓝 #4A6BB0（不往紫偏）
    // 冷色对撞的目标色（规范第 2 节冷阴影族）
    private static readonly Color V2AmbientSkyCool = new Color(0.165f, 0.227f, 0.400f); // ≈ #2A3A66
    private static readonly Color V2FillColor = new Color(0.300f, 0.430f, 0.700f);      // ≈ #4A6BB0，偏蓝不发紫

    /// <summary>
    /// 环境总配置：天空盒、三层环境光、指数雾、暖主方向光、反向冷补光。
    /// 取代原来的 ConfigureEnvironment(skybox)。
    /// </summary>
    internal static void ConfigureEnvironmentV2()
    {
        ConfigureSkyboxV2();
        ConfigureAmbientV2();
        ConfigureFogV2();
        BuildSunV2();
        BuildFillLightV2();
    }

    private static void ConfigureSkyboxV2()
    {
        string path = Root + "/RuinsSkyboxV2.mat";
        AssetDatabase.DeleteAsset(path);

        Shader shader = Shader.Find("Skybox/Procedural");
        if (shader == null)
        {
            Debug.LogWarning("找不到 Skybox/Procedural，跳过天空盒创建。");
            return;
        }

        Material skybox = new Material(shader) { name = "RuinsSkyboxV2" };
        skybox.SetColor("_SkyTint", V2ShadeBlue * 0.85f);          // 冷色天空
        skybox.SetColor("_GroundColor", new Color(0.2f, 0.17f, 0.16f));
        // 曝光再降一档到 0.85、太阳盘进一步缩小：读作暮色，而不是画面顶部的一条亮黄横条
        skybox.SetFloat("_AtmosphereThickness", 1.3f);
        skybox.SetFloat("_Exposure", 0.85f);
        skybox.SetFloat("_SunSize", 0.016f);
        skybox.SetFloat("_SunSizeConvergence", 6f);
        AssetDatabase.CreateAsset(skybox, path);

        RenderSettings.skybox = skybox;
    }

    private static void ConfigureAmbientV2()
    {
        // 三层环境光：天空冷 #2E3A5C~#4A5F8C、赤道中性、地面暖。
        // Trilight 模式下 ambientIntensity 不参与运算，因此把强度直接折进颜色。
        RenderSettings.ambientMode = AmbientMode.Trilight;
        // 天空段直接取 #2A3A66 并 ×1.35 提亮一档：冷色阴影靠这一段撑起来
        RenderSettings.ambientSkyColor = V2AmbientSkyCool * (V2AmbientIntensity * 1.35f);
        RenderSettings.ambientEquatorColor = new Color(0.48f, 0.47f, 0.48f) * V2AmbientIntensity;
        RenderSettings.ambientGroundColor = new Color(0.30f, 0.26f, 0.21f) * V2AmbientIntensity;
        RenderSettings.ambientIntensity = V2AmbientIntensity;
        RenderSettings.reflectionIntensity = 0.45f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
    }

    private static void ConfigureFogV2()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = V2FogDensity;
        // 雾色偏冷但比之前提亮一档：远处应像"空气里的尘雾"，不能变成一堵黑墙
        // 雾色转青（0.36,0.42,0.52）：暖金高光之外，远景读作"雾中的遗迹"
        RenderSettings.fogColor = new Color(0.36f, 0.42f, 0.52f);
    }

    /// <summary>暖色主方向光：仰角 42°，强度 1.5，软阴影，shadowStrength 0.86。</summary>
    private static void BuildSunV2()
    {
        GameObject lightObject = new GameObject("主方向光 暖");
        Light sun = lightObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        // 强度 2.0 → 1.35 → 1.50 折中：1.35 时庭院内部偏深蓝、与墙顶暖金割裂；
        // 1.50 下钢甲红通道峰值约 1.28，仍在 ACES 削顶之前，白盔甲不会再次糊成白板。
        sun.color = new Color(1f, 0.78f, 0.50f);
        sun.intensity = 1.50f;
        sun.shadows = LightShadows.Soft;
        // 阴影更实，明暗交界才拉得开
        sun.shadowStrength = 0.86f;
        sun.shadowBias = 0.03f;
        sun.shadowNormalBias = 0.35f;
        sun.shadowNearPlane = 0.2f;
        // 太阳在北侧偏东：42° 仰角让庭院地面吃满主光，同时保留高台与回廊的长影
        lightObject.transform.rotation = Quaternion.Euler(42f, 158f, 0f);

        RenderSettings.sun = sun;
    }

    /// <summary>与主光反向的冷蓝补光，提亮阴影面，不投影。</summary>
    private static void BuildFillLightV2()
    {
        GameObject lightObject = new GameObject("冷色补光");
        Light fill = lightObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        // 冷蓝 #4A6BB0，不混白（混白会丢掉冷色），也不往品红偏
        fill.color = V2FillColor;
        fill.intensity = V2FillIntensity;
        fill.shadows = LightShadows.None;
        // 从南偏东 30° 打回来：正对镜头一侧的墙面与高台正面不再死黑
        lightObject.transform.rotation = Quaternion.Euler(30f, -26f, 0f);
    }

    // =====================================================================
    // 后处理（规范第 4 节）
    // =====================================================================

    /// <summary>
    /// 把 Profile 升级为 RuinsGuardian 的电影感组合。
    /// 已存在的效果直接改数值；缺的效果补进来（Profile 已是资源时同时登记为子资源）。
    /// </summary>
    internal static void UpgradePostProcess(PostProcessProfile profile)
    {
        if (profile == null)
        {
            Debug.LogWarning("UpgradePostProcess：Profile 为空，跳过后处理升级。");
            return;
        }

        bool isAsset = AssetDatabase.Contains(profile);

        // ---------- 环境光遮蔽：只压结构缝隙，不把石块整体压黑 ----------
        AmbientOcclusion ao = GetOrAddEffectV2<AmbientOcclusion>(profile, isAsset);
        ao.enabled.Override(true);
        ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
        ao.intensity.Override(0.7f);
        ao.thicknessModifier.Override(2.0f);
        ao.ambientOnly.Override(true);
        ao.quality.Override(AmbientOcclusionQuality.High);
        ao.color.Override(new Color(0.18f, 0.15f, 0.22f));

        // ---------- 泛光：只让火光、符文、宝石溢出 ----------
        // intensity 7 → 2.8（正常区间 1~3），threshold 1 → 1.14：
        // 近景 12.7% 像素过 235 主要是"高光区被泛光再糊一层"，把阈值再抬高收窄来源
        Bloom bloom = GetOrAddEffectV2<Bloom>(profile, isAsset);
        bloom.enabled.Override(true);
        bloom.intensity.Override(2.8f);
        bloom.threshold.Override(1.14f);
        bloom.softKnee.Override(0.6f);
        bloom.diffusion.Override(7f);
        bloom.anamorphicRatio.Override(0f);
        bloom.color.Override(new Color(1f, 0.86f, 0.72f));
        bloom.fastMode.Override(false);

        // 自动曝光在固定光比的场景里只会让画面飘，强制关闭
        if (profile.TryGetSettings<AutoExposure>(out AutoExposure exposure))
            exposure.enabled.Override(false);

        // ---------- 色彩分级：ACES + 高饱和 + 强对比 ----------
        ColorGrading grading = GetOrAddEffectV2<ColorGrading>(profile, isAsset);
        grading.enabled.Override(true);
        grading.gradingMode.Override(GradingMode.HighDefinitionRange);
        grading.tonemapper.Override(Tonemapper.ACES);
        // 暖金电影感：温度 4 → 14（暖），曝光 0.50 → 0.44（近景高光不再堆爆），
        // 饱和 22 → 30、对比 17 → 22（色块更浓、明暗更硬）
        grading.temperature.Override(14f);
        grading.tint.Override(-2f);
        grading.postExposure.Override(0.44f);
        grading.contrast.Override(22f);
        grading.saturation.Override(30f);
        // lift 的蓝偏收掉一半（原 1.12 的蓝只留 1.06，地面不再发蓝），
        // gain 高光加暖（1.14/1.03/0.88）：高光走暖金、阴影留一点点蓝
        grading.lift.Override(new Vector4(0.99f, 1.00f, 1.06f, 0f));
        grading.gain.Override(new Vector4(1.14f, 1.03f, 0.88f, 0f));

        // ---------- 暗角：重新给边缘一点压暗，回收构图 ----------
        Vignette vignette = GetOrAddEffectV2<Vignette>(profile, isAsset);
        vignette.enabled.Override(true);
        vignette.mode.Override(VignetteMode.Classic);
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.6f);
        vignette.roundness.Override(1f);
        vignette.rounded.Override(true);
        vignette.color.Override(new Color(0.09f, 0.08f, 0.14f));

        // ---------- 胶片颗粒（PPSv2 里叫 Grain） ----------
        Grain grain = GetOrAddEffectV2<Grain>(profile, isAsset);
        grain.enabled.Override(true);
        grain.colored.Override(false);
        grain.intensity.Override(0.15f);
        grain.size.Override(1.15f);
        grain.lumContrib.Override(0.7f);

        // ---------- 色差：边缘轻微分离，复古镜头感 ----------
        ChromaticAberration aberration = GetOrAddEffectV2<ChromaticAberration>(profile, isAsset);
        aberration.enabled.Override(true);
        aberration.intensity.Override(0.06f);
        aberration.fastMode.Override(true);

        if (isAsset)
        {
            // 新增的效果要作为子资源写进 .asset，否则重新加载场景后会丢失
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>取或补一个后处理效果；Profile 已是资源时把新增效果登记成子资源。</summary>
    private static T GetOrAddEffectV2<T>(PostProcessProfile profile, bool isAsset) where T : PostProcessEffectSettings
    {
        if (profile.TryGetSettings<T>(out T existing)) return existing;

        T added = profile.AddSettings<T>();
        if (isAsset) AssetDatabase.AddObjectToAsset(added, profile);
        return added;
    }
}
