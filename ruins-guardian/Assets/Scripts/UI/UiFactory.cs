using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 程序化 UGUI 工具箱（运行时版）。
/// 用代码生成并缓存纯色 / 圆角 / 描边 / 斜切 / 圆环 / 菱形 / 渐变 / 暗角等 Sprite，
/// 并提供 Image、Text、Button、Slider 的工厂方法与 RectTransform 布局辅助。
/// 像素生成与 Editor 阶段烘焙共用同一张表（CreateTexture + BorderOf），保证两边像素完全一致。
/// 字体使用内置 LegacyRuntime.ttf，绝不依赖 TextMeshPro。
/// </summary>
public static class UiFactory
{
    // ==================== 调色板（规范第 2 节，禁止自创色） ====================

    /// <summary>暗底。</summary>
    public static readonly Color Dark = Hex("14161F");
    /// <summary>稍亮的卡片底色。</summary>
    public static readonly Color DarkCard = Hex("1B1E2A");
    /// <summary>暖金：主描边与高亮。</summary>
    public static readonly Color Gold = Hex("FFB066");
    /// <summary>暖橙：热态与火光。</summary>
    public static readonly Color GoldHot = Hex("FF7A3C");
    /// <summary>符文青：点缀与就绪态。</summary>
    public static readonly Color Teal = Hex("46E0C8");
    /// <summary>宝石蓝。</summary>
    public static readonly Color Gem = Hex("3DE0FF");
    /// <summary>血红：危险与受击。</summary>
    public static readonly Color Blood = Hex("D84A3A");
    /// <summary>冷阴影。</summary>
    public static readonly Color Cold = Hex("2E3A5C");
    /// <summary>冷阴影亮部。</summary>
    public static readonly Color ColdLight = Hex("4A5F8C");
    /// <summary>石材本体，用作正文文字色。</summary>
    public static readonly Color Stone = Hex("C8C0AE");
    /// <summary>正文亮白（主文字，深色底上对比足）。</summary>
    public static readonly Color TextPrimary = Hex("EFE8DC");
    /// <summary>辅助文字：比冷阴影 #4A5F8C 亮得多，小字也不会糊成灰。</summary>
    public static readonly Color TextDim = Hex("A9B4C6");
    /// <summary>亮青：宝石数值等需要跳出的数字。</summary>
    public static readonly Color TextBright = Hex("BFEFFF");
    /// <summary>按钮文字：近黑暖棕，压在亮金底上对比最高。</summary>
    public static readonly Color ButtonLabel = Hex("1B1206");
    /// <summary>按钮底色：提亮暖金（比 #FFB066 更亮），保证深色文字清晰。</summary>
    public static readonly Color ButtonFace = Hex("FFC078");

    // ==================== Sprite 键 ====================

    public const string KeyWhite = "ui_white";
    public const string KeyRound6 = "ui_round6";
    public const string KeyRound12 = "ui_round12";
    public const string KeyRound16 = "ui_round16";
    public const string KeyRound24 = "ui_round24";
    public const string KeyRound6Frame = "ui_round6_frame";
    public const string KeyRound12Frame = "ui_round12_frame";
    public const string KeyRound16Frame = "ui_round16_frame";
    public const string KeyRound24Frame = "ui_round24_frame";
    public const string KeySlant = "ui_slant";
    public const string KeyRing = "ui_ring";
    public const string KeyRingThin = "ui_ring_thin";
    public const string KeyDisc = "ui_disc";
    public const string KeyRhombus = "ui_rhombus";
    public const string KeyGradientV = "ui_grad_v";
    public const string KeyGradientH = "ui_grad_h";
    public const string KeyGlow = "ui_glow";
    public const string KeyVignette = "ui_vignette";
    public const string KeyCorner = "ui_corner";
    public const string KeyChevron = "ui_chevron";

    private static readonly string[] allKeys =
    {
        KeyWhite, KeyRound6, KeyRound12, KeyRound16, KeyRound24,
        KeyRound6Frame, KeyRound12Frame, KeyRound16Frame, KeyRound24Frame,
        KeySlant, KeyRing, KeyRingThin, KeyDisc, KeyRhombus,
        KeyGradientV, KeyGradientH, KeyGlow, KeyVignette, KeyCorner, KeyChevron
    };

    /// <summary>全部 Sprite 键，Editor 阶段据此烘焙 PNG。</summary>
    public static string[] AllKeys { get { return allKeys; } }

    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private static Font cachedFont;

    // ==================== 颜色辅助 ====================

    /// <summary>把 "RRGGBB" 十六进制串转成不透明色。</summary>
    public static Color Hex(string hex)
    {
        int value = 0;
        for (int i = 0; i < hex.Length && i < 6; i++)
        {
            char c = hex[i];
            int digit;
            if (c >= '0' && c <= '9') digit = c - '0';
            else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
            else digit = 0;
            value = value * 16 + digit;
        }
        return new Color(((value >> 16) & 0xFF) / 255f, ((value >> 8) & 0xFF) / 255f, (value & 0xFF) / 255f, 1f);
    }

    /// <summary>替换 alpha 通道。</summary>
    public static Color Alpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    /// <summary>
    /// 新建文本使用的字体：
    /// 运行时用 UiFonts 解析出的清晰中文字体；编辑器（生成场景）用内置 LegacyRuntime.ttf，
    /// 因为运行时创建的动态字体不是资源，写进场景会丢引用。
    /// </summary>
    public static Font DefaultFont
    {
        get
        {
            if (Application.isPlaying)
            {
                Font runtimeFont = UiFonts.Font;
                if (runtimeFont != null) return runtimeFont;
            }
            return BuiltinFont;
        }
    }

    /// <summary>内置字体（LegacyRuntime.ttf，带 Arial 兜底），生成场景时使用。</summary>
    public static Font BuiltinFont
    {
        get
        {
            if (cachedFont != null) return cachedFont;
            try { cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { cachedFont = null; }
            if (cachedFont == null)
            {
                try { cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
                catch { cachedFont = null; }
            }
            return cachedFont;
        }
    }

    // ==================== 像素生成 ====================

    private delegate bool InsideTest(float x, float y);
    private delegate float FieldTest(float u, float v);

    /// <summary>按 Sprite 键生成纹理（白底 + alpha 覆盖度，交给 Image.color 染色）。</summary>
    public static Texture2D CreateTexture(string key)
    {
        switch (key)
        {
            case KeyWhite:
                return BuildShape(8, 8, delegate (float x, float y) { return true; });

            case KeyRound6:
                return BuildShape(24, 24, delegate (float x, float y) { return InRoundRect(x, y, 24f, 24f, 6f); });
            case KeyRound12:
                return BuildShape(40, 40, delegate (float x, float y) { return InRoundRect(x, y, 40f, 40f, 12f); });
            case KeyRound16:
                return BuildShape(64, 64, delegate (float x, float y) { return InRoundRect(x, y, 64f, 64f, 16f); });
            case KeyRound24:
                return BuildShape(96, 96, delegate (float x, float y) { return InRoundRect(x, y, 96f, 96f, 24f); });

            case KeyRound6Frame:
                return BuildShape(24, 24, delegate (float x, float y) { return InRoundFrame(x, y, 24f, 24f, 6f, 2f); });
            case KeyRound12Frame:
                return BuildShape(40, 40, delegate (float x, float y) { return InRoundFrame(x, y, 40f, 40f, 12f, 2f); });
            case KeyRound16Frame:
                return BuildShape(64, 64, delegate (float x, float y) { return InRoundFrame(x, y, 64f, 64f, 16f, 2f); });
            case KeyRound24Frame:
                return BuildShape(96, 96, delegate (float x, float y) { return InRoundFrame(x, y, 96f, 96f, 24f, 3f); });

            case KeySlant:
                return BuildShape(64, 32, delegate (float x, float y) { return InSlant(x, y, 64f, 32f, 10f); });

            case KeyRing:
                return BuildShape(128, 128, delegate (float x, float y) { return InRing(x, y, 128f, 8f); });
            case KeyRingThin:
                return BuildShape(128, 128, delegate (float x, float y) { return InRing(x, y, 128f, 2.5f); });
            case KeyDisc:
                return BuildShape(128, 128, delegate (float x, float y) { return InDisc(x, y, 128f); });
            case KeyRhombus:
                return BuildShape(64, 64, delegate (float x, float y) { return InRhombus(x, y, 64f, 64f); });

            case KeyGradientV:
                return BuildField(8, 64, delegate (float u, float v) { return v; });
            case KeyGradientH:
                return BuildField(64, 8, delegate (float u, float v) { return 1f - u; });

            case KeyGlow:
                return BuildField(128, 128, delegate (float u, float v)
                {
                    float dx = (u - 0.5f) * 2f;
                    float dy = (v - 0.5f) * 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    return Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                });

            case KeyVignette:
                return BuildField(256, 256, delegate (float u, float v)
                {
                    float dx = (u - 0.5f) * 2f;
                    float dy = (v - 0.5f) * 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 0.72f;
                    return Mathf.Pow(Mathf.Clamp01((r - 0.34f) / 0.6f), 1.5f);
                });

            case KeyCorner:
                return BuildShape(48, 48, delegate (float x, float y) { return x <= 3f || y >= 45f; });

            case KeyChevron:
                return BuildShape(32, 20, delegate (float x, float y)
                {
                    return InTriangle(x, y, new Vector2(2f, 15f), new Vector2(30f, 15f), new Vector2(16f, 4f));
                });
        }
        return BuildShape(8, 8, delegate (float x, float y) { return true; });
    }

    /// <summary>按 Sprite 键返回九宫格边距（Image.type = Sliced 时生效）。</summary>
    public static Vector4 BorderOf(string key)
    {
        switch (key)
        {
            case KeyRound6: return new Vector4(7f, 7f, 7f, 7f);
            case KeyRound12: return new Vector4(13f, 13f, 13f, 13f);
            case KeyRound16: return new Vector4(17f, 17f, 17f, 17f);
            case KeyRound24: return new Vector4(25f, 25f, 25f, 25f);
            case KeyRound6Frame: return new Vector4(8f, 8f, 8f, 8f);
            case KeyRound12Frame: return new Vector4(14f, 14f, 14f, 14f);
            case KeyRound16Frame: return new Vector4(18f, 18f, 18f, 18f);
            case KeyRound24Frame: return new Vector4(27f, 27f, 27f, 27f);
            case KeyCorner: return new Vector4(6f, 6f, 6f, 6f);
        }
        return Vector4.zero;
    }

    /// <summary>Sprite 解析器：Editor 阶段注入，用于把键解析成 Assets/Generated/Ui 下的资源。</summary>
    public delegate Sprite SpriteResolver(string key);

    /// <summary>非空时优先生效（Editor 烘焙期间注入），退出后置回 null。</summary>
    public static SpriteResolver Resolver;

    private static Sprite Resolve(string key)
    {
        if (Resolver != null)
        {
            Sprite injected = Resolver(key);
            if (injected != null) return injected;
        }
        return Get(key);
    }

    /// <summary>取得（首次生成并缓存的）Sprite。</summary>
    public static Sprite Get(string key)
    {
        Sprite cached;
        if (spriteCache.TryGetValue(key, out cached) && cached != null) return cached;
        Texture2D texture = CreateTexture(key);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect, BorderOf(key));
        sprite.name = key;
        spriteCache[key] = sprite;
        return sprite;
    }

    // ---- 形状测试（像素空间，y 轴向上） ----

    private static Texture2D BuildShape(int width, int height, InsideTest test)
    {
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float coverage = 0f;
                if (test(x + 0.25f, y + 0.25f)) coverage += 0.25f;
                if (test(x + 0.75f, y + 0.25f)) coverage += 0.25f;
                if (test(x + 0.25f, y + 0.75f)) coverage += 0.25f;
                if (test(x + 0.75f, y + 0.75f)) coverage += 0.25f;
                pixels[y * width + x] = new Color(1f, 1f, 1f, coverage);
            }
        }
        return Finalize(width, height, pixels);
    }

    private static Texture2D BuildField(int width, int height, FieldTest test)
    {
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(test(u, v)));
            }
        }
        return Finalize(width, height, pixels);
    }

    private static Texture2D Finalize(int width, int height, Color[] pixels)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "UiTexture";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private static bool InRoundRect(float x, float y, float width, float height, float radius)
    {
        float cx = Mathf.Clamp(x, radius, width - radius);
        float cy = Mathf.Clamp(y, radius, height - radius);
        float dx = x - cx;
        float dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool InRoundFrame(float x, float y, float width, float height, float radius, float thickness)
    {
        if (!InRoundRect(x, y, width, height, radius)) return false;
        return !InRoundRect(x - thickness, y - thickness, width - thickness * 2f, height - thickness * 2f,
            Mathf.Max(0.5f, radius - thickness));
    }

    private static bool InSlant(float x, float y, float width, float height, float skew)
    {
        float t = Mathf.Clamp01(y / height);
        float left = skew * t;
        float right = width - skew + skew * t;
        return x >= left && x <= right;
    }

    private static bool InRing(float x, float y, float size, float thickness)
    {
        float dx = x - size * 0.5f;
        float dy = y - size * 0.5f;
        float radius = Mathf.Sqrt(dx * dx + dy * dy);
        float middle = (size - thickness) * 0.5f;
        return Mathf.Abs(radius - middle) <= thickness * 0.5f;
    }

    private static bool InDisc(float x, float y, float size)
    {
        float dx = x - size * 0.5f;
        float dy = y - size * 0.5f;
        return dx * dx + dy * dy <= size * 0.5f * size * 0.5f;
    }

    private static bool InRhombus(float x, float y, float width, float height)
    {
        float u = Mathf.Abs(x - width * 0.5f) / (width * 0.5f);
        float v = Mathf.Abs(y - height * 0.5f) / (height * 0.5f);
        return u + v <= 1f;
    }

    private static bool InTriangle(float px, float py, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (px - b.x) * (a.y - b.y) - (a.x - b.x) * (py - b.y);
        float d2 = (px - c.x) * (b.y - c.y) - (b.x - c.x) * (py - c.y);
        float d3 = (px - a.x) * (c.y - a.y) - (c.x - a.x) * (py - a.y);
        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNegative && hasPositive);
    }

    // ==================== 工厂：控件 ====================

    /// <summary>建立一个空的 RectTransform 容器。</summary>
    public static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>建立 Image（默认关闭射线，交给按钮/面板显式打开）。</summary>
    public static Image CreateImage(RectTransform parent, string name, string spriteKey, Color tint, bool raycast = false)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = Resolve(spriteKey);
        image.color = tint;
        image.raycastTarget = raycast;
        image.type = Image.Type.Simple;
        return image;
    }

    /// <summary>建立九宫格 Image（圆角/描边专用）。</summary>
    public static Image CreateSliced(RectTransform parent, string name, string spriteKey, Color tint, bool raycast = false)
    {
        Image image = CreateImage(parent, name, spriteKey, tint, raycast);
        if (BorderOf(spriteKey) != Vector4.zero) image.type = Image.Type.Sliced;
        return image;
    }

    /// <summary>建立横向填充的 Image（血条/冷却环专用）。</summary>
    public static Image CreateFilled(RectTransform parent, string name, string spriteKey, Color tint, bool radial = false)
    {
        Image image = CreateImage(parent, name, spriteKey, tint);
        image.type = Image.Type.Filled;
        if (radial)
        {
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillClockwise = true;
            image.fillOrigin = 2;
        }
        else
        {
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
        }
        image.fillAmount = 1f;
        return image;
    }

    /// <summary>建立文本（默认不接收射线，行内溢出）。</summary>
    public static Text CreateText(RectTransform parent, string name, string content, int size, Color color,
        TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = DefaultFont;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = true;
        return text;
    }

    /// <summary>给图形加描边（UGUI 内置 Outline，不依赖 TMP）。</summary>
    public static Outline AddOutline(Graphic target, Color color, float distance = 1.5f)
    {
        Outline outline = target.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
        outline.useGraphicAlpha = true;
        return outline;
    }

    /// <summary>给图形加投影。</summary>
    public static Shadow AddShadow(Graphic target, Color color, float distance = 2f)
    {
        Shadow shadow = target.gameObject.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = new Vector2(distance, -distance);
        shadow.useGraphicAlpha = true;
        return shadow;
    }

    /// <summary>建立带悬停缩放反馈的按钮（暖金实心 + 描边 + 文字）。</summary>
    public static Button CreateButton(RectTransform parent, string name, string label, Vector2 size, int fontSize,
        out Text labelText)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;

        Image background = go.GetComponent<Image>();
        background.sprite = Resolve(KeyRound16);
        if (BorderOf(KeyRound16) != Vector4.zero) background.type = Image.Type.Sliced;
        background.color = ButtonFace;
        background.raycastTarget = true;

        Image glow = CreateImage(rect, "Glow", KeyGlow, Alpha(GoldHot, 0.10f));
        Stretch(glow.rectTransform);

        Image frame = CreateSliced(rect, "Frame", KeyRound16Frame, Alpha(Hex("7A4A18"), 0.55f));
        Stretch(frame.rectTransform);

        // 文字用近黑深棕 + 粗体，并强制放到最后一个子节点，确保永远压在底色之上
        labelText = CreateText(rect, "Label", label, fontSize, ButtonLabel, TextAnchor.MiddleCenter);
        labelText.fontStyle = FontStyle.Bold;
        Stretch(labelText.rectTransform);
        labelText.transform.SetAsLastSibling();

        Button button = go.GetComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;                            // 满亮度亮金底
        colors.highlightedColor = new Color(1.18f, 1.18f, 1.12f, 1f); // 悬停更亮（>1 在 UI 顶点色上有效）
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = new Color(1.08f, 1.08f, 1.05f, 1f);
        colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 0.7f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        UiButtonMotion motion = go.AddComponent<UiButtonMotion>();
        motion.Configure(1.045f, 0.965f);
        return button;
    }

    /// <summary>建立按钮（不需要拿文字引用时的简便重载）。</summary>
    public static Button CreateButton(RectTransform parent, string name, string label, Vector2 size, int fontSize)
    {
        Text ignored;
        return CreateButton(parent, name, label, size, fontSize, out ignored);
    }

    /// <summary>建立音量滑杆（轨道 + 填充 + 圆形把手）。</summary>
    public static Slider CreateSlider(RectTransform parent, string name, Vector2 size, float value)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Slider));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;

        Image track = CreateSliced(rect, "Background", KeyRound6, Alpha(Hex("0A0C12"), 0.95f), true);
        Stretch(track.rectTransform);
        Stretch(track.rectTransform, 0f, 8f, 0f, 8f);

        RectTransform fillArea = CreateRect(rect, "Fill Area");
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.offsetMin = new Vector2(8f, -6f);
        fillArea.offsetMax = new Vector2(-8f, 6f);

        Image fill = CreateSliced(fillArea, "Fill", KeyRound6, Teal);
        Stretch(fill.rectTransform);

        RectTransform handleArea = CreateRect(rect, "Handle Slide Area");
        Stretch(handleArea);
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);

        Image handle = CreateSliced(handleArea, "Handle", KeyRound6, Gold, true);
        handle.rectTransform.sizeDelta = new Vector2(18f, size.y);

        Slider slider = go.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = value;

        ColorBlock colors = slider.colors;
        colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.fadeDuration = 0.08f;
        slider.colors = colors;
        return slider;
    }

    // ==================== 布局辅助 ====================

    /// <summary>拉伸填满父级（可整体内缩）。</summary>
    public static void Stretch(RectTransform rect, float padding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    /// <summary>拉伸填满父级并指定四边内缩。</summary>
    public static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>按锚点/轴心定位并定尺寸。</summary>
    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static readonly Vector2 TopLeftAnchor = new Vector2(0f, 1f);
    private static readonly Vector2 TopRightAnchor = new Vector2(1f, 1f);
    private static readonly Vector2 TopCenterAnchor = new Vector2(0.5f, 1f);
    private static readonly Vector2 BottomLeftAnchor = new Vector2(0f, 0f);
    private static readonly Vector2 BottomRightAnchor = new Vector2(1f, 0f);
    private static readonly Vector2 BottomCenterAnchor = new Vector2(0.5f, 0f);
    private static readonly Vector2 CenterAnchor = new Vector2(0.5f, 0.5f);

    public static void TopLeft(RectTransform rect, Vector2 position, Vector2 size)
    {
        Place(rect, TopLeftAnchor, TopLeftAnchor, position, size);
    }

    public static void TopRight(RectTransform rect, Vector2 position, Vector2 size)
    {
        Place(rect, TopRightAnchor, TopRightAnchor, position, size);
    }

    public static void TopCenter(RectTransform rect, Vector2 position, Vector2 size)
    {
        Place(rect, TopCenterAnchor, TopCenterAnchor, position, size);
    }

    public static void BottomLeft(RectTransform rect, Vector2 position, Vector2 size)
    {
        Place(rect, BottomLeftAnchor, BottomLeftAnchor, position, size);
    }

    public static void BottomRight(RectTransform rect, Vector2 position, Vector2 size)
    {
        Place(rect, BottomRightAnchor, BottomRightAnchor, position, size);
    }

    public static void BottomCenter(RectTransform rect, Vector2 position, Vector2 size)
    {
        Place(rect, BottomCenterAnchor, BottomCenterAnchor, position, size);
    }

    public static void Center(RectTransform rect, Vector2 position, Vector2 size)
    {
        Place(rect, CenterAnchor, CenterAnchor, position, size);
    }
}
