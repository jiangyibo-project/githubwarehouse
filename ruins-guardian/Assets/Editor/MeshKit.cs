using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 程序化低多边形网格工厂（T1 角色模块）。
/// 约定：
/// 1) 每个网格生成后立刻 AssetDatabase.CreateAsset 保存到 Assets/Generated/Meshes/，文件名由参数决定；
///    同名资源已存在则直接复用，不重复生成（本类另有内存缓存）。
/// 2) 输出前统一重算法线（逐三角形独立顶点 = 硬边低多边形）、UV、切线（描边 shader 依赖切线）。
/// 3) 坐标系：Y 轴向上。棱柱 / 圆环以原点为中心；圆锥底面在 y=0、尖端在 y=+height；
///    卡面（披风）上边缘在 y=0，向下垂到 y=-height。
/// </summary>
public static class MeshKit
{
    private const string MeshRoot = "Assets/Generated/Meshes";

    // 生成算法版本：算法改动后改这里即可让旧资源全部失效重建（参数相同的资源直接复用）
    private const string Version = "v3";

    private static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

    // 已确认带 uv3 平滑法线的网格 key，避免同一网格被反复检查
    private static readonly HashSet<string> uv3Verified = new HashSet<string>();

    // ================= 公开工厂 =================

    /// <summary>倒角立方体。segments=1 为单段 45° 倒角，越大棱角越圆滑。</summary>
    public static Mesh BeveledBox(Vector3 size, float bevelWidth, int segments)
    {
        int n = Mathf.Clamp(segments, 1, 6);
        string key = "Box_" + F(size.x) + "x" + F(size.y) + "x" + F(size.z) + "_b" + F(bevelWidth) + "_s" + n;
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildBeveledBox(builder, size, bevelWidth, n);
        return Store(key, builder);
    }

    /// <summary>锥台棱柱：上下半径不同，可整体扭转，以原点为中心竖直放置。</summary>
    public static Mesh TaperedPrism(float topRadius, float bottomRadius, float height, int sides, float twist)
    {
        int s = Mathf.Clamp(sides, 3, 24);
        string key = "Prism_" + F(topRadius) + "_" + F(bottomRadius) + "_" + F(height) + "_" + s + "_" + F(twist);
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildTaperedPrism(builder, topRadius, bottomRadius, height, s, twist);
        return Store(key, builder);
    }

    /// <summary>低多边形球（二十面体细分）。subdivisions=0 为 20 面，1 为 80 面，2 为 320 面。</summary>
    public static Mesh LowPolySphere(float radius, int subdivisions)
    {
        int s = Mathf.Clamp(subdivisions, 0, 3);
        string key = "Sphere_" + F(radius) + "_" + s;
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildSphere(builder, radius, s);
        return Store(key, builder);
    }

    /// <summary>圆锥：底面圆心在原点、底面在 y=0，尖端在 y=+height。</summary>
    public static Mesh Cone(float radius, float height, int sides)
    {
        int s = Mathf.Clamp(sides, 3, 24);
        string key = "Cone_" + F(radius) + "_" + F(height) + "_" + s;
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildCone(builder, radius, height, s);
        return Store(key, builder);
    }

    /// <summary>剑刃：六边截面多段拉伸，两侧开锋、末端收尖，刀背端面封口。沿 +Y 生长，握柄端在 y=0。</summary>
    public static Mesh Blade(float length, float width, float thickness, int segments)
    {
        int s = Mathf.Clamp(segments, 2, 16);
        string key = "Blade_" + F(length) + "_" + F(width) + "_" + F(thickness) + "_" + s;
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildBlade(builder, length, width, thickness, s);
        return Store(key, builder);
    }

    /// <summary>披风 / 旗帜条带：上边缘在 y=0 向下垂到 y=-height，横向按抛物线弯曲，纵向细分为 segments 段（供顶点动画）。双面几何。</summary>
    public static Mesh Card(float width, float height, float curve, int segments)
    {
        return Card(width, height, curve, segments, 1f, 0, 0f);
    }

    /// <summary>
    /// 披风 / 旗帜条带（扩展版）：在下摆与竖褶上做文章，褶子做在同一张网格里，
    /// 因此 CapeMotion 的顶点摆动会连褶子一起带动，不会穿模。
    /// flare：下摆相对顶部的宽度倍数（1 = 不收放，1.2 = 下摆外扩 20%）；
    /// folds：竖向褶数（0 = 平面），每条褶为一列顶点沿 +Z（朝身体内侧）内凹；
    /// foldDepth：内凹深度。
    /// </summary>
    public static Mesh Card(float width, float height, float curve, int segments, float flare, int folds, float foldDepth)
    {
        int s = Mathf.Clamp(segments, 1, 16);
        int fold = Mathf.Clamp(folds, 0, 6);
        float fl = Mathf.Clamp(flare, 0.2f, 3f);
        float fd = Mathf.Clamp(foldDepth, 0f, 0.12f);
        string key = "Card_" + F(width) + "_" + F(height) + "_" + F(curve) + "_" + s + "_f" + F(fl) + "_n" + fold + "_d" + F(fd);
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildCard(builder, width, height, curve, s, fl, fold, fd);
        return Store(key, builder);
    }

    /// <summary>切角八面体宝石：顶面小平台 → 上锥 → 腰部棱台 → 下锥 → 底面小平台，轴向 +Y，以腰线为原点。</summary>
    public static Mesh Gem(float radius, float height, int sides)
    {
        int s = Mathf.Clamp(sides, 4, 16);
        string key = "Gem_" + F(radius) + "_" + F(height) + "_" + s;
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildGem(builder, radius, height, s);
        return Store(key, builder);
    }

    /// <summary>环形（腰带 / 护腕 / 下摆）：内外壁 + 上下环面，以原点为中心，轴向 +Y。</summary>
    public static Mesh Ring(float inner, float outer, float height, int segments)
    {
        int s = Mathf.Clamp(segments, 3, 32);
        float i = Mathf.Clamp(inner, 0.0005f, outer * 0.98f);
        string key = "Ring_" + F(i) + "_" + F(outer) + "_" + F(height) + "_" + s;
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        BuildRing(builder, i, Mathf.Max(outer, i + 0.0005f), height, s);
        return Store(key, builder);
    }

    /// <summary>阶梯：count 级，每级宽 width、升高 rise、进深 run，从原点向 +Y / +Z 递进。</summary>
    public static Mesh Stair(int count, float width, float rise, float run)
    {
        int c = Mathf.Clamp(count, 1, 12);
        string key = "Stair_" + c + "_" + F(width) + "_" + F(rise) + "_" + F(run);
        Mesh mesh = Load(key);
        if (mesh != null) return mesh;
        Builder builder = new Builder();
        for (int i = 0; i < c; i++)
        {
            Vector3 center = new Vector3(0f, rise * (i + 0.5f), run * (i + 0.5f));
            BuildBoxFaces(builder, center, new Vector3(width, rise, run));
        }
        return Store(key, builder);
    }

    // ================= 资源管理 =================

    private static string F(float value)
    {
        return value.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static Mesh Load(string key)
    {
        Mesh mesh;
        if (cache.TryGetValue(key, out mesh) && mesh != null)
        {
            EnsureSmoothNormals(key, mesh);
            return mesh;
        }
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshRoot + "/" + key + "_" + Version + ".asset");
        if (mesh != null)
        {
            EnsureSmoothNormals(key, mesh);
            cache[key] = mesh;
        }
        return mesh;
    }

    private static Mesh Store(string key, Builder builder)
    {
        EnsureFolder(MeshRoot);
        Mesh mesh = builder.Finish(key);
        AssetDatabase.CreateAsset(mesh, MeshRoot + "/" + key + "_" + Version + ".asset");
        cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// 把"平滑法线"写入 uv3 通道（TEXCOORD2 = SetUVs(2)，Vector3），供描边 pass 沿其外扩。
    /// 背景：主几何是逐三角形独立顶点（硬边），各面法线互不相同，描边若沿顶点法线外扩，
    /// 棱角处各面朝不同方向炸开，会漏出黑色背面；uv3 提供的按位置平均法线在棱角处连续，
    /// 外扩后仍能包住模型。
    /// 算法：位置量化（1mm）合并 → 面法线 × 三角形面积累加 → 归一化 → 按顶点写出。
    /// 不改变顶点顺序、不合并顶点，硬边外观完全保留。
    /// </summary>
    private static void ApplySmoothNormalsToUv3(Mesh mesh)
    {
        int vertexCount = mesh.vertexCount;
        if (vertexCount == 0) return;

        Vector3[] vertices = mesh.vertices;
        List<int> triangles = new List<int>();
        mesh.GetTriangles(triangles, 0);

        // 1) 位置量化哈希：同一空间位置的多个硬边顶点合并到同一个桶
        Dictionary<long, int> buckets = new Dictionary<long, int>(vertexCount);
        int[] bucketOf = new int[vertexCount];
        List<Vector3> weightedSum = new List<Vector3>(vertexCount);
        for (int i = 0; i < vertexCount; i++)
        {
            long key = PositionKey(vertices[i]);
            int index;
            if (!buckets.TryGetValue(key, out index))
            {
                index = weightedSum.Count;
                buckets.Add(key, index);
                weightedSum.Add(Vector3.zero);
            }
            bucketOf[i] = index;
        }

        // 2) 面法线 × 面积累加（cross 的长度即两倍面积，cross * 0.5 就是面积加权法线）
        for (int t = 0; t + 2 < triangles.Count; t += 3)
        {
            int i0 = triangles[t];
            int i1 = triangles[t + 1];
            int i2 = triangles[t + 2];
            Vector3 cross = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
            if (cross.sqrMagnitude < 1e-16f) continue; // 退化三角形，不计入
            Vector3 weighted = cross * 0.5f;

            int b0 = bucketOf[i0];
            int b1 = bucketOf[i1];
            int b2 = bucketOf[i2];
            weightedSum[b0] = weightedSum[b0] + weighted;
            weightedSum[b1] = weightedSum[b1] + weighted;
            weightedSum[b2] = weightedSum[b2] + weighted;
        }

        // 3) 归一化后按顶点顺序写出；累加为零（退化 / 双面片正反抵消）时写 zero，shader 侧回退到顶点法线
        List<Vector3> smoothNormals = new List<Vector3>(vertexCount);
        for (int i = 0; i < vertexCount; i++)
        {
            Vector3 sum = weightedSum[bucketOf[i]];
            float length = sum.magnitude;
            smoothNormals.Add(length > 1e-6f ? sum / length : Vector3.zero);
        }

        // 写 TEXCOORD2（Unity 的 mesh.uv3 → SetUVs(2)），与 StylizedCharacter.shader 主通道一致
        mesh.SetUVs(2, smoothNormals);
    }

    /// <summary>把世界/局部位置量化到 1mm 后压成 64 位 key，避免浮点误差把同一点判成不同点。</summary>
    private static long PositionKey(Vector3 position)
    {
        const long bias = 1048576L; // 2^20，容纳 ±1048m 的范围
        long x = (long)Mathf.RoundToInt(position.x * 1000f) + bias;
        long y = (long)Mathf.RoundToInt(position.y * 1000f) + bias;
        long z = (long)Mathf.RoundToInt(position.z * 1000f) + bias;
        return (x << 42) | (y << 21) | z;
    }

    /// <summary>老资源兜底：命中缓存/资产时若缺 uv3(TEXCOORD2) 就补写并保存，避免旧网格仍然炸描边。</summary>
    private static void EnsureSmoothNormals(string key, Mesh mesh)
    {
        if (uv3Verified.Contains(key)) return;
        Vector2[] existing = mesh.uv3;
        if (existing == null || existing.Length != mesh.vertexCount)
        {
            ApplySmoothNormalsToUv3(mesh);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
        }
        uv3Verified.Add(key);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            parent = parent.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        }
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    // ================= 几何生成 =================

    private static Vector3 Axis(int axis)
    {
        return axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
    }

    /// <summary>普通六面盒（无倒角），供阶梯与细节片使用。</summary>
    private static void BuildBoxFaces(Builder builder, Vector3 center, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 a = new Vector3();
                Vector3 b = new Vector3();
                Vector3 c = new Vector3();
                Vector3 d = new Vector3();
                a[axis] = sign * h[axis]; a[u] = -h[u]; a[v] = -h[v];
                b[axis] = sign * h[axis]; b[u] = h[u]; b[v] = -h[v];
                c[axis] = sign * h[axis]; c[u] = h[u]; c[v] = h[v];
                d[axis] = sign * h[axis]; d[u] = -h[u]; d[v] = h[v];
                Vector3 outward = Axis(axis) * sign;
                builder.AddQuad(a + center, b + center, c + center, d + center, outward);
            }
        }
    }

    private static void BuildBeveledBox(Builder builder, Vector3 size, float bevelWidth, int n)
    {
        Vector3 h = size * 0.5f;
        float limit = Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.95f;
        float bv = Mathf.Clamp(bevelWidth, 0f, Mathf.Max(0f, limit));
        if (bv <= 0.0002f)
        {
            BuildBoxFaces(builder, Vector3.zero, size);
            return;
        }

        // 六个内缩平面
        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;
            float cu = h[u] - bv;
            float cv = h[v] - bv;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                Vector3 a = new Vector3();
                Vector3 b = new Vector3();
                Vector3 c = new Vector3();
                Vector3 d = new Vector3();
                a[axis] = sign * h[axis]; a[u] = -cu; a[v] = -cv;
                b[axis] = sign * h[axis]; b[u] = cu; b[v] = -cv;
                c[axis] = sign * h[axis]; c[u] = cu; c[v] = cv;
                d[axis] = sign * h[axis]; d[u] = -cu; d[v] = cv;
                builder.AddQuad(a, b, c, d, Axis(axis) * sign);
            }
        }

        // 十二条棱：以未归一化重心插值取圆弧点，保证与角块边界顶点完全重合
        for (int a1 = 0; a1 < 3; a1++)
        {
            for (int a2 = a1 + 1; a2 < 3; a2++)
            {
                int t = 3 - a1 - a2;
                float c1 = h[a1] - bv;
                float c2 = h[a2] - bv;
                float ct = h[t] - bv;
                for (int s1 = -1; s1 <= 1; s1 += 2)
                {
                    for (int s2 = -1; s2 <= 1; s2 += 2)
                    {
                        Vector3[] minus = new Vector3[n + 1];
                        Vector3[] plus = new Vector3[n + 1];
                        for (int k = 0; k <= n; k++)
                        {
                            float dx = s1 * (n - k) / (float)n;
                            float dy = s2 * k / (float)n;
                            float len = Mathf.Sqrt(dx * dx + dy * dy);
                            dx /= len;
                            dy /= len;
                            Vector3 p = new Vector3();
                            p[a1] = s1 * c1 + bv * dx;
                            p[a2] = s2 * c2 + bv * dy;
                            p[t] = -ct;
                            minus[k] = p;
                            p[t] = ct;
                            plus[k] = p;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            Vector3 outward = (minus[k] + plus[k] + minus[k + 1] + plus[k + 1]) * 0.25f;
                            builder.AddQuad(minus[k], minus[k + 1], plus[k + 1], plus[k], outward);
                        }
                    }
                }
            }
        }

        // 八个角：球心内缩 bv，按三角网格细分球面八分之一
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 center = new Vector3(sx * (h.x - bv), sy * (h.y - bv), sz * (h.z - bv));
                    Vector3 e1 = new Vector3(sx, 0f, 0f);
                    Vector3 e2 = new Vector3(0f, sy, 0f);
                    Vector3 e3 = new Vector3(0f, 0f, sz);
                    for (int i = 0; i < n; i++)
                    {
                        for (int j = 0; j < n - i; j++)
                        {
                            Vector3 p00 = CornerPoint(center, e1, e2, e3, n, i, j, bv);
                            Vector3 p10 = CornerPoint(center, e1, e2, e3, n, i + 1, j, bv);
                            Vector3 p01 = CornerPoint(center, e1, e2, e3, n, i, j + 1, bv);
                            builder.AddTriangle(p00, p10, p01, (p00 + p10 + p01) / 3f - center);
                            if (i + j < n - 1)
                            {
                                Vector3 p11 = CornerPoint(center, e1, e2, e3, n, i + 1, j + 1, bv);
                                builder.AddTriangle(p10, p11, p01, (p10 + p11 + p01) / 3f - center);
                            }
                        }
                    }
                }
            }
        }
    }

    private static Vector3 CornerPoint(Vector3 center, Vector3 e1, Vector3 e2, Vector3 e3, int n, int i, int j, float bv)
    {
        int k = n - i - j;
        Vector3 dir = e1 * i + e2 * j + e3 * k;
        return center + dir.normalized * bv;
    }

    private static void BuildTaperedPrism(Builder builder, float topRadius, float bottomRadius, float height, int sides, float twist)
    {
        float half = height * 0.5f;
        float twistRad = twist * Mathf.Deg2Rad;
        Vector3[] bottom = new Vector3[sides];
        Vector3[] top = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            bottom[i] = new Vector3(Mathf.Cos(angle) * bottomRadius, -half, Mathf.Sin(angle) * bottomRadius);
            top[i] = new Vector3(Mathf.Cos(angle + twistRad) * topRadius, half, Mathf.Sin(angle + twistRad) * topRadius);
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Vector3 outward = (bottom[i] + bottom[j] + top[i] + top[j]) * 0.25f;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-8f) outward = Vector3.right;
            builder.AddQuad(bottom[i], bottom[j], top[j], top[i], outward);
        }
        builder.AddFan(top, new Vector3(0f, half, 0f), Vector3.up);
        builder.AddFan(bottom, new Vector3(0f, -half, 0f), Vector3.down);
    }

    private static void BuildSphere(Builder builder, float radius, int subdivisions)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        Vector3[] seed =
        {
            new Vector3(-1f, t, 0f), new Vector3(1f, t, 0f), new Vector3(-1f, -t, 0f), new Vector3(1f, -t, 0f),
            new Vector3(0f, -1f, t), new Vector3(0f, 1f, t), new Vector3(0f, -1f, -t), new Vector3(0f, 1f, -t),
            new Vector3(t, 0f, -1f), new Vector3(t, 0f, 1f), new Vector3(-t, 0f, -1f), new Vector3(-t, 0f, 1f)
        };
        for (int i = 0; i < seed.Length; i++) seed[i] = seed[i].normalized * radius;

        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        List<Vector3[]> triangles = new List<Vector3[]>(faces.Length / 3);
        for (int i = 0; i < faces.Length; i += 3)
        {
            triangles.Add(new[] { seed[faces[i]], seed[faces[i + 1]], seed[faces[i + 2]] });
        }

        for (int level = 0; level < subdivisions; level++)
        {
            List<Vector3[]> next = new List<Vector3[]>(triangles.Count * 4);
            for (int i = 0; i < triangles.Count; i++)
            {
                Vector3 a = triangles[i][0];
                Vector3 b = triangles[i][1];
                Vector3 c = triangles[i][2];
                Vector3 ab = ((a + b) * 0.5f).normalized * radius;
                Vector3 bc = ((b + c) * 0.5f).normalized * radius;
                Vector3 ca = ((c + a) * 0.5f).normalized * radius;
                next.Add(new[] { a, ab, ca });
                next.Add(new[] { b, bc, ab });
                next.Add(new[] { c, ca, bc });
                next.Add(new[] { ab, bc, ca });
            }
            triangles = next;
        }

        for (int i = 0; i < triangles.Count; i++)
        {
            Vector3 a = triangles[i][0];
            Vector3 b = triangles[i][1];
            Vector3 c = triangles[i][2];
            builder.AddTriangle(a, b, c, a + b + c);
        }
    }

    private static void BuildCone(Builder builder, float radius, float height, int sides)
    {
        Vector3 apex = new Vector3(0f, height, 0f);
        Vector3[] ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            ring[i] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Vector3 outward = (ring[i] + ring[j]) * 0.5f;
            if (outward.sqrMagnitude < 1e-8f) outward = Vector3.right;
            builder.AddTriangle(ring[i], ring[j], apex, outward);
        }
        builder.AddFan(ring, Vector3.zero, Vector3.down);
    }

    private static void BuildBlade(Builder builder, float length, float width, float thickness, int segments)
    {
        float hw = width * 0.5f;
        float ht = thickness * 0.5f;
        float shoulder = width * 0.18f;
        // 截面（X = 刃宽，Z = 刃厚）：两端为刃口，中段为两侧平面
        Vector2[] profile =
        {
            new Vector2(hw, 0f),
            new Vector2(shoulder, ht),
            new Vector2(-shoulder, ht),
            new Vector2(-hw, 0f),
            new Vector2(-shoulder, -ht),
            new Vector2(shoulder, -ht)
        };
        int count = profile.Length;

        Vector3[][] rings = new Vector3[segments][];
        for (int k = 0; k < segments; k++)
        {
            float ratio = k / (float)segments;
            float scale = BladeWidth(ratio);
            float y = length * ratio;
            rings[k] = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                rings[k][i] = new Vector3(profile[i].x * scale, y, profile[i].y * scale);
            }
        }

        for (int k = 0; k < segments - 1; k++)
        {
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 a = rings[k][i];
                Vector3 b = rings[k][j];
                Vector3 c = rings[k + 1][j];
                Vector3 d = rings[k + 1][i];
                Vector3 outward = (a + b + c + d) * 0.25f;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-8f) outward = Vector3.right;
                builder.AddQuad(a, b, c, d, outward);
            }
        }

        // 剑尖：末端截面收到一点
        Vector3 tip = new Vector3(0f, length, 0f);
        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            Vector3 a = rings[segments - 1][i];
            Vector3 b = rings[segments - 1][j];
            Vector3 centroid = (a + b + tip) / 3f;
            builder.AddTriangle(a, b, tip, new Vector3(centroid.x, 0f, centroid.z));
        }

        // 握柄端封口（藏在护手内，仍需封闭以避免背面穿透）
        builder.AddFan(rings[0], Vector3.zero, Vector3.down);
    }

    private static float BladeWidth(float ratio)
    {
        if (ratio <= 0.62f) return 1f;
        float k = Mathf.InverseLerp(0.62f, 1f, ratio);
        return Mathf.Lerp(1f, 0.16f, k * k);
    }

    private static void BuildCard(Builder builder, float width, float height, float curve, int segments, float flare, int folds, float foldDepth)
    {
        // 有褶子时加密横向列数，褶落在每 4 列的第 3 列上，两侧各留一列做斜坡
        int columns = folds > 0 ? folds * 4 : 4;
        Vector3[] grid = new Vector3[(segments + 1) * (columns + 1)];
        for (int r = 0; r <= segments; r++)
        {
            float v = r / (float)segments;
            float y = -height * v;
            float curl = 1f + 0.6f * v;
            float halfWidth = width * 0.5f * Mathf.Lerp(1f, flare, v);
            for (int c = 0; c <= columns; c++)
            {
                float u = c / (float)columns * 2f - 1f;
                float x = u * halfWidth;
                float z = curve * u * u * curl;
                if (folds > 0 && c % 4 == 2 && c > 0 && c < columns) z += foldDepth;
                grid[r * (columns + 1) + c] = new Vector3(x, y, z);
            }
        }
        for (int r = 0; r < segments; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Vector3 a = grid[r * (columns + 1) + c];
                Vector3 b = grid[r * (columns + 1) + c + 1];
                Vector3 cc = grid[(r + 1) * (columns + 1) + c + 1];
                Vector3 d = grid[(r + 1) * (columns + 1) + c];
                Vector3 normal = Vector3.Cross(b - a, cc - a);
                if (normal.sqrMagnitude < 1e-12f) continue;
                normal.Normalize();
                builder.AddQuad(a, b, cc, d, normal);
                builder.AddQuad(a, b, cc, d, -normal);
            }
        }
    }

    private static void BuildRing(Builder builder, float inner, float outer, float height, int segments)
    {
        float half = height * 0.5f;
        Vector3[] outsideLow = new Vector3[segments];
        Vector3[] outsideHigh = new Vector3[segments];
        Vector3[] insideLow = new Vector3[segments];
        Vector3[] insideHigh = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            outsideLow[i] = new Vector3(cos * outer, -half, sin * outer);
            outsideHigh[i] = new Vector3(cos * outer, half, sin * outer);
            insideLow[i] = new Vector3(cos * inner, -half, sin * inner);
            insideHigh[i] = new Vector3(cos * inner, half, sin * inner);
        }
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % segments;
            Vector3 radial = (outsideLow[i] + outsideLow[j]) * 0.5f;
            radial.y = 0f;
            if (radial.sqrMagnitude < 1e-8f) radial = Vector3.right;
            builder.AddQuad(outsideLow[i], outsideLow[j], outsideHigh[j], outsideHigh[i], radial);
            builder.AddQuad(insideLow[i], insideLow[j], insideHigh[j], insideHigh[i], -radial);
            builder.AddQuad(insideHigh[i], insideHigh[j], outsideHigh[j], outsideHigh[i], Vector3.up);
            builder.AddQuad(insideLow[i], insideLow[j], outsideLow[j], outsideLow[i], Vector3.down);
        }
    }

    private static void BuildGem(Builder builder, float radius, float height, int sides)
    {
        float top = height * 0.5f;
        float bottom = -height * 0.5f;
        Vector3[] crown = RingVertices(sides, radius * 0.16f, top);
        Vector3[] upper = RingVertices(sides, radius, top * 0.36f);
        Vector3[] lower = RingVertices(sides, radius, bottom * 0.36f);
        Vector3[] tip = RingVertices(sides, radius * 0.14f, bottom);

        BuildRingBand(builder, crown, upper, Vector3.up);    // 上锥
        BuildRingBand(builder, upper, lower, Vector3.right); // 腰部棱台
        BuildRingBand(builder, lower, tip, Vector3.down);    // 下锥
        builder.AddFan(crown, new Vector3(0f, top, 0f), Vector3.up);
        builder.AddFan(tip, new Vector3(0f, bottom, 0f), Vector3.down);
    }

    private static Vector3[] RingVertices(int sides, float radius, float y)
    {
        Vector3[] ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            ring[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }
        return ring;
    }

    private static void BuildRingBand(Builder builder, Vector3[] lowerRing, Vector3[] upperRing, Vector3 fallback)
    {
        for (int i = 0; i < lowerRing.Length; i++)
        {
            int j = (i + 1) % lowerRing.Length;
            Vector3 outward = (lowerRing[i] + lowerRing[j] + upperRing[i] + upperRing[j]) * 0.25f;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-8f) outward = fallback;
            builder.AddQuad(lowerRing[i], lowerRing[j], upperRing[j], upperRing[i], outward);
        }
    }

    // ================= 构建器 =================

    private sealed class Builder
    {
        private readonly List<Vector3> verts = new List<Vector3>(512);
        private readonly List<Vector2> uvs = new List<Vector2>(512);
        private readonly List<int> tris = new List<int>(1024);

        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-14f) return;
            if (Vector3.Dot(normal, outward) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
                normal = -normal;
            }
            normal.Normalize();
            int start = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            uvs.Add(ProjectUV(a, normal));
            uvs.Add(ProjectUV(b, normal));
            uvs.Add(ProjectUV(c, normal));
            tris.Add(start);
            tris.Add(start + 1);
            tris.Add(start + 2);
        }

        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            AddTriangle(a, b, c, outward);
            AddTriangle(a, c, d, outward);
        }

        public void AddFan(Vector3[] ring, Vector3 center, Vector3 outward)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                int j = (i + 1) % ring.Length;
                AddTriangle(ring[i], ring[j], center, outward);
            }
        }

        public Mesh Finish(string name)
        {
            Mesh mesh = new Mesh();
            mesh.name = name;
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            // 主几何保持硬边不变，额外把"平滑法线"写进 uv3 供描边 pass 沿其外扩
            ApplySmoothNormalsToUv3(mesh);
            return mesh;
        }

        /// <summary>按面法线主轴做平面投影，保证任何三角形都有可用 UV（切线计算需要）。</summary>
        private static Vector2 ProjectUV(Vector3 point, Vector3 normal)
        {
            float ax = Mathf.Abs(normal.x);
            float ay = Mathf.Abs(normal.y);
            float az = Mathf.Abs(normal.z);
            if (ay >= ax && ay >= az) return new Vector2(point.x, point.z);
            if (ax >= az) return new Vector2(point.z, point.y);
            return new Vector2(point.x, point.y);
        }
    }
}
