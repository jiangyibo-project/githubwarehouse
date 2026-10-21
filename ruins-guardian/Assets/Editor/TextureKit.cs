using UnityEngine;

/// <summary>
/// 程序化 PBR 贴图工厂（升级规范 5.3，T2 世界模块）。
/// 只负责生成 CPU 侧 Texture2D；落盘（AssetDatabase.CreateAsset）与材质绑定由调用方负责。
/// 法线贴图同时把 x 分量写入 R 与 A 通道，兼容 RGB 与 DXT5nm 两种 UnpackNormal 约定。
/// 所有噪声都按 uv * period 采样，保证左右上下无缝平铺。
/// </summary>
public static class TextureKit
{
    private const int BrickColumns = 4;
    private const int BrickRows = 2;

    /// <summary>苔藓基色：在规范苔痕 #5F7A4A 基础上压暗、降饱和并偏黄（hue≈80°/sat≈33%），避免纯绿扎眼。</summary>
    private static readonly Color MossColor = new Color(0.300f, 0.340f, 0.220f);

    // ================= 公开签名（规范 5.3） =================

    /// <summary>
    /// 石材反照率：错缝砖块（逐砖明度与色相抖动）+ 低频斑驳 + 超大尺度冷暖漂移
    /// + 区域化裂纹 + 压进砖缝与低洼处的苔藓。三层不同频率的变化是为了打散平铺重复感。
    /// </summary>
    /// <param name="size">正方形边长（像素）。</param>
    /// <param name="baseColor">石材基色。</param>
    /// <param name="mottle">斑驳强度，0 干净、1 严重脏化。</param>
    /// <param name="seed">随机种子。</param>
    /// <param name="moss">苔藓覆盖强度，0 无苔、1 长满。</param>
    public static Texture2D StoneAlbedo(int size, Color baseColor, float mottle, int seed, float moss)
    {
        size = Mathf.Clamp(size, 16, 2048);
        float mottleAmount = Mathf.Clamp01(mottle);
        float mossAmount = Mathf.Clamp01(moss);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                int brickId = BrickField(u, v, BrickColumns, BrickRows, size, out float jointTexels);

                // 每块砖基色 + 色相同时抖动：既有明度差也有冷暖差，重复感的第一层打散
                float brickTone = Hash(brickId, seed, 11);
                float brickWarmth = Hash(brickId, seed, 29);
                Color color = baseColor * (0.84f + brickTone * 0.28f);
                color.r *= 0.95f + brickWarmth * 0.10f;
                color.b *= 1.05f - brickWarmth * 0.10f;

                // 高频颗粒
                float grain = ValueNoise(u * size * 0.5f, v * size * 0.5f, Mathf.Max(1, size / 2), seed + 5);
                color *= 0.94f + grain * 0.12f;

                // 低频斑驳（脏化）
                float patch = Fbm(u, v, 3, 4, seed + 21);
                color *= 1f + (patch - 0.5f) * mottleAmount * 1.6f;

                // 超大尺度冷暖漂移：跨越数块砖的暖区/冷区，第二层打散（平铺后仍能看出成片色差）
                float region = Fbm(u * 0.5f, v * 0.5f, 2, 3, seed + 71);
                color.r *= 0.94f + region * 0.14f;
                color.g *= 0.97f + region * 0.06f;
                color.b *= 1.06f - region * 0.14f;

                // 砖缝压暗，靠近缝的两三像素额外过渡
                float jointFade = Mathf.Clamp01(jointTexels / 3.2f);
                color *= Mathf.Lerp(0.42f, 1f, jointFade);

                // 裂缝：两层脊状噪声 + 区域遮罩。
                // 混合强度砍半（最深 0.82，原 0.64）、阈值收紧、区域门槛 0.42→0.55：
                // 目标是"近看有细纹、远看是干净色块"，不要大理石那种满铺网纹。
                float crackRegion = Fbm(u, v, 2, 3, seed + 87);
                float crack = Mathf.Max(RidgedFbm(u, v, 7, 3, seed + 33),
                    RidgedFbm(u * 2.1f, v * 2.1f, 11, 2, seed + 41) * 0.94f);
                if (crackRegion > 0.55f && crack > 0.945f) color *= Mathf.Lerp(0.82f, 1f, (1f - crack) * 10f);

                // 苔藓：优先长在砖缝与低洼砖块上（积水积土处），而不是均匀铺一层绿
                if (mossAmount > 0.001f)
                {
                    float mossNoise = Fbm(u * 1.7f, v * 1.7f, 4, 4, seed + 47);
                    float mossMask = Mathf.Clamp01((mossNoise - 0.42f) / 0.32f) * mossAmount;
                    float jointBias = 1f - Mathf.Clamp01(jointTexels / 5f);   // 缝中心 = 1
                    float lowBias = 1f - brickTone;                           // 越低的砖越潮
                    mossMask *= Mathf.Lerp(0.40f, 1f, jointBias) * Mathf.Lerp(0.55f, 1f, lowBias);
                    color = Color.Lerp(color, MossColor * (0.82f + mossNoise * 0.45f), mossMask * 0.9f);
                }

                color.a = 1f;
                pixels[y * size + x] = color;
            }
        }

        Texture2D texture = NewTexture(size, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    /// <summary>
    /// 石材切线空间法线：砖面凸起、砖缝凹陷、表面噪声与每块砖错位。
    /// </summary>
    /// <param name="size">正方形边长（像素）。</param>
    /// <param name="strength">法线强度，1 为默认。</param>
    /// <param name="tileScale">砖块尺度，越大砖越小（1 = 默认密度）。</param>
    /// <param name="seed">随机种子。</param>
    public static Texture2D StoneNormal(int size, float strength, float tileScale, int seed)
    {
        size = Mathf.Clamp(size, 16, 2048);
        int columns = Mathf.Clamp(Mathf.RoundToInt(BrickColumns * Mathf.Max(0.25f, tileScale)), 1, 16);
        int rows = Mathf.Clamp(Mathf.RoundToInt(BrickRows * Mathf.Max(0.25f, tileScale)), 1, 8);

        float[] height = new float[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                int brickId = BrickField(u, v, columns, rows, size, out float jointTexels);

                // 砖面高于砖缝
                float h = 0.72f * Mathf.Clamp01(jointTexels / 3.6f);
                h += 0.18f * Fbm(u, v, 8, 4, seed + 3);
                h += 0.10f * (Hash(brickId, seed, 7) - 0.5f);
                height[y * size + x] = h;
            }
        }

        // 中心差分求切线空间法线；分辨率越高每像素落差越小，所以按 size 补偿系数
        float scale = Mathf.Max(0.05f, strength) * 3.2f * (size / 512f);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float left = height[y * size + WrapIndex(x - 1, size)];
                float right = height[y * size + WrapIndex(x + 1, size)];
                float down = height[WrapIndex(y - 1, size) * size + x];
                float up = height[WrapIndex(y + 1, size) * size + x];
                Vector3 normal = new Vector3((left - right) * scale, (down - up) * scale, 1f).normalized;
                float nx = normal.x * 0.5f + 0.5f;
                float ny = normal.y * 0.5f + 0.5f;
                float nz = normal.z * 0.5f + 0.5f;
                pixels[y * size + x] = new Color(nx, ny, nz, nx);
            }
        }

        Texture2D texture = NewTexture(size, TextureFormat.RGBA32, true);
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    /// <summary>
    /// 粗糙度贴图：R/G 存放粗糙度，B 存放砖缝遮蔽（AO），方便不同 shader 取任一变体。
    /// </summary>
    /// <param name="size">正方形边长（像素）。</param>
    /// <param name="min">最光滑处（金属高光区域取小值）。</param>
    /// <param name="max">最粗糙处。</param>
    /// <param name="seed">随机种子。</param>
    public static Texture2D Roughness(int size, float min, float max, int seed)
    {
        size = Mathf.Clamp(size, 16, 2048);
        float low = Mathf.Clamp01(Mathf.Min(min, max));
        float high = Mathf.Clamp01(Mathf.Max(min, max));
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                BrickField(u, v, BrickColumns, BrickRows, size, out float jointTexels);

                float blotch = Fbm(u, v, 5, 4, seed);
                float wear = ValueNoise(u * size * 0.4f, v * size * 0.4f, Mathf.Max(1, size * 2 / 5), seed + 13);
                float roughness = Mathf.Lerp(low, high, blotch * 0.7f + wear * 0.3f);

                // 砖缝更粗糙、更暗
                float occlusion = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(jointTexels / 4f));
                roughness = Mathf.Clamp01(roughness + (1f - occlusion) * 0.25f);

                pixels[y * size + x] = new Color(roughness, roughness, occlusion, 1f);
            }
        }

        Texture2D texture = NewTexture(size, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    /// <summary>
    /// 植被卡片：一丛朝向随机、根部深色顶端泛黄的草叶，透明底，配 Foliage 裁切材质使用。
    /// </summary>
    public static Texture2D GrassCard(int size, int seed)
    {
        size = Mathf.Clamp(size, 32, 1024);
        System.Random random = new System.Random(seed);

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0f, 0f, 0f, 0f);

        // 叶片更多更细更短：读作柔软的草叶簇，而不是几根绿色尖刺
        int blades = Mathf.Max(18, size / 3);
        // 比规范苔痕 #5F7A4A 更暗、更灰、更偏黄的暖灰绿：暖光下不抢重点。
        // 三档颜色整体 +9%（而不是只提 alpha）：贴墙时不再读成"黑污渍"。
        Color rootDark = new Color(0.153f, 0.174f, 0.120f);
        Color tipLight = new Color(0.382f, 0.420f, 0.286f);

        for (int blade = 0; blade < blades; blade++)
        {
            float baseX = size * 0.5f + ((float)random.NextDouble() - 0.5f) * size * 0.78f;
            float halfWidth = Mathf.Lerp(size * 0.0035f, size * 0.010f, (float)random.NextDouble());
            float height = Mathf.Lerp(size * 0.30f, size * 0.72f, (float)random.NextDouble());
            float lean = ((float)random.NextDouble() - 0.5f) * size * 0.30f;
            // 尖端额外下垂：用三次曲线让上半段明显向外弯，像被踩过的草
            float droop = lean * Mathf.Lerp(0.5f, 1.1f, (float)random.NextDouble());
            float tint = (float)random.NextDouble();
            Color rootColor = Color.Lerp(rootDark, new Color(0.207f, 0.234f, 0.158f), tint);
            Color leafColor = Color.Lerp(tipLight * 0.85f, tipLight, tint);

            for (int t = 0; t < (int)height; t++)
            {
                float p = t / height;
                // 下段几乎直立，上段逐渐弯垂
                float centerX = baseX + lean * p * p + droop * p * p * p;
                float width = halfWidth * (1f - p * 0.9f);
                Color color = Color.Lerp(rootColor, leafColor, p);
                // 尖端半透明，边缘不再是硬切
                color.a = p > 0.82f ? Mathf.Lerp(1f, 0.55f, (p - 0.82f) / 0.18f) : 1f;

                int from = Mathf.FloorToInt(centerX - width);
                int to = Mathf.CeilToInt(centerX + width);
                for (int px = from; px <= to; px++)
                {
                    if (px < 0 || px >= size) continue;
                    Color existing = pixels[t * size + px];
                    // 叠加而不是覆盖，避免后画的叶子切掉先画的
                    pixels[t * size + px] = existing.a > 0.01f
                        ? Color.Lerp(existing, color, 0.55f)
                        : color;
                }
            }
        }

        Texture2D texture = NewTexture(size, TextureFormat.RGBA32, true);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    /// <summary>
    /// 方形石板反照率（地面专用）：3×3 正方石块、缝隙窄而深、整体更偏灰。
    /// 与 StoneAlbedo 的区别是砖块长宽比为 1:1，避免地面读成"长木地板"。
    /// 缝隙宽度按 UV 缩放到 0.7 倍（更窄），压暗倍率从 0.42 提到 0.30（更深）。
    /// </summary>
    public static Texture2D SlabAlbedo(int size, Color baseColor, float mottle, int seed, float moss)
    {
        size = Mathf.Clamp(size, 16, 2048);
        float mottleAmount = Mathf.Clamp01(mottle);
        float mossAmount = Mathf.Clamp01(moss);
        const int columns = 3;
        const int rows = 3;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                int slabId = BrickField(u, v, columns, rows, size, out float jointTexels);

                float tone = Hash(slabId, seed, 13);
                float warmth = Hash(slabId, seed, 31);
                Color color = baseColor * (0.88f + tone * 0.20f);
                color.r *= 0.97f + warmth * 0.06f;
                color.b *= 1.03f - warmth * 0.06f;

                float grain = ValueNoise(u * size * 0.5f, v * size * 0.5f, Mathf.Max(1, size / 2), seed + 5);
                color *= 0.95f + grain * 0.10f;

                float patch = Fbm(u, v, 3, 3, seed + 21);
                color *= 1f + (patch - 0.5f) * mottleAmount * 1.3f;

                // 窄而深的石缝：jointTexels 只有 0.7 倍宽，压暗到 0.30
                float jointFade = Mathf.Clamp01(jointTexels / (2.0f * 0.7f));
                color *= Mathf.Lerp(0.30f, 1f, jointFade);

                // 少量细裂：只在石缝附近、极浅
                float crack = RidgedFbm(u, v, 6, 2, seed + 61);
                if (crack > 0.95f) color *= 0.90f;

                if (mossAmount > 0.001f)
                {
                    float mossNoise = Fbm(u * 1.7f, v * 1.7f, 4, 4, seed + 47);
                    float mossMask = Mathf.Clamp01((mossNoise - 0.44f) / 0.30f) * mossAmount;
                    float jointBias = 1f - Mathf.Clamp01(jointTexels / 4f);
                    mossMask *= Mathf.Lerp(0.45f, 1f, jointBias);
                    color = Color.Lerp(color, MossColor * (0.85f + mossNoise * 0.4f), mossMask * 0.8f);
                }

                color.a = 1f;
                pixels[y * size + x] = color;
            }
        }

        Texture2D texture = NewTexture(size, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    /// <summary>方形石板法线：3×3 方砖、窄而深的缝（与 SlabAlbedo 对位）。</summary>
    public static Texture2D SlabNormal(int size, float strength, int seed)
    {
        size = Mathf.Clamp(size, 16, 2048);
        const int columns = 3;
        const int rows = 3;
        float[] height = new float[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                int slabId = BrickField(u, v, columns, rows, size, out float jointTexels);
                float h = 0.78f * Mathf.Clamp01(jointTexels / (2.4f * 0.7f));
                h += 0.14f * Fbm(u, v, 9, 3, seed + 3);
                h += 0.08f * (Hash(slabId, seed, 7) - 0.5f);
                height[y * size + x] = h;
            }
        }

        float scale = Mathf.Max(0.05f, strength) * 3.4f * (size / 512f);
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float left = height[y * size + WrapIndex(x - 1, size)];
                float right = height[y * size + WrapIndex(x + 1, size)];
                float down = height[WrapIndex(y - 1, size) * size + x];
                float up = height[WrapIndex(y + 1, size) * size + x];
                Vector3 normal = new Vector3((left - right) * scale, (down - up) * scale, 1f).normalized;
                float nx = normal.x * 0.5f + 0.5f;
                float ny = normal.y * 0.5f + 0.5f;
                float nz = normal.z * 0.5f + 0.5f;
                pixels[y * size + x] = new Color(nx, ny, nz, nx);
            }
        }

        Texture2D texture = NewTexture(size, TextureFormat.RGBA32, true);
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    /// <summary>无缝灰度 fbm 噪声，用于遮罩、顶点扰动与加性光斑（乘以颜色即可）。</summary>
    public static Texture2D Noise(int size, int seed)
    {
        size = Mathf.Clamp(size, 16, 2048);
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float value = Fbm(u, v, 6, 5, seed) * 0.7f
                              + ValueNoise(u * size * 0.5f, v * size * 0.5f, Mathf.Max(1, size / 2), seed + 9) * 0.3f;
                value = Mathf.Clamp01(value);
                pixels[y * size + x] = new Color(value, value, value, 1f);
            }
        }

        Texture2D texture = NewTexture(size, TextureFormat.RGB24, true);
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    // ================= 内部工具 =================

    private static Texture2D NewTexture(int size, TextureFormat format, bool mipChain)
    {
        Texture2D texture = new Texture2D(size, size, format, mipChain);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = mipChain ? FilterMode.Trilinear : FilterMode.Bilinear;
        texture.anisoLevel = 8;
        return texture;
    }

    /// <summary>
    /// 错缝砖块场：返回砖块 id，并用 jointTexels 输出"到最近砖缝的距离（像素）"。
    /// 行数为偶数时左右可无缝平铺。
    /// </summary>
    private static int BrickField(float u, float v, int columns, int rows, int size, out float jointTexels)
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);
        float cellWidth = 1f / columns;
        float cellHeight = 1f / rows;

        int row = Mathf.Clamp((int)(v / cellHeight), 0, rows - 1);
        float rowOffset = (row % 2 == 0) ? 0f : 0.5f;

        float shifted = u / cellWidth + rowOffset;
        int column = Mathf.FloorToInt(shifted);
        float localX = shifted - column;
        float localY = Mathf.Repeat(v / cellHeight, 1f);

        float distanceU = Mathf.Min(localX, 1f - localX) * cellWidth;
        float distanceV = Mathf.Min(localY, 1f - localY) * cellHeight;
        jointTexels = Mathf.Min(distanceU, distanceV) * size;

        return column * 1013 + row * 7919;
    }

    private static int WrapIndex(int index, int size)
    {
        int wrapped = index % size;
        return wrapped < 0 ? wrapped + size : wrapped;
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int hash = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            hash ^= hash >> 16;
            return (hash & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }
    }

    /// <summary>周期值噪声：period 为格子数，采样域 0..period 正好无缝环绕。</summary>
    private static float ValueNoise(float x, float y, int period, int seed)
    {
        period = Mathf.Max(1, period);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;
        float sx = fx * fx * (3f - 2f * fx);
        float sy = fy * fy * (3f - 2f * fy);

        int xa = WrapIndex(x0, period);
        int xb = WrapIndex(x0 + 1, period);
        int ya = WrapIndex(y0, period);
        int yb = WrapIndex(y0 + 1, period);

        float v00 = Hash(xa, ya, seed);
        float v10 = Hash(xb, ya, seed);
        float v01 = Hash(xa, yb, seed);
        float v11 = Hash(xb, yb, seed);

        return Mathf.Lerp(Mathf.Lerp(v00, v10, sx), Mathf.Lerp(v01, v11, sx), sy);
    }

    private static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0f;
        float total = 0f;
        float amplitude = 1f;
        int period = Mathf.Max(1, basePeriod);
        int count = Mathf.Max(1, octaves);

        for (int i = 0; i < count; i++)
        {
            sum += ValueNoise(u * period, v * period, period, seed + i * 17) * amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            period *= 2;
        }

        return total > 0f ? sum / total : 0f;
    }

    /// <summary>脊状噪声：1 - |2n-1|，高值处形成细脊，用来做裂纹。</summary>
    private static float RidgedFbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float value = Fbm(u, v, basePeriod, octaves, seed);
        return 1f - Mathf.Abs(value * 2f - 1f);
    }
}
