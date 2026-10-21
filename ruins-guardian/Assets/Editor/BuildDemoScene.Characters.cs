using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// T1 角色模块：低多边形人形角色构建（玩家 + 守卫）。
/// 节点层级严格遵循规范 5.5：Visual/Body/{Hips, Chest, Head, ArmL, ArmR, LegL, LegR}，
/// 手臂下 上臂 / 前臂 / 手 / WeaponPivot，腿下 大腿 / 小腿 / 靴子（Animator 曲线按路径驱动，改名即失效）。
/// 网格全部来自 MeshKit（已资源化复用），材质全部来自 ShaderKit.Character / ShaderKit.Emissive。
/// 尺寸基准：总高 1.85m，头身比 1:6.5，肩宽 0.52m；髋关节在 Body 局部 y=-0.06（世界 y=0.94）。
/// </summary>
public static partial class BuildDemoScene
{
    // ================= 调色板（规范第 2 节 + 角色专用中性色） =================

    /// <summary>玩家深蓝布衣（布衣 / 裤）。玩家配色固定走常量，不受集成方传参影响；三阶 ramp 生效后提亮到能读出蓝色。</summary>
    internal static readonly Color PlayerClothColor = new Color(0.22f, 0.34f, 0.66f);

    /// <summary>玩家披风：比布料更饱和的深蓝，作为画面里一块明确的色块。</summary>
    internal static readonly Color PlayerCapeColor = new Color(0.12f, 0.20f, 0.45f);

    /// <summary>守卫锈红布甲。</summary>
    internal static readonly Color GuardClothColor = new Color(0.42f, 0.16f, 0.12f);

    private static readonly Color PaletteBlood = new Color(0.85f, 0.29f, 0.23f);     // #D84A3A 血红

    // T3 新版 ramp 的边缘光：全部改冷色，软质与硬质两档
    private static readonly Color RimSoft = new Color(0.43f, 0.55f, 0.85f); // #6E8CD8 布料 / 披风 / 皮肤
    private static readonly Color RimHard = new Color(0.32f, 0.44f, 0.78f); // #5270C7 钢甲 / 金属件

    // 明暗硬度：钢甲暗部更狠、亮部交界更早；软质只压暗部，其余沿用 shader 默认（_RampEdge 0.45 / _RampHiEdge 0.62）
    private const float HardDarken = 0.38f;
    private const float HardRampHiEdge = 0.65f;
    private const float SoftDarken = 0.50f;
    private const float PlayerClothDarken = 0.42f; // 玩家布衣专用：暗部更硬，三阶 ramp 更分明

    // 玩家盔甲分层：肩甲最亮 → 胸甲 → 护臂与靴最暗，色差拉大到远看也能读出块面
    private static readonly Color PlayerArmorBright = new Color(0.82f, 0.84f, 0.88f);
    private static readonly Color PlayerArmorMid = new Color(0.68f, 0.72f, 0.78f);
    private static readonly Color PlayerArmorDeep = new Color(0.54f, 0.57f, 0.62f);
    private static readonly Color PlayerHelmetPlate = new Color(0.60f, 0.64f, 0.70f);
    private static readonly Color PlayerVisor = new Color(0.10f, 0.11f, 0.14f);
    private static readonly Color PlayerBlade = new Color(0.85f, 0.88f, 0.95f);
    private static readonly Color PlayerDarkMetal = new Color(0.35f, 0.36f, 0.40f);

    // 守卫铁件
    private static readonly Color GuardIronPlate = new Color(0.30f, 0.28f, 0.28f);
    private static readonly Color GuardFace = new Color(0.12f, 0.12f, 0.14f);
    private static readonly Color GuardDarkMetal = new Color(0.36f, 0.34f, 0.34f);
    private static readonly Color CommonSkin = new Color(0.62f, 0.50f, 0.42f);

    // 描边宽度（NDC，写入 ShaderKit.Character 的 _OutlineWidth；shader 按 clipPos.w 补偿 = 屏幕空间等宽）
    // 0.008 NDC 在 1080p 下约 8.6px 全宽（单侧约 4.3px），近景是细线；0.026 曾达约 28px，会把远景角色糊成黑剪影
    private const float PlayerOutline = 0.008f;
    private const float GuardOutline = 0.007f;

    private static readonly Dictionary<string, Material> charMaterialCache = new Dictionary<string, Material>();

    // ================= 对外入口 =================

    /// <summary>
    /// 构建玩家角色（仅视觉 + 碰撞 + 受击闪白；Health / PlayerExplorer / PlayerCombat 由集成方添加）。
    /// isPlayer=true 时配色走 PlayerClothColor / PlayerCapeColor 等固定常量（clothColor 传参不参与），
    /// 避免外部传入亮色在暖光下过曝；isPlayer=false 时使用传入的 clothColor。
    /// </summary>
    internal static GameObject BuildCharacterV2(string name, Vector3 position, Color clothColor, bool isPlayer)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 1f, 0f);
        collider.radius = 0.42f;
        collider.height = 1.95f;

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        Animator animator = visual.AddComponent<Animator>();
        if (controller != null) animator.runtimeAnimatorController = controller;

        GameObject body = CharNode("Body", visual.transform, new Vector3(0f, 1f, 0f));

        CharMaterials materials = CreateCharMaterials(clothColor, isPlayer);

        // ---- 规范 5.5 的七个驱动节点，必须全部是 Body 的直接子节点 ----
        GameObject hips = CharNode("Hips", body.transform, Vector3.zero);
        GameObject chest = CharNode("Chest", body.transform, new Vector3(0f, 0.10f, 0f));
        GameObject head = CharNode("Head", body.transform, new Vector3(0f, 0.60f, 0f));
        GameObject armL = CharNode("ArmL", body.transform, new Vector3(-0.195f, 0.475f, 0f));
        GameObject armR = CharNode("ArmR", body.transform, new Vector3(0.195f, 0.475f, 0f));
        GameObject legL = CharNode("LegL", body.transform, new Vector3(-0.105f, -0.06f, 0f));
        GameObject legR = CharNode("LegR", body.transform, new Vector3(0.105f, -0.06f, 0f));

        BuildHipsV2(hips.transform, materials, isPlayer);
        Renderer torso = BuildChestV2(chest.transform, materials, isPlayer);
        BuildHeadV2(head.transform, materials, isPlayer);
        BuildArmV2(armL.transform, materials, false, isPlayer);
        Transform weaponR = BuildArmV2(armR.transform, materials, true, isPlayer);
        BuildLegV2(legL.transform, materials, isPlayer);
        BuildLegV2(legR.transform, materials, isPlayer);
        BuildCapeV2(chest.transform, materials, isPlayer);

        if (isPlayer) BuildSwordV2(weaponR, materials);
        else BuildHammerV2(weaponR, materials);

        HitFlash flash = root.AddComponent<HitFlash>();
        SetPrivate(flash, "bodyRenderer", torso);

        return root;
    }

    /// <summary>构建守卫：角色本体 + Health + EnemyWanderAI（含巡逻点与头顶警觉图标）。</summary>
    internal static GameObject BuildGuardV2(string name, Vector3 position, Vector3[] patrolPoints, Transform player)
    {
        GameObject guard = BuildCharacterV2(name, position, GuardClothColor, false);

        Health health = guard.AddComponent<Health>();
        SetPrivate(health, "maxHealth", 3f);
        SetPrivate(health, "isPlayer", false);
        SetPrivate(health, "invulnerableTime", 0.1f);

        EnemyWanderAI ai = guard.AddComponent<EnemyWanderAI>();
        guard.AddComponent<SimpleCollisionResolver>();

        Transform[] points = new Transform[patrolPoints.Length];
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            GameObject point = new GameObject(name + " 巡逻点 " + (i + 1));
            point.transform.position = patrolPoints[i];
            points[i] = point.transform;
        }

        Canvas alertCanvas;
        GameObject question;
        GameObject exclamation;
        BuildAlertIcon(guard, out alertCanvas, out question, out exclamation);

        SetPrivate(ai, "player", player);
        SetPrivate(ai, "patrolPoints", points);
        SetPrivate(ai, "animator", guard.GetComponentInChildren<Animator>());
        SetPrivate(ai, "alertCanvas", alertCanvas);
        SetPrivate(ai, "questionMark", question);
        SetPrivate(ai, "exclamationMark", exclamation);

        return guard;
    }

    /// <summary>
    /// 宝石视觉体：切角八面体（上锥 + 腰部棱台 + 下锥，共 8 边 64 面），发光材质，不带碰撞体。
    /// 返回的根节点 pivot 在宝石腰线（几何中心），朝上为 +Y，没有额外旋转；
    /// scale 即宝石直径（米），scale=0.22 约等于原方块宝石的体积感。
    /// 用法：GameObject gem = BuildGemVisualV2(color, 0.22f); gem.transform.position = 目标点（y 抬到半高处即可）；
    /// 需要拾取请自行 AddComponent<SphereCollider>()（半径约 scale*0.5）或挂到 Collectible 上。
    /// </summary>
    internal static GameObject BuildGemVisualV2(Color color, float scale)
    {
        GameObject gem = new GameObject("宝石体");
        gem.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        MeshFilter filter = gem.AddComponent<MeshFilter>();
        filter.sharedMesh = MeshKit.Gem(0.5f, 1f, 8);

        MeshRenderer renderer = gem.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GemMaterial(color);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return gem;
    }

    /// <summary>宝石材质：按颜色缓存，同一颜色的所有宝石共用一份 Emissive 材质。</summary>
    private static Material GemMaterial(Color color)
    {
        string key = "Gem_" + ColorUtility.ToHtmlStringRGB(color);
        Material cached;
        if (charMaterialCache.TryGetValue(key, out cached) && cached != null) return cached;
        // 发光强度压到 1.35：配合 T2 把宝石点光降到 0.55/2.6，避免 Bloom 叠加后整颗发白
        Material material = ShaderKit.Emissive("Char" + key, color, 1.35f);
        charMaterialCache[key] = material;
        return material;
    }

    // ================= 材质 =================

    private sealed class CharMaterials
    {
        public Material cloth;       // 布衣 / 裤
        public Material cape;        // 披风
        public Material armorBright; // 肩甲（最亮）
        public Material armorMid;    // 胸甲 / 护颈 / 裙摆
        public Material armorDeep;   // 护臂 / 护膝 / 腹甲 / 锤头
        public Material helmet;      // 头盔
        public Material visor;       // 面罩缝 / 铁面（近黑，细描边）
        public Material steel;       // 剑刃（高亮金属）
        public Material darkMetal;   // 护手 / 剑柄 / 带扣 / 靴（细描边）
        public Material visorFlat;     // 胸甲分缝 / 通风槽：近黑细件，零描边
        public Material darkMetalFlat; // 盔中脊：暗金属细件，零描边
        public Material leather;
        public Material skin;
        public Material accent;
        public Material glow;
    }

    /// <summary>整体提亮一档，上限 0.99，任何部件都不会被推到纯白。</summary>
    private static Color Lift(Color color, float amount)
    {
        return new Color(Mathf.Min(0.99f, color.r + amount), Mathf.Min(0.99f, color.g + amount), Mathf.Min(0.99f, color.b + amount));
    }

    /// <summary>整体压暗一档。</summary>
    private static Color Darken(Color color, float amount)
    {
        return new Color(Mathf.Max(0f, color.r - amount), Mathf.Max(0f, color.g - amount), Mathf.Max(0f, color.b - amount));
    }

    private static CharMaterials CreateCharMaterials(Color clothColor, bool isPlayer)
    {
        CharMaterials materials = new CharMaterials();

        // 玩家走固定配色（忽略传参），守卫用传入的锈红
        Color baseCloth = isPlayer ? PlayerClothColor : clothColor;
        // 描边：玩家 0.008（约 8.6px 全宽），守卫 0.007（约 7.5px 全宽）；屏幕空间等宽，远景也不会包住整人
        float outline = isPlayer ? PlayerOutline : GuardOutline;
        // 近黑暗缝 / 面罩这类细窄件用更细的描边（0.7×），近黑细件本身走 outlineWidth=0
        float fineOutline = outline * 0.7f;

        // 软质件：冷 rim 是逆光下的主要轮廓来源，强度比钢甲低但不再压得太狠；只压暗部（_RampEdge / _RampHiEdge 用 shader 默认）
        // 玩家布料暗部压得更狠（0.42）让三阶更分明；守卫布甲维持软质 0.50
        materials.cloth = CharMat("Cloth", baseCloth, Lift(baseCloth, 0.10f), RimSoft, outline,
            isPlayer ? 0.75f : 0.55f, isPlayer ? PlayerClothDarken : SoftDarken);

        Color capeColor = isPlayer ? PlayerCapeColor : Darken(baseCloth, 0.08f);
        materials.cape = CharMat("Cape", capeColor, Lift(capeColor, 0.10f), RimSoft, outline,
            isPlayer ? 0.70f : 0.48f, SoftDarken);

        if (isPlayer)
        {
            // 银灰偏冷，三档分明：肩甲最亮 → 胸甲 → 护臂与靴最暗；钢甲用冷硬 rim + 更硬的明暗
            materials.armorBright = CharMat("SteelShoulder", PlayerArmorBright, Lift(PlayerArmorBright, 0.06f), RimHard, outline, 0.90f, HardDarken, HardRampHiEdge);
            materials.armorMid = CharMat("SteelChest", PlayerArmorMid, Lift(PlayerArmorMid, 0.06f), RimHard, outline, 0.88f, HardDarken, HardRampHiEdge);
            materials.armorDeep = CharMat("SteelArm", PlayerArmorDeep, Lift(PlayerArmorDeep, 0.06f), RimHard, outline, 0.80f, HardDarken, HardRampHiEdge);
            materials.helmet = CharMat("SteelHelm", PlayerHelmetPlate, Lift(PlayerHelmetPlate, 0.06f), RimHard, outline, 0.88f, HardDarken, HardRampHiEdge);
            materials.steel = CharMat("SteelBlade", PlayerBlade, new Color(0.90f, 0.92f, 0.97f), RimHard, outline, 0.95f, HardDarken, HardRampHiEdge);
            materials.darkMetal = CharMat("SteelDark", PlayerDarkMetal, Lift(PlayerDarkMetal, 0.09f), RimHard, outline, 0.80f, HardDarken, HardRampHiEdge);
            materials.visor = CharMat("VisorDark", PlayerVisor, Lift(PlayerVisor, 0.06f), RimHard, fineOutline, 0.30f, HardDarken, HardRampHiEdge);
            // 近黑细窄件专用：描边宽度写 0（ShaderKit.Character 内部是 Clamp(width,0,0.2)，0 会如实写入），
            // 颜色与尺寸和普通暗件一致，但绝不外扩，避免描边把细缝吞成黑块；也不要边缘光。
            materials.visorFlat = CharMat("VisorFlat", PlayerVisor, Lift(PlayerVisor, 0.06f), RimHard, 0f, 0f, HardDarken, HardRampHiEdge);
            materials.darkMetalFlat = CharMat("SteelDarkFlat", PlayerDarkMetal, Lift(PlayerDarkMetal, 0.09f), RimHard, 0f, 0.80f, HardDarken, HardRampHiEdge);
        }
        else
        {
            // 守卫：整副铁件压到同一暗档，铁面近黑，只留红色发光眼；rim 强度比玩家各低 0.15~0.20
            Material plate = CharMat("IronPlate", GuardIronPlate, Lift(GuardIronPlate, 0.08f), RimHard, outline, 0.70f, HardDarken, HardRampHiEdge);
            materials.armorBright = plate;
            materials.armorMid = plate;
            materials.armorDeep = plate;
            materials.helmet = plate;
            materials.steel = plate;
            materials.darkMetal = CharMat("IronDark", GuardDarkMetal, Lift(GuardDarkMetal, 0.08f), RimHard, outline, 0.62f, HardDarken, HardRampHiEdge);
            materials.visor = CharMat("IronFace", GuardFace, Lift(GuardFace, 0.06f), RimHard, fineOutline, 0.15f, HardDarken, HardRampHiEdge);
            materials.visorFlat = materials.visor;
            materials.darkMetalFlat = materials.darkMetal;
        }

        materials.leather = CharMat("Leather", new Color(0.24f, 0.18f, 0.13f), new Color(0.36f, 0.27f, 0.19f), RimSoft, outline,
            isPlayer ? 0.55f : 0.40f, SoftDarken);
        materials.skin = CharMat("Skin", CommonSkin, Darken(CommonSkin, 0.10f), RimSoft, outline,
            isPlayer ? 0.55f : 0.32f, SoftDarken);
        materials.accent = CharMat("Accent", PaletteBlood, new Color(0.95f, 0.45f, 0.32f), RimSoft, outline,
            isPlayer ? 0.75f : 0.40f, SoftDarken);
        materials.glow = CharGlow();
        return materials;
    }

    /// <summary>
    /// 取（或新建）角色材质。
    /// _RimColor 走 ShaderKit.Character 的工厂参数（签名里本来就有 rimColor）；
    /// _RimIntensity / _Darken / _RampHiEdge 工厂没有入口，在创建后用 SetFloat 覆盖（HasProperty 保护）。
    /// 三个覆盖值传 -1 表示沿用 ShaderKit 的默认（0.85 / 0.45 / 0.62）。
    /// </summary>
    private static Material CharMat(string slot, Color body, Color trim, Color rim, float outlineWidth,
        float rimIntensity = -1f, float darken = -1f, float rampHiEdge = -1f)
    {
        // 描边宽度、rim 色、边缘光强度、明暗参数全部纳入 key：同名 slot 参数不同必须生成不同材质
        string key = slot + "_" + ColorUtility.ToHtmlStringRGB(body) + "_" + ColorUtility.ToHtmlStringRGB(trim)
            + "_c" + ColorUtility.ToHtmlStringRGB(rim)
            + "_o" + outlineWidth.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
            + "_r" + rimIntensity.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            + "_d" + darken.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
            + "_h" + rampHiEdge.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        Material cached;
        if (charMaterialCache.TryGetValue(key, out cached) && cached != null) return cached;

        Material material = ShaderKit.Character("Char" + key, body, trim, rim, outlineWidth);
        bool overridden = false;
        if (rimIntensity >= 0f && material != null && material.HasProperty("_RimIntensity"))
        {
            material.SetFloat("_RimIntensity", rimIntensity);
            overridden = true;
        }
        if (darken >= 0f && material != null && material.HasProperty("_Darken"))
        {
            material.SetFloat("_Darken", darken);
            overridden = true;
        }
        if (rampHiEdge >= 0f && material != null && material.HasProperty("_RampHiEdge"))
        {
            material.SetFloat("_RampHiEdge", rampHiEdge);
            overridden = true;
        }
        if (overridden)
        {
            UnityEditor.EditorUtility.SetDirty(material);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(material);
        }
        charMaterialCache[key] = material;
        return material;
    }

    private static Material CharGlow()
    {
        Material cached;
        if (charMaterialCache.TryGetValue("Glow_Eyes", out cached) && cached != null) return cached;
        Material material = ShaderKit.Emissive("CharGuardEyes", new Color(1f, 0.22f, 0.16f), 4.5f);
        charMaterialCache["Glow_Eyes"] = material;
        return material;
    }

    // ================= 部件构建 =================

    /// <summary>髋部：骨盆 + 腰带 + 腰甲裙摆 + 前裙甲片。</summary>
    private static void BuildHipsV2(Transform hips, CharMaterials m, bool isPlayer)
    {
        CharPart("骨盆", MeshKit.BeveledBox(new Vector3(0.30f, 0.17f, 0.21f), 0.028f, 1), m.cloth, hips, new Vector3(0f, 0.005f, 0f));

        GameObject belt = CharPart("腰带", MeshKit.Ring(0.16f, 0.19f, 0.055f, 10), m.leather, hips, new Vector3(0f, 0.085f, 0f));
        belt.transform.localScale = new Vector3(1f, 1f, 0.72f);
        CharPart("带扣", MeshKit.BeveledBox(new Vector3(0.075f, 0.075f, 0.03f), 0.008f, 1), m.darkMetal, hips, new Vector3(0f, 0.085f, 0.135f));

        GameObject skirt = CharPart("腰甲裙摆", MeshKit.TaperedPrism(0.20f, 0.255f, 0.26f, 8, 18f), m.armorMid, hips, new Vector3(0f, -0.11f, 0f));
        skirt.transform.localScale = new Vector3(1f, 1f, 0.78f);

        for (int side = -1; side <= 1; side += 2)
        {
            CharPart("前裙甲片" + (side < 0 ? "L" : "R"),
                MeshKit.BeveledBox(new Vector3(0.09f, 0.17f, 0.024f), 0.006f, 1), m.darkMetal, hips,
                new Vector3(side * 0.055f, -0.165f, 0.145f), new Vector3(6f, side * 8f, 0f));
        }
    }

    /// <summary>躯干：布衣本体 + 胸甲 + 棱线 + 腹甲分块 + 护颈 + 层叠肩甲。返回躯干 Renderer（受击闪白用）。</summary>
    private static Renderer BuildChestV2(Transform chest, CharMaterials m, bool isPlayer)
    {
        Renderer torso = CharPart("躯干", MeshKit.BeveledBox(new Vector3(0.33f, 0.40f, 0.21f), 0.03f, 1), m.cloth, chest,
            new Vector3(0f, 0.19f, 0f)).GetComponent<Renderer>();

        // 胸甲本体（前胸外凸）+ 中央棱线
        CharPart("胸甲", MeshKit.BeveledBox(new Vector3(0.30f, 0.21f, 0.11f), 0.024f, 1), m.armorMid, chest,
            new Vector3(0f, 0.285f, 0.075f));
        CharPart("胸甲棱线", MeshKit.BeveledBox(new Vector3(0.032f, 0.20f, 0.042f), 0.008f, 1), m.darkMetal, chest,
            new Vector3(0f, 0.285f, 0.132f));

        // 胸甲横向分缝：两道近黑窄条贯穿甲面，把一大块金属切成三段可读块面
        if (isPlayer)
        {
            for (int i = 0; i < 2; i++)
            {
                CharPart("胸甲分缝" + (i + 1),
                    MeshKit.BeveledBox(new Vector3(0.305f, 0.014f, 0.096f), 0f, 1), m.visorFlat, chest,
                    new Vector3(0f, 0.285f + (i == 0 ? 0.052f : -0.056f), 0.086f));
            }
        }

        // 腹甲分块：三层横条，玩家为金属片、守卫为皮革束带
        for (int i = 0; i < 3; i++)
        {
            CharPart("腹甲" + (i + 1),
                MeshKit.BeveledBox(new Vector3(0.22f - i * 0.012f, 0.05f, 0.07f), 0.012f, 1),
                isPlayer ? m.armorDeep : m.leather, chest, new Vector3(0f, 0.045f + i * 0.06f, 0.082f));
        }

        // 护颈
        CharPart("护颈", MeshKit.BeveledBox(new Vector3(0.19f, 0.055f, 0.17f), 0.014f, 1), m.armorMid, chest,
            new Vector3(0f, 0.395f, -0.005f));

        // 层叠肩甲：每侧三层，逐层外翻下垂
        for (int side = -1; side <= 1; side += 2)
        {
            for (int layer = 0; layer < 3; layer++)
            {
                float width = 0.155f - layer * 0.012f;
                float depth = 0.20f + layer * 0.012f;
                Vector3 pos = new Vector3(side * (0.185f - layer * 0.004f), 0.43f - layer * 0.052f, -0.006f);
                Vector3 euler = new Vector3(4f * layer - 6f, 0f, -side * (14f + layer * 11f));
                CharPart("肩甲" + (side < 0 ? "L" : "R") + (layer + 1),
                    MeshKit.BeveledBox(new Vector3(width, 0.052f, depth), 0.014f, 1),
                    layer == 0 ? m.armorBright : (layer == 1 ? m.armorMid : m.armorDeep), chest, pos, euler);
            }
        }

        return torso;
    }

    /// <summary>头部：颈 + 头 + 头盔（玩家带面罩缝与两侧角；守卫为尖顶盔 + 深色铁面 + 发光眼）。</summary>
    private static void BuildHeadV2(Transform head, CharMaterials m, bool isPlayer)
    {
        CharPart("颈", MeshKit.TaperedPrism(0.048f, 0.056f, 0.08f, 6, 0f), m.skin, head, new Vector3(0f, -0.03f, 0f));
        CharPart("头", MeshKit.LowPolySphere(0.132f, 1), m.skin, head, new Vector3(0f, 0.075f, 0f));

        if (isPlayer)
        {
            // 头盔盔体（露出面部）
            CharPart("头盔", MeshKit.TaperedPrism(0.135f, 0.145f, 0.115f, 8, 0f), m.helmet, head, new Vector3(0f, 0.155f, -0.004f));
            // 面罩缝
            CharPart("面罩缝", MeshKit.BeveledBox(new Vector3(0.175f, 0.03f, 0.05f), 0.006f, 1), m.visor, head,
                new Vector3(0f, 0.075f, 0.12f));
            // 面颊护甲
            for (int side = -1; side <= 1; side += 2)
            {
                CharPart("面颊护甲" + (side < 0 ? "L" : "R"),
                    MeshKit.BeveledBox(new Vector3(0.028f, 0.09f, 0.10f), 0.006f, 1), m.helmet, head,
                    new Vector3(side * 0.10f, 0.04f, 0.03f));
                // 两侧角
                CharPart("盔角" + (side < 0 ? "L" : "R"), MeshKit.Cone(0.03f, 0.13f, 4), m.darkMetal, head,
                    new Vector3(side * 0.115f, 0.16f, -0.01f), new Vector3(-12f, 0f, side * -35f));
            }
            // 盔脊（顶部纵脊）
            CharPart("盔脊", MeshKit.BeveledBox(new Vector3(0.03f, 0.06f, 0.17f), 0.01f, 1), m.darkMetal, head,
                new Vector3(0f, 0.19f, -0.03f));
            // 正面中脊：从头盔前沿竖下，让盔面有明确的中线
            CharPart("盔中脊", MeshKit.BeveledBox(new Vector3(0.026f, 0.125f, 0.03f), 0.006f, 1), m.darkMetalFlat, head,
                new Vector3(0f, 0.15f, 0.132f));
            // 两侧竖向通风槽：贴合八边形盔体的斜面
            for (int side = -1; side <= 1; side += 2)
            {
                CharPart("通风槽" + (side < 0 ? "L" : "R"),
                    MeshKit.BeveledBox(new Vector3(0.016f, 0.085f, 0.022f), 0f, 1), m.visorFlat, head,
                    new Vector3(side * 0.078f, 0.155f, 0.108f), new Vector3(0f, side * 26f, 0f));
            }
        }
        else
        {
            // 尖顶盔
            CharPart("尖顶盔", MeshKit.TaperedPrism(0.140f, 0.150f, 0.11f, 8, 0f), m.helmet, head, new Vector3(0f, 0.155f, -0.004f));
            CharPart("盔尖", MeshKit.Cone(0.115f, 0.08f, 8), m.darkMetal, head, new Vector3(0f, 0.20f, -0.004f));
            // 深色铁面
            CharPart("铁面", MeshKit.BeveledBox(new Vector3(0.19f, 0.155f, 0.05f), 0.018f, 1), m.visor, head,
                new Vector3(0f, 0.055f, 0.115f));
            CharPart("铁面棱", MeshKit.BeveledBox(new Vector3(0.022f, 0.14f, 0.028f), 0.006f, 1), m.darkMetal, head,
                new Vector3(0f, 0.06f, 0.145f));
            // 眼部红光
            for (int side = -1; side <= 1; side += 2)
            {
                CharPart("眼" + (side < 0 ? "L" : "R"),
                    MeshKit.BeveledBox(new Vector3(0.048f, 0.016f, 0.02f), 0f, 1), m.glow, head,
                    new Vector3(side * 0.042f, 0.085f, 0.15f));
            }
        }
    }

    /// <summary>手臂：上臂 / 前臂 / 手 / WeaponPivot 四级节点链，返回 WeaponPivot。</summary>
    private static Transform BuildArmV2(Transform arm, CharMaterials m, bool right, bool isPlayer)
    {
        GameObject upper = CharNode("上臂", arm, Vector3.zero);
        CharPart("上臂体", MeshKit.TaperedPrism(0.058f, 0.049f, 0.25f, 6, 0f), m.cloth, upper.transform, new Vector3(0f, -0.125f, 0f));

        GameObject fore = CharNode("前臂", upper.transform, new Vector3(0f, -0.25f, 0f));
        CharPart("前臂体", MeshKit.TaperedPrism(0.048f, 0.042f, 0.24f, 6, 0f), m.skin, fore.transform, new Vector3(0f, -0.12f, 0f));
        CharPart("护臂", MeshKit.BeveledBox(new Vector3(0.098f, 0.135f, 0.098f), 0.02f, 1), m.armorDeep, fore.transform,
            new Vector3(0f, -0.10f, 0f));

        GameObject hand = CharNode("手", fore.transform, new Vector3(0f, -0.24f, 0f));
        CharPart("手掌", MeshKit.LowPolySphere(0.052f, 0), m.skin, hand.transform, new Vector3(0f, -0.03f, 0.005f));

        GameObject pivot = CharNode("WeaponPivot", hand.transform, new Vector3(0f, -0.05f, 0.02f));
        return pivot.transform;
    }

    /// <summary>腿：大腿 / 小腿 / 靴子 三级节点链，带护膝。</summary>
    private static void BuildLegV2(Transform leg, CharMaterials m, bool isPlayer)
    {
        GameObject thigh = CharNode("大腿", leg, Vector3.zero);
        CharPart("大腿体", MeshKit.TaperedPrism(0.078f, 0.062f, 0.42f, 6, 0f), m.cloth, thigh.transform, new Vector3(0f, -0.21f, 0f));
        CharPart("护膝", MeshKit.BeveledBox(new Vector3(0.105f, 0.09f, 0.04f), 0.012f, 1), m.armorDeep, thigh.transform,
            new Vector3(0f, -0.415f, 0.055f));

        GameObject shin = CharNode("小腿", thigh.transform, new Vector3(0f, -0.42f, 0f));
        CharPart("小腿体", MeshKit.TaperedPrism(0.062f, 0.05f, 0.40f, 6, 0f), m.cloth, shin.transform, new Vector3(0f, -0.20f, 0f));

        GameObject boot = CharNode("靴子", shin.transform, new Vector3(0f, -0.40f, 0f));
        CharPart("靴身", MeshKit.BeveledBox(new Vector3(0.115f, 0.125f, 0.245f), 0.022f, 1), m.armorDeep, boot.transform,
            new Vector3(0f, -0.0575f, 0.038f));
    }

    /// <summary>披风：挂在 Chest 后侧的卡面，带 CapeMotion 顶点摆动（守卫为破损多片）。</summary>
    private static void BuildCapeV2(Transform chest, CharMaterials m, bool isPlayer)
    {
        GameObject anchor = CharNode("Cape", chest, new Vector3(0f, 0.40f, -0.125f), new Vector3(10f, 0f, 0f));

        if (isPlayer)
        {
            // 下摆外扩 22%，3 条竖褶做在同一张网格里，CapeMotion 摆动时褶子跟着一起动
            GameObject cape = CharPart("披风", MeshKit.Card(0.44f, 0.62f, 0.10f, 8, 1.22f, 3, 0.016f), m.cape,
                anchor.transform, Vector3.zero);
            cape.AddComponent<CapeMotion>();
        }
        else
        {
            // 守卫披风：外扩 + 2 道褶，配合两片破布读起来更破碎
            GameObject cape = CharPart("破损披风", MeshKit.Card(0.40f, 0.52f, 0.14f, 8, 1.18f, 2, 0.014f), m.cape,
                anchor.transform, Vector3.zero);
            cape.AddComponent<CapeMotion>();
            CharPart("披风破片L", MeshKit.Card(0.16f, 0.36f, 0.10f, 5), m.cape, anchor.transform,
                new Vector3(-0.115f, 0f, 0.02f), new Vector3(0f, 0f, 6f));
            CharPart("披风破片R", MeshKit.Card(0.13f, 0.26f, 0.08f, 4), m.cape, anchor.transform,
                new Vector3(0.125f, 0f, 0.03f), new Vector3(0f, 0f, -9f));
        }
    }

    /// <summary>长剑：握柄 / 剑柄尾 / 护手 / 剑刃 / 剑穗，沿 WeaponPivot 的 +Y 方向竖握。</summary>
    private static void BuildSwordV2(Transform pivot, CharMaterials m)
    {
        CharPart("剑柄", MeshKit.TaperedPrism(0.020f, 0.023f, 0.15f, 6, 0f), m.darkMetal, pivot, new Vector3(0f, 0.045f, 0f));
        CharPart("剑柄尾", MeshKit.LowPolySphere(0.028f, 0), m.darkMetal, pivot, new Vector3(0f, -0.035f, 0f));
        CharPart("护手", MeshKit.BeveledBox(new Vector3(0.21f, 0.032f, 0.055f), 0.01f, 1), m.darkMetal, pivot, new Vector3(0f, 0.125f, 0f));
        CharPart("剑刃", MeshKit.Blade(0.78f, 0.078f, 0.022f, 6), m.steel, pivot, new Vector3(0f, 0.14f, 0f));
        CharPart("剑穗", MeshKit.Card(0.05f, 0.17f, 0.02f, 3), m.accent, pivot, new Vector3(0.045f, 0.13f, 0f), new Vector3(0f, 0f, -8f));
    }

    /// <summary>四棱战锤：锤柄 / 柄尾 / 方形锤头 + 四条棱刺。</summary>
    private static void BuildHammerV2(Transform pivot, CharMaterials m)
    {
        CharPart("锤柄", MeshKit.TaperedPrism(0.022f, 0.026f, 0.66f, 6, 0f), m.leather, pivot, new Vector3(0f, 0.20f, 0f));
        CharPart("柄尾", MeshKit.LowPolySphere(0.03f, 0), m.darkMetal, pivot, new Vector3(0f, -0.14f, 0f));
        CharPart("锤头", MeshKit.BeveledBox(new Vector3(0.19f, 0.22f, 0.19f), 0.03f, 1), m.armorDeep, pivot, new Vector3(0f, 0.60f, 0f));

        Vector3[] directions = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        Vector3[] eulers = { new Vector3(0f, 0f, -90f), new Vector3(0f, 0f, 90f), new Vector3(90f, 0f, 0f), new Vector3(-90f, 0f, 0f) };
        for (int i = 0; i < directions.Length; i++)
        {
            CharPart("锤棱" + (i + 1), MeshKit.Cone(0.032f, 0.10f, 4), m.darkMetal, pivot,
                new Vector3(0f, 0.60f, 0f) + directions[i] * 0.095f, eulers[i]);
        }
    }

    // ================= 构建工具 =================

    private static GameObject CharNode(string name, Transform parent, Vector3 localPosition)
    {
        GameObject node = new GameObject(name);
        node.transform.SetParent(parent, false);
        node.transform.localPosition = localPosition;
        return node;
    }

    private static GameObject CharNode(string name, Transform parent, Vector3 localPosition, Vector3 localEuler)
    {
        GameObject node = CharNode(name, parent, localPosition);
        node.transform.localRotation = Quaternion.Euler(localEuler);
        return node;
    }

    private static GameObject CharPart(string name, Mesh mesh, Material material, Transform parent, Vector3 localPosition)
    {
        return CharPart(name, mesh, material, parent, localPosition, Vector3.zero, Vector3.one);
    }

    private static GameObject CharPart(string name, Mesh mesh, Material material, Transform parent, Vector3 localPosition, Vector3 localEuler)
    {
        return CharPart(name, mesh, material, parent, localPosition, localEuler, Vector3.one);
    }

    private static GameObject CharPart(string name, Mesh mesh, Material material, Transform parent,
        Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = Quaternion.Euler(localEuler);
        part.transform.localScale = localScale;

        MeshFilter filter = part.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return part;
    }
}
