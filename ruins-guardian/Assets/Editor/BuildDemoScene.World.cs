using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// T2 世界模块：把 20×20 方盒子竞技场升级为"遗迹庭院"。
/// 地形：主庭院（y=0）/ 北侧高台（y=+1.6，仅视觉与台阶可达）/ 东侧蓄水池（**已填平，与庭院同为 y=0**）。
/// 说明：T6-5 起蓄水池不再下沉（用户要求"地面高度改成一样的平地"），只保留 0.26m 矮沿做视觉分区。
/// 所有几何体都走 MeshBatchV2 合并，物件数远低于 1200；墙体/柱子/石梁统一用 BoxCollider 阻挡。
/// </summary>
public static partial class BuildDemoScene
{
    // ================= 规范 5.2 调色板（禁止自创色） =================
    private static readonly Color V2StoneLight = new Color(0.784f, 0.753f, 0.682f); // #C8C0AE 石材本体
    private static readonly Color V2StoneDark = new Color(0.541f, 0.518f, 0.471f);  // #8A8478 石材暗部
    private static readonly Color V2Moss = new Color(0.373f, 0.478f, 0.290f);       // #5F7A4A 苔痕
    private static readonly Color V2FireWarm = new Color(1.000f, 0.690f, 0.400f);   // #FFB066 火光
    private static readonly Color V2FireDeep = new Color(1.000f, 0.478f, 0.235f);   // #FF7A3C 火芯
    private static readonly Color V2ShadeCold = new Color(0.180f, 0.227f, 0.361f);  // #2E3A5C 冷阴影
    private static readonly Color V2ShadeBlue = new Color(0.290f, 0.373f, 0.549f);  // #4A5F8C 冷环境光
    private static readonly Color V2Rune = new Color(0.275f, 0.878f, 0.784f);       // #46E0C8 符文青
    private static readonly Color V2Blood = new Color(0.847f, 0.290f, 0.227f);      // #D84A3A 血红
    private static readonly Color V2WetDeep = new Color(0.176f, 0.227f, 0.235f);    // 池底湿润（石材压暗后偏青）

    // ================= 关键尺寸 =================
    private const float V2TerraceTopY = 1.6f;    // 北侧高台面高
    private const float V2TerraceFrontZ = 8.4f;  // 高台正面（朝南）
    private const float V2TerraceDepth = 6f;     // 高台进深（8×6）
    private const float V2TerraceHalfWidth = 4f; // 高台半宽（宽 8）
    // 用户要求把蓄水池填平：池底与主庭院同高（y=0），不再有下沉段。
    private const float V2CisternFloorY = 0f;       // 蓄水池底（已填平，与庭院齐平）
    private const float V2CisternDeckY = 0.008f;    // 池底铺装比庭院铺装高 8mm，避免与下方石板共面 z-fighting
    private const float V2CisternRimHeight = 0.26f; // 池沿矮沿高度（< 0.36m 台阶上限，玩家可直接跨过）

    // ================= 缓存：材质 / 贴图 / 原始网格 =================
    private static Material v2Floor, v2FloorWarm, v2FloorCool, v2Stone, v2Wall, v2MossMat, v2Wet,
        v2Metal, v2Bone, v2Foliage, v2Wood, v2ClothRed, v2ClothCold, v2Far, v2FarDeep, v2Ridge, v2RidgeDeep,
        v2Flame, v2Beam, v2Dust;
    private static Mesh v2CubeMesh, v2CylinderMesh, v2QuadMesh;
    private static Transform v2WorldRoot;
    private static bool v2Prepared;
    private static int v2MeshSerial;

    /// <summary>被藤蔓覆盖的两尊雕像位置（装饰与爬藤共用）。</summary>
    private static readonly Vector3[] V2StatueSpots =
    {
        new Vector3(-3.4f, 0f, -2.6f),
        new Vector3(5.8f, 0f, -6.9f)
    };

    // =====================================================================
    // 入口一：地形与建筑
    // =====================================================================

    /// <summary>构建主庭院 / 北侧高台 / 西侧回廊 / 东侧蓄水池 / 外围断墙 / 远景塔楼。</summary>
    internal static void BuildWorldV2()
    {
        // 全量重建：清掉上一次的材质/网格缓存
        v2Prepared = false;
        v2MeshSerial = 0;
        PrepareMaterialsV2();

        GameObject world = new GameObject("三层遗迹庭院");
        v2WorldRoot = world.transform;

        BuildCourtyardFloorV2(NewGroupV2("主庭院").transform);
        BuildNorthTerraceV2(NewGroupV2("北侧高台").transform);
        BuildWestColonnadeV2(NewGroupV2("西侧回廊").transform);
        BuildEastCisternV2(NewGroupV2("东侧蓄水池").transform);
        BuildPerimeterV2(NewGroupV2("外围断墙").transform);
        BuildDistantTowersV2(NewGroupV2("远景塔楼").transform);
    }

    /// <summary>装饰层：倒伏石梁、碎石堆、苔藓、爬藤、旗帜、骸骨、雕像与庭院火盆。</summary>
    internal static void BuildDecorV2()
    {
        PrepareMaterialsV2();
        Transform root = NewGroupV2("装饰").transform;

        BuildFallenBeamsV2(root);
        BuildRubbleV2(root);
        BuildMossPatchesV2(root);
        BuildBonesV2(root);
        BuildStatuesV2(root);
        BuildBannersV2(root);
        BuildVinesV2(root);

        // 庭院层三处火盆：东北角、回廊北端、回廊中段
        BuildFirePitV2(new Vector3(7.6f, 0f, 7.8f), 1f, 2.4f, 6101);
        BuildFirePitV2(new Vector3(-8.6f, 0f, 7.8f), 0.9f, 2.2f, 6102);
        BuildFirePitV2(new Vector3(-8.5f, 0f, -2.3f), 0.9f, 2.2f, 6103);
    }

    /// <summary>氛围层：伪造体积光锥、漂浮尘埃、余烬。</summary>
    internal static void BuildAmbientV2()
    {
        PrepareMaterialsV2();
        Transform root = NewGroupV2("氛围").transform;
        BuildLightShaftsV2(root);
        BuildDustParticlesV2(root);
        BuildEmberParticlesV2(root);
    }

    /// <summary>中央祭坛 V2：三层台基 + 四角符文柱 + 中央火盆，位置保持 (0,0,0)。</summary>
    internal static void BuildAltarV2()
    {
        PrepareMaterialsV2();
        Transform root = NewGroupV2("中央祭坛").transform;

        MeshBatchV2 batch = new MeshBatchV2();
        // 三层台基（保持原位，外形比原版更厚重）
        batch.AddFlatSlab(new Vector3(0f, 0.16f, 0f), new Vector3(3.6f, 0.32f, 3.6f), Quaternion.identity, true, 1.8f, Vector2.zero);
        batch.AddFlatSlab(new Vector3(0f, 0.46f, 0f), new Vector3(2.7f, 0.28f, 2.7f), Quaternion.identity, true, 1.8f, Vector2.zero);
        batch.AddFlatSlab(new Vector3(0f, 0.72f, 0f), new Vector3(1.8f, 0.24f, 1.8f), Quaternion.identity, true, 1.8f, Vector2.zero);
        // 四边各一级收进踏步，方便从任意方向靠近祭坛
        for (int side = 0; side < 4; side++)
        {
            Quaternion yaw = Quaternion.Euler(0f, side * 90f, 0f);
            Vector3 offset = yaw * new Vector3(0f, 0.08f, 1.96f);
            batch.AddBox(offset, new Vector3(1.5f, 0.16f, 0.42f), yaw, false);
        }
        // 四角符文矮柱
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = new Vector3(i < 2 ? -1.35f : 1.35f, 0f, (i % 2 == 0) ? -1.35f : 1.35f);
            batch.AddBox(corner + Vector3.up * 0.9f, new Vector3(0.26f, 1.8f, 0.26f), true);
        }
        batch.Commit("祭坛台基", v2Stone, root);

        // 符文柱顶的发光块（复用 Build() 已创建的 runeMat）
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = new Vector3(i < 2 ? -1.35f : 1.35f, 0f, (i % 2 == 0) ? -1.35f : 1.35f);
            CreateBox("符文块", corner + Vector3.up * 1.9f, new Vector3(0.34f, 0.2f, 0.34f), runeMat, root);
        }

        BuildFirePitV2(new Vector3(0f, 0.84f, 0f), 0.9f, 2.8f, 6201);
    }

    // =====================================================================
    // 主庭院
    // =====================================================================

    private static void BuildCourtyardFloorV2(Transform parent)
    {
        // 底层暗石：220×220，让远山与塔楼都踩在同一片地面上（远到 100m 已被雾完全吃掉），
        // 同时盖住庭院板块之间的缝隙。仍是一个 Box，不增加 draw call。
        MeshBatchV2 ground = new MeshBatchV2();
        ground.AddBox(new Vector3(0f, -0.16f, 0f), new Vector3(220f, 0.3f, 220f), false);
        ground.Commit("地面基座", v2Wall, parent);

        // 主庭院铺装：5×5 块石板，轻微错位与高低，塌陷处留缝露出暗石。
        // 石板按 3 套材质 + 3 种 UV 相位轮流铺，贴图密度也各不相同，
        // 这样同一张"龟裂石材"不会在整片地面上对齐重复。
        MeshBatchV2 pavingA = new MeshBatchV2();
        MeshBatchV2 pavingB = new MeshBatchV2();
        MeshBatchV2 pavingC = new MeshBatchV2();
        MeshBatchV2[] pavings = { pavingA, pavingB, pavingC };
        // 方石贴图是 3×3 正方石块，texel=2.7 时一块石板约 0.9m 见方
        float[] pavingTexel = { 2.7f, 2.2f, 3.3f };
        System.Random random = new System.Random(9317);
        for (int ix = 0; ix < 5; ix++)
        {
            for (int iz = 0; iz < 5; iz++)
            {
                if ((ix == 4 && iz == 0) || (ix == 0 && iz == 4) || (ix == 2 && iz == 4)) continue;
                float centerX = -8f + ix * 4f;
                float centerZ = -8f + iz * 4f;
                float yaw = ((float)random.NextDouble() - 0.5f) * 1.4f;
                float lift = ((float)random.NextDouble() - 0.5f) * 0.012f;
                int variant = ((ix * 3 + iz * 5) % 3 + 3) % 3;
                Vector2 uvOffset = new Vector2((float)random.NextDouble(), (float)random.NextDouble());
                pavings[variant].AddFlatSlab(new Vector3(centerX, -0.05f + lift, centerZ),
                    new Vector3(3.92f, 0.1f, 3.92f), Quaternion.Euler(0f, yaw, 0f), false,
                    pavingTexel[variant], uvOffset);
            }
        }
        // 塌陷处的碎板
        pavingA.AddFlatSlab(new Vector3(7.9f, -0.02f, -7.7f), new Vector3(1.5f, 0.06f, 1.2f),
            Quaternion.Euler(0f, 24f, 0f), false, 0.9f, new Vector2(0.31f, 0.62f));
        pavingB.AddFlatSlab(new Vector3(-8.1f, -0.03f, 7.6f), new Vector3(1.2f, 0.05f, 1.6f),
            Quaternion.Euler(0f, -16f, 0f), false, 0.9f, new Vector2(0.71f, 0.13f));
        pavingC.AddFlatSlab(new Vector3(0.3f, -0.04f, 8.2f), new Vector3(2.4f, 0.05f, 0.9f),
            Quaternion.Euler(0f, 8f, 0f), false, 0.9f, new Vector2(0.44f, 0.87f));
        pavingA.Commit("庭院铺装 A", v2Floor, parent);
        pavingB.Commit("庭院铺装 B", v2FloorWarm, parent);
        pavingC.Commit("庭院铺装 C", v2FloorCool, parent);
    }

    // =====================================================================
    // 北侧高台（y=+1.6，8×6，正面双侧 5 级石阶）
    // =====================================================================

    private static void BuildNorthTerraceV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        float deckZ = V2TerraceFrontZ + V2TerraceDepth * 0.5f;

        // 台体本体（实心，顶面 y=1.6）。
        // 这块 8×6 的正面原本走 AddBox（每个面只贴一张图），近景时砖缝被放大到 2m 一块、
        // 被暖光糊成"一块亮黄板"；改成世界尺寸 UV（texel 2.6，砖块≈0.65m，与台面/侧栏同密度）。
        batch.AddBoxWithUv(new Vector3(0f, V2TerraceTopY * 0.5f, deckZ),
            new Vector3(V2TerraceHalfWidth * 2f, V2TerraceTopY, V2TerraceDepth),
            Quaternion.identity, true, 2.6f);
        // 顶面铺装（略浅，比台体大一圈形成线脚）；平板 UV，避免沿进深方向被拉成长条
        batch.AddFlatSlab(new Vector3(0f, V2TerraceTopY + 0.03f, deckZ),
            new Vector3(V2TerraceHalfWidth * 2f + 0.40f, 0.06f, V2TerraceDepth + 0.40f),
            Quaternion.identity, false, 2.7f, Vector2.zero);

        // 正面护栏：中央保留望台缺口；两条石阶（中心 x=±3.4）的顶端各开一个 1.25m 宽的上台缺口，
        // 否则玩家爬到顶会被 0.72m 高的护栏挡在台阶上。缺口两侧用柱头 + 角柱收边，不留"断开一截"的突兀感。
        float parapetZ = V2TerraceFrontZ + 0.16f;   // 护栏中心线 z=8.56
        for (int side = -1; side <= 1; side += 2)
        {
            // 望台口到台阶口之间的短护栏（x 1.75..2.35）
            batch.AddBox(new Vector3(side * 2.05f, V2TerraceTopY + 0.36f, parapetZ),
                new Vector3(0.60f, 0.72f, 0.32f), true);
            // 台阶口内侧柱头（x 2.35..2.65）与顶帽
            batch.AddBox(new Vector3(side * 2.50f, V2TerraceTopY + 0.475f, parapetZ),
                new Vector3(0.30f, 0.95f, 0.44f), true);
            batch.AddBox(new Vector3(side * 2.50f, V2TerraceTopY + 1.00f, parapetZ),
                new Vector3(0.38f, 0.10f, 0.52f), false);
            // 台阶口外侧角柱（x 3.90..4.20）：压在侧护栏前端，把缺口框住又不侵占通行宽度
            batch.AddBox(new Vector3(side * 4.05f, V2TerraceTopY + 0.47f, V2TerraceFrontZ),
                new Vector3(0.30f, 0.94f, 0.44f), true);
            batch.AddBox(new Vector3(side * 4.05f, V2TerraceTopY + 0.99f, V2TerraceFrontZ),
                new Vector3(0.40f, 0.10f, 0.54f), false);
            // 踏面：把最后一级（顶面 1.60）与台面铺装（顶面 1.66）连成一片。
            // 顶面 1.665 只比铺装高 5mm，既消除 6cm 的台阶断层，又不会与铺装面 z-fighting。
            batch.AddBox(new Vector3(side * 3.25f, V2TerraceTopY + 0.0075f, 8.24f),
                new Vector3(1.30f, 0.115f, 0.40f), true);
        }
        batch.AddBoxWithUv(new Vector3(-4.0f, V2TerraceTopY + 0.42f, deckZ), new Vector3(0.32f, 0.72f, V2TerraceDepth),
            Quaternion.identity, true, 2.5f);
        batch.AddBoxWithUv(new Vector3(4.0f, V2TerraceTopY + 0.42f, deckZ), new Vector3(0.32f, 0.72f, V2TerraceDepth),
            Quaternion.identity, true, 2.5f);
        // 台面碰撞补齐：铺装网格本身无碰撞体，若不补，玩家站在台面上会比可见面低 6cm（踏面处又会高 5mm）。
        // 这里只加碰撞、不改可见几何，尺寸与铺装完全对齐（8.8×6.4，顶面 1.665）。
        batch.AddColliderOnly(new Vector3(0f, V2TerraceTopY + 0.0325f, deckZ),
            new Vector3(V2TerraceHalfWidth * 2f + 0.40f, 0.065f, V2TerraceDepth + 0.40f), Quaternion.identity);

        // 正面双侧 5 级石阶：每级抬高 0.32m、进深 0.36m，最低一级落在 z=6.6 之外
        for (int side = -1; side <= 1; side += 2)
        {
            for (int step = 0; step < 5; step++)
            {
                float height = (step + 1) * 0.32f;
                float centerZ = V2TerraceFrontZ - 1.8f + (step + 0.5f) * 0.36f;
                batch.AddBoxWithUv(new Vector3(side * 3.4f, height * 0.5f, centerZ), new Vector3(1.2f, height, 0.36f),
                    Quaternion.identity, true, 2.0f);
            }
            // 阶侧挡墙，让台阶读作整体构件
            batch.AddBox(new Vector3(side * 4.02f, 0.55f, V2TerraceFrontZ - 0.9f), new Vector3(0.18f, 1.1f, 1.9f), true);
        }

        // 台面四根断柱（高度不一，顶部残破）
        Vector3[] columnSpots =
        {
            new Vector3(-2.8f, 0f, 9.6f), new Vector3(2.8f, 0f, 9.6f),
            new Vector3(-2.8f, 0f, 13.2f), new Vector3(2.8f, 0f, 13.2f)
        };
        float[] columnHeights = { 1.25f, 1.9f, 2.35f, 0.95f };
        for (int i = 0; i < columnSpots.Length; i++)
        {
            Vector3 foot = new Vector3(columnSpots[i].x, V2TerraceTopY, columnSpots[i].z);
            batch.AddBox(foot + Vector3.up * 0.13f, new Vector3(1.0f, 0.26f, 1.0f), false);
            float remaining = columnHeights[i];
            float cursor = 0.26f;
            int drum = 0;
            while (remaining > 0.22f)
            {
                float height = Mathf.Min(remaining, 0.62f);
                float drift = (i * 3 + drum) % 2 == 0 ? 0.035f : -0.03f;
                batch.AddCylinder(foot + Vector3.up * (cursor + height * 0.5f), 0.34f - drum * 0.02f, height,
                    Quaternion.Euler(drift * 55f, drum * 12f, drift * 40f), false);
                cursor += height;
                remaining -= height;
                drum++;
            }
            batch.AddColliderOnly(foot + Vector3.up * (cursor * 0.5f + 0.13f), new Vector3(0.78f, cursor + 0.26f, 0.78f), Quaternion.identity);
        }

        // 台面中央的矮祭台与符文
        batch.AddFlatSlab(new Vector3(0f, V2TerraceTopY + 0.22f, 11.6f), new Vector3(2.2f, 0.44f, 2.2f),
            Quaternion.identity, true, 1.8f, Vector2.zero);
        batch.AddFlatSlab(new Vector3(0f, V2TerraceTopY + 0.55f, 11.6f), new Vector3(1.3f, 0.28f, 1.3f),
            Quaternion.identity, true, 1.8f, Vector2.zero);

        batch.Commit("高台建筑", v2Stone, parent);

        CreateBox("高台符文碑", new Vector3(0f, V2TerraceTopY + 1.35f, 11.6f), new Vector3(0.9f, 1.3f, 0.16f), runeMat, parent);

        // 台上两处火盆
        BuildFirePitV2(new Vector3(-2.6f, V2TerraceTopY, 10.4f), 1f, 2.8f, 6301);
        BuildFirePitV2(new Vector3(2.6f, V2TerraceTopY, 10.4f), 1f, 2.8f, 6302);
    }

    // =====================================================================
    // 西侧回廊（6 根列柱，其中 2 根倒塌横卧）
    // =====================================================================

    private static void BuildWestColonnadeV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        const float columnX = -8.3f;

        // 台基两级
        // 台基是"平板"：旧路径按厚度 0.12m 缩放 V 轴，贴图沿 15.8m 长度被拉了 20 多倍 → 读成木地板。
        // 改用 AddFlatSlab：U/V 分别按 X/Z 两个水平轴缩放，石板边长 ≈ 2.2/3 ≈ 0.73m，横向纵向一致。
        batch.AddFlatSlab(new Vector3(columnX, 0.06f, 0.4f), new Vector3(3.1f, 0.12f, 15.8f),
            Quaternion.identity, false, 2.2f, Vector2.zero);
        batch.AddFlatSlab(new Vector3(columnX, 0.15f, 0.4f), new Vector3(2.7f, 0.08f, 15.4f),
            Quaternion.identity, false, 2.2f, Vector2.zero);

        float[] columnZ = { -6.2f, -3.6f, -1.0f, 1.6f, 4.2f, 6.8f };
        // 第 3 根朝庭院倒，第 5 根朝墙倒（避开守卫巡逻点 (-7,0,4)）
        int[] toppledDirection = { 0, 0, 1, 0, -1, 0 };
        for (int i = 0; i < columnZ.Length; i++)
        {
            Vector3 foot = new Vector3(columnX, 0f, columnZ[i]);
            if (toppledDirection[i] == 0) BuildStandingColumnV2(batch, foot);
            else BuildToppledColumnV2(batch, foot, toppledDirection[i]);
        }

        // 残梁：只保留两跨，其余塌落
        batch.AddBoxWithUv(new Vector3(columnX, 3.92f, -4.9f), new Vector3(0.82f, 0.44f, 2.9f),
            Quaternion.identity, true, 1.8f);
        batch.AddBoxWithUv(new Vector3(columnX, 3.52f, -2.3f), new Vector3(0.82f, 0.44f, 2.9f),
            Quaternion.Euler(0f, 0f, -7f), true, 1.8f);

        // 坍塌的拱券：地面上摊成一道弧
        for (int i = 0; i < 5; i++)
        {
            float angle = -40f + i * 32f;
            float radians = angle * Mathf.Deg2Rad;
            Vector3 position = new Vector3(-7.0f + Mathf.Sin(radians) * 1.9f, 0.22f, 1.0f + Mathf.Cos(radians) * 1.6f);
            batch.AddBox(position, new Vector3(1.15f, 0.44f, 0.5f), Quaternion.Euler(0f, -angle, 0f), true);
        }

        // 廊内倒伏的柱段与残块
        batch.AddCylinder(new Vector3(-6.5f, 0.3f, -5.4f), 0.3f, 1.5f, Quaternion.Euler(0f, 28f, 90f), false);
        batch.AddColliderOnly(new Vector3(-6.5f, 0.3f, -5.4f), new Vector3(0.6f, 1.5f, 0.6f), Quaternion.Euler(0f, 28f, 90f));

        batch.Commit("西侧回廊建筑", v2Stone, parent);

        // 被藤蔓缠住的断柱（复用回廊材质）
        MeshBatchV2 broken = new MeshBatchV2();
        broken.AddCylinder(new Vector3(-7.4f, 0.9f, 6.4f), 0.34f, 1.8f, Quaternion.Euler(3f, 0f, 4f), false);
        broken.AddBox(new Vector3(-7.4f, 1.86f, 6.4f), new Vector3(0.9f, 0.2f, 0.9f), Quaternion.Euler(4f, 18f, 3f), false);
        broken.AddColliderOnly(new Vector3(-7.4f, 0.9f, 6.4f), new Vector3(0.8f, 1.8f, 0.8f), Quaternion.identity);
        broken.Commit("回廊断柱", v2Stone, parent);
    }

    private static void BuildStandingColumnV2(MeshBatchV2 batch, Vector3 foot)
    {
        batch.AddBox(foot + Vector3.up * 0.36f, new Vector3(1.05f, 0.34f, 1.05f), false);
        batch.AddCylinder(foot + Vector3.up * 1.86f, 0.36f, 2.9f, Quaternion.identity, false);
        batch.AddBox(foot + Vector3.up * 3.46f, new Vector3(1.0f, 0.28f, 1.0f), false);
        batch.AddBox(foot + Vector3.up * 3.66f, new Vector3(1.16f, 0.14f, 1.16f), false);
        batch.AddColliderOnly(foot + Vector3.up * 1.9f, new Vector3(0.84f, 3.8f, 0.84f), Quaternion.identity);
    }

    private static void BuildToppledColumnV2(MeshBatchV2 batch, Vector3 foot, int direction)
    {
        // 只剩柱础，柱身横卧在廊下
        batch.AddBox(foot + Vector3.up * 0.3f, new Vector3(1.05f, 0.34f, 1.05f), false);
        batch.AddColliderOnly(foot + Vector3.up * 0.3f, new Vector3(1.05f, 0.34f, 1.05f), Quaternion.identity);

        if (direction > 0)
        {
            // 朝庭院倒下：柱身沿 X 轴横卧
            Vector3 lying = foot + new Vector3(1.5f, 0.4f, 0.25f);
            Quaternion lyingRotation = Quaternion.Euler(0f, 10f, 90f);
            batch.AddCylinder(lying, 0.36f, 2.5f, lyingRotation, false);
            batch.AddColliderOnly(lying, new Vector3(0.72f, 2.5f, 0.72f), lyingRotation);
            batch.AddCylinder(lying + new Vector3(1.45f, -0.06f, -0.35f), 0.3f, 0.5f,
                Quaternion.Euler(0f, 40f, 90f), false);
        }
        else
        {
            // 朝围墙倒下：柱身沿 Z 轴横卧在廊下，不穿进西墙
            Vector3 lying = foot + new Vector3(-0.95f, 0.38f, 1.1f);
            Quaternion lyingRotation = Quaternion.Euler(90f, 0f, 0f);
            batch.AddCylinder(lying, 0.36f, 2.2f, lyingRotation, false);
            batch.AddColliderOnly(lying, new Vector3(0.72f, 2.2f, 0.72f), lyingRotation);
            batch.AddCylinder(lying + new Vector3(-0.3f, 0f, 1.35f), 0.3f, 0.5f,
                Quaternion.Euler(90f, 0f, 25f), false);
        }
    }

    // =====================================================================
    // 东侧干涸蓄水池（下沉 0.6m）
    // =====================================================================

    private static void BuildEastCisternV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        const float minX = 4.8f, maxX = 9.4f, minZ = -1.6f, maxZ = 4.4f;
        const float width = maxX - minX;      // 4.6
        const float depth = maxZ - minZ;      // 6.0
        float centerX = (minX + maxX) * 0.5f;
        float centerZ = (minZ + maxZ) * 0.5f;

        // 池底（平板 UV）：与主庭院同高，只比庭院铺装高 8mm 防止共面闪烁。
        // 不再下沉 → 沉底地形、内壁、下沉段、进水台阶全部取消（用户要求整体填平）。
        batch.AddFlatSlab(new Vector3(centerX, V2CisternDeckY - 0.06f, centerZ),
            new Vector3(width, 0.12f, depth), Quaternion.identity, false, 2.2f, Vector2.zero);

        // 池沿矮沿：0.26m 高（低于 0.36m 台阶上限，玩家可直接跨进跨出），只作视觉分区
        float rimY = V2CisternRimHeight * 0.5f;
        batch.AddBoxWithUv(new Vector3(centerX, rimY, minZ + 0.18f), new Vector3(width + 0.36f, V2CisternRimHeight, 0.36f),
            Quaternion.identity, true, 2.0f);
        batch.AddBoxWithUv(new Vector3(centerX, rimY, maxZ - 0.18f), new Vector3(width + 0.36f, V2CisternRimHeight, 0.36f),
            Quaternion.identity, true, 2.0f);
        batch.AddBoxWithUv(new Vector3(maxX - 0.18f, rimY, centerZ), new Vector3(0.36f, V2CisternRimHeight, depth),
            Quaternion.identity, true, 2.0f);
        // 西沿分两段（保留造型缺口，已无实际通行含义）
        batch.AddBoxWithUv(new Vector3(minX + 0.18f, rimY, minZ + 1.3f), new Vector3(0.36f, V2CisternRimHeight, 2.6f),
            Quaternion.identity, true, 2.0f);
        batch.AddBoxWithUv(new Vector3(minX + 0.18f, rimY, maxZ - 0.9f), new Vector3(0.36f, V2CisternRimHeight, 1.8f),
            Quaternion.identity, true, 2.0f);

        // 池壁水痕：改成贴地薄片（y≈0.02），不再做成立在下沉池壁上的竖条
        batch.AddBox(new Vector3(maxX - 0.3f, V2CisternDeckY + 0.012f, centerZ + 1.2f),
            new Vector3(0.16f, 0.024f, 1.1f), false);

        batch.Commit("蓄水池结构", v2Stone, parent);

        // 池底：湿润水痕（深色 + _Wetness）+ 碎石
        MeshBatchV2 wet = new MeshBatchV2();
        System.Random random = new System.Random(5521);
        for (int i = 0; i < 7; i++)
        {
            float x = Mathf.Lerp(minX + 0.6f, maxX - 0.6f, (float)random.NextDouble());
            float z = Mathf.Lerp(minZ + 0.6f, maxZ - 0.6f, (float)random.NextDouble());
            float patchWidth = Mathf.Lerp(0.8f, 2.0f, (float)random.NextDouble());
            float patchDepth = Mathf.Lerp(0.7f, 1.7f, (float)random.NextDouble());
            wet.AddBox(new Vector3(x, V2CisternDeckY + 0.014f, z), new Vector3(patchWidth, 0.028f, patchDepth),
                Quaternion.Euler(0f, (float)random.NextDouble() * 60f, 0f), false);
        }
        wet.Commit("池底水痕", v2Wet, parent);

        MeshBatchV2 rubble = new MeshBatchV2();
        for (int i = 0; i < 26; i++)
        {
            float x = Mathf.Lerp(minX + 0.5f, maxX - 0.5f, (float)random.NextDouble());
            float z = Mathf.Lerp(minZ + 0.5f, maxZ - 0.5f, (float)random.NextDouble());
            float scale = Mathf.Lerp(0.12f, 0.34f, (float)random.NextDouble());
            rubble.AddBox(new Vector3(x, V2CisternDeckY + scale * 0.4f, z),
                new Vector3(scale, scale * 0.7f, scale * 0.8f),
                Quaternion.Euler((float)random.NextDouble() * 40f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 40f),
                false);
        }
        rubble.Commit("池底碎石", v2Stone, parent);
    }

    // =====================================================================
    // 外围断墙 + 南侧拱门
    // =====================================================================

    private static void BuildPerimeterV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        MeshBatchV2 slitBatch = new MeshBatchV2();   // 箭窗：深色，单独一批
        const float thickness = 1.1f;

        // 南墙：中央门楼，两侧 7.2m 城墙（门洞保持 x -2.4..2.4）
        AddTallWallV2(batch, slitBatch, new Vector3(-10f, 0f, -10f), Vector3.right, 7.6f, thickness, 7.2f, 4101);
        AddTallWallV2(batch, slitBatch, new Vector3(2.4f, 0f, -10f), Vector3.right, 7.6f, thickness, 7.2f, 4102);
        // 北墙：中间是 8×6 高台堡垒，两侧接 7.4m 城墙
        AddTallWallV2(batch, slitBatch, new Vector3(-10f, 0f, 10f), Vector3.right, 5.8f, thickness, 7.4f, 4103);
        AddTallWallV2(batch, slitBatch, new Vector3(4.2f, 0f, 10f), Vector3.right, 5.8f, thickness, 7.4f, 4104);
        // 东墙 / 西墙
        AddTallWallV2(batch, slitBatch, new Vector3(10f, 0f, -10f), Vector3.forward, 20f, thickness, 7.0f, 4105);
        AddTallWallV2(batch, slitBatch, new Vector3(-10f, 0f, -10f), Vector3.forward, 20f, thickness, 7.0f, 4106);

        // 四角塔：8.8~10.4m，压住天际线
        float[] cornerX = { -10f, 10f, -10f, 10f };
        float[] cornerZ = { -10f, -10f, 10f, 10f };
        float[] cornerHeight = { 9.2f, 10.4f, 10.0f, 8.8f };
        for (int i = 0; i < 4; i++)
        {
            Vector3 foot = new Vector3(cornerX[i], 0f, cornerZ[i]);
            batch.AddBoxWithUv(foot + Vector3.up * (cornerHeight[i] * 0.5f),
                new Vector3(3.4f, cornerHeight[i], 3.4f), Quaternion.identity, true, 3.4f);
            batch.AddBox(foot + Vector3.up * (cornerHeight[i] + 0.22f), new Vector3(3.9f, 0.44f, 3.9f), true);
            AddMerlonRingV2(batch, foot + Vector3.up * (cornerHeight[i] + 0.44f), 3.2f, 4100 + i);
        }

        // 南侧门楼：两座 9.6m 方塔 + 过梁 + 顶部小塔，门洞净高 5.9m（玩家 1.95m）
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 pierFoot = new Vector3(side * 3.6f, 0f, -10f);
            batch.AddBoxWithUv(pierFoot + Vector3.up * 4.8f, new Vector3(2.4f, 9.6f, 2.8f),
                Quaternion.identity, true, 2.6f);
            batch.AddBox(pierFoot + Vector3.up * 9.75f, new Vector3(2.9f, 0.4f, 3.3f), true);
            AddMerlonRingV2(batch, pierFoot + Vector3.up * 9.95f, 2.4f, 4200 + (side > 0 ? 1 : 0));
            // 门洞两侧的装饰壁柱：贴在门楼朝庭院的一面（z=-8.6 之外），读作门框柱
            batch.AddBox(new Vector3(side * 2.2f, 2.7f, -8.35f), new Vector3(0.5f, 5.4f, 0.5f), true);
        }
        batch.AddBoxWithUv(new Vector3(0f, 6.5f, -10f), new Vector3(5.6f, 1.2f, 2.6f),
            Quaternion.identity, true, 2.5f);
        batch.AddBoxWithUv(new Vector3(0f, 7.5f, -10f), new Vector3(4.6f, 0.8f, 2.2f),
            Quaternion.identity, true, 2.5f);
        batch.AddBoxWithUv(new Vector3(0f, 9.0f, -10f), new Vector3(3.0f, 2.2f, 2.2f),
            Quaternion.identity, true, 2.6f);
        batch.AddBoxWithUv(new Vector3(0f, 10.3f, -10f), new Vector3(3.4f, 0.4f, 2.6f),
            Quaternion.identity, true, 2.5f);
        AddMerlonRingV2(batch, new Vector3(0f, 10.5f, -10f), 3.0f, 4210);

        // 高台后方的北墙：这一段原来只有 1.6m 的堡垒台面，从庭院看过去能直接望到墙外大地。
        // 补一道 8m 高墙贴在高台后沿（z=14.5），把北侧也彻底封住，同时读作"高台背靠城墙"。
        batch.AddBoxWithUv(new Vector3(0f, 4f, 14.5f), new Vector3(8.4f, 8f, 1.2f),
            Quaternion.identity, true, 2.5f, new Vector2(0.43f, 0.21f));
        for (float x = -3.6f; x <= 3.6f; x += 1.85f)
        {
            batch.AddBox(new Vector3(x, 8.28f, 14.5f), new Vector3(0.85f, 0.56f, 1.32f), false);
        }

        batch.Commit("外围城墙", v2Wall, parent);
        slitBatch.Commit("城墙箭窗", v2Far, parent);
    }

    /// <summary>方形平台顶部的一圈垛口（角塔 / 门楼顶）。</summary>
    private static void AddMerlonRingV2(MeshBatchV2 batch, Vector3 topCenter, float size, int seed)
    {
        float half = size * 0.5f;
        for (float t = -half + 0.35f; t <= half - 0.2f; t += 0.9f)
        {
            batch.AddBox(topCenter + new Vector3(t, 0.28f, -half), new Vector3(0.55f, 0.56f, 0.32f), false);
            batch.AddBox(topCenter + new Vector3(t, 0.28f, half), new Vector3(0.55f, 0.56f, 0.32f), false);
            batch.AddBox(topCenter + new Vector3(-half, 0.28f, t), new Vector3(0.32f, 0.56f, 0.55f), false);
            batch.AddBox(topCenter + new Vector3(half, 0.28f, t), new Vector3(0.32f, 0.56f, 0.55f), false);
        }
    }

    /// <summary>
    /// 6~8m 高的封闭城墙：分段错落 + 顶部垛口 + 箭窗 + 竖向扶壁。
    /// 与旧版最大的区别是**不再留通底缺口**——旧版 15% 的段落是空的，相机越过墙顶就能从缺口
    /// 直接看到地图外的虚空。"残缺"改为：某些段落塌到 4.6~5.6m，并在顶部留犬牙状碎砖。
    /// </summary>
    private static void AddTallWallV2(MeshBatchV2 batch, MeshBatchV2 slitBatch, Vector3 start, Vector3 direction,
        float totalLength, float thickness, float baseHeight, int seed)
    {
        System.Random random = new System.Random(seed);
        bool alongX = Mathf.Abs(direction.x) > 0.5f;
        // 墙都在 ±10 上，朝原点的一侧就是庭院内侧
        Vector3 inward = alongX
            ? new Vector3(0f, 0f, Mathf.Sign(-start.z))
            : new Vector3(Mathf.Sign(-start.x), 0f, 0f);

        float cursor = 0f;
        while (cursor < totalLength - 0.3f)
        {
            float segment = Mathf.Min(Mathf.Lerp(2.2f, 3.6f, (float)random.NextDouble()), totalLength - cursor);
            bool collapsed = random.NextDouble() < 0.26f && cursor > 1.6f && totalLength - cursor > 2.6f;
            float height = collapsed
                ? Mathf.Lerp(4.6f, 5.6f, (float)random.NextDouble())
                : Mathf.Lerp(6.4f, 8.0f, (float)random.NextDouble());

            Vector3 center = start + direction * (cursor + segment * 0.5f) + Vector3.up * (height * 0.5f);
            Vector3 size = alongX ? new Vector3(segment, height, thickness) : new Vector3(thickness, height, segment);
            Vector2 wallUv = new Vector2(Mathf.Repeat(cursor * 0.31f, 1f), Mathf.Repeat(height * 0.13f, 1f));
            batch.AddBoxWithUv(center, size, Quaternion.identity, true, 2.5f, wallUv);

            if (!collapsed)
            {
                // 垛口：每 1.85m 一个，0.85 宽 0.56 高
                Vector3 merlonSize = alongX
                    ? new Vector3(0.85f, 0.56f, thickness + 0.12f)
                    : new Vector3(thickness + 0.12f, 0.56f, 0.85f);
                for (float t = 0.9f; t < segment - 0.4f; t += 1.85f)
                {
                    batch.AddBox(start + direction * (cursor + t) + Vector3.up * (height + 0.28f), merlonSize, false);
                }
            }
            else
            {
                // 塌口：顶部留 3 块犬牙状碎砖，避免"一刀切的平顶"
                for (int i = 0; i < 3; i++)
                {
                    float t = segment * (0.22f + i * 0.28f);
                    float bite = Mathf.Lerp(0.4f, 1.3f, (float)random.NextDouble());
                    Vector3 biteSize = alongX
                        ? new Vector3(Mathf.Lerp(0.55f, 1.2f, (float)random.NextDouble()), bite, thickness * 0.92f)
                        : new Vector3(thickness * 0.92f, bite, Mathf.Lerp(0.55f, 1.2f, (float)random.NextDouble()));
                    batch.AddBox(start + direction * (cursor + t) + Vector3.up * (height + bite * 0.5f), biteSize, false);
                }
            }

            // 箭窗：窄长竖缝，贴在内侧墙面 2.5~3.7m 高处（略微凸出，实心墙上才看得见）
            int slits = height > 6.2f ? 2 : 1;
            for (int i = 0; i < slits; i++)
            {
                float t = segment * (slits == 1 ? 0.5f : 0.32f + i * 0.36f);
                Vector3 slitCenter = start + direction * (cursor + t) + Vector3.up * 3.1f
                    + inward * (thickness * 0.5f + 0.02f);
                Vector3 slitSize = alongX ? new Vector3(0.16f, 1.2f, 0.08f) : new Vector3(0.08f, 1.2f, 0.16f);
                slitBatch.AddBox(slitCenter, slitSize, false);
            }

            cursor += segment + 0.02f;
        }

        // 竖向扶壁：每 4.6m 一道，0.6m 宽，比墙厚 1.0m（内外各凸 0.5m），破除"一整片平板墙"
        float buttressHeight = baseHeight * 0.94f;
        for (float t = 2.3f; t < totalLength - 0.8f; t += 4.6f)
        {
            Vector3 buttressSize = alongX
                ? new Vector3(0.6f, buttressHeight, thickness + 1.0f)
                : new Vector3(thickness + 1.0f, buttressHeight, 0.6f);
            batch.AddBox(start + direction * t + Vector3.up * (buttressHeight * 0.5f), buttressSize, true);
            Vector3 capSize = alongX
                ? new Vector3(0.72f, 0.4f, thickness + 1.2f)
                : new Vector3(thickness + 1.2f, 0.4f, 0.72f);
            batch.AddBox(start + direction * t + Vector3.up * (buttressHeight + 0.2f), capSize, false);
        }
    }

    // =====================================================================
    // 远景剪影塔楼
    // =====================================================================

    private static void BuildDistantTowersV2(Transform parent)
    {
        // 前层：30~52m 的断崖与塔楼（雾里仍能读出轮廓，负责挡住地平线）
        MeshBatchV2 near = new MeshBatchV2();
        AddTowerV2(near, new Vector3(17f, 0f, 30f), 5.4f, 24f, 7101);
        AddTowerV2(near, new Vector3(-21f, 0f, 34f), 6.8f, 29f, 7102);
        AddTowerV2(near, new Vector3(3f, 0f, 42f), 7.4f, 22f, 7103);
        AddTowerV2(near, new Vector3(-44f, 0f, 22f), 5.6f, 26f, 7104);
        AddTowerV2(near, new Vector3(40f, 0f, 18f), 5.0f, 21f, 7105);
        AddTowerV2(near, new Vector3(30f, 0f, -38f), 5.8f, 27f, 7106);
        AddTowerV2(near, new Vector3(-30f, 0f, -34f), 6.2f, 23f, 7107);

        MeshBatchV2 nearRidge = new MeshBatchV2();
        AddRidgeV2(nearRidge, new Vector3(0f, 0f, 52f), true, 130f, 18f, 18f, 7201);
        AddRidgeV2(nearRidge, new Vector3(0f, 0f, -46f), true, 120f, 15f, 18f, 7202);
        AddRidgeV2(nearRidge, new Vector3(54f, 0f, 0f), false, 110f, 16f, 18f, 7203);
        AddRidgeV2(nearRidge, new Vector3(-56f, 0f, 0f), false, 110f, 15f, 18f, 7204);
        AddRidgeV2(nearRidge, new Vector3(38f, 0f, 38f), true, 90f, 13f, 20f, 7205);
        AddRidgeV2(nearRidge, new Vector3(-40f, 0f, 40f), true, 90f, 14f, 20f, 7206);
        near.Commit("远景塔楼", v2Far, parent);
        nearRidge.Commit("远景断崖", v2Ridge, parent);

        // 后层：76~90m 的山脊（更暗更蓝，几乎只剩剪影，与雾色融为一体）
        MeshBatchV2 far = new MeshBatchV2();
        AddRidgeV2(far, new Vector3(0f, 0f, 88f), true, 280f, 30f, 30f, 7301);
        AddRidgeV2(far, new Vector3(0f, 0f, -84f), true, 260f, 26f, 30f, 7302);
        AddRidgeV2(far, new Vector3(88f, 0f, 0f), false, 240f, 27f, 30f, 7303);
        AddRidgeV2(far, new Vector3(-90f, 0f, 0f), false, 240f, 25f, 30f, 7304);
        AddRidgeV2(far, new Vector3(64f, 0f, 64f), true, 170f, 22f, 26f, 7305);
        AddRidgeV2(far, new Vector3(-66f, 0f, 62f), true, 170f, 24f, 26f, 7306);
        far.Commit("远景山脊", v2RidgeDeep, parent);
    }

    /// <summary>
    /// 连绵山体：一排高低起伏的棱柱块叠成的山脊，底边埋在台地里，只露出犬牙状轮廓。
    /// alongX 决定山脊沿 X 还是沿 Z 铺开——东/西两侧的山脊必须沿 Z，否则会横穿庭院。
    /// </summary>
    private static void AddRidgeV2(MeshBatchV2 batch, Vector3 center, bool alongX,
        float width, float height, float depth, int seed)
    {
        System.Random random = new System.Random(seed);
        // 单块间距 16m → 11m：块更碎，竖缝被更多随机斜面切散（130m 的山脊从 8 块变 12 块）
        int blocks = Mathf.Max(4, Mathf.RoundToInt(width / 11f));
        for (int i = 0; i < blocks; i++)
        {
            float t = (i + 0.5f) / blocks;
            // 中间高、两端低的轮廓，外加每块随机起伏，读作连绵山脊而不是一堵梯形
            float profile = Mathf.Sin(t * Mathf.PI) * 0.55f + 0.45f;
            float blockHeight = height * profile * Mathf.Lerp(0.72f, 1.12f, (float)random.NextDouble());
            float blockWidth = width / blocks * Mathf.Lerp(1.05f, 1.6f, (float)random.NextDouble());
            float blockDepth = depth * Mathf.Lerp(0.7f, 1.3f, (float)random.NextDouble());
            float jitter = ((float)random.NextDouble() - 0.5f) * depth * 0.45f;

            // 山体不再是一块平顶盒：主体只承担 62% 高度，剩下 38% 交给一块收窄的坡顶，
            // 坡顶绕 X/Z 随机斜切 ±(9~16°)，于是每座山都是不规则尖顶，竖缝也被打散。
            float trunkHeight = blockHeight * Mathf.Lerp(0.58f, 0.66f, (float)random.NextDouble());
            Quaternion rotation = Quaternion.Euler(0f,
                ((float)random.NextDouble() - 0.5f) * 8f, ((float)random.NextDouble() - 0.5f) * 4f);
            Vector3 trunkSize = alongX
                ? new Vector3(blockWidth, trunkHeight, blockDepth)
                : new Vector3(blockDepth, trunkHeight, blockWidth);
            Vector3 trunkCenter = center + (alongX
                ? new Vector3((t - 0.5f) * width, trunkHeight * 0.5f, jitter)
                : new Vector3(jitter, trunkHeight * 0.5f, (t - 0.5f) * width));
            batch.AddBox(trunkCenter, trunkSize, rotation, false);

            // 坡顶：宽度/进深收窄 30~40%（保留 0.60~0.70），高度 = 剩余 34~42%
            float taper = Mathf.Lerp(0.60f, 0.70f, (float)random.NextDouble());
            float capHeight = blockHeight - trunkHeight;
            float capWidth = blockWidth * taper;
            float capDepth = blockDepth * taper;
            Vector3 capSize = alongX
                ? new Vector3(capWidth, capHeight, capDepth)
                : new Vector3(capDepth, capHeight, capWidth);
            Quaternion capRotation = rotation * Quaternion.Euler(
                ((float)random.NextDouble() - 0.5f) * 26f, 0f, ((float)random.NextDouble() - 0.5f) * 26f);
            Vector3 capOffset = alongX
                ? new Vector3(((float)random.NextDouble() - 0.5f) * blockWidth * 0.22f, 0f, 0f)
                : new Vector3(0f, 0f, ((float)random.NextDouble() - 0.5f) * blockWidth * 0.22f);
            batch.AddBox(trunkCenter + Vector3.up * (trunkHeight * 0.5f + capHeight * 0.42f) + capOffset,
                capSize, capRotation, false);
        }
    }

    private static void AddTowerV2(MeshBatchV2 batch, Vector3 foot, float radius, float height, int seed)
    {
        System.Random random = new System.Random(seed);
        int tiers = 3;
        float cursor = 0f;
        for (int tier = 0; tier < tiers; tier++)
        {
            float tierHeight = height / tiers * Mathf.Lerp(0.7f, 1.25f, (float)random.NextDouble());
            float tierRadius = radius * (1f - tier * 0.16f);
            batch.AddCylinder(foot + Vector3.up * (cursor + tierHeight * 0.5f), tierRadius, tierHeight,
                Quaternion.Euler(0f, (float)random.NextDouble() * 30f, 0f), false);
            // 塔身外圈挑台
            batch.AddCylinder(foot + Vector3.up * (cursor + tierHeight), tierRadius + 0.45f, 0.32f, Quaternion.identity, false);
            cursor += tierHeight + 0.32f;
        }
        // 塌掉的塔顶：几块残片
        for (int i = 0; i < 3; i++)
        {
            Vector3 position = foot + Vector3.up * (cursor + 0.3f + i * 0.5f)
                + new Vector3(((float)random.NextDouble() - 0.5f) * radius, 0f,
                    ((float)random.NextDouble() - 0.5f) * radius);
            batch.AddBox(position, new Vector3(radius * 0.5f, 0.6f, radius * 0.5f),
                Quaternion.Euler((float)random.NextDouble() * 20f, (float)random.NextDouble() * 90f, 0f), false);
        }
    }

    // =====================================================================
    // 装饰：倒伏石梁 / 碎石 / 苔藓 / 骸骨 / 雕像 / 旗帜 / 爬藤
    // =====================================================================

    private static void BuildFallenBeamsV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        batch.AddBoxWithUv(new Vector3(-0.6f, 0.28f, -5.4f), new Vector3(0.7f, 0.56f, 3.2f),
            Quaternion.Euler(0f, 24f, 0f), true, 1.6f);
        batch.AddBoxWithUv(new Vector3(5.2f, 0.3f, 5.6f), new Vector3(0.66f, 0.6f, 2.8f),
            Quaternion.Euler(0f, -15f, 2f), true, 1.6f);
        batch.AddBoxWithUv(new Vector3(-5.6f, 0.22f, -5.4f), new Vector3(0.6f, 0.44f, 2.4f),
            Quaternion.Euler(0f, 68f, -3f), true, 1.6f);
        batch.AddBoxWithUv(new Vector3(2.0f, 0.26f, 3.4f), new Vector3(0.58f, 0.5f, 2.6f),
            Quaternion.Euler(0f, -52f, 1.5f), true, 1.6f);
        // 压在石梁上的碎块
        batch.AddBox(new Vector3(0.35f, 0.62f, -4.6f), new Vector3(0.5f, 0.34f, 0.6f), Quaternion.Euler(6f, 30f, 4f), false);
        batch.AddBox(new Vector3(5.7f, 0.66f, 6.2f), new Vector3(0.44f, 0.3f, 0.52f), Quaternion.Euler(-5f, -20f, 3f), false);
        batch.Commit("倒伏石梁", v2Stone, parent);
    }

    private static void BuildRubbleV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        Vector3[] piles =
        {
            new Vector3(-4.6f, 0f, -6.4f), new Vector3(3.2f, 0f, 1.0f), new Vector3(8.4f, 0f, -6.2f),
            new Vector3(-1.2f, 0f, -8.6f), new Vector3(5.4f, 0f, -8.4f), new Vector3(-6.6f, 0f, 7.6f)
        };
        System.Random random = new System.Random(8823);
        for (int pile = 0; pile < piles.Length; pile++)
        {
            int stones = 14 + pile % 4 * 3;
            for (int i = 0; i < stones; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt((float)random.NextDouble()) * 1.15f;
                float scale = Mathf.Lerp(0.1f, 0.34f, (float)random.NextDouble());
                Vector3 position = piles[pile] + new Vector3(Mathf.Cos(angle) * radius, scale * 0.35f, Mathf.Sin(angle) * radius);
                batch.AddBox(position, new Vector3(scale, scale * 0.72f, scale * 0.86f),
                    Quaternion.Euler((float)random.NextDouble() * 50f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 50f),
                    false);
            }
        }
        batch.Commit("碎石堆", v2Stone, parent);
    }

    private static void BuildMossPatchesV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        System.Random random = new System.Random(4477);
        for (int i = 0; i < 16; i++)
        {
            float x = (float)random.NextDouble() * 17.5f - 8.75f;
            float z = (float)random.NextDouble() * 17.5f - 8.75f;
            if (Mathf.Abs(x) < 2.2f && Mathf.Abs(z) < 2.2f) continue;         // 祭坛正下方不留
            if (x > 4.6f && z > -1.8f && z < 4.6f) continue;                   // 蓄水池范围
            if (Mathf.Abs(x) < 4.2f && z > 6.4f) continue;                     // 高台台阶前
            // 每块苔藓由 3 片大小不一、位置错开的**薄盒**叠成（不再是双面四边形）：
            // 薄盒是闭合实体，六个面法线各自朝外，Cull Back 下无论绕序约定如何都不会有"朝下的可见面"，
            // 从根本上消除双面几何 + 零厚度带来的黑面与黑边。
            float baseSize = Mathf.Lerp(0.4f, 0.95f, (float)random.NextDouble());
            // 3 片 → 5~6 片，且最外圈 1~2 片再收窄 30%~60%：
            // 轮廓由大到小自然收边，和碎石、石缝的破碎形状衔接，不再是一块硬边"绿色便利贴"
            int lobes = 5 + (random.NextDouble() < 0.5 ? 0 : 1);
            for (int lobe = 0; lobe < lobes; lobe++)
            {
                float offsetX = ((float)random.NextDouble() - 0.5f) * 1.15f;
                float offsetZ = ((float)random.NextDouble() - 0.5f) * 1.15f;
                float size = baseSize * Mathf.Lerp(0.55f, 1.2f, (float)random.NextDouble());
                bool outerLobe = lobe >= lobes - 2;
                if (outerLobe) size *= Mathf.Lerp(0.40f, 0.70f, (float)random.NextDouble());
                float depth = size * Mathf.Lerp(0.75f, 1.25f, (float)random.NextDouble());
                // 每片厚度 +2.2mm 递增，顶面高度互不相同 → 叠合处不会 z-fighting
                float thickness = 0.018f + lobe * 0.0022f;
                Quaternion flat = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                batch.AddBox(new Vector3(x + offsetX, 0.008f + thickness * 0.5f, z + offsetZ),
                    new Vector3(size, thickness, depth), flat, false);
            }
        }
        // 关闭薄片几何的投影：苔藓贴地 22mm，在 42° 主光下自身投影会在地砖上渗出黑边/黑三角，
        // 正是"黑色碎片"的来源；苔藓只需要接收阴影，不该投射阴影。
        batch.Commit("苔藓地块", v2MossMat, parent, false);
    }

    private static void BuildBonesV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        Vector3[] piles = { new Vector3(3.8f, 0f, 2.2f), new Vector3(-1.6f, 0f, -6.6f) };
        System.Random random = new System.Random(6611);
        for (int pile = 0; pile < piles.Length; pile++)
        {
            for (int i = 0; i < 12; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt((float)random.NextDouble()) * 0.95f;
                Vector3 position = piles[pile] + new Vector3(Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius);
                Quaternion rotation = Quaternion.Euler(
                    (float)random.NextDouble() * 16f - 8f,
                    (float)random.NextDouble() * 360f,
                    90f + (float)random.NextDouble() * 10f - 5f);
                batch.AddCylinder(position, 0.045f, Mathf.Lerp(0.3f, 0.6f, (float)random.NextDouble()), rotation, false);
            }
            // 头骨
            batch.AddBox(piles[pile] + new Vector3(0.22f, 0.13f, -0.16f), new Vector3(0.2f, 0.18f, 0.26f),
                Quaternion.Euler(0f, 28f, 8f), false);
        }
        batch.Commit("遗骸骨骼堆", v2Bone, parent);
    }

    private static void BuildStatuesV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        for (int i = 0; i < V2StatueSpots.Length; i++)
        {
            Vector3 foot = V2StatueSpots[i];
            Quaternion yaw = Quaternion.Euler(0f, i == 0 ? 62f : -118f, 0f);
            batch.AddBox(foot + yaw * new Vector3(0f, 0.17f, 0f), new Vector3(1.15f, 0.34f, 1.15f), yaw, true);
            batch.AddBox(foot + yaw * new Vector3(0f, 0.42f, 0f), new Vector3(0.86f, 0.18f, 0.86f), yaw, false);
            // 身躯、头部、双臂（残缺：只有一侧伸出手臂）
            batch.AddBox(foot + yaw * new Vector3(0f, 1.28f, 0f), new Vector3(0.56f, 1.5f, 0.44f), yaw * Quaternion.Euler(2f, 0f, 3f), false);
            batch.AddCylinder(foot + yaw * new Vector3(0f, 2.2f, 0f), 0.19f, 0.36f, yaw * Quaternion.Euler(4f, 0f, 5f), false);
            batch.AddCylinder(foot + yaw * new Vector3(0.44f, 1.85f, 0f), 0.11f, 0.9f, yaw * Quaternion.Euler(0f, 0f, -38f), false);
            batch.AddBox(foot + yaw * new Vector3(-0.36f, 1.72f, 0.02f), new Vector3(0.2f, 0.34f, 0.2f), yaw * Quaternion.Euler(0f, 0f, 22f), false);
            batch.AddColliderOnly(foot + Vector3.up * 1.1f, new Vector3(1.05f, 2.2f, 1.05f), Quaternion.identity);
        }
        batch.Commit("藤蔓雕像", v2Bone, parent);
    }

    private static void BuildBannersV2(Transform parent)
    {
        MeshBatchV2 red = new MeshBatchV2();
        MeshBatchV2 cold = new MeshBatchV2();

        // 回廊残梁下垂落的布幡（竖挂，面朝 ±X）
        AddPleatedBannerV2(red, new Vector3(-7.55f, 3.86f, -4.9f), 1.0f, 1.95f, 90f, 4f, 8101);
        AddPleatedBannerV2(cold, new Vector3(-7.55f, 3.72f, -2.3f), 0.9f, 1.7f, 90f, -3f, 8102);
        // 高台正面悬挂的布幡
        AddPleatedBannerV2(red, new Vector3(2.7f, 2.16f, V2TerraceFrontZ - 0.14f), 1.15f, 2.0f, 0f, 2f, 8103);
        AddPleatedBannerV2(cold, new Vector3(-2.7f, 2.16f, V2TerraceFrontZ - 0.14f), 1.15f, 2.0f, 0f, -2f, 8104);

        red.Commit("血色布幡", v2ClothRed, parent);
        cold.Commit("冷色布幡", v2ClothCold, parent);

        // 挂幡的木杆（复用旧 woodMat，Build() 已创建）
        CreateCylinder("幡杆", new Vector3(-7.55f, 3.98f, -4.9f), new Vector3(0.06f, 0.62f, 0.06f), woodMat, parent);
        CreateCylinder("幡杆", new Vector3(2.7f, 2.18f, V2TerraceFrontZ - 0.24f), new Vector3(0.05f, 0.55f, 0.05f), woodMat, parent);
    }

    /// <summary>
    /// 带竖向褶皱的布幡：7 条窄布条按正弦相位前后错开、并各自绕竖轴微扭，
    /// 相邻布条受光不同，才读得出"布"而不是一块纯色矩形。
    /// </summary>
    private static void AddPleatedBannerV2(MeshBatchV2 batch, Vector3 topCenter, float width, float height,
        float yaw, float lean, int seed)
    {
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 right = yawRotation * Vector3.right;
        Vector3 forward = yawRotation * Vector3.forward;

        const int strips = 7;
        float stripWidth = width / strips;
        for (int i = 0; i < strips; i++)
        {
            float u = (i + 0.5f) / strips;
            // 褶皱深度 ±7.5cm；相位里掺入种子，四块幡的褶皱走向各不相同
            float fold = Mathf.Sin(u * Mathf.PI * 2.4f + seed * 0.37f);
            float twist = fold * 22f;

            Vector3 center = topCenter
                + right * ((u - 0.5f) * width)
                + forward * (fold * 0.075f)
                + Vector3.down * (height * 0.5f + Mathf.Abs(fold) * 0.02f);

            Vector3 size = new Vector3(stripWidth * 1.12f, height * (1f - Mathf.Abs(fold) * 0.05f), 0.045f);
            Quaternion stripRotation = yawRotation * Quaternion.Euler(0f, twist, lean * (0.6f + fold * 0.4f));
            batch.AddBox(center, size, stripRotation, false);
        }

        // 顶部挂杆
        batch.AddBox(topCenter + Vector3.up * 0.04f, new Vector3(width * 1.06f, 0.08f, 0.08f), yawRotation, false);
    }

    private static void BuildVinesV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();
        System.Random random = new System.Random(3391);

        // ---------- 1) 墙面垂藤：只挂在真正有墙的位置，整片限制在 2.0~5.0m 高度带内 ----------
        // 北墙中段是 8×6 高台（x -4.6..4.6），那里没有墙，垂藤会悬在台面上方读成"墙头长草"，必须跳过。
        for (int i = 0; i < 26; i++)
        {
            float along = Mathf.Lerp(-8.4f, 8.4f, (float)random.NextDouble());
            Vector3 anchor;
            float yaw;
            int side = i % 4;
            if (side == 0)
            {
                anchor = new Vector3(along, 0f, -9.40f);
                yaw = 0f;
            }
            else if (side == 1)
            {
                if (Mathf.Abs(along) < 4.7f) continue;
                anchor = new Vector3(along, 0f, 9.40f);
                yaw = 180f;
            }
            else if (side == 2)
            {
                anchor = new Vector3(9.40f, 0f, along);
                yaw = 90f;
            }
            else
            {
                anchor = new Vector3(-9.40f, 0f, along);
                yaw = 90f;
            }

            // 单张大卡片会被相机放大到铺满画面（04 右上那块占了 1/4 屏），这里三件事一起做：
            // 尺寸压到 0.55~0.80m、高度带收到 3.2~5.0m、每丛改成两片垂直交叉（总有一片迎着光，不再整片发黑）
            float bottom = Mathf.Lerp(3.2f, 4.2f, (float)random.NextDouble());
            float height = Mathf.Lerp(0.55f, 0.80f, (float)random.NextDouble());
            anchor.y = Mathf.Min(bottom + height * 0.5f, 5.0f - height * 0.5f);
            float width = height * Mathf.Lerp(0.62f, 0.78f, (float)random.NextDouble());
            batch.AddQuad(anchor, new Vector2(width, height), Quaternion.Euler(0f, yaw, 0f));
            batch.AddQuad(anchor, new Vector2(width, height), Quaternion.Euler(0f, yaw + 90f, 0f));
        }

        // ---------- 2) 雕像身上的藤蔓（缩到 0.45×0.85，只做点缀） ----------
        for (int i = 0; i < V2StatueSpots.Length; i++)
        {
            Vector3 foot = V2StatueSpots[i];
            for (int k = 0; k < 3; k++)
            {
                Quaternion facing = Quaternion.Euler(0f, i * 120f + k * 55f, 0f);
                Vector3 center = foot + Vector3.up * (0.95f + k * 0.3f) + facing * new Vector3(0f, 0f, 0.34f);
                // 每处两片垂直交叉，尺寸 0.30×0.60（原来 0.45×0.85 单张）
                batch.AddQuad(center, new Vector2(0.30f, 0.60f), facing);
                batch.AddQuad(center, new Vector2(0.30f, 0.60f), facing * Quaternion.Euler(0f, 90f, 0f));
            }
        }

        // ---------- 3) 矮草丛：庭院地面与高台台面，0.35~0.6m，三片交叉成簇 ----------
        for (int i = 0; i < 40; i++)
        {
            float x = (float)random.NextDouble() * 17.4f - 8.7f;
            float z = (float)random.NextDouble() * 17.4f - 8.7f;
            if (Mathf.Abs(x) < 2.0f && Mathf.Abs(z) < 2.0f) continue;      // 祭坛正下方
            if (x > 4.6f && z > -1.8f && z < 4.6f) continue;                // 蓄水池
            if (Mathf.Abs(x) < 4.2f && z > 6.4f) continue;                  // 高台台阶前
            // 再往可通行区外收一档：贴墙 1.1m 内与回廊台基一带不放草，
            // 避免相机贴墙时把单簇草放大到占据大半个画面
            if (Mathf.Abs(x) > 7.6f || Mathf.Abs(z) > 7.6f) continue;
            if (x < -6.4f) continue;
            AddGrassTuftV2(batch, new Vector3(x, 0.02f, z), random);
        }
        // 台面上的少量矮草（台面顶面 y=1.66）
        for (int i = 0; i < 10; i++)
        {
            float x = (float)random.NextDouble() * 7.4f - 3.7f;
            float z = V2TerraceFrontZ + 0.8f + (float)random.NextDouble() * 4.8f;
            if (Mathf.Abs(x) < 1.3f && z > 10.4f) continue;                 // 台心祭台
            AddGrassTuftV2(batch, new Vector3(x, V2TerraceTopY + 0.07f, z), random);
        }

        // 双面薄卡片同样不投影：Foliage 着色器是 Cull Off + VFACE 翻转，法线不会发黑，
        // 但零厚度卡片在阴影贴图里极易产生自遮挡暗斑，一并关掉。
        batch.Commit("爬藤", v2Foliage, parent, false);
    }

    /// <summary>
    /// 单簇矮草：3 片交叉四边形，总高 0.35~0.6m、丛幅约为高度的 1.0~1.5 倍。
    /// 原来这里是 1.2~2.2m 的大卡片，读起来像灌木，还把角色与建筑的重点抢掉了。
    /// </summary>
    private static void AddGrassTuftV2(MeshBatchV2 batch, Vector3 basePosition, System.Random random)
    {
        // 高度 0.35~0.60 → 0.30~0.50；叶片从 3 片 60° 改成 4 片 45°：任一光向下总有叶片迎光，
        // 簇内不会再出现整片背光发黑的"墨渍"观感
        float height = Mathf.Lerp(0.30f, 0.50f, (float)random.NextDouble());
        float tuftWidth = height * Mathf.Lerp(0.90f, 1.30f, (float)random.NextDouble());
        for (int blade = 0; blade < 4; blade++)
        {
            float yaw = (float)random.NextDouble() * 180f + blade * 45f;
            float offsetX = ((float)random.NextDouble() - 0.5f) * 0.22f;
            float offsetZ = ((float)random.NextDouble() - 0.5f) * 0.22f;
            Vector3 center = basePosition + new Vector3(offsetX, height * 0.5f, offsetZ);
            batch.AddQuad(center, new Vector2(tuftWidth, height), Quaternion.Euler(0f, yaw, 0f));
        }
    }

    // =====================================================================
    // 火盆
    // =====================================================================

    private static void BuildFirePitV2(Vector3 position, float scale, float intensity, int seed)
    {
        Transform parent = v2WorldRoot;
        GameObject pit = new GameObject("火盆");
        pit.transform.SetParent(parent, false);
        pit.transform.position = position;

        // 盆体与支脚（金属件）；CreateCylinder/CreateBox 自带碰撞体，火盆能挡住玩家
        CreateCylinder("盆体", position + Vector3.up * (0.42f * scale),
            new Vector3(0.86f * scale, 0.2f * scale, 0.86f * scale), v2Metal, pit.transform);

        CreateBox("炉沿", position + Vector3.up * (0.56f * scale),
            new Vector3(0.98f * scale, 0.08f * scale, 0.98f * scale), v2Metal, pit.transform);
        for (int leg = 0; leg < 3; leg++)
        {
            float angle = leg * 120f * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (0.3f * scale);
            GameObject support = CreateBox("支脚", position + offset + Vector3.up * (0.16f * scale),
                new Vector3(0.1f * scale, 0.34f * scale, 0.1f * scale), v2Metal, pit.transform);
            support.transform.rotation = Quaternion.Euler(Mathf.Sin(angle) * 9f, 0f, -Mathf.Cos(angle) * 9f);
        }

        FirePit firePit = pit.AddComponent<FirePit>();
        firePit.Configure(v2Flame, intensity, 7.5f, scale, seed);
    }

    // =====================================================================
    // 氛围：体积光锥 / 尘埃 / 余烬
    // =====================================================================

    private static void BuildLightShaftsV2(Transform parent)
    {
        MeshBatchV2 batch = new MeshBatchV2();

        // 从缺口斜射下来的伪造体积光：北方天空来的主光，向南倾斜
        AddBeamV2(batch, new Vector3(1.8f, 6.4f, 10.6f), new Vector3(-0.4f, 0.1f, 4.0f), 0.9f, 2.6f);
        AddBeamV2(batch, new Vector3(-6.4f, 5.6f, -4.2f), new Vector3(-3.4f, 0.1f, -1.4f), 0.8f, 2.2f);
        AddBeamV2(batch, new Vector3(9.2f, 4.6f, -5.8f), new Vector3(6.4f, 0.1f, -2.6f), 0.7f, 1.9f);
        AddBeamV2(batch, new Vector3(2.6f, 6.0f, 2.2f), new Vector3(1.4f, 0.2f, -1.2f), 1.0f, 2.4f);

        batch.Commit("体积光锥", v2Beam, parent);
    }

    private static void AddBeamV2(MeshBatchV2 batch, Vector3 top, Vector3 bottom, float topRadius, float bottomRadius)
    {
        Mesh mesh = CreateBeamMeshV2(top, bottom, topRadius, bottomRadius);
        if (mesh != null) batch.AddCustomMesh(mesh);
    }

    /// <summary>沿轴生成 3 个交叉四边形；UV.y 由顶到底 0→1，配合渐变贴图做出柔和边缘。</summary>
    private static Mesh CreateBeamMeshV2(Vector3 top, Vector3 bottom, float topRadius, float bottomRadius)
    {
        Vector3 axis = bottom - top;
        float length = axis.magnitude;
        if (length < 0.05f) return null;

        Vector3 direction = axis / length;
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        Vector3 side = Vector3.Cross(direction, reference).normalized;

        const int quadCount = 3;
        Vector3[] vertices = new Vector3[quadCount * 4];
        Vector2[] uvs = new Vector2[quadCount * 4];
        Vector3[] normals = new Vector3[quadCount * 4];
        int[] triangles = new int[quadCount * 12];

        for (int q = 0; q < quadCount; q++)
        {
            Vector3 offset = Quaternion.AngleAxis(q * 60f, direction) * side;
            int v = q * 4;
            vertices[v + 0] = top - offset * topRadius;
            vertices[v + 1] = top + offset * topRadius;
            vertices[v + 2] = bottom - offset * bottomRadius;
            vertices[v + 3] = bottom + offset * bottomRadius;

            uvs[v + 0] = new Vector2(0f, 0f);
            uvs[v + 1] = new Vector2(1f, 0f);
            uvs[v + 2] = new Vector2(0f, 1f);
            uvs[v + 3] = new Vector2(1f, 1f);

            Vector3 normal = Vector3.Cross(direction, offset).normalized;
            for (int n = 0; n < 4; n++) normals[v + n] = normal;

            int t = q * 12;
            triangles[t + 0] = v + 0; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 2; triangles[t + 4] = v + 3; triangles[t + 5] = v + 1;
            triangles[t + 6] = v + 1; triangles[t + 7] = v + 2; triangles[t + 8] = v + 0;
            triangles[t + 9] = v + 1; triangles[t + 10] = v + 3; triangles[t + 11] = v + 2;
        }

        Mesh mesh = new Mesh { name = "V2LightBeam" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// 漂浮尘埃：一层覆盖庭院的主系统 + 火盆上方两处局部浓尘箱。
    /// 速度远低于 0.15m/s、寿命 8~15s、暖白 #D8CFC0、alpha 0.10~0.25。
    /// </summary>
    private static void BuildDustParticlesV2(Transform parent)
    {
        // 主尘埃：整层庭院（18×6.5×18），颗粒数比原来翻倍以上
        CreateDustSystemV2(parent, "空气中的尘埃", new Vector3(0f, 2.6f, -0.4f),
            new Vector3(18f, 6.5f, 18f), 32f, 700);
        // 局部浓尘：祭坛火盆正上方（0.9×1.6×0.9）
        CreateDustSystemV2(parent, "祭坛浓尘", new Vector3(0f, 1.85f, 0f),
            new Vector3(0.9f, 1.6f, 0.9f), 8f, 120);
        // 局部浓尘：高台两座火盆上方
        CreateDustSystemV2(parent, "高台浓尘", new Vector3(0f, 2.9f, 10.4f),
            new Vector3(6.4f, 1.6f, 0.9f), 8f, 120);
    }

    private static void CreateDustSystemV2(Transform parent, string name, Vector3 center,
        Vector3 boxScale, float rate, int maxParticles)
    {
        GameObject dustObject = new GameObject(name);
        dustObject.transform.SetParent(parent, false);
        dustObject.transform.position = center;
        ParticleSystem dust = dustObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = dust.main;
        main.loop = true;
        main.prewarm = true;                     // 一进场景就是满密度，不用等十几秒
        // 暖白 #D8CFC0，alpha 0.10~0.25：受雾色影响，不发生硬边
        Color dustColor = new Color(0.847f, 0.812f, 0.753f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(dustColor.r, dustColor.g, dustColor.b, 0.10f),
            new Color(dustColor.r, dustColor.g, dustColor.b, 0.25f));
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.08f);   // 极慢，远低于 0.15m/s
        main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 15f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;
        main.gravityModifier = -0.0015f;         // 微弱上浮

        ParticleSystem.EmissionModule emission = dust.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);

        ParticleSystem.ShapeModule shape = dust.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = boxScale;

        ParticleSystem.VelocityOverLifetimeModule velocity = dust.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.07f, 0.07f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.01f, 0.10f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.07f, 0.07f);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = dust.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FadeInOutGradientV2());

        ParticleSystemRenderer renderer = dustObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = v2Dust;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>六座火盆上方的发光余烬：自发光暖橙、向上飘、带拖尾，亮度压在 Bloom 阈值以下。</summary>
    private static void BuildEmberParticlesV2(Transform parent)
    {
        Vector3[] firePits =
        {
            new Vector3(0f, 0.84f, 0f),        // 祭坛中央
            new Vector3(-2.6f, 1.6f, 10.4f),   // 高台西
            new Vector3(2.6f, 1.6f, 10.4f),    // 高台东
            new Vector3(7.6f, 0f, 7.8f),       // 庭院东北
            new Vector3(-8.6f, 0f, 7.8f),      // 回廊北端
            new Vector3(-8.5f, 0f, -2.3f)      // 回廊中段
        };
        for (int i = 0; i < firePits.Length; i++)
        {
            CreateEmberSystemV2(parent, "火盆余烬 " + (i + 1), firePits[i] + Vector3.up * 0.62f);
        }
    }

    private static void CreateEmberSystemV2(Transform parent, string name, Vector3 origin)
    {
        GameObject emberObject = new GameObject(name);
        emberObject.transform.SetParent(parent, false);
        emberObject.transform.position = origin;
        ParticleSystem embers = emberObject.AddComponent<ParticleSystem>();
        embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // HDR 亮度 0.95 < Bloom 阈值 1.05：粒子本身发光但不会再把火光糊成一团
        const float hdr = 0.95f;
        Color outer = new Color(1f, 0.60f, 0.30f) * hdr;   // #FF9A4D
        Color inner = new Color(1f, 0.35f, 0.18f) * hdr;   // #FF5A2D

        ParticleSystem.MainModule main = embers.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startColor = new ParticleSystem.MinMaxGradient(outer, inner);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 30;
        main.gravityModifier = -0.015f;

        ParticleSystem.EmissionModule emission = embers.emission;
        emission.enabled = true;
        emission.rateOverTime = new ParticleSystem.MinMaxCurve(6.5f);   // 稳态约 13~26 个存活

        ParticleSystem.ShapeModule shape = embers.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.16f;
        shape.rotation = new Vector3(-90f, 0f, 0f);   // 锥口朝上

        // 被气流轻推的横向摆动
        ParticleSystem.NoiseModule noise = embers.noise;
        noise.enabled = true;
        noise.strength = 0.28f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.25f;
        noise.damping = true;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = embers.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(FadeInOutGradientV2());

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = embers.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, EmberSizeCurveV2());

        // 拖尾：世界空间、0.22~0.4s、宽度由粗到细、颜色 alpha 渐隐；只让 35% 粒子带拖尾控制填充率
        ParticleSystem.TrailModule trails = embers.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.ratio = 0.35f;
        trails.lifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f);
        trails.minVertexDistance = 0.03f;
        trails.worldSpace = true;
        trails.dieWithParticles = true;
        trails.sizeAffectsWidth = true;
        trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));
        trails.colorOverTrail = new ParticleSystem.MinMaxGradient(EmberTrailGradientV2());

        ParticleSystemRenderer renderer = emberObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sharedMaterial = v2Dust;      // 与尘埃共享同一个程序化圆点贴图（加性）
        renderer.trailMaterial = v2Dust;       // 拖尾复用同一材质，不新增贴图资源
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        embers.Play();
    }

    /// <summary>余烬先胀后缩的尺寸曲线（出生 0.75、25% 处最大、末尾缩到 0.2）。</summary>
    private static AnimationCurve EmberSizeCurveV2()
    {
        AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 0.75f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.2f));
        curve.SmoothTangents(1, 0f);
        return curve;
    }

    /// <summary>拖尾渐变：亮橙 → 暗橙，alpha 0.8 → 0。</summary>
    private static Gradient EmberTrailGradientV2()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.75f, 0.45f), 0f),
                new GradientColorKey(new Color(1f, 0.45f, 0.22f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.8f, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    private static Gradient FadeInOutGradientV2()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.22f),
                new GradientAlphaKey(0.8f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    // =====================================================================
    // 材质与贴图
    // =====================================================================

    /// <summary>幂等地准备 V2 贴图与材质（第二次调用直接返回）。</summary>
    internal static void PrepareMaterialsV2()
    {
        // Build() 每次都会删除 Materials 目录，被删掉的资源在 C# 里会变成 null，
        // 因此不能只靠布尔标记，必须校验材质引用仍然有效
        if (v2Prepared && v2Stone != null && v2Floor != null && v2Wall != null) return;
        v2Prepared = true;

        // 清掉上一次生成留下的合并网格，避免资源堆积
        AssetDatabase.DeleteAsset(Root + "/Meshes");
        EnsureFolder(Root + "/Meshes");

        v2CubeMesh = PrimitiveMeshV2(PrimitiveType.Cube);
        v2CylinderMesh = PrimitiveMeshV2(PrimitiveType.Cylinder);
        v2QuadMesh = CreateQuadMeshV2();

        // ---------- 贴图 ----------
        Texture2D stoneAlbedo = TextureKit.StoneAlbedo(512, V2StoneLight, 0.24f, 1201, 0.34f);
        Texture2D stoneNormal = TextureKit.StoneNormal(512, 1f, 1f, 1202);
        Texture2D stoneRough = TextureKit.Roughness(256, 0.62f, 0.95f, 1203);
        // 苔藓基色刻意比 #5F7A4A 更黄更灰（约 hue 80°/sat 33%），避免纯绿方块扎眼
        Texture2D mossAlbedo = TextureKit.StoneAlbedo(256, new Color(0.42f, 0.46f, 0.30f), 0.46f, 1204, 0.85f);
        Texture2D mossNormal = TextureKit.StoneNormal(256, 0.7f, 2f, 1205);
        // 地面专用方石：3×3 正方石块、缝隙窄而深、整体更灰，避免地面读成"长木地板"
        Texture2D slabAlbedo = TextureKit.SlabAlbedo(512, new Color(0.70f, 0.69f, 0.66f), 0.30f, 1301, 0.28f);
        Texture2D slabNormal = TextureKit.SlabNormal(512, 1f, 1302);
        Texture2D grassCard = TextureKit.GrassCard(256, 1206);
        Texture2D noise = TextureKit.Noise(256, 1207);
        Texture2D flameSpot = CreateSoftSpotTextureV2(128);
        Texture2D beamTexture = CreateBeamTextureV2(128);

        SaveTexture(stoneAlbedo, "V2StoneAlbedo");
        SaveTexture(stoneNormal, "V2StoneNormal");
        SaveTexture(stoneRough, "V2StoneRoughness");
        SaveTexture(mossAlbedo, "V2MossAlbedo");
        SaveTexture(mossNormal, "V2MossNormal");
        SaveTexture(slabAlbedo, "V2SlabAlbedo");
        SaveTexture(slabNormal, "V2SlabNormal");
        SaveTexture(grassCard, "V2FoliageCard");
        SaveTexture(noise, "V2Noise");
        SaveTexture(flameSpot, "V2FlameSpot");
        SaveTexture(beamTexture, "V2BeamGradient");

        // ---------- 材质（唯一入口 ShaderKit） ----------
        // 注意：ShaderKit.Stone 的着色公式是"反照率 × 色调A"，而贴图里已经烘焙了 #C8C0AE 基色，
        // 因此 tintA 必须接近白，暗部与苔痕交给 tintB 承担，否则等于把石材反照率再砍一半。
        // 地面：换方石贴图，材质 tiling 固定 1（UV 缩放交给 AddFlatSlab 的 texel 控制，
        // 两级乘算会把石板压到 0.25m 见方、碎成一片噪点）。三套变体只改色偏与粗糙度。
        v2Floor = ShaderKit.Stone("V2Floor", slabAlbedo, slabNormal,
            new Color(0.92f, 0.91f, 0.89f), new Color(0.50f, 0.49f, 0.47f), 1f, 0.82f);
        v2FloorWarm = ShaderKit.Stone("V2FloorWarm", slabAlbedo, slabNormal,
            new Color(0.90f, 0.885f, 0.86f), new Color(0.52f, 0.50f, 0.47f), 1f, 0.86f);
        v2FloorCool = ShaderKit.Stone("V2FloorCool", slabAlbedo, slabNormal,
            new Color(0.87f, 0.89f, 0.91f), new Color(0.46f, 0.48f, 0.51f), 1f, 0.78f);
        v2Stone = ShaderKit.Stone("V2Stone", stoneAlbedo, stoneNormal, new Color(0.90f, 0.88f, 0.84f), V2StoneDark, 1.6f, 0.86f);
        v2Wall = ShaderKit.Stone("V2Wall", stoneAlbedo, stoneNormal, new Color(0.82f, 0.80f, 0.76f), new Color(0.58f, 0.56f, 0.53f), 1.4f, 0.9f);
        v2MossMat = ShaderKit.Stone("V2MossPatch", mossAlbedo, mossNormal,
            new Color(0.42f, 0.47f, 0.31f), new Color(0.24f, 0.27f, 0.17f), 1.2f, 0.95f);
        v2Wet = ShaderKit.Stone("V2WetStone", stoneAlbedo, stoneNormal, V2WetDeep * 1.4f, V2WetDeep, 1.6f, 0.28f);
        v2Metal = ShaderKit.Metal("V2Metal", stoneNormal);
        v2Bone = ShaderKit.Stone("V2Bone", stoneAlbedo, stoneNormal, new Color(0.86f, 0.83f, 0.74f), new Color(0.6f, 0.58f, 0.5f), 1.4f, 0.72f);
        v2Foliage = ShaderKit.Foliage("V2Foliage", grassCard, 0.42f);
        v2Wood = ShaderKit.Stone("V2Wood", stoneAlbedo, stoneNormal, new Color(0.36f, 0.26f, 0.18f), new Color(0.2f, 0.14f, 0.1f), 1.4f, 0.9f);
        v2ClothRed = ShaderKit.Stone("V2ClothRed", stoneAlbedo, stoneNormal, V2Blood, V2Blood * 0.62f, 1.2f, 0.95f);
        v2ClothCold = ShaderKit.Stone("V2ClothCold", stoneAlbedo, stoneNormal, V2ShadeBlue * 1.25f, V2ShadeCold, 1.2f, 0.95f);
        v2Far = ShaderKit.Stone("V2Far", stoneAlbedo, stoneNormal, new Color(0.2f, 0.22f, 0.29f), new Color(0.11f, 0.12f, 0.16f), 1.2f, 1f);
        // 更远的一层剪影：更深更蓝，配合雾形成两层纵深
        v2FarDeep = ShaderKit.Stone("V2FarDeep", stoneAlbedo, stoneNormal,
            new Color(0.15f, 0.17f, 0.23f), new Color(0.09f, 0.10f, 0.14f), 1f, 1f);
        // 山脊专用：在塔楼剪影的基础上再压暗 15%，让山只剩轮廓、竖向接缝被雾吃掉
        // 近层山脊再压暗 6%（0.170/0.187/0.247 → 0.160/0.176/0.232）：中景只剩轮廓，竖缝更早沉进雾里
        v2Ridge = ShaderKit.Stone("V2Ridge", stoneAlbedo, stoneNormal,
            new Color(0.160f, 0.176f, 0.232f), new Color(0.088f, 0.096f, 0.128f), 1f, 1f);
        v2RidgeDeep = ShaderKit.Stone("V2RidgeDeep", stoneAlbedo, stoneNormal,
            new Color(0.128f, 0.145f, 0.196f), new Color(0.077f, 0.085f, 0.119f), 1f, 1f);

        // 湿润度：蓄水池底的水痕
        if (v2Wet != null && v2Wet.HasProperty("_Wetness")) v2Wet.SetFloat("_Wetness", 0.85f);
        // 粗糙度贴图：若着色器提供通道就挂上，缺失则忽略
        if (v2Floor != null && v2Floor.HasProperty("_RoughnessMap")) v2Floor.SetTexture("_RoughnessMap", stoneRough);

        v2Flame = CreateAdditiveMaterialV2("V2FlameAdditive", flameSpot, 0.55f);
        v2Beam = CreateAdditiveMaterialV2("V2LightBeam", beamTexture, 0.3f);
        v2Dust = CreateAdditiveMaterialV2("V2DustMotes", flameSpot, 0.18f);
    }

    /// <summary>加性半透明材质（火焰、尘埃、体积光），用内置加性粒子着色器。</summary>
    private static Material CreateAdditiveMaterialV2(string name, Texture2D texture, float tint)
    {
        string path = MatRoot + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);

        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("找不到加性混合着色器：" + name);
            return v2Metal;
        }

        Material material = new Material(shader) { name = name };
        material.mainTexture = texture;
        if (material.HasProperty("_TintColor"))
            material.SetColor("_TintColor", new Color(V2FireWarm.r * tint, V2FireWarm.g * tint, V2FireWarm.b * tint, tint));
        if (material.HasProperty("_InvFade")) material.SetFloat("_InvFade", 0.8f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>径向柔光贴图：火焰粒子与尘埃共用。</summary>
    private static Texture2D CreateSoftSpotTextureV2(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / radius;
                float falloff = Mathf.Clamp01(1f - distance);
                falloff = falloff * falloff * (3f - 2f * falloff);
                pixels[y * size + x] = new Color(1f, 0.88f, 0.72f, falloff);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    /// <summary>体积光用的竖向渐变：顶部亮、底部淡、左右柔和收边。</summary>
    private static Texture2D CreateBeamTextureV2(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            float alongFade = Mathf.Pow(1f - v, 0.8f);
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float across = 1f - Mathf.Abs(u * 2f - 1f);
                across = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(across * 1.4f));
                float value = Mathf.Clamp01(alongFade * across);
                value *= 0.85f + Mathf.PerlinNoise(u * 5f, v * 5f) * 0.3f;
                value = Mathf.Clamp01(value);
                pixels[y * size + x] = new Color(value, value * 0.95f, value * 0.88f, value);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    // =====================================================================
    // 合并网格基础件
    // =====================================================================

    /// <summary>碰撞盒描述（合并网格后统一挂 BoxCollider）。</summary>
    private struct ColliderSpecV2
    {
        public Vector3 Center;
        public Vector3 Size;
        public Quaternion Rotation;
    }

    /// <summary>
    /// 网格累加器：把成百上千个小石块合并成一个 Mesh 资源，渲染只占一个 draw call；
    /// 轴向对齐的碰撞盒合并成同一物体上的多个 BoxCollider，带旋转的单独开子物体。
    /// </summary>
    private sealed class MeshBatchV2
    {
        private readonly List<CombineInstance> parts = new List<CombineInstance>();
        private readonly List<ColliderSpecV2> colliders = new List<ColliderSpecV2>();
        private readonly List<Mesh> temporaries = new List<Mesh>();

        public void AddBox(Vector3 center, Vector3 size)
        {
            AddBox(center, size, Quaternion.identity, true);
        }

        public void AddBox(Vector3 center, Vector3 size, bool withCollider)
        {
            AddBox(center, size, Quaternion.identity, withCollider);
        }

        public void AddBox(Vector3 center, Vector3 size, Quaternion rotation, bool withCollider)
        {
            AddMesh(v2CubeMesh, Matrix4x4.TRS(center, rotation, size));
            if (withCollider) colliders.Add(new ColliderSpecV2 { Center = center, Size = size, Rotation = rotation });
        }

        /// <summary>按世界尺寸缩放 UV，让大墙面与小构件的贴图密度一致。</summary>
        public void AddBoxWithUv(Vector3 center, Vector3 size, Quaternion rotation, bool withCollider, float texelWorldSize)
        {
            AddBoxWithUv(center, size, rotation, withCollider, texelWorldSize, Vector2.zero);
        }

        /// <summary>
        /// 同上，但带 UV 相位偏移：相邻石板/墙段各用不同相位，同一张贴图就不会在整面上"对齐重复"。
        /// </summary>
        public void AddBoxWithUv(Vector3 center, Vector3 size, Quaternion rotation, bool withCollider,
            float texelWorldSize, Vector2 uvOffset)
        {
            Vector2 scale = new Vector2(
                Mathf.Max(0.25f, Mathf.Max(size.x, size.z) / texelWorldSize),
                Mathf.Max(0.25f, size.y / texelWorldSize));
            AddBoxWithUvScale(center, size, rotation, withCollider, scale, uvOffset);
        }

        /// <summary>
        /// 平板（地面 / 台面 / 池底）专用：可见面是顶面，UV 必须按 X/Z 两个水平轴缩放。
        /// 旧实现按 Y（厚度 0.1m）缩放 V 轴，把石板贴图沿 Z 拉了将近 30 倍，才会像"长木地板"。
        /// </summary>
        public void AddFlatSlab(Vector3 center, Vector3 size, Quaternion rotation, bool withCollider,
            float texelWorldSize, Vector2 uvOffset)
        {
            Vector2 scale = new Vector2(
                Mathf.Max(0.2f, size.x / texelWorldSize),
                Mathf.Max(0.2f, size.z / texelWorldSize));
            AddBoxWithUvScale(center, size, rotation, withCollider, scale, uvOffset);
        }

        private void AddBoxWithUvScale(Vector3 center, Vector3 size, Quaternion rotation, bool withCollider,
            Vector2 scale, Vector2 uvOffset)
        {
            Mesh mesh = Object.Instantiate(v2CubeMesh);
            mesh.name = "V2CubeScaled";
            Vector2[] uvs = mesh.uv;
            for (int i = 0; i < uvs.Length; i++)
                uvs[i] = new Vector2(uvs[i].x * scale.x + uvOffset.x, uvs[i].y * scale.y + uvOffset.y);
            mesh.uv = uvs;
            temporaries.Add(mesh);

            AddMesh(mesh, Matrix4x4.TRS(center, rotation, size));
            if (withCollider) colliders.Add(new ColliderSpecV2 { Center = center, Size = size, Rotation = rotation });
        }

        /// <summary>Unity 原始圆柱高 2、直径 1，这里换算成"半径 + 完整高度"。</summary>
        public void AddCylinder(Vector3 center, float radius, float height, Quaternion rotation, bool withCollider)
        {
            AddMesh(v2CylinderMesh, Matrix4x4.TRS(center, rotation, new Vector3(radius * 2f, height * 0.5f, radius * 2f)));
            if (withCollider)
                colliders.Add(new ColliderSpecV2
                {
                    Center = center,
                    Size = new Vector3(radius * 2f, height, radius * 2f),
                    Rotation = rotation
                });
        }

        public void AddQuad(Vector3 center, Vector2 size, Quaternion rotation)
        {
            AddMesh(v2QuadMesh, Matrix4x4.TRS(center, rotation, new Vector3(size.x, size.y, 1f)));
        }

        public void AddColliderOnly(Vector3 center, Vector3 size, Quaternion rotation)
        {
            colliders.Add(new ColliderSpecV2 { Center = center, Size = size, Rotation = rotation });
        }

        public void AddMesh(Mesh mesh, Matrix4x4 matrix)
        {
            parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = 0, transform = matrix });
        }

        public void AddCustomMesh(Mesh mesh)
        {
            temporaries.Add(mesh);
            AddMesh(mesh, Matrix4x4.identity);
        }

        public GameObject Commit(string name, Material material, Transform parent, bool castShadows = true)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            if (parts.Count > 0)
            {
                Mesh mesh = new Mesh { name = name };
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(parts.ToArray(), true, true);
                NormalizeNormalsV2(mesh);
                mesh.RecalculateBounds();

                string path = Root + "/Meshes/" + SanitizeV2(name) + "_" + (v2MeshSerial++) + ".asset";
                AssetDatabase.CreateAsset(mesh, path);

                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }

            foreach (ColliderSpecV2 spec in colliders) AddColliderV2(root, spec);

            foreach (Mesh temporary in temporaries)
                if (temporary != null) Object.DestroyImmediate(temporary);
            temporaries.Clear();

            return root;
        }
    }

    /// <summary>合并后的法线可能因非等比缩放变长，归一化并补齐切线。</summary>
    private static void NormalizeNormalsV2(Mesh mesh)
    {
        Vector3[] normals = mesh.normals;
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].sqrMagnitude > 1e-8f ? normals[i].normalized : Vector3.up;
        mesh.normals = normals;
        if (mesh.tangents.Length != mesh.vertexCount) mesh.RecalculateTangents();
    }

    private static void AddColliderV2(GameObject host, ColliderSpecV2 spec)
    {
        if (Quaternion.Angle(spec.Rotation, Quaternion.identity) < 0.05f)
        {
            // 轴向对齐：直接挂在合并物体上，省下大量空物体
            BoxCollider collider = host.AddComponent<BoxCollider>();
            collider.center = spec.Center;
            collider.size = spec.Size;
            return;
        }

        GameObject holder = new GameObject("碰撞体");
        holder.transform.SetParent(host.transform, false);
        holder.transform.position = spec.Center;
        holder.transform.rotation = spec.Rotation;
        BoxCollider rotated = holder.AddComponent<BoxCollider>();
        rotated.size = spec.Size;
    }

    private static GameObject NewGroupV2(string name)
    {
        GameObject group = new GameObject(name);
        if (v2WorldRoot != null) group.transform.SetParent(v2WorldRoot, false);
        return group;
    }

    private static string SanitizeV2(string name)
    {
        return name.Replace(" ", "_").Replace("/", "_");
    }

    private static Mesh PrimitiveMeshV2(PrimitiveType type)
    {
        GameObject temporary = GameObject.CreatePrimitive(type);
        Mesh mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temporary);
        return mesh;
    }

    /// <summary>
    /// 双面四边形——**只允许给 Foliage 材质用**（Ruins/Foliage 是 Cull Off + VFACE 翻转法线，
    /// 背面会被正确翻转照亮）。反过来朝的那组三角形与共享顶点共用同一套法线，
    /// 因此任何 Cull Back 的材质（例如 Ruins/StylizedStone）套在上面都会有一个面读成纯黑；
    /// 需要"薄片"时请用 0.02m 的实心薄盒（BuildMossPatchesV2 就是这么做的）。
    /// </summary>
    private static Mesh CreateQuadMeshV2()
    {
        Mesh mesh = new Mesh { name = "V2Quad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
        };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.triangles = new[]
        {
            0, 2, 1, 2, 3, 1,
            1, 2, 0, 1, 3, 2
        };
        mesh.RecalculateBounds();
        return mesh;
    }
}
