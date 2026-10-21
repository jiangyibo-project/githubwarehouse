using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 运行期字体方案：把全部 UGUI Text 统一切换到系统清晰中文字体
/// （优先 微软雅黑 Microsoft YaHei，其次 黑体 SimHei / 等线 DengXian / 宋体），
/// 并适当加大字号、给亮色文字补深色描边，保证深色背景上的中文清晰可读。
/// 编辑器里生成场景时用的是内置 LegacyRuntime.ttf（资源引用可序列化），
/// 只有运行时才换成系统字体，避免把运行时 Font 写进场景导致丢引用。
/// </summary>
public static class UiFonts
{
    private const int BaseSize = 40;
    private static float sizeScale = 1.1f;

    private static Font font;
    private static string sourceName = "未初始化";
    private static bool loaded;

    /// <summary>优先尝试的系统字体族（按顺序）。</summary>
    private static readonly string[] preferred =
    {
        "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑",
        "SimHei", "黑体", "DengXian", "等线", "Source Han Sans SC", "思源黑体",
        "SimSun", "宋体", "NSimSun"
    };

    /// <summary>运行期使用的字体（首次访问时解析并缓存）。</summary>
    public static Font Font
    {
        get { EnsureLoaded(); return font; }
    }

    /// <summary>最终选中的字体来源说明，用于日志与回报。</summary>
    public static string SourceName
    {
        get { EnsureLoaded(); return sourceName; }
    }

    /// <summary>字号放大倍率（只在 1..3 之间生效）。</summary>
    public static float SizeScale
    {
        get { return sizeScale; }
        set { sizeScale = Mathf.Clamp(value, 1f, 3f); }
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        string matched = FindInstalledFont();
        if (!string.IsNullOrEmpty(matched))
        {
            font = Font.CreateDynamicFontFromOSFont(matched, BaseSize);
            if (font != null) sourceName = "系统字体：" + matched;
        }

        if (font == null)
        {
            // 回退链：让 Unity 自己在候选里挑一个可用的
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "DengXian", "Arial" }, BaseSize);
            if (font != null) sourceName = "系统字体回退链：Microsoft YaHei / SimHei / DengXian / Arial";
        }

        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { font = null; }
            sourceName = font != null ? "内置 LegacyRuntime.ttf（系统字体不可用）" : "无可用字体";
        }

        Debug.Log("[UI] 字体：" + sourceName + "，字号倍率 " + sizeScale.ToString("0.00"));
    }

    private static string FindInstalledFont()
    {
        string[] installed = null;
        try { installed = Font.GetOSInstalledFontNames(); }
        catch { installed = null; }
        if (installed == null || installed.Length == 0) return null;

        for (int i = 0; i < preferred.Length; i++)
        {
            // 先精确匹配（忽略大小写），再退化为包含匹配
            for (int j = 0; j < installed.Length; j++)
            {
                if (string.Equals(installed[j], preferred[i], System.StringComparison.OrdinalIgnoreCase))
                    return installed[j];
            }
            for (int j = 0; j < installed.Length; j++)
            {
                if (!string.IsNullOrEmpty(installed[j]) &&
                    installed[j].IndexOf(preferred[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return installed[j];
            }
        }
        return null;
    }

    /// <summary>把场景里所有 Text（含未激活对象）统一处理一遍。</summary>
    public static void ApplyAll()
    {
        EnsureLoaded();
        Text[] texts = Object.FindObjectsOfType<Text>(true);
        for (int i = 0; i < texts.Length; i++) Polish(texts[i]);
    }

    /// <summary>单个 Text：换字体 + 放大字号 + 需要时补深色描边。</summary>
    public static void Polish(Text text)
    {
        if (text == null) return;
        if (font != null) text.font = font;
        BumpSize(text);
        EnsureContrast(text);
    }

    private static void BumpSize(Text text)
    {
        int size = text.fontSize;
        if (size <= 0 || size >= 56) return; // 大标题不再放大，避免超框
        int bumped = Mathf.CeilToInt(size * sizeScale);
        if (bumped <= size) bumped = size + 1;
        text.fontSize = bumped;
    }

    private static void EnsureContrast(Text text)
    {
        // Outline 是 Shadow 的子类，已经有效果的不要再叠
        if (text.GetComponent<Shadow>() != null) return;

        Color color = text.color;
        float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
        if (luminance < 0.35f) return; // 深色字（例如金底按钮上的近黑字）不需要再加描边

        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.82f);
        outline.effectDistance = new Vector2(1.1f, -1.1f);
        outline.useGraphicAlpha = true;
    }
}
