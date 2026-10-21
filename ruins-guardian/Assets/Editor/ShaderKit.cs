using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 风格化材质工厂：全工程获取美术材质的唯一入口（契约见 Docs/UPGRADE_SPEC.md 5.2）。
/// 所有材质都落盘为 Assets/Generated/Materials/ 下的 .mat 资源，不返回运行时临时材质。
/// 自定义 shader 找不到时记录错误并回退 Standard，保证永不返回 null。
/// </summary>
public static class ShaderKit
{
    private const string GeneratedFolder = "Assets/Generated";
    private const string MaterialFolder = GeneratedFolder + "/Materials";

    private const string StoneShaderName = "Ruins/StylizedStone";
    private const string CharacterShaderName = "Ruins/StylizedCharacter";
    private const string EmissiveShaderName = "Ruins/Emissive";
    private const string FoliageShaderName = "Ruins/Foliage";
    private const string MetalShaderName = "Ruins/Metal";
    private const string FallbackShaderName = "Standard";

    // 调色板（规范第 2 节，禁止自创色）
    private static readonly Color ColdShadow = Hex("#2E3A5C");
    private static readonly Color MetalBase = Hex("#4A5F8C");
    private static readonly Color MossGreen = Hex("#5F7A4A");

    /// <summary>石材：反照率 × 色调A，法线 + 细节噪声混入色调B，三阶卡通明暗 + 冷阴影 + 湿润高光。</summary>
    public static Material Stone(string name, Texture2D albedo, Texture2D normal, Color tintA, Color tintB, float tiling = 4f, float roughness = 0.85f)
    {
        Material m = CreateMaterial(name, StoneShaderName);

        SetTexture(m, "_MainTex", albedo);
        SetTexture(m, "_NormalMap", normal);
        SetColor(m, "_TintA", tintA);
        SetColor(m, "_TintB", tintB);
        SetColor(m, "_ShadowColor", ColdShadow);
        SetFloat(m, "_Tiling", Mathf.Max(0.01f, tiling));
        SetFloat(m, "_Roughness", Mathf.Clamp01(roughness));
        SetFloat(m, "_Wetness", 0f);
        SetFloat(m, "_DetailStrength", 0.45f);
        SetFloat(m, "_DetailTiling", 1.5f);
        SetFloat(m, "_Band1", 0.25f);
        SetFloat(m, "_Band2", 0.55f);
        SetFloat(m, "_AmbientStrength", 1f);
        SetFloat(m, "_SpecPower", Mathf.Lerp(64f, 8f, Mathf.Clamp01(roughness)));
        SetFloat(m, "_SpecIntensity", 0.25f);
        SetFloat(m, "_VertexColorAmount", 0f);

        Finish(m);
        return m;
    }

    /// <summary>金属：高光 + unity_SpecCube0 环境反射 + 法线，暗部走冷色。</summary>
    public static Material Metal(string name, Texture2D normal)
    {
        Material m = CreateMaterial(name, MetalShaderName);

        SetTexture(m, "_NormalMap", normal);
        SetColor(m, "_Color", MetalBase);
        SetColor(m, "_FresnelColor", MetalBase);
        SetColor(m, "_ShadowColor", ColdShadow);
        SetFloat(m, "_Tiling", 2f);
        SetFloat(m, "_NormalStrength", 1f);
        SetFloat(m, "_Smoothness", 0.7f);
        SetFloat(m, "_SpecPower", 48f);
        SetFloat(m, "_SpecIntensity", 1.6f);
        SetFloat(m, "_ReflectionStrength", 0.9f);
        SetFloat(m, "_DiffuseStrength", 0.35f);
        SetFloat(m, "_FresnelPower", 3f);

        Finish(m);
        return m;
    }

    /// <summary>角色：三阶卡通明暗 + 冷环境 + 菲涅尔边缘光 + 受击闪白 + 屏幕等宽描边（第二 Pass）。</summary>
    public static Material Character(string name, Color bodyColor, Color trimColor, Color rimColor, float outlineWidth = 0.02f)
    {
        Material m = CreateMaterial(name, CharacterShaderName);

        SetColor(m, "_Color", bodyColor);
        SetColor(m, "_TrimColor", trimColor);
        SetColor(m, "_RimColor", rimColor);
        SetColor(m, "_ShadowColor", ColdShadow);
        // 描边色由冷阴影色压暗得到，避免引入调色板外的颜色
        SetColor(m, "_OutlineColor", new Color(ColdShadow.r * 0.35f, ColdShadow.g * 0.35f, ColdShadow.b * 0.35f, 1f));
        SetFloat(m, "_TrimStrength", 0.35f);
        SetFloat(m, "_RimPower", 3.2f);
        SetFloat(m, "_RimIntensity", 0.85f);
        SetFloat(m, "_RampEdge", 0.45f);
        SetFloat(m, "_RampHiEdge", 0.62f);
        SetFloat(m, "_Darken", 0.45f);
        SetFloat(m, "_AmbientStrength", 1f);
        SetFloat(m, "_Flash", 0f);
        SetFloat(m, "_OutlineWidth", Mathf.Clamp(outlineWidth, 0f, 0.2f));
        SetFloat(m, "_VertexColorAmount", 0f);

        Finish(m);
        return m;
    }

    /// <summary>自发光：HDR 输出（可触发 Bloom）+ 菲涅尔边缘 + 正弦脉动。</summary>
    public static Material Emissive(string name, Color color, float emission)
    {
        Material m = CreateMaterial(name, EmissiveShaderName);

        SetColor(m, "_Color", color);
        SetColor(m, "_Emission", color);
        SetColor(m, "_ShadowColor", ColdShadow);
        SetFloat(m, "_EmissionIntensity", Mathf.Max(0f, emission));
        SetFloat(m, "_FresnelPower", 2.5f);
        SetFloat(m, "_FresnelBoost", 1.2f);
        SetFloat(m, "_PulseSpeed", 1.2f);
        SetFloat(m, "_PulseAmount", 0.3f);
        SetFloat(m, "_Band1", 0.3f);
        SetFloat(m, "_Band2", 0.62f);
        SetFloat(m, "_AmbientStrength", 1f);

        Finish(m);
        return m;
    }

    /// <summary>植被：Alpha 裁切 + 双面 + 顶点风摆 + 卡通明暗。</summary>
    public static Material Foliage(string name, Texture2D albedo, float cutoff = 0.4f)
    {
        Material m = CreateMaterial(name, FoliageShaderName);

        SetTexture(m, "_MainTex", albedo);
        SetColor(m, "_Color", MossGreen);
        SetColor(m, "_ShadowColor", ColdShadow);
        SetFloat(m, "_Cutoff", Mathf.Clamp01(cutoff));
        SetFloat(m, "_WindStrength", 0.35f);
        SetFloat(m, "_WindSpeed", 1.6f);
        SetFloat(m, "_WindFrequency", 1.5f);
        SetFloat(m, "_TintVariance", 0.3f);
        SetFloat(m, "_Band1", 0.3f);
        SetFloat(m, "_Band2", 0.62f);
        SetFloat(m, "_AmbientStrength", 1f);

        Finish(m);
        return m;
    }

    // ------------------------------------------------------------------
    // 内部工具
    // ------------------------------------------------------------------

    /// <summary>取 shader（带回退），再在 Generated/Materials 下取出或新建同名材质资源。</summary>
    private static Material CreateMaterial(string name, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("[ShaderKit] 找不到 shader \"" + shaderName + "\"，材质 " + name + " 回退到 Standard。请确认 Assets/Shaders 下的 .shader 已编译通过。", null);
            shader = Shader.Find(FallbackShaderName);
        }
        if (shader == null)
        {
            // 连 Standard 都没有属于工程异常，仍然保证不返回 null
            Debug.LogError("[ShaderKit] 连 Standard shader 都找不到，材质 " + name + " 无法创建正确外观。", null);
            shader = Shader.Find("Hidden/InternalErrorShader");
        }

        EnsureFolder();

        string fileName = SanitizeName(name);
        string path = MaterialFolder + "/" + fileName + ".mat";

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            material.name = fileName;
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            // 复用已有资源，保持 GUID 不变，避免场景引用断开
            material.shader = shader;
            material.name = fileName;
        }

        return material;
    }

    private static void Finish(Material m)
    {
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedFolder))
        {
            AssetDatabase.CreateFolder("Assets", "Generated");
        }
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder(GeneratedFolder, "Materials");
        }
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "GeneratedMaterial";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            for (int j = 0; j < invalid.Length; j++)
            {
                if (chars[i] == invalid[j])
                {
                    chars[i] = '_';
                    break;
                }
            }
        }

        string result = new string(chars).Trim();
        return string.IsNullOrEmpty(result) ? "GeneratedMaterial" : result;
    }

    // Standard 上没有这些属性，回退时必须静默跳过，不能抛异常也不能刷警告
    private static void SetColor(Material m, string property, Color value)
    {
        if (m.HasProperty(property)) m.SetColor(property, value);
    }

    private static void SetFloat(Material m, string property, float value)
    {
        if (m.HasProperty(property)) m.SetFloat(property, value);
    }

    private static void SetTexture(Material m, string property, Texture2D value)
    {
        if (value != null && m.HasProperty(property)) m.SetTexture(property, value);
    }

    private static Color Hex(string hex)
    {
        Color color;
        if (ColorUtility.TryParseHtmlString(hex, out color)) return color;
        return Color.white;
    }
}
