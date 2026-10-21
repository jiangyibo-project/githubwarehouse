using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 程序化生成"Ruins Guardian"完整游戏：遗迹场景、分肢体人形角色（四肢动画）、
/// 混合树 Animator、五状态 AI、后处理、宝石收集、HUD 与菜单。零外部素材。
/// </summary>
public static partial class BuildDemoScene
{
    private const string Root = "Assets/Generated";
    private const string MatRoot = Root + "/Materials";
    private const string AnimRoot = Root + "/Animations";

    private static Material groundMat;
    private static Material wallMat;
    private static Material stoneMat;
    private static Material metalMat;
    private static Material woodMat;
    private static Material statueMat;
    private static Material grassMat;
    private static Material runeMat;
    private static Material gemMat;
    private static AnimatorController controller;

    [MenuItem("Tools/Ruins Guardian/生成演示场景")]
    public static void Build()
    {
        EnsureFolder(Root);
        AssetDatabase.DeleteAsset(MatRoot);
        AssetDatabase.DeleteAsset(AnimRoot);
        AssetDatabase.DeleteAsset(Root + "/RuinsGuard.controller");
        AssetDatabase.DeleteAsset(Root + "/RuinsPostProcessProfile.asset");
        EnsureFolder(MatRoot);
        EnsureFolder(AnimRoot);

        // ---------- 纹理 ----------
        Texture2D stoneTex = CreateStoneTexture(512, new Color(0.4f, 0.38f, 0.33f), 4, 88, 0.12f);
        Texture2D wallTex = CreateStoneTexture(512, new Color(0.44f, 0.41f, 0.36f), 3, 99, 0.05f);
        Texture2D runeTex = CreateRuneTexture(256);
        Texture2D grassTex = CreateGrassTexture(128);
        SaveTexture(stoneTex, "StoneTex");
        SaveTexture(wallTex, "WallTex");
        SaveTexture(runeTex, "RuneTex");
        SaveTexture(grassTex, "GrassTex");

        // ---------- 材质（全部保存为独立 .mat，修复品红） ----------
        groundMat = CreateMaterial("GroundMat", Shader.Find("Standard"), stoneTex, new Vector2(8f, 8f), 0f, 0.9f);
        wallMat = CreateMaterial("WallMat", Shader.Find("Standard"), wallTex, new Vector2(4f, 1f), 0f, 0.9f);
        stoneMat = CreateMaterial("StoneMat", Shader.Find("Standard"), stoneTex, new Vector2(1f, 1f), 0.1f, 0.85f);
        metalMat = CreateMaterial("MetalMat", Shader.Find("Standard"), null, Vector2.zero, 0.85f, 0.35f);
        metalMat.SetColor("_Color", new Color(0.32f, 0.32f, 0.35f));
        woodMat = CreateMaterial("WoodMat", Shader.Find("Standard"), null, Vector2.zero, 0f, 0.9f);
        woodMat.SetColor("_Color", new Color(0.35f, 0.24f, 0.15f));
        statueMat = CreateMaterial("StatueMat", Shader.Find("Standard"), stoneTex, new Vector2(1f, 1f), 0.1f, 0.9f);
        statueMat.SetColor("_Color", new Color(0.72f, 0.74f, 0.72f));

        // 草：双面透明
        grassMat = new Material(Shader.Find("Standard"));
        grassMat.mainTexture = grassTex;
        grassMat.SetFloat("_Glossiness", 0.2f);
        grassMat.SetFloat("_Mode", 2); // Cutout
        grassMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        grassMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        grassMat.EnableKeyword("_ALPHATEST_ON");
        grassMat.SetFloat("_Cutoff", 0.35f);
        AssetDatabase.CreateAsset(grassMat, MatRoot + "/GrassMat.mat");

        // 符文石碑
        runeMat = new Material(Shader.Find("Standard"));
        runeMat.mainTexture = runeTex;
        runeMat.SetFloat("_Glossiness", 0.5f);
        runeMat.EnableKeyword("_EMISSION");
        runeMat.SetColor("_EmissionColor", new Color(0.3f, 0.5f, 0.35f) * 0.5f);
        AssetDatabase.CreateAsset(runeMat, MatRoot + "/RuneMat.mat");

        // 宝石
        gemMat = new Material(Shader.Find("Standard"));
        gemMat.SetColor("_Color", new Color(0.2f, 0.85f, 0.95f));
        gemMat.EnableKeyword("_EMISSION");
        gemMat.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 1f) * 1.4f);
        AssetDatabase.CreateAsset(gemMat, MatRoot + "/GemMat.mat");

        // ---------- 天空盒 ----------
        Material skybox = CreateSkyboxMaterial();
        AssetDatabase.CreateAsset(skybox, Root + "/RuinsSkybox.mat");

        controller = CreateController();
        PostProcessProfile profile = CreatePostProcessProfile();
        UpgradePostProcess(profile);      // V2 后处理：辉光 / 色彩分级 / 暗角 / 颗粒 / 色差 / AO

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- V2 世界：三层遗迹庭院、程序化 PBR 材质、火盆与伪造体积光 ----
        ConfigureEnvironmentV2();
        BuildWorldV2();      // 必须第一个跑：初始化贴图、材质与合并网格缓存
        BuildDecorV2();
        BuildAltarV2();
        BuildAmbientV2();

        // ---- V2 角色：低多边形人形（盔甲、披风、长剑），保留分肢体动画层级 ----
        // 出生点略向庭院内侧收，避免开局相机后撤时贴到西南角塔（相机避障另有处理，这里只是留余量）
        GameObject player = BuildCharacterV2("Player", new Vector3(-5.5f, 0f, -5.5f), PlayerClothColor, true);
        player.tag = "Player";
        Health playerHealth = player.AddComponent<Health>();
        SetPrivate(playerHealth, "maxHealth", 100f);
        SetPrivate(playerHealth, "isPlayer", true);
        SetPrivate(playerHealth, "invulnerableTime", 0.7f);
        player.AddComponent<PlayerExplorer>();
        player.AddComponent<PlayerCombat>();
        player.AddComponent<SimpleCollisionResolver>();
        player.AddComponent<PlayerDodge>();
        player.AddComponent<TargetLock>();

        GameObject guard1 = BuildGuardV2("Ruins Guard 1", new Vector3(-6f, 0f, 3f),
            new Vector3[] { new Vector3(-7f, 0, 4f), new Vector3(-3f, 0, 6f), new Vector3(-1f, 0, 3f), new Vector3(-4f, 0, 0.5f) },
            player.transform);
        GameObject guard2 = BuildGuardV2("Ruins Guard 2", new Vector3(6f, 0f, -3f),
            new Vector3[] { new Vector3(7f, 0, -4f), new Vector3(3f, 0, -6f), new Vector3(1f, 0, -3f), new Vector3(4f, 0, -0.5f) },
            player.transform);
        GameObject guard3 = BuildGuardV2("Ruins Guard 3", new Vector3(0f, 0f, 6f),
            new Vector3[] { new Vector3(-2f, 0, 6f), new Vector3(2f, 0, 6f) },
            player.transform);

        BuildGems(player.transform);

        // ---- V2 关卡层：地形碰撞、符文机关与石门、新敌人、波次与复活 ----
        BuildGameplayV2(player.transform, new[] { guard1, guard2, guard3 });

        BuildCamera(player.transform, profile);
        BuildPostProcessObject(profile);

        GameObject bgmSource = BuildBgmSource();
        // ---- V2 界面与管理器 ----
        BuildUiAndManagers(playerHealth, bgmSource, new[] { guard1, guard2, guard3 });
        BuildAudioV2(null, bgmSource, playerHealth, player.transform);
        AttachCombatFeel(player.transform);
        CreateCharacterKeyLight(player.transform);

        EditorSceneManager.SaveScene(scene, Root + "/RuinsDemo.unity");
        AssetDatabase.SaveAssets();

        EditorBuildSettingsScene buildScene = new EditorBuildSettingsScene(Root + "/RuinsDemo.unity", true);
        EditorBuildSettings.scenes = new[] { buildScene };

        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog("Ruins Guardian",
                "场景已生成：Assets/Generated/RuinsDemo.unity\n按 Play 即可开始游戏。", "确定");
        }
        else
        {
            Debug.Log("BUILD DONE: Assets/Generated/RuinsDemo.unity");
        }
    }

    /// <summary>V2：把战斗手感组件挂到管理器与玩家上，并注入相机引用。</summary>
    /// <summary>挂一盏不投影的前上补光（玩家子物体跟随）：逆光时布料仍有明暗，不被主光打成纯色板。</summary>
    private static void CreateCharacterKeyLight(Transform player)
    {
        // 聚光灯从前上方只照角色身体：解决逆光纯色板，又不把周围地面与尘埃烤亮
        GameObject lightObject = new GameObject("角色前上补光");
        lightObject.transform.SetParent(player, false);
        lightObject.transform.localPosition = new Vector3(0f, 2.2f, 0.7f);
        lightObject.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
        Light key = lightObject.AddComponent<Light>();
        key.type = LightType.Spot;
        key.color = new Color(1f, 0.92f, 0.78f);
        key.intensity = 0.45f;
        key.range = 3.2f;
        key.spotAngle = 38f;
        key.shadows = LightShadows.None;
    }

    private static void AttachCombatFeel(Transform player)
    {
        GameObject managerObject = GameObject.Find("GameManager");
        if (managerObject != null)
        {
            if (managerObject.GetComponent<JuiceDirector>() == null) managerObject.AddComponent<JuiceDirector>();
            if (managerObject.GetComponent<ImpactFx>() == null) managerObject.AddComponent<ImpactFx>();
        }

        Camera camera = Object.FindObjectOfType<Camera>();
        if (camera == null || player == null) return;

        TargetLock targetLock = player.GetComponent<TargetLock>();
        if (targetLock != null) SetPrivate(targetLock, "cameraTransform", camera.transform);

        PlayerDodge dodge = player.GetComponent<PlayerDodge>();
        if (dodge != null) SetPrivate(dodge, "cameraTransform", camera.transform);
    }

    // ================= 纹理 =================

    private static Texture2D CreateStoneTexture(int size, Color baseColor, int tiles, int seed, float moss)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, true);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.anisoLevel = 4;

        System.Random random = new System.Random(seed);
        int cell = size / tiles;
        Color[] pixels = new Color[size * size];

        // 苔藓斑块位置
        float mossX = (float)random.NextDouble() * size;
        float mossY = (float)random.NextDouble() * size;
        float mossX2 = (float)random.NextDouble() * size;
        float mossY2 = (float)random.NextDouble() * size;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int row = y / cell;
                int offset = (row % 2) * cell / 2;
                int localX = (x + offset) % cell;
                int localY = y % cell;

                float noise = (float)random.NextDouble();
                float grain = (float)random.NextDouble();
                Color c = baseColor * (0.7f + noise * 0.45f);

                // 颗粒
                if (grain < 0.06f) c *= 0.6f;

                // 砖缝
                if (localX < 3 || localY < 3) c *= 0.4f;
                else if (localX < 6 || localY < 6) c *= 0.78f;

                // 裂缝
                float crack = Mathf.Abs(Mathf.Sin((x + y * 1.7f) * 0.08f + seed));
                if (crack < 0.04f && localX > 5 && localY > 5) c *= 0.5f;

                // 苔藓
                float d1 = Mathf.Sqrt((x - mossX) * (x - mossX) + (y - mossY) * (y - mossY));
                float d2 = Mathf.Sqrt((x - mossX2) * (x - mossX2) + (y - mossY2) * (y - mossY2));
                if (d1 < size * 0.18f)
                    c = Color.Lerp(c, new Color(0.22f, 0.32f, 0.18f), moss * (1f - d1 / (size * 0.18f)));
                if (d2 < size * 0.12f)
                    c = Color.Lerp(c, new Color(0.22f, 0.32f, 0.18f), moss * 0.7f * (1f - d2 / (size * 0.12f)));

                pixels[y * size + x] = c;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    private static Texture2D CreateRuneTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(0.12f, 0.12f, 0.13f);
        texture.SetPixels(pixels);

        // 简单几何符文：发光的线条与圆环
        Color rune = new Color(0.55f, 0.9f, 0.65f);
        DrawLine(texture, size / 2, size / 6, size / 2, size * 5 / 6, 4, rune);
        DrawLine(texture, size / 3, size / 3, size * 2 / 3, size / 3, 3, rune);
        DrawLine(texture, size / 3, size * 2 / 3, size * 2 / 3, size * 2 / 3, 3, rune);
        DrawCircle(texture, size / 2, size / 2, size / 4, 3, rune);
        texture.Apply(false);
        return texture;
    }

    private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, int thickness, Color color)
    {
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int steps = Mathf.Max(dx, dy, 1);
        for (int i = 0; i <= steps; i++)
        {
            int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, (float)i / steps));
            int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, (float)i / steps));
            for (int ox = -thickness / 2; ox <= thickness / 2; ox++)
                for (int oy = -thickness / 2; oy <= thickness / 2; oy++)
                {
                    int px = x + ox, py = y + oy;
                    if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                        tex.SetPixel(px, py, color);
                }
        }
    }

    private static void DrawCircle(Texture2D tex, int cx, int cy, int radius, int thickness, Color color)
    {
        for (int a = 0; a < 360; a += 3)
        {
            float rad = a * Mathf.Deg2Rad;
            int x = Mathf.RoundToInt(cx + Mathf.Cos(rad) * radius);
            int y = Mathf.RoundToInt(cy + Mathf.Sin(rad) * radius);
            for (int ox = -thickness / 2; ox <= thickness / 2; ox++)
                for (int oy = -thickness / 2; oy <= thickness / 2; oy++)
                {
                    int px = x + ox, py = y + oy;
                    if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                        tex.SetPixel(px, py, color);
                }
        }
    }

    private static Texture2D CreateGrassTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        System.Random random = new System.Random(31);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

        for (int blade = 0; blade < 40; blade++)
        {
            int bx = random.Next(size / 4, size * 3 / 4);
            int by = 0;
            int height = random.Next(size / 2, size);
            int lean = random.Next(-12, 12);
            Color c = Color.Lerp(new Color(0.25f, 0.38f, 0.18f), new Color(0.45f, 0.55f, 0.25f), (float)random.NextDouble());
            for (int t = 0; t < height; t++)
            {
                int x = bx + Mathf.RoundToInt(Mathf.Lerp(0, lean, (float)t / height));
                int y = by + t;
                if (x >= 0 && x < size && y < size)
                {
                    pixels[y * size + x] = c;
                    if (random.NextDouble() < 0.5) pixels[y * size + Mathf.Clamp(x + 1, 0, size - 1)] = c;
                }
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false);
        return texture;
    }

    private static void SaveTexture(Texture2D texture, string name)
    {
        AssetDatabase.CreateAsset(texture, Root + "/" + name + ".asset");
    }

    // ================= 材质 =================

    private static Material CreateMaterial(string name, Shader shader, Texture2D texture,
        Vector2 tiling, float metallic, float smoothness)
    {
        Material material = new Material(shader);
        if (texture != null)
        {
            material.mainTexture = texture;
            material.SetTextureScale("_MainTex", tiling);
        }
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        AssetDatabase.CreateAsset(material, MatRoot + "/" + name + ".mat");
        return material;
    }

    private static Material CreateSkyboxMaterial()
    {
        Material sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetColor("_SkyTint", new Color(0.55f, 0.5f, 0.6f));
        sky.SetColor("_GroundColor", new Color(0.2f, 0.18f, 0.18f));
        sky.SetFloat("_AtmosphereThickness", 0.8f);
        sky.SetFloat("_Exposure", 1.15f);
        return sky;
    }

    // ================= 环境 =================

    private static void ConfigureEnvironment(Material skybox)
    {
        RenderSettings.skybox = skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.018f;
        RenderSettings.fogColor = new Color(0.42f, 0.38f, 0.44f);
        RenderSettings.ambientLight = new Color(0.92f, 0.9f, 0.98f);

        GameObject lightObject = new GameObject("Directional Light");
        Light sun = lightObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.82f, 0.62f);
        sun.intensity = 2.6f;
        sun.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(42f, -30f, 0f);
    }

    // ================= 场景建筑 =================

    private static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material, Transform parent = null)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (parent != null) go.transform.SetParent(parent, true);
        return go;
    }

    private static GameObject CreateCylinder(string name, Vector3 position, Vector3 scale, Material material, Transform parent = null)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (parent != null) go.transform.SetParent(parent, true);
        return go;
    }

    private static void BuildArena()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "遗迹地面";
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        floor.GetComponent<Renderer>().sharedMaterial = groundMat;

        // 南墙留门洞（拱门入口），分两段
        CreateBox("南墙 西", new Vector3(-6f, 0.7f, -10f), new Vector3(8.6f, 1.4f, 0.6f), wallMat);
        CreateBox("南墙 东", new Vector3(6f, 0.7f, -10f), new Vector2(8.6f, 1.4f).To3(0.6f), wallMat);
        CreateBox("北墙", new Vector3(0f, 0.7f, 10f), new Vector3(20.6f, 1.4f, 0.6f), wallMat);
        CreateBox("东墙", new Vector3(10f, 0.7f, 0f), new Vector3(0.6f, 1.4f, 20.6f), wallMat);
        CreateBox("西墙", new Vector3(-10f, 0.7f, 0f), new Vector3(0.6f, 1.4f, 20.6f), wallMat);

        // 拱门
        CreateCylinder("拱门柱 L", new Vector3(-1.7f, 2f, -10f), new Vector3(0.45f, 2f, 0.45f), stoneMat);
        CreateCylinder("拱门柱 R", new Vector3(1.7f, 2f, -10f), new Vector3(0.45f, 2f, 0.45f), stoneMat);
        CreateBox("拱门横梁", new Vector3(0f, 4.1f, -10f), new Vector3(4.2f, 0.5f, 0.8f), stoneMat);
        CreateBox("拱门装饰", new Vector3(0f, 4.6f, -10f), new Vector3(1f, 0.6f, 0.8f), stoneMat);

        // 墙顶垛口
        for (float x = -9f; x <= 9f; x += 2f)
        {
            if (Mathf.Abs(x) < 2f) continue;
            CreateBox("垛口", new Vector3(x, 1.55f, 10f), new Vector3(0.9f, 0.3f, 0.7f), wallMat);
            CreateBox("垛口", new Vector3(x, 1.55f, -10f), new Vector3(0.9f, 0.3f, 0.7f), wallMat);
        }
        for (float z = -9f; z <= 9f; z += 2f)
        {
            CreateBox("垛口", new Vector3(10f, 1.55f, z), new Vector3(0.7f, 0.3f, 0.9f), wallMat);
            CreateBox("垛口", new Vector3(-10f, 1.55f, z), new Vector3(0.7f, 0.3f, 0.9f), wallMat);
        }

        // 四角角柱
        Vector3[] corners = {
            new Vector3(-8.5f, 0f, -8.5f), new Vector3(8.5f, 0f, -8.5f),
            new Vector3(-8.5f, 0f, 8.5f), new Vector3(8.5f, 0f, 8.5f)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            CreateCylinder("角柱", corners[i] + Vector3.up * 2f, new Vector3(0.8f, 2f, 0.8f), stoneMat);
            CreateBox("柱础", corners[i] + Vector3.up * 0.15f, new Vector3(1.5f, 0.3f, 1.5f), stoneMat);
            CreateBox("柱顶", corners[i] + Vector3.up * 4.1f, new Vector3(1.5f, 0.3f, 1.5f), stoneMat);
        }

        // 断墙
        CreateBox("断墙 A", new Vector3(-4f, 0.7f, -3f), new Vector3(3.2f, 1.4f, 0.5f), stoneMat);
        GameObject broken = CreateBox("断墙 B", new Vector3(4f, 0.5f, 3.5f), new Vector3(2.6f, 1f, 0.5f), stoneMat);
        broken.transform.Rotate(0f, 30f, 0f);
        CreateBox("断墙残块", new Vector3(5.3f, 0.2f, 4.3f), new Vector3(0.7f, 0.4f, 0.7f), stoneMat);

        // 碎柱
        CreateCylinder("碎柱 A", new Vector3(-5.5f, 0.55f, 4.5f), new Vector3(0.45f, 0.55f, 0.45f), stoneMat);
        CreateCylinder("碎柱 B", new Vector3(6.5f, 0.4f, 6.5f), new Vector3(0.5f, 0.4f, 0.5f), stoneMat);
    }

    private static void BuildDecorations()
    {
        // 雕像
        BuildStatue(new Vector3(-3.2f, 0f, -0.5f), Quaternion.Euler(0f, 70f, 0f));
        BuildStatue(new Vector3(3.2f, 0f, 0.5f), Quaternion.Euler(0f, -110f, 0f));

        // 符文石碑
        BuildRuneStone(new Vector3(-6.5f, 0f, -3.5f), 20f);
        BuildRuneStone(new Vector3(6.5f, 0f, -3.5f), -25f);
        BuildRuneStone(new Vector3(-2f, 0f, 8f), 180f);
        BuildRuneStone(new Vector3(2f, 0f, -8f), 0f);

        // 枯树
        BuildDeadTree(new Vector3(-7.5f, 0f, 6.5f), 11);
        BuildDeadTree(new Vector3(7.5f, 0f, 5f), 22);

        // 草丛
        System.Random random = new System.Random(707);
        for (int i = 0; i < 16; i++)
        {
            float x = (float)(random.NextDouble() * 17 - 8.5);
            float z = (float)(random.NextDouble() * 17 - 8.5);
            if (Mathf.Abs(x) < 2f && Mathf.Abs(z) < 2f) continue;
            BuildGrassClump(new Vector3(x, 0f, z), 0.7f + (float)random.NextDouble() * 0.6f);
        }

        // 木箱、木桶
        CreateBox("木箱", new Vector3(-8f, 0.4f, -5.5f), new Vector3(0.8f, 0.8f, 0.8f), woodMat);
        CreateBox("木箱 堆", new Vector3(-7.2f, 0.3f, -5.8f), new Vector3(0.6f, 0.6f, 0.6f), woodMat);
        CreateCylinder("木桶", new Vector3(7.5f, 0.45f, -6f), new Vector3(0.45f, 0.45f, 0.45f), woodMat);

        // 骨头
        System.Random boneRandom = new System.Random(404);
        for (int i = 0; i < 4; i++)
        {
            float x = (float)(boneRandom.NextDouble() * 14 - 7);
            float z = (float)(boneRandom.NextDouble() * 14 - 7);
            GameObject bone = CreateCylinder("骸骨", new Vector3(x, 0.06f, z), new Vector3(0.03f, 0.25f, 0.03f), statueMat);
            bone.transform.Rotate(0f, (float)boneRandom.NextDouble() * 360f, (float)boneRandom.NextDouble() * 90f - 45f);
        }
    }

    private static void BuildStatue(Vector3 position, Quaternion rotation)
    {
        GameObject root = new GameObject("遗迹雕像");
        root.transform.position = position;
        root.transform.rotation = rotation;
        CreateBox("基座", new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 1.1f), stoneMat, root.transform);
        CreateCylinder("身躯", new Vector3(0f, 0.9f, 0f), new Vector3(0.28f, 0.5f, 0.28f), statueMat, root.transform);
        CreateCylinder("头部", new Vector3(0f, 1.65f, 0f), new Vector3(0.2f, 0.2f, 0.2f), statueMat, root.transform);
        GameObject arm1 = CreateCylinder("手臂", new Vector3(-0.45f, 1.05f, 0f), new Vector3(0.08f, 0.4f, 0.08f), statueMat, root.transform);
        arm1.transform.Rotate(0f, 0f, 60f);
        GameObject arm2 = CreateCylinder("手臂", new Vector3(0.45f, 1.05f, 0f), new Vector3(0.08f, 0.4f, 0.08f), statueMat, root.transform);
        arm2.transform.Rotate(0f, 0f, -60f);
    }

    private static void BuildRuneStone(Vector3 position, float yaw)
    {
        GameObject root = new GameObject("符文石碑");
        root.transform.position = position;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        CreateBox("碑座", new Vector3(0f, 0.1f, 0f), new Vector3(1f, 0.2f, 0.5f), stoneMat, root.transform);
        GameObject stele = CreateBox("碑身", new Vector3(0f, 1.1f, 0f), new Vector3(0.9f, 2f, 0.22f), runeMat, root.transform);
        CreateBox("碑顶", new Vector3(0f, 2.2f, 0f), new Vector3(1f, 0.25f, 0.35f), stoneMat, root.transform);
    }

    private static void BuildDeadTree(Vector3 position, int seed)
    {
        System.Random random = new System.Random(seed);
        GameObject root = new GameObject("枯树");
        root.transform.position = position;
        GameObject trunk = CreateCylinder("树干", new Vector3(0f, 1.6f, 0f), new Vector3(0.14f, 1.6f, 0.14f), woodMat, root.transform);
        trunk.transform.rotation = Quaternion.Euler(3f, 0f, 2f);
        for (int b = 0; b < 4; b++)
        {
            float angle = (float)(random.NextDouble() * 360f);
            float height = 1.2f + (float)random.NextDouble() * 1.5f;
            GameObject branch = CreateCylinder("树枝", new Vector3(0f, height, 0f),
                new Vector3(0.05f, 0.7f, 0.05f), woodMat, root.transform);
            branch.transform.rotation = Quaternion.Euler(50f + (float)random.NextDouble() * 20f, angle, 0f);
        }
    }

    private static void BuildGrassClump(Vector3 position, float scale)
    {
        GameObject root = new GameObject("草丛");
        root.transform.position = position;
        for (int i = 0; i < 3; i++)
        {
            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(blade.GetComponent<Collider>());
            blade.name = "草叶";
            blade.transform.SetParent(root.transform, false);
            blade.transform.localPosition = Vector3.zero;
            blade.transform.localScale = new Vector3(0.6f, scale, 0.02f);
            blade.transform.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
            blade.GetComponent<Renderer>().sharedMaterial = grassMat;
        }
    }

    private static void BuildAltar()
    {
        GameObject altar = new GameObject("中央祭坛");
        CreateBox("祭坛第一层", new Vector3(0f, 0.15f, 0f), new Vector3(3.4f, 0.3f, 3.4f), stoneMat, altar.transform);
        CreateBox("祭坛第二层", new Vector3(0f, 0.42f, 0f), new Vector3(2.5f, 0.26f, 2.5f), stoneMat, altar.transform);
        CreateBox("祭坛顶层", new Vector3(0f, 0.65f, 0f), new Vector3(1.6f, 0.22f, 1.6f), stoneMat, altar.transform);
        GameObject brazier = CreateCylinder("祭坛火盆", new Vector3(0f, 0.95f, 0f), new Vector3(0.45f, 0.25f, 0.45f), metalMat, altar.transform);
        AddFlame(brazier.transform, new Vector3(0f, 0.45f, 0f), 0.8f, 1.8f);
    }

    private static void AddFlame(Transform parent, Vector3 localPosition, float scale, float lightIntensity)
    {
        Material flameMaterial = new Material(Shader.Find("Sprites/Default"));
        flameMaterial.color = new Color(1f, 0.55f, 0.15f, 0.95f);

        GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(flame.GetComponent<Collider>());
        flame.name = "火焰";
        flame.transform.SetParent(parent, false);
        flame.transform.localPosition = localPosition;
        flame.transform.localScale = Vector3.one * scale;
        flame.GetComponent<Renderer>().sharedMaterial = flameMaterial;

        GameObject lightObject = new GameObject("火焰光");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = localPosition;
        Light fireLight = lightObject.AddComponent<Light>();
        fireLight.type = LightType.Point;
        fireLight.color = new Color(1f, 0.6f, 0.25f);
        fireLight.range = 7f;
        fireLight.intensity = lightIntensity;

        TorchFlicker flicker = lightObject.AddComponent<TorchFlicker>();
        SetPrivate(flicker, "fireLight", fireLight);
        SetPrivate(flicker, "flame", flame.transform);
    }

    private static void BuildAmbientParticles()
    {
        // 漂浮尘埃
        GameObject dust = new GameObject("空气中的尘埃");
        ParticleSystem ps = dust.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.startColor = new Color(0.8f, 0.75f, 0.6f, 0.4f);
        main.startSize = 0.03f;
        main.startSpeed = 0.2f;
        main.startLifetime = 12f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 8;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(18f, 5f, 18f);
        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

        // 场景四周的火把（照明）
        Vector3[] torches = {
            new Vector3(0f, 0f, 8.6f), new Vector3(8.6f, 0f, 0f),
            new Vector3(-8.6f, 0f, 0f), new Vector3(8.6f, 0f, 8.6f),
            new Vector3(-8.6f, 0f, 8.6f)
        };
        for (int i = 0; i < torches.Length; i++)
        {
            GameObject stand = CreateCylinder("火把杆", torches[i] + Vector3.up * 1.1f,
                new Vector3(0.09f, 1.1f, 0.09f), metalMat);
            GameObject bowl = CreateCylinder("火盆", torches[i] + Vector3.up * 2.2f,
                new Vector3(0.3f, 0.18f, 0.3f), metalMat);
            AddFlame(bowl.transform, new Vector3(0f, 0.35f, 0f), 0.6f, 1.5f);
        }
    }

    // ================= 人形角色 =================

    private static GameObject BuildCharacter(string name, Vector3 position, Color clothColor, bool isPlayer)
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
        animator.runtimeAnimatorController = controller;

        // Body pivot：动画的主要驱动对象
        GameObject body = new GameObject("Body");
        body.transform.SetParent(visual.transform, false);
        body.transform.localPosition = Vector3.up;

        Material cloth = new Material(Shader.Find("Standard"));
        cloth.color = clothColor;
        Material skin = new Material(Shader.Find("Standard"));
        skin.color = new Color(0.82f, 0.68f, 0.56f);

        // 髋部
        CreateBox("Hips", new Vector3(0f, 0f, 0f), new Vector3(0.42f, 0.2f, 0.24f), cloth, body.transform);
        // 躯干
        GameObject chest = CreateBox("Chest", new Vector3(0f, 0.34f, 0f), new Vector3(0.48f, 0.5f, 0.27f),
            isPlayer ? cloth : metalMat, body.transform);
        // 肩甲
        CreateBox("肩甲 L", new Vector3(-0.3f, 0.58f, 0f), new Vector3(0.16f, 0.1f, 0.18f), metalMat, body.transform);
        CreateBox("肩甲 R", new Vector3(0.3f, 0.58f, 0f), new Vector3(0.16f, 0.1f, 0.18f), metalMat, body.transform);

        // 头
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(head.GetComponent<Collider>());
        head.name = "Head";
        head.transform.SetParent(body.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        head.transform.localScale = Vector3.one * 0.32f;
        head.GetComponent<Renderer>().sharedMaterial = skin;

        // 眼睛
        Material eyeMat = new Material(Shader.Find("Standard"));
        eyeMat.color = Color.black;
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.transform.SetParent(head.transform, false);
            eye.transform.localPosition = new Vector3(side * 0.4f, 0.05f, 0.85f);
            eye.transform.localScale = Vector3.one * 0.22f;
            eye.GetComponent<Renderer>().sharedMaterial = eyeMat;
        }

        // 头盔
        GameObject helmet = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(helmet.GetComponent<Collider>());
        helmet.name = "Helmet";
        helmet.transform.SetParent(head.transform, false);
        helmet.transform.localPosition = new Vector3(0f, 0.13f, -0.02f);
        helmet.transform.localScale = new Vector3(0.72f, 0.26f, 0.72f);
        helmet.GetComponent<Renderer>().sharedMaterial = isPlayer ? metalMat : new Material(metalMat) { color = new Color(0.25f, 0.2f, 0.2f) };
        // 盔脊
        GameObject ridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(ridge.GetComponent<Collider>());
        ridge.transform.SetParent(head.transform, false);
        ridge.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        ridge.transform.localScale = new Vector3(0.08f, 0.25f, 0.9f);
        ridge.GetComponent<Renderer>().sharedMaterial = metalMat;

        // 手臂
        BuildArm(body.transform, "ArmL", new Vector3(-0.31f, 0.56f, 0f), cloth, false);
        BuildArm(body.transform, "ArmR", new Vector3(0.31f, 0.56f, 0f), cloth, true);
        // 武器挂在右手
        BuildWeapon(body.transform.Find("ArmR"), isPlayer);

        // 腿
        BuildLeg(body.transform, "LegL", new Vector3(-0.13f, -0.05f, 0f), cloth);
        BuildLeg(body.transform, "LegR", new Vector3(0.13f, -0.05f, 0f), cloth);

        // 受击闪白（闪躯干）
        HitFlash flash = root.AddComponent<HitFlash>();
        SetPrivate(flash, "bodyRenderer", chest.GetComponent<Renderer>());

        return root;
    }

    private static void BuildArm(Transform body, string name, Vector3 pivot, Material cloth, bool right)
    {
        GameObject arm = new GameObject(name);
        arm.transform.SetParent(body, false);
        arm.transform.localPosition = pivot;

        GameObject upper = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(upper.GetComponent<Collider>());
        upper.name = "上臂";
        upper.transform.SetParent(arm.transform, false);
        upper.transform.localPosition = new Vector3(0f, -0.16f, 0f);
        upper.transform.localScale = new Vector3(0.1f, 0.2f, 0.1f);
        upper.GetComponent<Renderer>().sharedMaterial = cloth;

        GameObject fore = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(fore.GetComponent<Collider>());
        fore.name = "前臂";
        fore.transform.SetParent(arm.transform, false);
        fore.transform.localPosition = new Vector3(0f, -0.42f, 0f);
        fore.transform.localScale = new Vector3(0.085f, 0.17f, 0.085f);
        fore.GetComponent<Renderer>().sharedMaterial = cloth;

        // 手
        GameObject hand = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(hand.GetComponent<Collider>());
        hand.name = "手";
        hand.transform.SetParent(arm.transform, false);
        hand.transform.localPosition = new Vector3(0f, -0.6f, 0f);
        hand.transform.localScale = Vector3.one * 0.16f;
        Material skin = new Material(Shader.Find("Standard"));
        skin.color = new Color(0.82f, 0.68f, 0.56f);
        hand.GetComponent<Renderer>().sharedMaterial = skin;
    }

    private static void BuildLeg(Transform body, string name, Vector3 pivot, Material cloth)
    {
        GameObject leg = new GameObject(name);
        leg.transform.SetParent(body, false);
        leg.transform.localPosition = pivot;

        GameObject thigh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(thigh.GetComponent<Collider>());
        thigh.name = "大腿";
        thigh.transform.SetParent(leg.transform, false);
        thigh.transform.localPosition = new Vector3(0f, -0.22f, 0f);
        thigh.transform.localScale = new Vector3(0.13f, 0.24f, 0.13f);
        thigh.GetComponent<Renderer>().sharedMaterial = cloth;

        GameObject shin = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(shin.GetComponent<Collider>());
        shin.name = "小腿";
        shin.transform.SetParent(leg.transform, false);
        shin.transform.localPosition = new Vector3(0f, -0.58f, 0f);
        shin.transform.localScale = new Vector3(0.11f, 0.22f, 0.11f);
        shin.GetComponent<Renderer>().sharedMaterial = cloth;

        // 靴子
        GameObject boot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(boot.GetComponent<Collider>());
        boot.name = "靴子";
        boot.transform.SetParent(leg.transform, false);
        boot.transform.localPosition = new Vector3(0f, -0.82f, 0.04f);
        boot.transform.localScale = new Vector3(0.16f, 0.1f, 0.26f);
        boot.GetComponent<Renderer>().sharedMaterial = metalMat;
    }

    private static void BuildWeapon(Transform arm, bool isPlayer)
    {
        GameObject weapon = new GameObject("WeaponPivot");
        weapon.transform.SetParent(arm, false);
        weapon.transform.localPosition = new Vector3(0f, -0.6f, 0.05f);

        if (isPlayer)
        {
            GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(grip.GetComponent<Collider>());
            grip.transform.SetParent(weapon.transform, false);
            grip.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            grip.transform.localScale = new Vector3(0.05f, 0.12f, 0.05f);
            grip.GetComponent<Renderer>().sharedMaterial = woodMat;

            GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(blade.GetComponent<Collider>());
            blade.name = "剑刃";
            blade.transform.SetParent(weapon.transform, false);
            blade.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            blade.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            blade.transform.localScale = new Vector3(0.045f, 0.55f, 0.045f);
            blade.GetComponent<Renderer>().sharedMaterial = metalMat;

            GameObject guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(guard.GetComponent<Collider>());
            guard.transform.SetParent(weapon.transform, false);
            guard.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            guard.transform.localScale = new Vector3(0.26f, 0.05f, 0.06f);
            guard.GetComponent<Renderer>().sharedMaterial = metalMat;
        }
        else
        {
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(weapon.transform, false);
            shaft.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localScale = new Vector3(0.06f, 0.4f, 0.06f);
            shaft.GetComponent<Renderer>().sharedMaterial = woodMat;

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "锤头";
            head.transform.SetParent(weapon.transform, false);
            head.transform.localPosition = new Vector3(0f, 0f, 0.6f);
            head.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            head.GetComponent<Renderer>().sharedMaterial = metalMat;
        }
    }

    private static GameObject BuildGuard(string name, Vector3 position, Vector3[] patrolPoints, Transform player)
    {
        GameObject guard = BuildCharacter(name, position, new Color(0.6f, 0.18f, 0.15f), false);
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

        BuildAlertIcon(guard, out Canvas alertCanvas, out GameObject question, out GameObject exclamation);

        SetPrivate(ai, "player", player);
        SetPrivate(ai, "patrolPoints", points);
        SetPrivate(ai, "animator", guard.GetComponentInChildren<Animator>());
        SetPrivate(ai, "alertCanvas", alertCanvas);
        SetPrivate(ai, "questionMark", question);
        SetPrivate(ai, "exclamationMark", exclamation);

        return guard;
    }

    private static void BuildAlertIcon(GameObject guard, out Canvas canvas, out GameObject question, out GameObject exclamation)
    {
        GameObject iconRoot = new GameObject("Alert Icon");
        iconRoot.transform.SetParent(guard.transform, false);
        iconRoot.transform.localPosition = new Vector3(0f, 2.5f, 0f);

        GameObject canvasObject = new GameObject("Alert Canvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.transform.SetParent(iconRoot.transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100f, 100f);
        rect.localScale = Vector3.one * 0.012f;
        rect.localPosition = Vector3.zero;

        question = CreateWorldText(rect, "Question", "?", new Color(1f, 0.85f, 0.2f));
        exclamation = CreateWorldText(rect, "Exclamation", "!", new Color(1f, 0.3f, 0.25f));
        canvas.enabled = false;
    }

    private static GameObject CreateWorldText(RectTransform parent, string name, string value, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Text text = go.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 72;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return go;
    }

    // ================= 宝石 =================

    private static void BuildGems(Transform player)
    {
        Vector3[] positions = {
            new Vector3(-7f, 0.9f, -8f),
            new Vector3(-8f, 0.9f, 8f),
            new Vector3(0f, 0.9f, 2.6f),
            new Vector3(-5.5f, 0.9f, 5.2f),
            new Vector3(6.5f, 0.9f, 6.5f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject root = new GameObject("遗迹宝石 " + (i + 1));
            root.transform.position = positions[i];

            // V2：切角八面体宝石（顶平台→上锥→腰棱台→下锥），自发光材质自带辉光
            GameObject model = BuildGemVisualV2(new Color(0.24f, 0.88f, 1f), 0.30f);
            model.name = "宝石模型";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;

            GameObject lightObject = new GameObject("宝石光");
            lightObject.transform.SetParent(root.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.3f, 0.9f, 1f);
            // 只给宝石本体一点环境辉光，不再把周围地面照成白色光斑
            light.range = 2.6f;
            light.intensity = 0.55f;

            Collectible collectible = root.AddComponent<Collectible>();
            SetPrivate(collectible, "player", player);
            SetPrivate(collectible, "gemLight", light);
            SetPrivate(collectible, "gemModel", model.transform);
        }
    }

    // ================= 后处理 =================

    private static PostProcessProfile CreatePostProcessProfile()
    {
        PostProcessProfile profile = ScriptableObject.CreateInstance<PostProcessProfile>();

        AmbientOcclusion ao = profile.AddSettings<AmbientOcclusion>();
        ao.enabled.Override(true);
        ao.intensity.Override(0.25f);
        ao.color.Override(new Color(0.2f, 0.18f, 0.17f));

        Bloom bloom = profile.AddSettings<Bloom>();
        bloom.enabled.Override(true);
        bloom.intensity.Override(0.75f);
        bloom.threshold.Override(1.05f);
        bloom.softKnee.Override(0.6f);
        bloom.color.Override(new Color(0.9f, 0.95f, 1f));

        AutoExposure exposure = profile.AddSettings<AutoExposure>();
        exposure.enabled.Override(false);

        ColorGrading grading = profile.AddSettings<ColorGrading>();
        grading.enabled.Override(false);
        grading.tonemapper.Override(Tonemapper.Neutral);
        grading.temperature.Override(6f);
        grading.tint.Override(-3f);
        grading.saturation.Override(0.05f);
        grading.contrast.Override(2f);
        grading.postExposure.Override(0.55f);

        Vignette vignette = profile.AddSettings<Vignette>();
        vignette.enabled.Override(false);
        vignette.intensity.Override(0.22f);
        vignette.smoothness.Override(0.55f);
        vignette.color.Override(new Color(0.15f, 0.1f, 0.18f));

        // 所有设置添加完成后，再保存资产（settings 作为 sub-asset 一起序列化）
        AssetDatabase.CreateAsset(profile, Root + "/RuinsPostProcessProfile.asset");
        foreach (PostProcessEffectSettings setting in profile.settings)
        {
            AssetDatabase.AddObjectToAsset(setting, profile);
        }
        AssetDatabase.SaveAssets();

        return profile;
    }

    private static void BuildPostProcessObject(PostProcessProfile profile)
    {
        GameObject volumeObject = new GameObject("Post Process Volume");
        PostProcessVolume volume = volumeObject.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;
        volume.profile = profile;
    }

    // ================= 相机 =================

    private static void BuildCamera(Transform target, PostProcessProfile profile)
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 60f;
        camera.allowHDR = true;
        camera.allowMSAA = true;
        camera.transform.position = new Vector3(-7f, 4.5f, -12f);
        camera.transform.LookAt(target.position + Vector3.up);

        // 后处理层
        PostProcessLayer layer = cameraObject.AddComponent<PostProcessLayer>();
        layer.Init(PostProcessResourcesUtility.GetResources());
        layer.volumeLayer = ~0;
        layer.volumeTrigger = cameraObject.transform;
        layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;

        OrbitFollowCamera follow = cameraObject.AddComponent<OrbitFollowCamera>();
        SetPrivate(follow, "target", target);
        // 注：俯仰由 OrbitFollowCamera 的序列化字段 pitch 决定（currentPitch 属私有非序列化字段，
        // 用 SerializedObject 设置会打警告且无效），这里不再强行覆盖。
    }

    private static GameObject BuildBgmSource()
    {
        GameObject go = new GameObject("BGM Audio Source");
        AudioSource source = go.AddComponent<AudioSource>();
        source.volume = 0.45f;
        source.loop = true;
        source.playOnAwake = false;
        return go;
    }

    // ================= UI =================

    private struct GameUiReferences
    {
        public GameObject hudRoot;
        public Image healthFill;
        public Text healthText;
        public Text gemText;
        public Text objectiveText;
        public Image damageOverlay;
        public RectTransform floatingLayer;
        public GameObject crosshair;
        public GameObject startPanel;
        public GameObject pausePanel;
        public GameObject endPanel;
        public Text endTitleText;
        public Text endStatsText;
        public Button startButton;
        public Button resumeButton;
        public Button pauseRestartButton;
        public Button endRestartButton;
        public Slider volumeSlider;
        public Button playButton;
        public Button pauseButton;
        public Button muteButton;
        public Button aiToggleButton;
        public Button resetGuardButton;
    }

    private static GameUiReferences BuildUi(Health playerHealth)
    {
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        GameUiReferences ui = new GameUiReferences();

        BuildHudCanvas(out ui.hudRoot, out ui.healthFill, out ui.healthText, out ui.gemText,
            out ui.objectiveText, out ui.damageOverlay, out ui.floatingLayer, out ui.crosshair);
        BuildMenuCanvas(out ui.startPanel, out ui.pausePanel, out ui.endPanel,
            out ui.endTitleText, out ui.endStatsText, out ui.startButton,
            out ui.resumeButton, out ui.pauseRestartButton, out ui.endRestartButton);
        BuildAudioCanvas(out ui.volumeSlider, out ui.playButton, out ui.pauseButton,
            out ui.muteButton, out ui.aiToggleButton, out ui.resetGuardButton);
        return ui;
    }

    private static Canvas CreateCanvas(string name, int sortingOrder)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return canvas;
    }

    private static void BuildHudCanvas(out GameObject hudRoot, out Image healthFill, out Text healthText,
        out Text gemText, out Text objectiveText, out Image damageOverlay,
        out RectTransform floatingLayer, out GameObject crosshair)
    {
        Canvas canvas = CreateCanvas("HUD Canvas", 1);

        hudRoot = new GameObject("HUD Root");
        RectTransform hudRect = hudRoot.AddComponent<RectTransform>();
        hudRect.SetParent(canvas.transform, false);
        Stretch(hudRect);

        // 血条底板
        GameObject healthBg = CreateUiImage(hudRect, "血条底板", new Color(0.08f, 0.08f, 0.09f, 0.85f));
        RectTransform bgRect = healthBg.GetComponent<RectTransform>();
        TopLeft(bgRect, new Vector2(24f, -24f), new Vector2(360f, 34f));

        // 十字图标
        Text crossIcon = CreateUiText(bgRect, "十字", "+", 24, new Color(0.9f, 0.25f, 0.25f));
        RectTransform iconRect = crossIcon.rectTransform;
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(18f, 0f);
        iconRect.sizeDelta = new Vector2(28f, 28f);

        // 填充
        GameObject fillArea = CreateUiImage(bgRect, "填充区", new Color(0.2f, 0.15f, 0.15f));
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = fillAreaRect.anchorMax = new Vector2(0f, 0.5f);
        fillAreaRect.pivot = new Vector2(0f, 0.5f);
        fillAreaRect.anchoredPosition = new Vector2(42f, 0f);
        fillAreaRect.sizeDelta = new Vector2(210f, 22f);

        GameObject fillObject = CreateUiImage(fillAreaRect, "血条填充", new Color(0.85f, 0.18f, 0.18f));
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        Stretch(fillRect);
        healthFill = fillObject.GetComponent<Image>();
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;
        healthFill.fillAmount = 1f;

        healthText = CreateUiText(bgRect, "血量文字", "100 / 100", 15, Color.white);
        RectTransform htRect = healthText.rectTransform;
        htRect.anchorMin = htRect.anchorMax = new Vector2(0f, 0.5f);
        htRect.pivot = new Vector2(0f, 0.5f);
        htRect.anchoredPosition = new Vector2(258f, 0f);
        htRect.sizeDelta = new Vector2(96f, 26f);

        // 宝石计数
        gemText = CreateUiText(hudRect, "宝石计数", "◆ 遗迹宝石  0 / 5", 20, new Color(0.4f, 0.95f, 1f));
        RectTransform gemRect = gemText.rectTransform;
        TopLeft(gemRect, new Vector2(24f, -68f), new Vector2(320f, 30f));
        gemText.alignment = TextAnchor.MiddleLeft;

        // 顶部目标
        objectiveText = CreateUiText(hudRect, "目标提示", "收集全部遗迹宝石", 20, new Color(1f, 0.9f, 0.65f));
        TopCenter(objectiveText.rectTransform, new Vector2(0f, -24f), new Vector2(720f, 34f));

        // 底部提示
        Text controls = CreateUiText(hudRect, "操作提示",
            "WASD 移动　Shift 冲刺　空格 跳跃　Q 闪避　左键 攻击　鼠标移动旋转视角　Esc 暂停",
            15, new Color(0.85f, 0.85f, 0.85f, 0.8f));
        BottomCenter(controls.rectTransform, new Vector2(0f, 18f), new Vector2(900f, 28f));

        // 受击红闪
        GameObject overlay = CreateUiImage(hudRect, "受击红闪", new Color(0.7f, 0f, 0f, 0f));
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        Stretch(overlayRect);
        overlay.GetComponent<Image>().raycastTarget = false;
        damageOverlay = overlay.GetComponent<Image>();

        // 飘字层
        GameObject layer = new GameObject("Floating Text Layer");
        floatingLayer = layer.AddComponent<RectTransform>();
        floatingLayer.SetParent(hudRect, false);
        Stretch(floatingLayer);

        // 准星
        crosshair = BuildCrosshair(hudRect);
    }

    private static GameObject BuildCrosshair(RectTransform parent)
    {
        GameObject root = new GameObject("准星");
        RectTransform rect = root.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(40f, 40f);

        Color c = new Color(1f, 1f, 1f, 0.75f);
        string[] names = { "上", "下", "左", "右" };
        Vector2[] positions = { new Vector2(0, 8), new Vector2(0, -8), new Vector2(-8, 0), new Vector2(8, 0) };
        Vector2[] sizes = { new Vector2(2, 6), new Vector2(2, 6), new Vector2(6, 2), new Vector2(6, 2) };
        for (int i = 0; i < 4; i++)
        {
            GameObject tick = CreateUiImage(rect, names[i], c);
            RectTransform tr = tick.GetComponent<RectTransform>();
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.anchoredPosition = positions[i];
            tr.sizeDelta = sizes[i];
        }
        GameObject dot = CreateUiImage(rect, "中心", new Color(1f, 0.3f, 0.3f, 0.9f));
        RectTransform dr = dot.GetComponent<RectTransform>();
        dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0.5f);
        dr.pivot = new Vector2(0.5f, 0.5f);
        dr.anchoredPosition = Vector2.zero;
        dr.sizeDelta = new Vector2(3f, 3f);
        return root;
    }

    private static void BuildMenuCanvas(out GameObject startPanel, out GameObject pausePanel, out GameObject endPanel,
        out Text endTitleText, out Text endStatsText, out Button startButton,
        out Button resumeButton, out Button pauseRestartButton, out Button endRestartButton)
    {
        Canvas canvas = CreateCanvas("Menu Canvas", 20);

        GameObject dim = CreateUiImage(canvas.transform, "全屏遮罩", new Color(0.04f, 0.03f, 0.07f, 0.78f));
        Stretch(dim.GetComponent<RectTransform>());

        startPanel = CreatePanel(canvas.transform, "开始面板");
        CreatePanelText(startPanel, "标题", "RUINS GUARDIAN", 54, new Color(1f, 0.82f, 0.48f),
            new Vector2(0f, 200f), new Vector2(600f, 84f));
        CreatePanelText(startPanel, "副标题", "交互界面 · 角色动画 · 有限状态机 AI", 20,
            new Color(0.72f, 0.77f, 0.86f), new Vector2(0f, 142f), new Vector2(600f, 34f));
        Text body = CreatePanelText(startPanel, "说明",
            "你是一名潜入古老遗迹的探险家。\n守卫在断壁残垣间巡逻，一旦发现入侵者就会追击。\n\n收集散落在遗迹中的 5 颗宝石即可完成试炼；\n被守卫击倒则试炼失败。\n\nWASD 移动，Shift 冲刺，空格或鼠标左键挥剑；\n按住鼠标右键拖动旋转视角，滚轮缩放。",
            19, new Color(0.88f, 0.88f, 0.9f), new Vector2(0f, -12f), new Vector2(520f, 310f));
        body.alignment = TextAnchor.MiddleCenter;
        startButton = CreatePanelButton(startPanel, "开始探索", new Vector2(0f, -238f), new Vector2(250f, 56f), 24);

        pausePanel = CreatePanel(canvas.transform, "暂停面板");
        CreatePanelText(pausePanel, "标题", "已 暂 停", 42, Color.white,
            new Vector2(0f, 90f), new Vector2(600f, 72f));
        resumeButton = CreatePanelButton(pausePanel, "继续探索", new Vector2(0f, 0f), new Vector2(250f, 52f), 22);
        pauseRestartButton = CreatePanelButton(pausePanel, "重新开始", new Vector2(0f, -78f), new Vector2(250f, 52f), 22);
        pausePanel.SetActive(false);

        endPanel = CreatePanel(canvas.transform, "结算面板");
        endTitleText = CreatePanelText(endPanel, "结算标题", "", 42, Color.white,
            new Vector2(0f, 115f), new Vector2(600f, 72f));
        endStatsText = CreatePanelText(endPanel, "结算数据", "", 20,
            new Color(0.9f, 0.9f, 0.92f), new Vector2(0f, -12f), new Vector2(520f, 210f));
        endStatsText.alignment = TextAnchor.MiddleCenter;
        endRestartButton = CreatePanelButton(endPanel, "再来一次", new Vector2(0f, -178f), new Vector2(250f, 54f), 22);
        endPanel.SetActive(false);
    }

    private static void BuildAudioCanvas(out Slider volumeSlider, out Button playButton, out Button pauseButton,
        out Button muteButton, out Button aiToggleButton, out Button resetGuardButton)
    {
        Canvas canvas = CreateCanvas("Audio Canvas", 10);

        GameObject panel = CreateUiImage(canvas.transform, "音频面板", new Color(0.09f, 0.11f, 0.16f, 0.92f));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        TopRight(panelRect, new Vector2(-24f, -24f), new Vector2(320f, 230f));

        Text title = CreateUiText(panelRect, "标题", "音频控制面板", 20, new Color(0.6f, 0.85f, 1f));
        TopCenter(title.rectTransform, new Vector2(0f, -14f), new Vector2(300f, 34f));

        playButton = CreateUiButton(panelRect, "播放", new Vector2(14f, -58f), new Vector2(92f, 38f));
        pauseButton = CreateUiButton(panelRect, "暂停", new Vector2(114f, -58f), new Vector2(92f, 38f));
        muteButton = CreateUiButton(panelRect, "静音", new Vector2(214f, -58f), new Vector2(92f, 38f));
        aiToggleButton = CreateUiButton(panelRect, "AI：开", new Vector2(64f, -106f), new Vector2(92f, 38f));
        resetGuardButton = CreateUiButton(panelRect, "复位守卫", new Vector2(164f, -106f), new Vector2(92f, 38f));
        volumeSlider = CreateUiSlider(panelRect, new Vector2(20f, -172f), new Vector2(280f, 24f));
    }

    private static void BuildGameManager(Health playerHealth, GameObject bgmSource, GameUiReferences ui,
        GameObject[] guards)
    {
        GameObject managerObject = new GameObject("GameManager");

        AudioSource sfxSource = managerObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        ProceduralAudioBank bank = managerObject.AddComponent<ProceduralAudioBank>();
        SetPrivate(bank, "sfxSource", sfxSource);
        SetPrivate(bank, "bgmSource", bgmSource.GetComponent<AudioSource>());
        SetPrivate(bank, "sfxVolume", 0.85f);

        HUDController hud = managerObject.AddComponent<HUDController>();
        SetPrivate(hud, "hudRoot", ui.hudRoot);
        SetPrivate(hud, "healthFill", ui.healthFill);
        SetPrivate(hud, "healthText", ui.healthText);
        SetPrivate(hud, "gemText", ui.gemText);
        SetPrivate(hud, "objectiveText", ui.objectiveText);
        SetPrivate(hud, "damageOverlay", ui.damageOverlay);
        SetPrivate(hud, "floatingTextLayer", ui.floatingLayer);

        GameMenuController menu = managerObject.AddComponent<GameMenuController>();
        SetPrivate(menu, "startPanel", ui.startPanel);
        SetPrivate(menu, "pausePanel", ui.pausePanel);
        SetPrivate(menu, "endPanel", ui.endPanel);
        SetPrivate(menu, "endTitleText", ui.endTitleText);
        SetPrivate(menu, "endStatsText", ui.endStatsText);
        SetPrivate(menu, "startButton", ui.startButton);
        SetPrivate(menu, "resumeButton", ui.resumeButton);
        SetPrivate(menu, "pauseRestartButton", ui.pauseRestartButton);
        SetPrivate(menu, "endRestartButton", ui.endRestartButton);

        AudioPanelController audioPanel = managerObject.AddComponent<AudioPanelController>();
        SetPrivate(audioPanel, "audioSource", bgmSource.GetComponent<AudioSource>());
        SetPrivate(audioPanel, "volumeSlider", ui.volumeSlider);
        SetPrivate(audioPanel, "playButton", ui.playButton);
        SetPrivate(audioPanel, "pauseButton", ui.pauseButton);
        SetPrivate(audioPanel, "muteButton", ui.muteButton);
        SetPrivate(audioPanel, "aiToggleButton", ui.aiToggleButton);
        SetPrivate(audioPanel, "resetGuardButton", ui.resetGuardButton);
        EnemyWanderAI[] guardAis = new EnemyWanderAI[guards.Length];
        for (int i = 0; i < guards.Length; i++)
            guardAis[i] = guards[i].GetComponent<EnemyWanderAI>();
        SetPrivate(audioPanel, "guards", guardAis);

        GameManager manager = managerObject.AddComponent<GameManager>();
        SetPrivate(manager, "playerHealth", playerHealth);
        SetPrivate(manager, "hud", hud);
        SetPrivate(manager, "menu", menu);
        SetPrivate(manager, "audioBank", bank);
    }

    // ---- UI 基础 ----

    private static GameObject CreateUiImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = color;
        return go;
    }

    private static Text CreateUiText(Transform parent, string name, string value, int size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateUiButton(Transform parent, string label, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        TopLeft(rect, position, size);
        go.GetComponent<Image>().color = new Color(0.18f, 0.38f, 0.58f);
        Text text = CreateUiText(rect, "Label", label, 18, Color.white);
        Stretch(text.rectTransform);
        return go.GetComponent<Button>();
    }

    private static Slider CreateUiSlider(Transform parent, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject("Volume Slider", typeof(RectTransform), typeof(Slider));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        TopLeft(rect, position, size);
        Slider slider = go.GetComponent<Slider>();
        slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0.45f;

        GameObject track = CreateUiImage(rect, "Background", new Color(0.25f, 0.28f, 0.33f));
        Stretch(track.GetComponent<RectTransform>());

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(rect, false);
        RectTransform fa = fillArea.GetComponent<RectTransform>();
        fa.anchorMin = new Vector2(0f, 0.25f);
        fa.anchorMax = new Vector2(1f, 0.75f);
        fa.offsetMin = new Vector2(4f, 0f);
        fa.offsetMax = new Vector2(-4f, 0f);

        GameObject fill = CreateUiImage(fa, "Fill", new Color(0.28f, 0.76f, 0.95f));
        RectTransform fr = fill.GetComponent<RectTransform>();
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(rect, false);
        Stretch(handleArea.GetComponent<RectTransform>());

        GameObject handle = CreateUiImage(handleArea.transform, "Handle", Color.white);
        RectTransform hr = handle.GetComponent<RectTransform>();
        hr.sizeDelta = new Vector2(20f, 32f);

        slider.fillRect = fr;
        slider.handleRect = hr;
        slider.targetGraphic = handle.GetComponent<Image>();
        return slider;
    }

    private static GameObject CreatePanel(Transform parent, string name)
    {
        GameObject panel = CreateUiImage(parent, name, new Color(0.11f, 0.1f, 0.15f, 0.97f));
        RectTransform rect = panel.GetComponent<RectTransform>();
        Center(rect, new Vector2(640f, 620f));
        return panel;
    }

    private static Text CreatePanelText(GameObject panel, string name, string value, int size,
        Color color, Vector2 position, Vector2 dimensions)
    {
        Text text = CreateUiText(panel.transform, name, value, size, color);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        text.alignment = TextAnchor.MiddleCenter;
        return text;
    }

    private static Button CreatePanelButton(GameObject panel, string label, Vector2 position,
        Vector2 size, int fontSize)
    {
        GameObject go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(panel.transform, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.72f, 0.48f, 0.2f);
        Text text = CreateUiText(rect, "Label", label, fontSize, Color.white);
        Stretch(text.rectTransform);
        return go.GetComponent<Button>();
    }

    // ---- RectTransform ----

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private static void TopLeft(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void TopRight(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void TopCenter(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void BottomCenter(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
    }

    // ================= Animator =================

    private static AnimatorController CreateController()
    {
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "/RuinsGuard.controller");
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("AttackTrigger", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("HitTrigger", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimationClip idle = CreateClip("Idle", 2f);
        AnimationClip walk = CreateClip("Walk", 0.55f);
        AnimationClip run = CreateClip("Run", 0.32f);
        AnimationClip attack = CreateClip("Attack", 0.55f);
        AnimationClip hit = CreateClip("Hit", 0.4f);
        AnimationClip dead = CreateClip("Dead", 1.2f);

        BlendTree tree = new BlendTree();
        tree.name = "LocomotionTree";
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 2f);
        tree.AddChild(run, 6f);

        AnimatorState locomotion = machine.AddState("Locomotion");
        locomotion.motion = tree;
        machine.defaultState = locomotion;

        AnimatorState attackState = machine.AddState("Attack");
        attackState.motion = attack;
        AnimatorState hitState = machine.AddState("Hit");
        hitState.motion = hit;
        AnimatorState deadState = machine.AddState("Dead");
        deadState.motion = dead;

        AnimatorStateTransition toAttack = locomotion.AddTransition(attackState);
        toAttack.hasExitTime = false;
        toAttack.AddCondition(AnimatorConditionMode.If, 0f, "AttackTrigger");
        toAttack.duration = 0.06f;

        AnimatorStateTransition fromAttack = attackState.AddTransition(locomotion);
        fromAttack.hasExitTime = true;
        fromAttack.exitTime = 0.92f;
        fromAttack.duration = 0.1f;

        AnimatorStateTransition toDead = machine.AddAnyStateTransition(deadState);
        toDead.hasExitTime = false;
        toDead.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");
        toDead.duration = 0.15f;
        toDead.canTransitionToSelf = false;

        AnimatorStateTransition toHit = machine.AddAnyStateTransition(hitState);
        toHit.hasExitTime = false;
        toHit.AddCondition(AnimatorConditionMode.If, 0f, "HitTrigger");
        toHit.duration = 0.05f;
        toHit.canTransitionToSelf = false;

        AnimatorStateTransition fromHit = hitState.AddTransition(locomotion);
        fromHit.hasExitTime = true;
        fromHit.exitTime = 0.92f;
        fromHit.duration = 0.08f;

        return controller;
    }

    private static AnimationClip CreateClip(string name, float duration)
    {
        AnimationClip clip = new AnimationClip { name = name, legacy = false };

        switch (name)
        {
            case "Idle":
                clip.SetCurve("Body", typeof(Transform), "localPosition.y",
                    new AnimationCurve(
                        new Keyframe(0f, 1.0f),
                        new Keyframe(duration * 0.5f, 1.025f),
                        new Keyframe(duration, 1.0f)));
                clip.wrapMode = WrapMode.Loop;
                break;

            case "Walk":
                clip.SetCurve("Body", typeof(Transform), "localPosition.y",
                    new AnimationCurve(
                        new Keyframe(0f, 1.0f),
                        new Keyframe(duration * 0.5f, 1.04f),
                        new Keyframe(duration, 1.0f)));
                AddLimbCurve(clip, "Body/LegL", duration, 30f, false);
                AddLimbCurve(clip, "Body/LegR", duration, 30f, true);
                AddLimbCurve(clip, "Body/ArmL", duration, 25f, true);
                AddLimbCurve(clip, "Body/ArmR", duration, 12f, false);
                clip.wrapMode = WrapMode.Loop;
                break;

            case "Run":
                clip.SetCurve("Body", typeof(Transform), "localPosition.y",
                    new AnimationCurve(
                        new Keyframe(0f, 1.0f),
                        new Keyframe(duration * 0.5f, 1.07f),
                        new Keyframe(duration, 1.0f)));
                clip.SetCurve("Body/Chest", typeof(Transform), "localEulerAnglesRaw.x",
                    AnimationCurve.Constant(0f, duration, 8f));
                AddLimbCurve(clip, "Body/LegL", duration, 50f, false);
                AddLimbCurve(clip, "Body/LegR", duration, 50f, true);
                AddLimbCurve(clip, "Body/ArmL", duration, 35f, true);
                AddLimbCurve(clip, "Body/ArmR", duration, 20f, false);
                clip.wrapMode = WrapMode.Loop;
                break;

            case "Attack":
                // 右臂举剑（0.15s 前）→ 劈下（0.38s）
                clip.SetCurve("Body/ArmR", typeof(Transform), "localEulerAnglesRaw.x",
                    new AnimationCurve(
                        new Keyframe(0f, -20f),
                        new Keyframe(duration * 0.27f, -70f),
                        new Keyframe(duration * 0.6f, 55f),
                        new Keyframe(duration, 0f)));
                clip.SetCurve("Body/Chest", typeof(Transform), "localEulerAnglesRaw.x",
                    new AnimationCurve(
                        new Keyframe(0f, 0f),
                        new Keyframe(duration * 0.5f, 10f),
                        new Keyframe(duration, 0f)));
                clip.SetCurve("Body", typeof(Transform), "localPosition.y",
                    new AnimationCurve(
                        new Keyframe(0f, 1.0f),
                        new Keyframe(duration * 0.5f, 0.97f),
                        new Keyframe(duration, 1.0f)));
                clip.wrapMode = WrapMode.Once;
                break;

            case "Hit":
                clip.SetCurve("Body/Chest", typeof(Transform), "localEulerAnglesRaw.x",
                    new AnimationCurve(
                        new Keyframe(0f, -16f),
                        new Keyframe(duration * 0.4f, -16f),
                        new Keyframe(duration, 0f)));
                clip.wrapMode = WrapMode.Once;
                break;

            case "Dead":
                clip.SetCurve("Body", typeof(Transform), "localEulerAnglesRaw.x",
                    AnimationCurve.EaseInOut(0f, 0f, duration, 80f));
                clip.SetCurve("Body", typeof(Transform), "localPosition.y",
                    AnimationCurve.EaseInOut(0f, 1f, duration, 0.45f));
                clip.wrapMode = WrapMode.Once;
                break;
        }

        AssetDatabase.CreateAsset(clip, AnimRoot + "/" + name + ".anim");
        clip.EnsureQuaternionContinuity();
        return clip;
    }

    private static void AddLimbCurve(AnimationClip clip, string path, float duration, float amplitude, bool invert)
    {
        float a = invert ? -amplitude : amplitude;
        clip.SetCurve(path, typeof(Transform), "localEulerAnglesRaw.x",
            new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(duration * 0.25f, a),
                new Keyframe(duration * 0.5f, 0f),
                new Keyframe(duration * 0.75f, -a),
                new Keyframe(duration, 0f)));
    }

    // ================= 序列化 =================

    private static void SetPrivate(object component, string propertyName, object value)
    {
        SerializedObject serialized = new SerializedObject(component as Object);
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning("未找到序列化字段：" + propertyName + "（" + component.GetType().Name + "）");
            return;
        }
        SetPropertyValue(property, value);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetPropertyValue(SerializedProperty property, object value)
    {
        if (value is float) property.floatValue = (float)value;
        else if (value is bool) property.boolValue = (bool)value;
        else if (value is int) property.intValue = (int)value;
        else if (value is Object) property.objectReferenceValue = (Object)value;
        else if (value is Transform[])
        {
            Transform[] array = (Transform[])value;
            property.arraySize = array.Length;
            for (int i = 0; i < array.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
        }
        else if (value is EnemyWanderAI[])
        {
            EnemyWanderAI[] array = (EnemyWanderAI[])value;
            property.arraySize = array.Length;
            for (int i = 0; i < array.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
        }
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

/// <summary>向量扩展辅助。</summary>
public static class VectorExtensions
{
    public static Vector3 To3(this Vector2 v, float z)
    {
        return new Vector3(v.x, v.y, z);
    }
}

/// <summary>后处理资源定位辅助。</summary>
public static class PostProcessResourcesUtility
{
    public static PostProcessResources GetResources()
    {
        // 后处理包首次导入时自动创建资源
        string[] guids = AssetDatabase.FindAssets("t:PostProcessResources");
        if (guids.Length > 0)
            return AssetDatabase.LoadAssetAtPath<PostProcessResources>(
                AssetDatabase.GUIDToAssetPath(guids[0]));
        return null;
    }
}
