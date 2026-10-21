using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 只读卡点扫描探针（编辑模式，不改场景、不保存）。
///
/// 判定两类"真卡点"（报告的坐标一律是**推出前**的原始站立位置）：
///  A 对立推力对：胶囊同时嵌进两个碰撞体，且两者的分离方向近似相反（dot &lt; -0.5）且都 &gt; 2cm。
///    这正是 SimpleCollisionResolver 五轮推出互相抵消、永远出不来的形态。再顺带检查它 0.6m 内是否有
///    干净的站立格（有 = 玩家被推一下就能进去，属于可达卡点）。
///  B 无处可走：胶囊本身没有穿透，但 8 个水平方向全被「高过台阶上限的墙」挡住（非穿透型死角）。
///
/// 结果写 Logs/stuck_scan.txt，并把摘要打到 Console。
/// </summary>
public static class LevelStuckProbe
{
    private const string ScenePath = "Assets/Generated/RuinsDemo.unity";
    private const string ReportPath = "Logs/stuck_scan.txt";

    // 与 BuildCharacterV2 / PlayerExplorer 完全一致
    private const float CapsuleRadius = 0.42f;
    private const float CapsuleHeight = 1.95f;
    private const float CapsuleCenterY = 1f;
    private const float GroundOffset = 0.02f;

    // 与 SimpleCollisionResolver 的默认参数一致
    private const int PushIterations = 5;
    private const float MaxVerticalPush = 0.3f;
    private const float StepUpLimit = 0.36f;
    private const float UnstickDistance = 0.6f;
    private const float DepthEpsilon = 0.02f;

    private static readonly RaycastHit[] CastBuffer = new RaycastHit[16];
    private const float BlockedProbeDistance = 0.25f;
    private const float GridStep = 0.10f;
    private const float GroundProbeTop = 3f;
    private const float GroundProbeLength = 12f;

    // 重点区域：蓄水池（含池沿、踏步、下水口、池底石碑周边）
    private const float CisternMinX = 4.10f;
    private const float CisternMaxX = 9.60f;
    private const float CisternMinZ = -2.30f;
    private const float CisternMaxZ = 4.90f;

    // 全场（只做 A 类对立推力检测）
    private const float ArenaHalf = 9.30f;

    private sealed class Solid
    {
        public Collider Collider;
        public Bounds Bounds;
        public string Name;
    }

    private sealed class Hit
    {
        public Vector3 Feet;
        public float Depth;
        public string Solids;
        public bool AdjacentToStandable;
    }

    [MenuItem("Tools/Ruins Guardian/卡点扫描（只读）")]
    public static void ScanFromMenu()
    {
        Scan();
    }

    /// <summary>批处理入口：Unity.exe -batchmode -executeMethod LevelStuckProbe.Scan</summary>
    public static void Scan()
    {
        if (!Application.isBatchMode)
        {
            if (!EditorUtility.DisplayDialog("卡点扫描（只读）",
                    "将加载 " + ScenePath + " 并做网格扫描，不会修改或保存任何资源。继续？", "开始", "取消")) return;
        }

        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject probeObject = new GameObject("__stuck_probe__");
        probeObject.hideFlags = HideFlags.DontSave;
        CapsuleCollider capsule = probeObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = CapsuleRadius;
        capsule.height = CapsuleHeight;
        capsule.center = new Vector3(0f, CapsuleCenterY, 0f);

        List<Solid> solids = CollectSolids(capsule);

        List<Hit> opposing = new List<Hit>();     // A：对立推力对（全场扫描）
        int cells = 0;
        for (float x = -ArenaHalf; x <= ArenaHalf + 0.001f; x += GridStep)
        {
            for (float z = -ArenaHalf; z <= ArenaHalf + 0.001f; z += GridStep)
            {
                cells++;
                if (!TryStandPoint(x, z, out Vector3 feet)) continue;
                if (!TryGetOpposingPair(capsule, solids, feet, out float depth, out string names)) continue;
                opposing.Add(new Hit
                {
                    Feet = feet,
                    Depth = depth,
                    Solids = names,
                    AdjacentToStandable = HasCleanNeighbour(capsule, solids, feet)
                });
            }
        }

        // B：站得住（无穿透）但 8 个方向全被高墙挡住 —— 全场扫描（这才是"角色没站进墙里却走不出去"的形态）
        // 同一次扫描顺带采集"玩家可站立表面高度"，用于填平后的地面高度自检。
        List<Hit> trappedCells = new List<Hit>();
        List<Hit> heightSamples = new List<Hit>();
        int cisternCells = 0;
        int cisternBelowZero = 0;
        for (float x = -ArenaHalf; x <= ArenaHalf + 0.001f; x += GridStep)
        {
            for (float z = -ArenaHalf; z <= ArenaHalf + 0.001f; z += GridStep)
            {
                bool inCistern = x > CisternMinX && x < CisternMaxX && z > CisternMinZ && z < CisternMaxZ;
                if (inCistern) cisternCells++;
                if (!TryStandPoint(x, z, out Vector3 feet, out float surfaceY)) continue;
                // Depth 字段在这里借用来存"脚下地表高度"
                heightSamples.Add(new Hit { Feet = feet, Depth = surfaceY, Solids = inCistern ? "蓄水池" : string.Empty });
                if (inCistern && surfaceY < -0.01f) cisternBelowZero++;
                if (!HasSolidWithin(solids, feet, 0.32f)) continue;                // 周围没实体：不可能被挡
                if (MaxPenetration(capsule, solids, feet) > 0.005f) continue;      // 只看干净站位
                if (CountBlockedDirections(capsule, solids, feet) < 8) continue;   // 只看八向全挡
                trappedCells.Add(new Hit { Feet = feet, Depth = 0f, Solids = string.Empty, AdjacentToStandable = true });
            }
        }

        Object.DestroyImmediate(probeObject);

        string report = BuildReport(cells, opposing, cisternCells, trappedCells, heightSamples, cisternBelowZero);
        string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportPath));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute));
        File.WriteAllText(absolute, report, new UTF8Encoding(false));

        int reachable = 0;
        for (int i = 0; i < opposing.Count; i++) if (opposing[i].AdjacentToStandable) reachable++;
        Debug.Log("[卡点扫描] 全场格=" + cells + " | A 对立推力对=" + opposing.Count + "（紧贴可站立区 " + reachable + "）" +
                  " | B 无穿透八向全挡(全场)=" + trappedCells.Count +
                  " | 地面高度：蓄水池 <0 的格=" + cisternBelowZero + " → " + ReportPath);
        Debug.Log(report);
    }

    private static List<Solid> CollectSolids(CapsuleCollider capsule)
    {
        List<Solid> list = new List<Solid>();
        Collider[] all = Object.FindObjectsOfType<Collider>();
        for (int i = 0; i < all.Length; i++)
        {
            Collider candidate = all[i];
            if (candidate == null || candidate == capsule || candidate.isTrigger) continue;
            if (candidate.GetComponentInParent<Health>() != null) continue;
            list.Add(new Solid { Collider = candidate, Bounds = candidate.bounds, Name = candidate.name });
        }
        return list;
    }

    private static bool TryStandPoint(float x, float z, out Vector3 feet)
    {
        return TryStandPoint(x, z, out feet, out _);
    }

    /// <summary>同时给出站立点与脚下地表高度（hit.point.y），用于地面高度自检。</summary>
    private static bool TryStandPoint(float x, float z, out Vector3 feet, out float surfaceY)
    {
        feet = Vector3.zero;
        surfaceY = 0f;
        if (!Physics.Raycast(new Vector3(x, GroundProbeTop, z), Vector3.down, out RaycastHit hit,
                GroundProbeLength, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (hit.normal.y < 0.5f) return false;
        surfaceY = hit.point.y;
        feet = new Vector3(x, hit.point.y + GroundOffset, z);
        return true;
    }

    /// <summary>找出"同时嵌进两个碰撞体、且分离方向近似相反"的站位（= 推出互相抵消的死点）。</summary>
    private static bool TryGetOpposingPair(CapsuleCollider capsule, List<Solid> solids, Vector3 feet,
        out float depth, out string names)
    {
        depth = 0f;
        names = string.Empty;
        Bounds bounds = CapsuleBounds(feet);
        List<Vector3> directions = new List<Vector3>(4);
        List<float> distances = new List<float>(4);
        List<string> found = new List<string>(4);

        for (int i = 0; i < solids.Count; i++)
        {
            Solid solid = solids[i];
            if (solid.Collider == null || !solid.Collider.enabled) continue;
            if (!solid.Bounds.Intersects(bounds)) continue;
            if (!Physics.ComputePenetration(capsule, feet, Quaternion.identity,
                    solid.Collider, solid.Collider.transform.position, solid.Collider.transform.rotation,
                    out Vector3 direction, out float distance)) continue;
            if (distance <= DepthEpsilon) continue;
            directions.Add(direction.normalized);
            distances.Add(distance);
            found.Add(solid.Name);
        }

        for (int a = 0; a < directions.Count; a++)
        {
            for (int b = a + 1; b < directions.Count; b++)
            {
                if (Vector3.Dot(directions[a], directions[b]) > -0.5f) continue;
                depth = Mathf.Max(distances[a], distances[b]);
                names = found[a] + "+" + found[b];
                return true;
            }
        }
        return false;
    }

    private static float MaxPenetration(CapsuleCollider capsule, List<Solid> solids, Vector3 feet)
    {
        Bounds bounds = CapsuleBounds(feet);
        float max = 0f;
        for (int i = 0; i < solids.Count; i++)
        {
            Solid solid = solids[i];
            if (solid.Collider == null || !solid.Collider.enabled) continue;
            if (!solid.Bounds.Intersects(bounds)) continue;
            if (!Physics.ComputePenetration(capsule, feet, Quaternion.identity,
                    solid.Collider, solid.Collider.transform.position, solid.Collider.transform.rotation,
                    out Vector3 direction, out float distance)) continue;
            if (distance > max) max = distance;
        }
        return max;
    }

    /// <summary>8 个水平方向各有 0.25m 是否被"高过台阶上限的墙"挡住（与解算器的先探再走同判据）。</summary>
    private static int CountBlockedDirections(CapsuleCollider capsule, List<Solid> solids, Vector3 feet)
    {
        int blocked = 0;
        Vector3 point1 = feet + Vector3.up * (CapsuleCenterY + CapsuleHeight * 0.5f - CapsuleRadius);
        Vector3 point2 = feet + Vector3.up * (CapsuleCenterY - CapsuleHeight * 0.5f + CapsuleRadius);
        float steppableTop = feet.y + StepUpLimit + 0.02f;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            int count = Physics.CapsuleCastNonAlloc(point1, point2, CapsuleRadius - 0.02f, direction, CastBuffer,
                BlockedProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
            {
                Collider collider = CastBuffer[h].collider;
                if (collider == null || collider.isTrigger) continue;
                if (collider.GetComponentInParent<Health>() != null) continue;
                if (CastBuffer[h].distance <= 0.005f) continue;
                if (collider.bounds.max.y <= steppableTop) continue;
                if (Mathf.Abs(CastBuffer[h].normal.y) > 0.7f) continue;
                blocked++;
                break;
            }
        }
        return blocked;
    }

    /// <summary>胶囊 AABB 外扩 margin 后是否与任何实体相交（廉价预筛，避免全场都做 8 次胶囊扫描）。</summary>
    private static bool HasSolidWithin(List<Solid> solids, Vector3 feet, float margin)
    {
        Bounds bounds = CapsuleBounds(feet);
        bounds.Expand(margin);
        for (int i = 0; i < solids.Count; i++)
        {
            Solid solid = solids[i];
            if (solid.Collider == null || !solid.Collider.enabled) continue;
            if (solid.Bounds.Intersects(bounds)) return true;
        }
        return false;
    }

    private static bool HasCleanNeighbour(CapsuleCollider capsule, List<Solid> solids, Vector3 feet)
    {
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 candidate = feet + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * UnstickDistance;
            if (MaxPenetration(capsule, solids, candidate) <= 0.005f) return true;
        }
        return false;
    }

    private static Bounds CapsuleBounds(Vector3 feet)
    {
        Vector3 center = feet + new Vector3(0f, CapsuleCenterY, 0f);
        Vector3 size = new Vector3(CapsuleRadius * 2f, CapsuleHeight, CapsuleRadius * 2f);
        return new Bounds(center, size);
    }

    private static string BuildReport(int cells, List<Hit> opposing, int cisternCells, List<Hit> trapped,
        List<Hit> heightSamples, int cisternBelowZero)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("卡点扫描（只读，网格 " + F(GridStep) + "m；坐标均为推出前原始站立位置）");
        builder.AppendLine("全场扫描格=" + cells + " | 蓄水池区域格=" + cisternCells);
        builder.AppendLine();

        AppendHeightSection(builder, heightSamples, cisternBelowZero, cisternCells);
        builder.AppendLine();

        List<Hit> reachable = new List<Hit>();
        List<Hit> isolated = new List<Hit>();
        for (int i = 0; i < opposing.Count; i++)
        {
            if (opposing[i].AdjacentToStandable) reachable.Add(opposing[i]);
            else isolated.Add(opposing[i]);
        }

        builder.AppendLine("== A1 对立推力对 + 紧贴可站立区（玩家可能被推/卡进去，优先修）共 " + reachable.Count + " 格 ==");
        AppendClusters(builder, reachable);
        builder.AppendLine();
        builder.AppendLine("== A2 对立推力对但四周 0.6m 全是实体（多半在墙体内，不可进入）共 " + isolated.Count + " 格 ==");
        AppendClusters(builder, isolated);
        builder.AppendLine();
        builder.AppendLine("== B 无穿透但八向全挡（全场）共 " + trapped.Count + " 格 ==");
        AppendClusters(builder, trapped);
        return builder.ToString();
    }

    /// <summary>地面高度自检：下沉格必须为 0；高于地面的表面按高度聚类列出（高台 / 祭坛台阶 / 矮沿 / 石梁顶等设计内表面）。</summary>
    private static void AppendHeightSection(StringBuilder builder, List<Hit> samples, int cisternBelowZero, int cisternCells)
    {
        int below = 0;
        int ground = 0;
        List<Hit> raised = new List<Hit>();

        for (int i = 0; i < samples.Count; i++)
        {
            float surface = samples[i].Depth;
            if (surface < -0.01f) below++;
            else if (surface <= 0.01f) ground++;
            else raised.Add(samples[i]);
        }

        builder.AppendLine("== 地面高度自检（站立面 hit.point.y；合计 " + samples.Count + " 个可站立格）==");
        builder.AppendLine("  下沉 y < -0.01m（必须为 0）: " + below + " 格");
        builder.AppendLine("  平地 |y| <= 0.01m         : " + ground + " 格");
        builder.AppendLine("  高于地面 y > 0.01m        : " + raised.Count + " 格（见下表，均为设计内的高台/台阶/矮沿/石梁顶）");
        builder.AppendLine("  蓄水池区域（x " + F(CisternMinX) + "~" + F(CisternMaxX) + "，z " + F(CisternMinZ) + "~" + F(CisternMaxZ) +
                           "）：共 " + cisternCells + " 格，其中下沉格 = " + cisternBelowZero + "（必须为 0）");
        if (raised.Count > 0)
        {
            builder.AppendLine("  — 高于地面的表面聚类 —");
            AppendClusters(builder, raised);
        }
    }

    private static void AppendClusters(StringBuilder builder, List<Hit> hits)
    {
        List<Hit> remaining = new List<Hit>(hits);
        int index = 0;
        while (remaining.Count > 0)
        {
            index++;
            List<Hit> cluster = new List<Hit> { remaining[0] };
            remaining.RemoveAt(0);
            bool grown = true;
            while (grown)
            {
                grown = false;
                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    for (int c = 0; c < cluster.Count; c++)
                    {
                        Vector3 a = remaining[i].Feet;
                        Vector3 b = cluster[c].Feet;
                        if (Mathf.Abs(a.x - b.x) <= GridStep * 1.51f &&
                            Mathf.Abs(a.z - b.z) <= GridStep * 1.51f &&
                            Mathf.Abs(a.y - b.y) <= 0.5f)
                        {
                            cluster.Add(remaining[i]);
                            remaining.RemoveAt(i);
                            grown = true;
                            break;
                        }
                    }
                }
            }

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue, maxDepth = 0f;
            HashSet<string> names = new HashSet<string>();
            for (int i = 0; i < cluster.Count; i++)
            {
                Vector3 p = cluster[i].Feet;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                maxDepth = Mathf.Max(maxDepth, cluster[i].Depth);
                if (cluster[i].Solids != null)
                {
                    foreach (string n in cluster[i].Solids.Split('+')) if (n.Length > 0) names.Add(n);
                }
            }

            builder.AppendLine("#" + index + " x[" + F(minX) + ", " + F(maxX) + "] z[" + F(minZ) + ", " + F(maxZ) +
                               "] 脚底y[" + F(minY) + ", " + F(maxY) + "] 格数=" + cluster.Count +
                               " 深度/高度=" + F(maxDepth) + "m 碰撞体=" +
                               (names.Count == 0 ? "-" : string.Join("+", new List<string>(names).ToArray())));
        }
    }

    private static string F(float value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
