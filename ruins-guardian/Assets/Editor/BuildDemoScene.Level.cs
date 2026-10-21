using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 关卡层生成器（partial 文件）：地形可行走的碰撞地板、3 块符文石碑、北侧石门与门后内容、
/// 波次增援哨兵、治疗瓶模板、关卡调度组件。
/// 由调度者在 Build() 里调用（不修改 BuildDemoScene.cs / .World.cs）：
///     BuildGameplayV2(player.transform, new[] { guard1, guard2, guard3 });
/// 地形坐标只从 World.cs 读取（主庭院 y=0、北侧高台 y=+1.6、蓄水池底 y=-0.6），不写回。
/// </summary>
public static partial class BuildDemoScene
{
    // ===== 与 BuildDemoScene.World.cs 对齐的关键数值（World 里是 private const，这里本地化一份） =====
    private const float LevelCisternFloorY = 0f;      // 蓄水池底（T6-5 起填平，与庭院同为 y=0）
    private const float LevelCisternMinX = 4.8f;
    private const float LevelCisternMaxX = 9.4f;
    private const float LevelCisternMinZ = -1.6f;
    private const float LevelCisternMaxZ = 4.4f;

    /// <summary>关卡层总入口。</summary>
    internal static void BuildGameplayV2(Transform player, GameObject[] guards)
    {
        if (player == null)
        {
            Debug.LogWarning("BuildGameplayV2 缺少玩家引用，已跳过关卡层生成。");
            return;
        }

        Transform root = new GameObject("关卡层").transform;

        // ---------- 1. 地形可行走：补一层不可见碰撞地板 ----------
        BuildWalkableFloorV2(root);

        // ---------- 2. 环境平行面窄缝填死 ----------
        // （T6-5）原「蓄水池进出踏步」（下水石台 + 池底三级踏步）与「池底内角 45° 斜切」整体移除：
        // 蓄水池已填平为 y=0、池沿降到 0.26m，既没有下沉坑，也没有需要斜切的直角池壁。
        BuildGapFillersV2(root);

        // ---------- 2·补三. 探针 B 类剩余格子的填缝 ----------
        BuildColonnadeFillV2(root);

        // ---------- 3. 关卡材质（全部走 ShaderKit，落成 .mat 资源） ----------
        Material runeDormant = ShaderKit.Emissive("LevelRuneDormant", new Color(0.16f, 0.32f, 0.30f), 0.2f);
        Material runeActive = ShaderKit.Emissive("LevelRuneActive", new Color(0.275f, 0.878f, 0.784f), 4.5f);
        Material beamMaterial = ShaderKit.Emissive("LevelRuneBeam", new Color(0.275f, 0.878f, 0.784f), 2.4f);
        Material boltCore = ShaderKit.Emissive("LevelBoltCore", new Color(0.35f, 0.95f, 0.88f), 6f);
        Material shockMaterial = ShaderKit.Emissive("LevelShockwave", new Color(1f, 0.55f, 0.30f), 3.2f);
        Material orbDim = ShaderKit.Emissive("LevelChargeDim", new Color(0.30f, 0.60f, 0.58f), 0.8f);
        Material orbHot = ShaderKit.Emissive("LevelChargeHot", new Color(0.55f, 1f, 0.92f), 7f);
        Material healGlow = ShaderKit.Emissive("LevelHealGlow", new Color(0.45f, 1f, 0.55f), 2.6f);
        Material debrisMaterial = ShaderKit.Emissive("LevelDebris", new Color(0.52f, 0.17f, 0.13f), 1.4f);

        // ---------- 4. 模板（顺序：先模板，再石门，再石碑） ----------
        GameObject healTemplate = BuildHealTemplateV2(root, player, healGlow);
        GameObject boltTemplate = BuildBoltTemplateV2(root, boltCore);
        RuneGate gate = BuildRuneGateV2(root, player, runeActive, healTemplate, debrisMaterial, shockMaterial);
        RunePillar[] pillars = BuildRunePillarsV2(root, player, gate, runeDormant, runeActive, beamMaterial);
        gate.SetPillars(pillars);

        // ---------- 5. 波次增援：2 名 Sentinel（南侧拱门） ----------
        Transform[] spawnPoints;
        GameObject[] sentinels = BuildSentinelTemplatesV2(root, player, boltTemplate, orbDim, orbHot, out spawnPoints);

        // ---------- 6. 调度组件 ----------
        BuildDirectorsV2(root, player, sentinels, spawnPoints);

        Debug.Log("关卡层生成完成：3 块符文石碑 / 1 座石门 / " + sentinels.Length + " 名增援哨兵 / 治疗瓶模板");
    }

    // =====================================================================
    // 一、可行走地板
    // =====================================================================

    /// <summary>
    /// V2 世界的庭院铺装与蓄水池底都是 withCollider=false 的纯装饰网格（World.cs 有意省碰撞），
    /// 向下射线会直接穿过去，因此这里补一套不可见碰撞地板。只加碰撞，不改 World.cs。
    /// </summary>
    private static void BuildWalkableFloorV2(Transform parent)
    {
        GameObject group = new GameObject("可行走地板（碰撞）");
        group.transform.SetParent(parent, false);

        // 主庭院（西半区，含高台与前庭）
        CreateColliderBoxV2(group.transform, "地板 主庭院", new Vector3(-2.4f, -0.25f, 0f), new Vector3(14.4f, 0.5f, 19.2f));
        // 主庭院东侧：蓄水池以北、以南两片（蓄水池本身是下沉的，不能盖住）
        CreateColliderBoxV2(group.transform, "地板 庭院东北", new Vector3(7.2f, -0.25f, 7.0f), new Vector3(4.8f, 0.5f, 5.2f));
        CreateColliderBoxV2(group.transform, "地板 庭院东南", new Vector3(7.2f, -0.25f, -5.6f), new Vector3(4.8f, 0.5f, 8.0f));
        // 蓄水池底（y=-0.6 是有效地面）
        float poolCenterX = (LevelCisternMinX + LevelCisternMaxX) * 0.5f;
        float poolCenterZ = (LevelCisternMinZ + LevelCisternMaxZ) * 0.5f;
        CreateColliderBoxV2(group.transform, "地板 蓄水池底",
            new Vector3(poolCenterX, LevelCisternFloorY - 0.15f, poolCenterZ),
            new Vector3(LevelCisternMaxX - LevelCisternMinX, 0.3f, LevelCisternMaxZ - LevelCisternMinZ));
        // （T6-5）原「地板 池边台座」不可见碰撞盒已删除：蓄水池填平后它与地面同高，留着只会造成
        // "隐形台阶"的错觉；现在蓄水池区域由「地板 蓄水池底」与庭院地板一起覆盖，顶面统一在 y=0。
    }

    private static GameObject CreateColliderBoxV2(Transform parent, string name, Vector3 center, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = size;
        return go;
    }

    // =====================================================================
    // 一·补：死缝填充
    // =====================================================================

    /// <summary>
    /// 死缝填充。判定规则（胶囊直径 0.84 / 半径 0.42）：
    /// 缝宽 0.28~0.60m 且两侧是**平行平面**时最危险 —— 胶囊挤得进去一半，两侧 ComputePenetration 的
    /// 推出方向正好相反、互相抵消，就是"卡住出不去"；若四面包围（U 形 / 方形凹角）则必然抵消。
    /// 处置原则：宁可让玩家绕路，也不能让角色挤进去；填充物优先做成遗迹风格的碎块 / 柱础，不用突兀方盒。
    /// </summary>
    private static void BuildGapFillersV2(Transform parent)
    {
        GameObject group = new GameObject("死缝填充");
        group.transform.SetParent(parent, false);

        // F1：倒伏石梁(5.2, 0.3, 5.6, 偏转 -15°) 西端 与 高台石阶外侧挡墙(x 3.93~4.11, z 6.55~8.45) 之间
        //     在 z 6.6~7.1 只剩 0.41m → 补一块石墩（视觉上读作阶侧残石，同时把缝堵死）。
        LevelPart("阶侧残石", group.transform, new Vector3(4.335f, 0.5f, 6.80f), new Vector3(0.57f, 1.0f, 0.64f),
            stoneMat, PrimitiveType.Cube, true);

        // F2：西侧回廊列柱(-8.3, -1.0，碰撞 0.84²) 与 回廊中段火盆(-8.5, -2.3，直径 0.88) 之间剩 0.44m。
        //     缝很窄，加可见几何反而突兀，用不可见碰撞体补死。
        CreateColliderBoxV2(group.transform, "柱盆夹缝", new Vector3(-8.5f, 0.3f, -1.64f), new Vector3(0.90f, 0.6f, 0.48f));

        // F3（场地右侧卡点主因）：祭坛四角符文柱(±1.35, 0.26², 高 1.8) 与 祭坛顶台(x/z ±0.90, 顶面 0.84) 之间，
        //     是一个 **0.32×0.32m、四面封闭** 的方形凹角：西/南面是顶台、东/北面是柱身，两对平行面推力两两相反，
        //     胶囊中心一旦落进这个 0.32 方块（站上第二层台基后中心可以进去），五轮推出会互相抵消 → 永久卡死。
        //     处置：在 4 个角补「柱础」—— 0.49×0.24×0.49 的倒角石墩，顶面与顶台齐平(0.84)、内侧咬进顶台 0.04、
        //     外侧咬进柱身 0.13，把凹角整个填平；外观是柱脚的石础，不是凑数的方盒。
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                GameObject collar = LevelMeshPart("祭坛柱础", group.transform,
                    new Vector3(sx * 1.105f, 0.72f, sz * 1.105f), Vector3.one,
                    MeshKit.BeveledBox(new Vector3(0.49f, 0.24f, 0.49f), 0.03f, 1), stoneMat);
                collar.AddComponent<BoxCollider>().size = new Vector3(0.49f, 0.24f, 0.49f);
            }
        }

        // F4（场地最右侧）：庭院最东侧火盆(7.6, 7.8, 直径 0.88) 与 东北角塔内角(8.30, 8.30) 之间
        //     只剩 0.42m 的对角缝。圆柱与方角虽都是凸形，但缝窄于胶囊直径，斜冲时会被两件同时顶住并来回弹。
        //     处置：在塔基补一块 0.40×0.50×0.40 的倒角碎石（偏转 18° 读作塌落石块），
        //     南侧咬进火盆 0.06、东/北侧咬进塔基 0.05，缝彻底消失。
        GameObject rubble = LevelMeshPart("塔基碎石", group.transform, new Vector3(8.15f, 0.25f, 8.15f), Vector3.one,
            MeshKit.BeveledBox(new Vector3(0.40f, 0.50f, 0.40f), 0.05f, 1), stoneMat);
        rubble.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);
        rubble.AddComponent<BoxCollider>().size = new Vector3(0.40f, 0.50f, 0.40f);

        // F5（场地最右侧的塔基缝，最可能的"最右侧柱子"卡点）：
        //   东墙扶壁(x 8.95~11.05, z -8.00~-7.40, 高 6.6) 的**南面** 与 东南角塔**北面**(z -8.30, x 8.30~11.70)
        //   之间只剩 0.30m 的平行缝，正好落在 0.28~0.6 危险带；两者都是高墙，被挤进去时
        //   一个往 +z 推、一个往 -z 推，五轮推出互相抵消 = 永久卡死（击退 / 竖直推出把人送进去后就会发生）。
        //   处置：在 x 8.90~9.50 补一块 0.60×1.20×0.45 的墙角残石，南面咬进塔基 0.05、北面咬进扶壁 0.05、
        //   东面咬进城墙 0.05，缝彻底消失；外观是塔基下的塌落石块。
        GameObject cornerStone = LevelMeshPart("塔基残石 东南", group.transform, new Vector3(9.20f, 0.6f, -8.15f), Vector3.one,
            MeshKit.BeveledBox(new Vector3(0.60f, 1.20f, 0.45f), 0.05f, 1), stoneMat);
        cornerStone.AddComponent<BoxCollider>().size = new Vector3(0.60f, 1.20f, 0.45f);

        // F6：同型缝在西南角塔（西墙扶壁 x -11.05~-8.95 / z -8.00~-7.40 ↔ 西南角塔北面 z -8.30）同样存在 0.30m，
        //   一并填掉，避免换个角度又卡在另一侧。
        GameObject cornerStoneWest = LevelMeshPart("塔基残石 西南", group.transform, new Vector3(-9.20f, 0.6f, -8.15f), Vector3.one,
            MeshKit.BeveledBox(new Vector3(0.60f, 1.20f, 0.45f), 0.05f, 1), stoneMat);
        cornerStoneWest.AddComponent<BoxCollider>().size = new Vector3(0.60f, 1.20f, 0.45f);

        // F7：西北角塔东面(x -8.30) ↔ 北墙扶壁西面(x -8.00, z 8.95~11.05) 也是 0.30m 平行缝（玩家 z 被钳制在 9.2，
        //   只露出 z 8.95~9.20 的浅口袋，但击退/竖直推出仍可能把人送进去）→ 同样补墙角残石。
        GameObject cornerStoneNorthWest = LevelMeshPart("塔基残石 西北", group.transform, new Vector3(-8.15f, 0.6f, 9.275f), Vector3.one,
            MeshKit.BeveledBox(new Vector3(0.40f, 1.20f, 0.65f), 0.05f, 1), stoneMat);
        cornerStoneNorthWest.AddComponent<BoxCollider>().size = new Vector3(0.40f, 1.20f, 0.65f);
    }

    // =====================================================================
    // 一·补二：全场"无穿透八向全挡"格子的填缝（探针 B 类）
    // =====================================================================

    /// <summary>
    /// 只读探针全场扫描（3.5 万格）后，B 类（站得住、但 8 个方向全被高过台阶的墙挡住）只剩 2 格：
    ///   (-8.90, 0.02, 3.10) 与 (-8.20, 0.02, 3.10)  —— 西侧回廊里，z=3.10 正好落在列柱(z=1.6 / 4.2)之间的空档，
    ///   西边是城墙、南北是列柱、东南是坍塌拱券碎石，胶囊被四面包在 1.3m 见方的坑里。
    /// 处置：补一块 0.90×1.10×0.30 的倒角塌石（视觉上是回廊里的落石），把这两格连成实体。
    /// 位置选在列柱之间的空档，不堵任何可通行路线（列柱与城墙之间只剩 0.73m < 胶囊 0.84m，本来就过不去）。
    /// </summary>
    private static void BuildColonnadeFillV2(Transform parent)
    {
        GameObject group = new GameObject("回廊落石");
        group.transform.SetParent(parent, false);

        GameObject debris = LevelMeshPart("回廊落石", group.transform, new Vector3(-8.55f, 0.55f, 3.10f), Vector3.one,
            MeshKit.BeveledBox(new Vector3(0.90f, 1.10f, 0.30f), 0.06f, 1), stoneMat);
        debris.transform.localRotation = Quaternion.Euler(0f, 8f, 0f);
        debris.AddComponent<BoxCollider>().size = new Vector3(0.90f, 1.10f, 0.30f);
    }

    // =====================================================================
    // 二、符文石碑
    // =====================================================================

    private static RunePillar[] BuildRunePillarsV2(Transform parent, Transform player, RuneGate gate,
        Material dormant, Material active, Material beamMaterial)
    {
        // 选址约束（避免"宽度介于胶囊半径与直径之间"的死缝）：
        // 与任何其它碰撞体的间隙必须 >= 1.2m（可通行）或 ~0（贴死不可进入），不能落在 0.28~0.6m。
        Vector3[] spots =
        {
            new Vector3(-5.0f, 0f, -1.6f),                 // 西侧回廊内侧（离倒塌拱券 1.5m、离列柱 2.3m、离祭坛 2.6m）
            new Vector3(3.2f, 0f, -5.4f),                  // 主庭院东南（离雕像 1.36m、离倒伏石梁 2.27m）
            new Vector3(8.44f, LevelCisternFloorY, 2.0f)   // 蓄水池底靠东池壁：东侧贴死（无缝），西侧 1.34m 通行、北侧离斜切 0.69m
        };
        string[] labels = { "回廊石碑", "庭院石碑", "池底石碑" };

        RunePillar[] pillars = new RunePillar[spots.Length];
        for (int i = 0; i < spots.Length; i++)
        {
            pillars[i] = BuildRunePillarV2(parent, player, gate, labels[i], spots[i], stoneMat, dormant, active, beamMaterial);
        }
        return pillars;
    }

    private static RunePillar BuildRunePillarV2(Transform parent, Transform player, RuneGate gate, string label,
        Vector3 groundPoint, Material stone, Material dormant, Material active, Material beamMaterial)
    {
        GameObject root = new GameObject("符文石碑 " + label);
        root.transform.SetParent(parent, false);
        root.transform.position = groundPoint;

        RunePillar pillar = root.AddComponent<RunePillar>();

        // 基座（带碰撞，读作实心石座）
        LevelPart("基座", root.transform, new Vector3(0f, 0.18f, 0f), new Vector3(1.12f, 0.36f, 1.12f),
            stone, PrimitiveType.Cube, true);
        LevelPart("基座压顶", root.transform, new Vector3(0f, 0.4f, 0f), new Vector3(1.28f, 0.08f, 1.28f),
            stone, PrimitiveType.Cube, true);

        // 升降体（激活后抬升 0.45m）
        GameObject rise = new GameObject("升降体");
        rise.transform.SetParent(root.transform, false);
        rise.transform.localPosition = new Vector3(0f, 0.44f, 0f);
        LevelPart("碑身", rise.transform, new Vector3(0f, 0.78f, 0f), new Vector3(0.66f, 1.56f, 0.30f),
            stone, PrimitiveType.Cube, false);
        GameObject runePlate = LevelPart("符文面", rise.transform, new Vector3(0f, 0.86f, -0.17f),
            new Vector3(0.36f, 1.02f, 0.04f), dormant, PrimitiveType.Cube, false);

        // 光柱（未激活时隐藏）
        GameObject beam = LevelPart("光柱", root.transform, new Vector3(0f, 1.9f, 0f), new Vector3(0.7f, 1.7f, 0.7f),
            beamMaterial, PrimitiveType.Cylinder, false);

        // 符文点光
        GameObject lightObject = new GameObject("符文光");
        lightObject.transform.SetParent(root.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        Light runeLight = lightObject.AddComponent<Light>();
        runeLight.type = LightType.Point;
        runeLight.color = new Color(0.275f, 0.878f, 0.784f);
        runeLight.range = 7f;
        runeLight.intensity = 0f;
        runeLight.enabled = false;

        pillar.Configure(player, gate, runePlate.GetComponent<MeshRenderer>(), dormant, active,
            rise.transform, beam.transform, runeLight, label);
        return pillar;
    }

    // =====================================================================
    // 三、北侧石门 + 门后内容
    // =====================================================================

    private static RuneGate BuildRuneGateV2(Transform parent, Transform player, Material runeMaterial,
        GameObject healTemplate, Material debrisMaterial, Material shockMaterial)
    {
        GameObject root = new GameObject("北侧石门");
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(0f, 0f, 6.7f);

        RuneGate gate = root.AddComponent<RuneGate>();

        Material stone = stoneMat;

        // 门框：双柱 + 门楣（都带碰撞；门洞净高 2.5m，玩家胶囊 1.95m 可通过）
        LevelPart("门柱 左", root.transform, new Vector3(-1.6f, 1.25f, 0f), new Vector3(0.36f, 2.5f, 0.5f), stone, PrimitiveType.Cube, true);
        LevelPart("门柱 右", root.transform, new Vector3(1.6f, 1.25f, 0f), new Vector3(0.36f, 2.5f, 0.5f), stone, PrimitiveType.Cube, true);
        LevelPart("门楣", root.transform, new Vector3(0f, 2.65f, 0f), new Vector3(3.9f, 0.30f, 0.6f), stone, PrimitiveType.Cube, true);
        LevelPart("门楣压顶", root.transform, new Vector3(0f, 2.86f, 0f), new Vector3(4.3f, 0.14f, 0.8f), stone, PrimitiveType.Cube, true);
        LevelPart("门柱符文 左", root.transform, new Vector3(-1.6f, 1.30f, -0.27f), new Vector3(0.10f, 1.5f, 0.04f), runeMaterial, PrimitiveType.Cube, false);
        LevelPart("门柱符文 右", root.transform, new Vector3(1.6f, 1.30f, -0.27f), new Vector3(0.10f, 1.5f, 0.04f), runeMaterial, PrimitiveType.Cube, false);

        // 凹室侧墙（后墙就是高台本体 z=8.4）
        LevelPart("凹室墙 左", root.transform, new Vector3(-1.6f, 1.25f, 0.85f), new Vector3(0.36f, 2.5f, 1.7f), stone, PrimitiveType.Cube, true);
        LevelPart("凹室墙 右", root.transform, new Vector3(1.6f, 1.25f, 0.85f), new Vector3(0.36f, 2.5f, 1.7f), stone, PrimitiveType.Cube, true);

        // 侧翼扶壁：填掉「凹室外墙 (x=±1.78) 与高台石阶内侧 (x=±2.80)」之间那条
        // 1.02m 宽 × 1.70m 深的 U 形死角（0.84m 胶囊只有 9cm 余量，斜着推进会两墙互相顶住）。
        // 高 1.55m（不是 1.2m）：扶壁顶面与上方「踏面碰撞体」（底面 1.55）刚好贴合，
        // 否则两者之间会留一条 0.35m 高的挤压空腔，角色被顶上去后头顶卡在踏面底下。
        LevelPart("侧翼扶壁 左", root.transform, new Vector3(-2.29f, 0.775f, 0.85f), new Vector3(1.02f, 1.55f, 1.7f), stone, PrimitiveType.Cube, true);
        LevelPart("侧翼扶壁 右", root.transform, new Vector3(2.29f, 0.775f, 0.85f), new Vector3(1.02f, 1.55f, 1.7f), stone, PrimitiveType.Cube, true);

        // 石门本体：高 1.6m，开启时整体下沉 1.6m（门洞上方仍留 0.9m 的开口，可看出门后的宝石）
        GameObject slabRoot = new GameObject("石门");
        slabRoot.transform.SetParent(root.transform, false);
        slabRoot.transform.localPosition = Vector3.zero;
        GameObject slabBody = LevelPart("门板", slabRoot.transform, new Vector3(0f, 0.8f, 0f),
            new Vector3(2.84f, 1.6f, 0.30f), stone, PrimitiveType.Cube, true);
        BoxCollider slabCollider = slabBody.GetComponent<BoxCollider>();
        LevelPart("门面浮雕", slabRoot.transform, new Vector3(0f, 0.8f, -0.17f),
            new Vector3(1.60f, 0.90f, 0.04f), runeMaterial, PrimitiveType.Cube, false);

        // 门后的宝石（一开始就激活：GameManager 在 Awake 统计宝石总数，藏起来会导致无法通关）
        GameObject gem = CreateLevelGemV2("门后宝石", new Vector3(0f, 0.95f, 8.05f), player);
        gem.transform.SetParent(root.transform, true);

        // 门后的精英守卫（石门开启后由 RuneGate 激活）
        Vector3[] brutePatrol =
        {
            new Vector3(0f, 0f, 7.55f), new Vector3(0.7f, 0f, 6.9f), new Vector3(-0.7f, 0f, 7.0f)
        };
        GameObject brute = BuildBruteV2(root.transform, "精英守卫 石室", new Vector3(0f, 0f, 7.55f), brutePatrol,
            player, healTemplate, debrisMaterial, shockMaterial);
        brute.SetActive(false);

        // 门缝光
        GameObject lightObject = new GameObject("门缝光");
        lightObject.transform.SetParent(root.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 1.2f, -0.45f);
        Light gateLight = lightObject.AddComponent<Light>();
        gateLight.type = LightType.Point;
        gateLight.color = new Color(0.275f, 0.878f, 0.784f);
        gateLight.range = 7f;
        gateLight.intensity = 0f;
        gateLight.enabled = false;

        gate.Configure(new RunePillar[0], slabRoot.transform, slabCollider, new[] { brute }, gateLight);
        return gate;
    }

    // =====================================================================
    // 四、新敌人：Sentinel / Brute
    // =====================================================================

    private static GameObject BuildSentinelV2(Transform parent, string name, Vector3 position, Vector3[] waypoints,
        Transform player, GameObject boltTemplate, Material orbDim, Material orbHot)
    {
        GameObject root = BuildCharacterV2(name, position, new Color(0.20f, 0.34f, 0.40f), false);
        root.transform.SetParent(parent, true);

        Health health = root.AddComponent<Health>();
        SetPrivate(health, "maxHealth", 3f);
        SetPrivate(health, "isPlayer", false);
        SetPrivate(health, "invulnerableTime", 0.1f);

        SentinelAI ai = root.AddComponent<SentinelAI>();
        root.AddComponent<SimpleCollisionResolver>();

        // 发射点挂在 Chest 上（不随手臂动画抖动）
        Transform chest = root.transform.Find("Visual/Body/Chest");
        Transform anchor = chest != null ? chest : root.transform;
        GameObject muzzle = new GameObject("发射点");
        muzzle.transform.SetParent(anchor, false);
        muzzle.transform.localPosition = chest != null ? new Vector3(0.24f, 0.30f, 0.34f) : new Vector3(0f, 1.35f, 0.4f);

        GameObject orb = LevelPart("蓄力球", muzzle.transform, Vector3.zero, new Vector3(0.18f, 0.18f, 0.18f),
            orbDim, PrimitiveType.Sphere, false);
        MeshRenderer orbRenderer = orb.GetComponent<MeshRenderer>();

        GameObject lightObject = new GameObject("蓄力灯");
        lightObject.transform.SetParent(muzzle.transform, false);
        Light chargeLight = lightObject.AddComponent<Light>();
        chargeLight.type = LightType.Point;
        chargeLight.color = new Color(0.42f, 1f, 0.92f);
        chargeLight.range = 5f;
        chargeLight.intensity = 0f;
        chargeLight.enabled = false;

        Transform[] points = CreatePatrolPointsV2(name, waypoints);
        ai.Configure(player, points, boltTemplate, muzzle.transform, orb.transform, orbRenderer, orbDim, orbHot, chargeLight);
        BuildEnemyHealthBarV2(root, 2.15f, new Color(0.35f, 0.90f, 0.85f));
        return root;
    }

    private static GameObject BuildBruteV2(Transform parent, string name, Vector3 position, Vector3[] waypoints,
        Transform player, GameObject healTemplate, Material debrisMaterial, Material shockMaterial)
    {
        GameObject root = BuildCharacterV2(name, position, new Color(0.30f, 0.13f, 0.10f), false);
        root.transform.SetParent(parent, true);

        // 只放大视觉体（缩放根节点会被 Health 的死亡溶解 / Restore 覆盖）
        Transform visual = root.transform.Find("Visual");
        if (visual != null) visual.localScale = new Vector3(1.20f, 1.12f, 1.20f);

        Health health = root.AddComponent<Health>();
        SetPrivate(health, "maxHealth", 8f);
        SetPrivate(health, "isPlayer", false);
        SetPrivate(health, "invulnerableTime", 0.1f);

        BruteAI ai = root.AddComponent<BruteAI>();
        root.AddComponent<SimpleCollisionResolver>();

        // 冲击波圆环
        GameObject ringObject = new GameObject("冲击波");
        ringObject.transform.SetParent(root.transform, false);
        ringObject.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        ShockwaveRing ring = ringObject.AddComponent<ShockwaveRing>();
        GameObject ringVisual = LevelMeshPart("圆环", ringObject.transform, Vector3.zero, Vector3.one,
            MeshKit.Ring(0.82f, 1f, 0.14f, 28), shockMaterial);
        ring.Configure(ringVisual.transform, 0.45f);

        Transform[] points = CreatePatrolPointsV2(name, waypoints);
        ai.Configure(player, points, ring, healTemplate, debrisMaterial);
        BuildEnemyHealthBarV2(root, 2.35f, new Color(0.85f, 0.29f, 0.23f));
        return root;
    }

    private static GameObject[] BuildSentinelTemplatesV2(Transform parent, Transform player, GameObject boltTemplate,
        Material orbDim, Material orbHot, out Transform[] spawnPoints)
    {
        Vector3[] spawnPositions =
        {
            new Vector3(1.35f, 0f, -8.7f),
            new Vector3(-1.35f, 0f, -8.7f)
        };
        Vector3[][] routes =
        {
            new[] { new Vector3(2.6f, 0f, -6.4f), new Vector3(0.6f, 0f, -4.4f), new Vector3(3.6f, 0f, -3.0f) },
            new[] { new Vector3(-2.6f, 0f, -6.4f), new Vector3(-0.8f, 0f, -4.6f), new Vector3(-4.2f, 0f, -6.8f) }
        };

        spawnPoints = new Transform[spawnPositions.Length];
        GameObject[] templates = new GameObject[spawnPositions.Length];
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            GameObject point = new GameObject("增援点 " + (i + 1));
            point.transform.SetParent(parent, false);
            point.transform.position = spawnPositions[i];
            point.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            spawnPoints[i] = point.transform;

            templates[i] = BuildSentinelV2(parent, "哨兵 " + (i + 1), spawnPositions[i], routes[i], player, boltTemplate, orbDim, orbHot);
            templates[i].SetActive(false);   // 拾取第 3 颗宝石时由 WaveDirector 激活
        }
        return templates;
    }

    // =====================================================================
    // 五、投射物 / 掉落物模板
    // =====================================================================

    private static GameObject BuildBoltTemplateV2(Transform parent, Material boltMaterial)
    {
        GameObject root = new GameObject("能量弹模板");
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(0f, -60f, 0f);

        GameObject visual = LevelPart("弹体", root.transform, Vector3.zero, new Vector3(0.22f, 0.22f, 0.42f),
            boltMaterial, PrimitiveType.Sphere, false);

        GameObject lightObject = new GameObject("弹体光");
        lightObject.transform.SetParent(root.transform, false);
        Light boltLight = lightObject.AddComponent<Light>();
        boltLight.type = LightType.Point;
        boltLight.color = new Color(0.42f, 1f, 0.95f);
        boltLight.range = 3.5f;
        boltLight.intensity = 2.4f;

        EnergyBolt bolt = root.AddComponent<EnergyBolt>();
        SetPrivate(bolt, "visual", visual.transform);
        SetPrivate(bolt, "boltLight", boltLight);

        root.SetActive(false);
        return root;
    }

    private static GameObject BuildHealTemplateV2(Transform parent, Transform player, Material glow)
    {
        GameObject root = new GameObject("治疗瓶模板");
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(0f, -60f, 0f);

        LevelPart("瓶身", root.transform, Vector3.zero, new Vector3(0.18f, 0.13f, 0.18f), glow, PrimitiveType.Cylinder, false);
        LevelPart("瓶颈", root.transform, new Vector3(0f, 0.17f, 0f), new Vector3(0.07f, 0.05f, 0.07f), metalMat, PrimitiveType.Cylinder, false);

        GameObject lightObject = new GameObject("治疗光");
        lightObject.transform.SetParent(root.transform, false);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.45f, 1f, 0.55f);
        light.range = 3f;
        light.intensity = 1.6f;

        HealPickup pickup = root.AddComponent<HealPickup>();
        pickup.Configure(player, 30f);

        root.SetActive(false);
        return root;
    }

    // =====================================================================
    // 六、宝石 / 血条 / 调度
    // =====================================================================

    private static GameObject CreateLevelGemV2(string name, Vector3 position, Transform player)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;

        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(model.GetComponent<Collider>());
        model.name = "宝石模型";
        model.transform.SetParent(root.transform, false);
        model.transform.rotation = Quaternion.Euler(45f, 0f, 45f);
        model.transform.localScale = Vector3.one * 0.45f;
        model.GetComponent<Renderer>().sharedMaterial = gemMat;

        GameObject lightObject = new GameObject("宝石光");
        lightObject.transform.SetParent(root.transform, false);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.3f, 0.9f, 1f);
        light.range = 4f;
        light.intensity = 1.2f;

        Collectible collectible = root.AddComponent<Collectible>();
        SetPrivate(collectible, "player", player);
        SetPrivate(collectible, "gemLight", light);
        SetPrivate(collectible, "gemModel", model.transform);
        return root;
    }

    private static EnemyHealthBar BuildEnemyHealthBarV2(GameObject enemy, float height, Color fillColor)
    {
        GameObject barRoot = new GameObject("头顶血条");
        barRoot.transform.SetParent(enemy.transform, false);
        barRoot.transform.localPosition = new Vector3(0f, height, 0f);

        GameObject canvasObject = new GameObject("血条 Canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(barRoot.transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(120f, 16f);
        rect.localScale = Vector3.one * 0.008f;
        rect.localPosition = Vector3.zero;

        GameObject background = CreateUiImage(rect, "血条底", new Color(0.05f, 0.05f, 0.06f, 0.78f));
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        GameObject fillObject = CreateUiImage(rect, "血条填充", fillColor);
        Image fill = fillObject.GetComponent<Image>();
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        EnemyHealthBar bar = enemy.AddComponent<EnemyHealthBar>();
        bar.Configure(enemy.GetComponent<Health>(), canvas, fill, false);
        return bar;
    }

    private static void BuildDirectorsV2(Transform parent, Transform player, GameObject[] sentinels, Transform[] spawnPoints)
    {
        GameObject root = new GameObject("关卡调度");
        root.transform.SetParent(parent, false);

        WaveDirector wave = root.AddComponent<WaveDirector>();
        wave.Configure(player, sentinels, spawnPoints);

        Health playerHealth = player.GetComponent<Health>();
        PlayerExplorer explorer = player.GetComponent<PlayerExplorer>();
        if (playerHealth != null)
        {
            ReviveRuneSystem revive = root.AddComponent<ReviveRuneSystem>();
            revive.Configure(playerHealth, explorer, 1);
        }
    }

    // =====================================================================
    // 七、小工具
    // =====================================================================

    /// <summary>生成一个基元部件（默认去掉碰撞，只有 withCollider=true 时才保留）。</summary>
    private static GameObject LevelPart(string name, Transform parent, Vector3 localPosition, Vector3 localScale,
        Material material, PrimitiveType type, bool withCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;

        if (!withCollider)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }
        return go;
    }

    /// <summary>生成一个 MeshKit 网格部件（没有碰撞）。</summary>
    private static GameObject LevelMeshPart(string name, Transform parent, Vector3 localPosition, Vector3 localScale,
        Mesh mesh, Material material)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return go;
    }

    private static Transform[] CreatePatrolPointsV2(string ownerName, Vector3[] positions)
    {
        if (positions == null || positions.Length == 0) return new Transform[0];
        Transform[] points = new Transform[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            GameObject point = new GameObject(ownerName + " 巡逻点 " + (i + 1));
            point.transform.position = positions[i];
            points[i] = point.transform;
        }
        return points;
    }
}
