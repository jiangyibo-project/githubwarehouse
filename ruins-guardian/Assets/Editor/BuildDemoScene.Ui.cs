using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI 生成器 V2（partial，T5 拥有）。
/// 生成现代动作游戏风格的 HUD、开始 / 暂停 / 结算三张菜单面板，以及可折叠的音频面板，
/// 并把所有引用打包成 UiV2References 交回给 Build()。
/// 生成期间把 UiFactory.Resolver 指向 Assets/Generated/Ui 下的 Sprite 资源，
/// 因此场景里保存的是真实的 PNG 资源引用（不是运行时临时纹理）。
/// </summary>
public static partial class BuildDemoScene
{
    private const string UiSpriteRoot = "Assets/Generated/Ui";

    private static readonly Dictionary<string, Sprite> uiSprites = new Dictionary<string, Sprite>();

    /// <summary>V2 界面引用包：HUD / 菜单 / 音频面板的全部控件。</summary>
    internal struct UiV2References
    {
        // HUD
        public GameObject hudRoot;
        public Image healthFill;
        public Text healthText;
        public Text gemText;
        public Text objectiveText;
        public Image damageOverlay;
        public RectTransform floatingLayer;
        public RectTransform crosshairRoot;
        public HudAnimator hudAnimator;
        public UiFeedback feedback;

        // 菜单
        public GameObject startPanel;
        public GameObject pausePanel;
        public GameObject endPanel;
        public Text endTitleText;
        public Text endStatsText;
        public Button startButton;
        public Button resumeButton;
        public Button pauseRestartButton;
        public Button endRestartButton;
        public RectTransform startCard;
        public RectTransform pauseCard;
        public RectTransform endCard;
        public RectTransform startShine;
        public RectTransform pauseShine;
        public RectTransform endShine;
        public Text endTimeValue;
        public Text endKillValue;
        public Text endGemValue;

        // 音频面板
        public Slider volumeSlider;
        public Button playButton;
        public Button pauseButton;
        public Button muteButton;
        public Button aiToggleButton;
        public Button resetGuardButton;
    }

    // ==================== Sprite 烘焙 ====================

    /// <summary>把 UiFactory 的全部 Sprite 生成 PNG 资源并导入（幂等，重复生成会覆盖）。</summary>
    private static void BakeUiSprites()
    {
        uiSprites.Clear();
        EnsureFolder(UiSpriteRoot);

        string[] keys = UiFactory.AllKeys;
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            string path = UiSpriteRoot + "/" + key + ".png";

            Texture2D texture = UiFactory.CreateTexture(key);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(path), png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = UiFactory.BorderOf(key);
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 256;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) uiSprites[key] = sprite;
        }
    }

    /// <summary>UiFactory.Resolver 的 Editor 实现：按 key 返回磁盘上的 Sprite 资源。</summary>
    private static Sprite UiSpriteAsset(string key)
    {
        Sprite sprite;
        if (uiSprites.TryGetValue(key, out sprite) && sprite != null) return sprite;
        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiSpriteRoot + "/" + key + ".png");
        if (sprite != null) uiSprites[key] = sprite;
        return sprite;
    }

    // ==================== 对外入口 ====================

    /// <summary>生成整套 V2 界面，返回引用包。</summary>
    internal static UiV2References BuildUiV2(Health playerHealth)
    {
        BakeUiSprites();
        UiFactory.Resolver = UiSpriteAsset;
        try
        {
            if (Object.FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            UiV2References ui = new UiV2References();
            BuildHudCanvasV2(playerHealth, ref ui);
            BuildMenuCanvasV2(ref ui);
            BuildAudioCanvasV2(ref ui);
            return ui;
        }
        finally
        {
            UiFactory.Resolver = null;
        }
    }

    /// <summary>
    /// 一次性完成 V2 界面与 GameManager 接线。
    /// Build() 里只需把原来的
    ///   GameUiReferences ui = BuildUi(playerHealth);
    ///   BuildGameManager(playerHealth, bgmSource, ui, new[] { guard1, guard2, guard3 });
    /// 换成
    ///   BuildUiAndManagers(playerHealth, bgmSource, new[] { guard1, guard2, guard3 });
    /// </summary>
    internal static void BuildUiAndManagers(Health playerHealth, GameObject bgmSource, GameObject[] guards)
    {
        UiV2References ui = BuildUiV2(playerHealth);
        BuildGameManagerV2(playerHealth, bgmSource, ui, guards);
    }

    /// <summary>沿用旧的 BuildGameManager 建立管理器，再补上 V2 新增的字段。</summary>
    internal static void BuildGameManagerV2(Health playerHealth, GameObject bgmSource, UiV2References ui, GameObject[] guards)
    {
        GameUiReferences legacy = new GameUiReferences();
        legacy.hudRoot = ui.hudRoot;
        legacy.healthFill = ui.healthFill;
        legacy.healthText = ui.healthText;
        legacy.gemText = ui.gemText;
        legacy.objectiveText = ui.objectiveText;
        legacy.damageOverlay = ui.damageOverlay;
        legacy.floatingLayer = ui.floatingLayer;
        legacy.crosshair = ui.crosshairRoot != null ? ui.crosshairRoot.gameObject : null;
        legacy.startPanel = ui.startPanel;
        legacy.pausePanel = ui.pausePanel;
        legacy.endPanel = ui.endPanel;
        legacy.endTitleText = ui.endTitleText;
        legacy.endStatsText = ui.endStatsText;
        legacy.startButton = ui.startButton;
        legacy.resumeButton = ui.resumeButton;
        legacy.pauseRestartButton = ui.pauseRestartButton;
        legacy.endRestartButton = ui.endRestartButton;
        legacy.volumeSlider = ui.volumeSlider;
        legacy.playButton = ui.playButton;
        legacy.pauseButton = ui.pauseButton;
        legacy.muteButton = ui.muteButton;
        legacy.aiToggleButton = ui.aiToggleButton;
        legacy.resetGuardButton = ui.resetGuardButton;

        BuildGameManager(playerHealth, bgmSource, legacy, guards);

        HUDController hud = Object.FindObjectOfType<HUDController>();
        if (hud != null)
        {
            SetPrivate(hud, "hudAnimator", ui.hudAnimator);
            SetPrivate(hud, "feedback", ui.feedback);
            SetPrivate(hud, "crosshairRoot", ui.crosshairRoot);
            SetPrivate(hud, "worldCamera", Camera.main);
        }

        GameMenuController menu = Object.FindObjectOfType<GameMenuController>();
        if (menu != null)
        {
            SetPrivate(menu, "startCard", ui.startCard);
            SetPrivate(menu, "pauseCard", ui.pauseCard);
            SetPrivate(menu, "endCard", ui.endCard);
            SetPrivate(menu, "startShine", ui.startShine);
            SetPrivate(menu, "pauseShine", ui.pauseShine);
            SetPrivate(menu, "endShine", ui.endShine);
            SetPrivate(menu, "endTimeValue", ui.endTimeValue);
            SetPrivate(menu, "endKillValue", ui.endKillValue);
            SetPrivate(menu, "endGemValue", ui.endGemValue);
        }
    }

    // ==================== HUD ====================

    private static void BuildHudCanvasV2(Health playerHealth, ref UiV2References ui)
    {
        Canvas canvas = UiCanvas("HUD Canvas", 1);

        GameObject hudRoot = new GameObject("HUD Root", typeof(RectTransform));
        RectTransform hudRect = hudRoot.GetComponent<RectTransform>();
        hudRect.SetParent(canvas.transform, false);
        UiFactory.Stretch(hudRect);
        ui.hudRoot = hudRoot;

        HudAnimator animator = hudRoot.AddComponent<HudAnimator>();
        UiFeedback feedback = hudRoot.AddComponent<UiFeedback>();
        ui.hudAnimator = animator;
        ui.feedback = feedback;

        float current = playerHealth != null ? playerHealth.CurrentHealth : 100f;
        float maximum = playerHealth != null ? playerHealth.MaxHealth : 100f;

        // ---------- 左上：斜切分段血条 ----------
        RectTransform healthGroup = UiRect(hudRect, "Health Group");
        UiFactory.TopLeft(healthGroup, new Vector2(36f, -26f), new Vector2(480f, 96f));

        Image badge = UiFactory.CreateSliced(healthGroup, "Badge", UiFactory.KeyDisc, UiFactory.Alpha(UiFactory.Dark, 0.92f));
        UiFactory.Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -6f), new Vector2(54f, 54f));
        Image badgeRing = UiFactory.CreateSliced(badge.rectTransform, "Badge Ring", UiFactory.KeyRingThin, UiFactory.Alpha(UiFactory.Gold, 0.85f));
        UiFactory.Stretch(badgeRing.rectTransform);
        Text badgeLabel = UiFactory.CreateText(badge.rectTransform, "Badge Label", "HP", 18, UiFactory.Alpha(UiFactory.Gold, 0.95f));
        UiFactory.Stretch(badgeLabel.rectTransform);

        RectTransform bar = UiRect(healthGroup, "Health Bar");
        UiFactory.Place(bar, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(66f, -2f), new Vector2(410f, 34f));

        Image barBack = UiFactory.CreateSliced(bar, "Back", UiFactory.KeyRound6, UiFactory.Alpha(UiFactory.Hex("0A0C12"), 0.95f));
        UiFactory.Stretch(barBack.rectTransform);

        Image delayedFill = UiFactory.CreateFilled(bar, "Delayed Fill", UiFactory.KeySlant, UiFactory.Alpha(UiFactory.GoldHot, 0.72f));
        UiFactory.Stretch(delayedFill.rectTransform, 4f);

        Image instantFill = UiFactory.CreateFilled(bar, "Instant Fill", UiFactory.KeySlant, UiFactory.Gold);
        UiFactory.Stretch(instantFill.rectTransform, 4f);

        Image flashFill = UiFactory.CreateImage(bar, "Flash", UiFactory.KeySlant, UiFactory.Alpha(Color.white, 0f));
        UiFactory.Stretch(flashFill.rectTransform, 4f);

        Image gloss = UiFactory.CreateImage(bar, "Gloss", UiFactory.KeyGradientV, UiFactory.Alpha(Color.white, 0.16f));
        UiFactory.Stretch(gloss.rectTransform, 5f);

        for (int i = 1; i <= 4; i++)
        {
            Image divider = UiFactory.CreateImage(bar, "Segment " + i, UiFactory.KeySlant, UiFactory.Alpha(UiFactory.Hex("0A0C12"), 0.8f));
            UiFactory.Place(divider.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(410f * i / 5f, 0f), new Vector2(3f, 28f));
        }

        Image barFrame = UiFactory.CreateSliced(bar, "Frame", UiFactory.KeyRound6Frame, UiFactory.Alpha(UiFactory.Gold, 0.9f));
        UiFactory.Stretch(barFrame.rectTransform);

        Text healthValue = UiFactory.CreateText(healthGroup, "Health Value",
            Mathf.CeilToInt(current) + " / " + Mathf.CeilToInt(maximum), 17,
            UiFactory.Alpha(UiFactory.TextPrimary, 0.95f), TextAnchor.MiddleLeft);
        UiFactory.Place(healthValue.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(68f, -44f), new Vector2(240f, 20f));

        // ---------- 左上第二行：宝石计数 ----------
        RectTransform gemGroup = UiRect(hudRect, "Gem Group");
        UiFactory.TopLeft(gemGroup, new Vector2(36f, -114f), new Vector2(320f, 44f));

        Image gemGlow = UiFactory.CreateImage(gemGroup, "Gem Glow", UiFactory.KeyGlow, UiFactory.Alpha(UiFactory.Gem, 0.28f));
        UiFactory.Place(gemGlow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-8f, -24f), new Vector2(60f, 60f));
        Image gemIcon = UiFactory.CreateImage(gemGroup, "Gem Icon", UiFactory.KeyRhombus, UiFactory.Gem);
        UiFactory.Place(gemIcon.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -6f), new Vector2(28f, 28f));
        Text gemValue = UiFactory.CreateText(gemGroup, "Gem Value", "0 / 6", 24, UiFactory.TextBright, TextAnchor.MiddleLeft);
        UiFactory.Place(gemValue.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -4f), new Vector2(120f, 32f));
        Text gemLabel = UiFactory.CreateText(gemGroup, "Gem Label", "遗迹宝石", 15, UiFactory.Alpha(UiFactory.TextDim, 0.95f), TextAnchor.MiddleLeft);
        UiFactory.Place(gemLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, -8f), new Vector2(120f, 24f));

        // ---------- 顶部中央：目标提示条 ----------
        RectTransform objectiveGroup = UiRect(hudRect, "Objective Group");
        UiFactory.TopCenter(objectiveGroup, new Vector2(0f, -26f), new Vector2(660f, 44f));
        CanvasGroup objectiveCanvasGroup = objectiveGroup.gameObject.AddComponent<CanvasGroup>();
        objectiveCanvasGroup.alpha = 0f;

        Image objectiveBack = UiFactory.CreateSliced(objectiveGroup, "Back", UiFactory.KeyRound12, UiFactory.Alpha(UiFactory.Hex("0B0D14"), 0.78f));
        UiFactory.Stretch(objectiveBack.rectTransform);
        Image objectiveAccent = UiFactory.CreateSliced(objectiveGroup, "Accent", UiFactory.KeyRound6, UiFactory.Alpha(UiFactory.Gold, 0.95f));
        UiFactory.Place(objectiveAccent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(5f, 22f));
        Image objectiveFrame = UiFactory.CreateSliced(objectiveGroup, "Frame", UiFactory.KeyRound12Frame, UiFactory.Alpha(UiFactory.Gold, 0.42f));
        UiFactory.Stretch(objectiveFrame.rectTransform);
        Text objectiveLabel = UiFactory.CreateText(objectiveGroup, "Objective Text", "激活 3 块符文石碑，开启北侧石门", 20,
            UiFactory.Hex("FFE2A8"));
        UiFactory.Stretch(objectiveLabel.rectTransform, 26f, 0f, 26f, 0f);
        UiFactory.AddOutline(objectiveLabel, UiFactory.Alpha(UiFactory.Hex("2A1506"), 0.85f), 1.2f);

        // ---------- 中央：准心 ----------
        RectTransform crosshair = UiRect(hudRect, "Crosshair");
        UiFactory.Center(crosshair, Vector2.zero, new Vector2(56f, 56f));

        Image crosshairRing = UiFactory.CreateImage(crosshair, "Ring", UiFactory.KeyRingThin, UiFactory.Alpha(Color.white, 0.55f));
        UiFactory.Stretch(crosshairRing.rectTransform);

        string[] tickNames = { "Top", "Bottom", "Left", "Right" };
        Vector2[] tickPositions = { new Vector2(0f, 13f), new Vector2(0f, -13f), new Vector2(-13f, 0f), new Vector2(13f, 0f) };
        Vector2[] tickSizes = { new Vector2(2f, 8f), new Vector2(2f, 8f), new Vector2(8f, 2f), new Vector2(8f, 2f) };
        for (int i = 0; i < tickNames.Length; i++)
        {
            Image tick = UiFactory.CreateImage(crosshair, "Tick " + tickNames[i], UiFactory.KeyWhite, UiFactory.Alpha(Color.white, 0.75f));
            UiFactory.Center(tick.rectTransform, tickPositions[i], tickSizes[i]);
        }
        Image crosshairDot = UiFactory.CreateImage(crosshair, "Dot", UiFactory.KeyDisc, UiFactory.Alpha(Color.white, 0.9f));
        UiFactory.Center(crosshairDot.rectTransform, Vector2.zero, new Vector2(4f, 4f));

        // ---------- 中央偏右：连击 ----------
        RectTransform comboGroup = UiRect(hudRect, "Combo Group");
        UiFactory.Center(comboGroup, new Vector2(300f, 118f), new Vector2(240f, 110f));
        CanvasGroup comboCanvasGroup = comboGroup.gameObject.AddComponent<CanvasGroup>();
        comboCanvasGroup.alpha = 0f;
        comboCanvasGroup.blocksRaycasts = false;

        RectTransform comboRoot = UiRect(comboGroup, "Combo Root");
        UiFactory.Stretch(comboRoot);
        Text comboValue = UiFactory.CreateText(comboRoot, "Combo Value", "x0", 64, UiFactory.Gold, TextAnchor.LowerRight);
        UiFactory.Stretch(comboValue.rectTransform, 0f, 18f, 0f, 0f);
        UiFactory.AddOutline(comboValue, UiFactory.Alpha(UiFactory.Hex("2A1506"), 0.9f), 2f);
        Text comboLabel = UiFactory.CreateText(comboRoot, "Combo Label", "连击", 16, UiFactory.Alpha(UiFactory.TextDim, 0.95f), TextAnchor.LowerRight);
        UiFactory.Stretch(comboLabel.rectTransform);

        // ---------- 右下：闪避冷却环 + 按键提示 ----------
        RectTransform dodgeGroup = UiRect(hudRect, "Dodge Group");
        UiFactory.BottomRight(dodgeGroup, new Vector2(-36f, 34f), new Vector2(178f, 162f));

        RectTransform cooldownRoot = UiRect(dodgeGroup, "Cooldown Root");
        UiFactory.Place(cooldownRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(46f, 0f), new Vector2(96f, 96f));

        Image ringBack = UiFactory.CreateImage(cooldownRoot, "Ring Back", UiFactory.KeyRing, UiFactory.Alpha(UiFactory.Hex("0A0C12"), 0.9f));
        UiFactory.Stretch(ringBack.rectTransform);
        Image cooldownFill = UiFactory.CreateFilled(cooldownRoot, "Ring Fill", UiFactory.KeyRing, UiFactory.Teal, true);
        UiFactory.Stretch(cooldownFill.rectTransform);
        Image ringCore = UiFactory.CreateImage(cooldownRoot, "Ring Core", UiFactory.KeyDisc, UiFactory.Alpha(UiFactory.Dark, 0.88f));
        UiFactory.Stretch(ringCore.rectTransform, 13f);
        Text cooldownLabel = UiFactory.CreateText(cooldownRoot, "Cooldown Text", "READY", 16, UiFactory.Teal);
        UiFactory.Stretch(cooldownLabel.rectTransform);

        RectTransform hintRoot = UiRect(dodgeGroup, "Hint Root");
        UiFactory.Place(hintRoot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -102f), new Vector2(168f, 56f));
        CanvasGroup hintGroup = hintRoot.gameObject.AddComponent<CanvasGroup>();

        RectTransform keyCap = UiRect(hintRoot, "Key Cap");
        UiFactory.Place(keyCap, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(80f, 30f));
        Image capBack = UiFactory.CreateSliced(keyCap, "Cap Back", UiFactory.KeyRound6Frame, UiFactory.Alpha(UiFactory.Gold, 0.85f));
        UiFactory.Stretch(capBack.rectTransform);
        Text capLabel = UiFactory.CreateText(keyCap, "Cap Label", "Q", 14, UiFactory.Hex("FFE2A8"));
        UiFactory.Stretch(capLabel.rectTransform);
        Text hintLabel = UiFactory.CreateText(hintRoot, "Hint Label", "闪避", 14, UiFactory.Alpha(UiFactory.TextDim, 0.95f), TextAnchor.MiddleLeft);
        UiFactory.Place(hintLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(88f, -3f), new Vector2(80f, 24f));

        // ---------- 底部操作说明 ----------
        Text controlHint = UiFactory.CreateText(hudRect, "Control Hint",
            "WASD 移动　Shift 冲刺　空格 跳跃　左键 攻击　Q 闪避　Tab 锁定　E 激活符文　R 复活　鼠标移动旋转视角　滚轮缩放　Esc 暂停",
            16, UiFactory.Alpha(UiFactory.TextDim, 0.9f));
        UiFactory.BottomCenter(controlHint.rectTransform, new Vector2(0f, 22f), new Vector2(1500f, 28f));

        // ---------- 受击红闪（暗角） ----------
        Image vignette = UiFactory.CreateImage(hudRect, "Damage Vignette", UiFactory.KeyVignette, UiFactory.Alpha(UiFactory.Blood, 0f));
        UiFactory.Stretch(vignette.rectTransform);

        // ---------- 飘字层 ----------
        RectTransform floatingLayer = UiRect(hudRect, "Floating Layer");
        UiFactory.Stretch(floatingLayer);

        // ---------- 拾取提示 ----------
        RectTransform pickupRoot = UiRect(hudRect, "Pickup Toast");
        UiFactory.BottomCenter(pickupRoot, new Vector2(0f, 96f), new Vector2(460f, 56f));
        CanvasGroup pickupGroup = pickupRoot.gameObject.AddComponent<CanvasGroup>();
        pickupGroup.alpha = 0f;

        Image pickupBack = UiFactory.CreateSliced(pickupRoot, "Back", UiFactory.KeyRound16, UiFactory.Alpha(UiFactory.Hex("0B0D14"), 0.84f));
        UiFactory.Stretch(pickupBack.rectTransform);
        Image pickupFrame = UiFactory.CreateSliced(pickupRoot, "Frame", UiFactory.KeyRound16Frame, UiFactory.Alpha(UiFactory.Gem, 0.75f));
        UiFactory.Stretch(pickupFrame.rectTransform);
        Image pickupIcon = UiFactory.CreateImage(pickupRoot, "Icon", UiFactory.KeyRhombus, UiFactory.Gem);
        UiFactory.Place(pickupIcon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(38f, 0f), new Vector2(22f, 22f));
        Text pickupText = UiFactory.CreateText(pickupRoot, "Text", "遗迹宝石 +1", 20, UiFactory.Alpha(Color.white, 0.96f), TextAnchor.MiddleLeft);
        UiFactory.Stretch(pickupText.rectTransform, 64f, 0f, 20f, 0f);

        // ---------- 锁定目标框 ----------
        RectTransform lockRoot = UiRect(hudRect, "Lock Frame");
        UiFactory.Center(lockRoot, Vector2.zero, new Vector2(64f, 64f));
        CanvasGroup lockGroup = lockRoot.gameObject.AddComponent<CanvasGroup>();
        lockGroup.alpha = 0f;
        lockGroup.blocksRaycasts = false;

        Image lockRing = UiFactory.CreateImage(lockRoot, "Ring", UiFactory.KeyRingThin, UiFactory.Alpha(UiFactory.Blood, 0.95f));
        UiFactory.Stretch(lockRing.rectTransform);
        for (int i = 0; i < 4; i++)
        {
            Image corner = UiFactory.CreateImage(lockRoot, "Corner " + i, UiFactory.KeyCorner, UiFactory.Alpha(UiFactory.Blood, 0.95f));
            Vector2 anchor = new Vector2(i % 2 == 0 ? 0f : 1f, i < 2 ? 1f : 0f);
            UiFactory.Place(corner.rectTransform, anchor, anchor, Vector2.zero, new Vector2(18f, 18f));
            corner.rectTransform.localScale = new Vector3(i % 2 == 0 ? 1f : -1f, i < 2 ? 1f : -1f, 1f);
        }
        Text lockLabel = UiFactory.CreateText(lockRoot, "Label", "锁定", 14, UiFactory.Alpha(UiFactory.Blood, 0.95f), TextAnchor.UpperCenter);
        UiFactory.Place(lockLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(120f, 20f));

        ui.healthFill = instantFill;
        ui.healthText = healthValue;
        ui.gemText = gemValue;
        ui.objectiveText = objectiveLabel;
        ui.damageOverlay = vignette;
        ui.floatingLayer = floatingLayer;
        ui.crosshairRoot = crosshair;

        // ---------- 动效组件接线 ----------
        SetPrivate(animator, "shakeRoot", healthGroup);
        SetPrivate(animator, "instantFill", instantFill);
        SetPrivate(animator, "delayedFill", delayedFill);
        SetPrivate(animator, "flashFill", flashFill);
        SetPrivate(animator, "frameImage", barFrame);
        SetPrivate(animator, "healthText", healthValue);
        SetPrivate(animator, "gemRoot", gemGroup);
        SetPrivate(animator, "gemText", gemValue);
        SetPrivate(animator, "gemIcon", gemIcon);
        SetPrivate(animator, "cooldownFill", cooldownFill);
        SetPrivate(animator, "cooldownText", cooldownLabel);
        SetPrivate(animator, "cooldownRoot", cooldownRoot);
        SetPrivate(animator, "comboGroup", comboCanvasGroup);
        SetPrivate(animator, "comboRoot", comboRoot);
        SetPrivate(animator, "comboValue", comboValue);
        SetPrivate(animator, "crosshairRoot", crosshair);
        SetPrivate(animator, "crosshairRing", crosshairRing);
        SetPrivate(animator, "crosshairDot", crosshairDot);

        SetPrivate(feedback, "pickupGroup", pickupGroup);
        SetPrivate(feedback, "pickupRoot", pickupRoot);
        SetPrivate(feedback, "pickupText", pickupText);
        SetPrivate(feedback, "pickupIcon", pickupIcon);
        SetPrivate(feedback, "objectiveGroup", objectiveCanvasGroup);
        SetPrivate(feedback, "objectiveText", objectiveLabel);
        SetPrivate(feedback, "objectiveAccent", objectiveAccent.rectTransform);
        SetPrivate(feedback, "lockGroup", lockGroup);
        SetPrivate(feedback, "lockRoot", lockRoot);
        SetPrivate(feedback, "lockLabel", lockLabel);
        SetPrivate(feedback, "worldCamera", Camera.main);
        SetPrivate(feedback, "hintGroup", hintGroup);
        SetPrivate(feedback, "hintKeyText", capLabel);
        SetPrivate(feedback, "hintLabelText", hintLabel);
        SetPrivate(feedback, "hintRoot", hintRoot);
    }
    // ==================== 菜单 ====================

    private static void BuildMenuCanvasV2(ref UiV2References ui)
    {
        Canvas canvas = UiCanvas("Menu Canvas", 20);

        // ---------- 开始面板 ----------
        RectTransform startPanel = UiPanelRoot(canvas.transform, "Start Panel");
        RectTransform startShine;
        RectTransform startCard = UiCard(startPanel, "Start Card", new Vector2(900f, 780f), out startShine);

        Image titleGlow = UiFactory.CreateImage(startCard, "Title Glow", UiFactory.KeyGlow, UiFactory.Alpha(UiFactory.Gold, 0.16f));
        UiFactory.Center(titleGlow.rectTransform, new Vector2(0f, 250f), new Vector2(920f, 260f));

        UiGradientTitle(startCard, "Title", "遗迹守卫", 78, new Vector2(0f, 250f), new Vector2(780f, 104f),
            UiFactory.Hex("FFF0D4"), UiFactory.Hex("FF9A4A"));

        UiLabel(startCard, "Subtitle", "交互界面 · 角色动画 · 有限状态机 AI", 19,
            UiFactory.Alpha(UiFactory.TextDim, 0.95f), new Vector2(0f, 168f), new Vector2(780f, 28f));
        UiDivider(startCard, 136f, 420f);

        Text body = UiLabel(startCard, "Body",
            "你是一名潜入古老遗迹的探险家。守卫在断壁残垣间巡逻，一旦发现入侵者就会追击。\n\n" +
            "激活散落的 3 块符文石碑，开启北侧石门；集齐全部 6 颗遗迹宝石即可完成试炼。\n\n" +
            "被守卫击倒时会消耗一枚复活符文，第二次倒下则试炼失败。",
            17, UiFactory.Alpha(UiFactory.TextPrimary, 0.92f), new Vector2(0f, 14f), new Vector2(720f, 220f));
        body.lineSpacing = 1.35f;

        Button startButton = UiFactory.CreateButton(startCard, "Start Button", "开始探索", new Vector2(320f, 68f), 26, out Text startLabel);
        UiFactory.Center(startButton.GetComponent<RectTransform>(), new Vector2(0f, -176f), new Vector2(320f, 68f));

        Text footNote = UiLabel(startCard, "Foot Note",
            "左键 攻击 · 空格 跳跃 · Q 闪避 · Tab 锁定 · E 激活符文 · R 复活 · Shift 冲刺 · 右键旋转视角 · Esc 暂停", 14,
            UiFactory.Alpha(UiFactory.TextDim, 0.85f), new Vector2(0f, -340f), new Vector2(820f, 24f));

        // ---------- 暂停面板 ----------
        RectTransform pausePanel = UiPanelRoot(canvas.transform, "Pause Panel");
        RectTransform pauseShine;
        RectTransform pauseCard = UiCard(pausePanel, "Pause Card", new Vector2(620f, 440f), out pauseShine);

        Text pauseTitle = UiLabel(pauseCard, "Title", "已 暂 停", 48, UiFactory.Gold, new Vector2(0f, 138f), new Vector2(560f, 70f));
        UiFactory.AddOutline(pauseTitle, UiFactory.Alpha(UiFactory.Hex("2A1506"), 0.9f), 2f);
        UiDivider(pauseCard, 90f, 320f);

        Button resumeButton = UiFactory.CreateButton(pauseCard, "Resume Button", "继续探索", new Vector2(300f, 62f), 24, out Text resumeLabel);
        UiFactory.Center(resumeButton.GetComponent<RectTransform>(), new Vector2(0f, 14f), new Vector2(300f, 62f));

        Button pauseRestartButton = UiFactory.CreateButton(pauseCard, "Pause Restart Button", "重新开始", new Vector2(300f, 58f), 22, out Text pauseRestartLabel);
        UiFactory.Center(pauseRestartButton.GetComponent<RectTransform>(), new Vector2(0f, -66f), new Vector2(300f, 58f));

        UiLabel(pauseCard, "Hint", "按 Esc 继续探索", 14, UiFactory.Alpha(UiFactory.TextDim, 0.9f), new Vector2(0f, -146f), new Vector2(520f, 24f));

        // ---------- 结算面板 ----------
        RectTransform endPanel = UiPanelRoot(canvas.transform, "End Panel");
        RectTransform endShine;
        RectTransform endCard = UiCard(endPanel, "End Card", new Vector2(800f, 620f), out endShine);

        Text endTitle = UiLabel(endCard, "End Title", "", 52, UiFactory.Gold, new Vector2(0f, 226f), new Vector2(720f, 76f));
        UiFactory.AddOutline(endTitle, UiFactory.Alpha(UiFactory.Hex("2A1506"), 0.9f), 2f);
        UiDivider(endCard, 168f, 380f);

        string[] statLabels = { "用时", "击杀", "宝石" };
        Text[] statValues = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 220f;
            statValues[i] = UiLabel(endCard, "Stat Value " + statLabels[i], "—", 34, UiFactory.Gold,
                new Vector2(x, 104f), new Vector2(200f, 44f));
            UiLabel(endCard, "Stat Label " + statLabels[i], statLabels[i], 14,
                UiFactory.Alpha(UiFactory.TextDim, 0.95f), new Vector2(x, 66f), new Vector2(200f, 24f));
        }

        Text endStats = UiLabel(endCard, "End Stats", "", 17, UiFactory.Alpha(UiFactory.TextPrimary, 0.9f),
            new Vector2(0f, -18f), new Vector2(660f, 130f));
        endStats.lineSpacing = 1.3f;

        Button endRestartButton = UiFactory.CreateButton(endCard, "End Restart Button", "再来一次", new Vector2(320f, 62f), 24, out Text endRestartLabel);
        UiFactory.Center(endRestartButton.GetComponent<RectTransform>(), new Vector2(0f, -230f), new Vector2(320f, 62f));

        startPanel.gameObject.SetActive(true);
        pausePanel.gameObject.SetActive(false);
        endPanel.gameObject.SetActive(false);

        ui.startPanel = startPanel.gameObject;
        ui.pausePanel = pausePanel.gameObject;
        ui.endPanel = endPanel.gameObject;
        ui.endTitleText = endTitle;
        ui.endStatsText = endStats;
        ui.startButton = startButton;
        ui.resumeButton = resumeButton;
        ui.pauseRestartButton = pauseRestartButton;
        ui.endRestartButton = endRestartButton;
        ui.startCard = startCard;
        ui.pauseCard = pauseCard;
        ui.endCard = endCard;
        ui.startShine = startShine;
        ui.pauseShine = pauseShine;
        ui.endShine = endShine;
        ui.endTimeValue = statValues[0];
        ui.endKillValue = statValues[1];
        ui.endGemValue = statValues[2];
    }

    // ==================== 音频面板（可折叠） ====================

    private static void BuildAudioCanvasV2(ref UiV2References ui)
    {
        Canvas canvas = UiCanvas("Audio Canvas", 10);

        RectTransform panel = UiRect(canvas.transform, "Audio Panel");
        UiFactory.TopRight(panel, new Vector2(-28f, -24f), new Vector2(316f, 318f));

        Image back = UiFactory.CreateSliced(panel, "Back", UiFactory.KeyRound12, UiFactory.Alpha(UiFactory.Hex("101320"), 0.95f), true);
        UiFactory.Stretch(back.rectTransform);
        Image headerGlow = UiFactory.CreateImage(panel, "Header Glow", UiFactory.KeyGradientV, UiFactory.Alpha(UiFactory.Gold, 0.12f));
        UiFactory.Place(headerGlow.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(316f, 96f));
        Image frame = UiFactory.CreateSliced(panel, "Frame", UiFactory.KeyRound12Frame, UiFactory.Alpha(UiFactory.Gold, 0.72f));
        UiFactory.Stretch(frame.rectTransform);

        RectTransform header = UiRect(panel, "Header");
        UiFactory.Place(header, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(316f, 58f));
        Text headerTitle = UiFactory.CreateText(header, "Title", "音频控制", 18, UiFactory.Gold, TextAnchor.MiddleLeft);
        UiFactory.Place(headerTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(180f, 26f));
        Text headerHint = UiFactory.CreateText(header, "Hint", "第 5 章 · 音频", 12, UiFactory.Alpha(UiFactory.TextDim, 0.85f), TextAnchor.MiddleLeft);
        UiFactory.Place(headerHint.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110f, 0f), new Vector2(140f, 22f));

        Button foldButton = UiFactory.CreateButton(header, "Fold Button", "", new Vector2(38f, 30f), 12, out Text foldLabel);
        UiFactory.Place(foldButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(38f, 30f));
        Image chevron = UiFactory.CreateImage(foldButton.GetComponent<RectTransform>(), "Chevron", UiFactory.KeyChevron, UiFactory.Dark);
        UiFactory.Center(chevron.rectTransform, Vector2.zero, new Vector2(16f, 10f));

        RectTransform content = UiRect(panel, "Content");
        UiFactory.Place(content, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(316f, 252f));
        CanvasGroup contentGroup = content.gameObject.AddComponent<CanvasGroup>();

        Button playButton = UiAudioButton(content, "Play Button", "播放", new Vector2(14f, -12f), 88f);
        Button pauseButton = UiAudioButton(content, "Pause Button", "暂停", new Vector2(114f, -12f), 88f);
        Button muteButton = UiAudioButton(content, "Mute Button", "静音", new Vector2(214f, -12f), 88f);
        Button aiToggleButton = UiAudioButton(content, "AI Toggle Button", "AI：开", new Vector2(14f, -58f), 138f);
        Button resetGuardButton = UiAudioButton(content, "Reset Guards Button", "复位守卫", new Vector2(162f, -58f), 138f);

        Text volumeLabel = UiFactory.CreateText(content, "Volume Label", "音量", 15, UiFactory.Alpha(UiFactory.TextPrimary, 0.9f), TextAnchor.MiddleLeft);
        UiFactory.Place(volumeLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -106f), new Vector2(80f, 22f));

        Slider volumeSlider = UiFactory.CreateSlider(content, "Volume Slider", new Vector2(288f, 26f), 0.45f);
        UiFactory.Place(volumeSlider.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -136f), new Vector2(288f, 26f));

        Text foldHint = UiFactory.CreateText(content, "Fold Hint", "折叠后 HUD 更清爽", 12, UiFactory.Alpha(UiFactory.TextDim, 0.8f));
        UiFactory.Place(foldHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(300f, 20f));

        UiPanelFold fold = panel.gameObject.AddComponent<UiPanelFold>();
        SetPrivate(fold, "foldButton", foldButton);
        SetPrivate(fold, "panel", panel);
        SetPrivate(fold, "content", content);
        SetPrivate(fold, "contentGroup", contentGroup);
        SetPrivate(fold, "chevron", chevron.rectTransform);

        ui.volumeSlider = volumeSlider;
        ui.playButton = playButton;
        ui.pauseButton = pauseButton;
        ui.muteButton = muteButton;
        ui.aiToggleButton = aiToggleButton;
        ui.resetGuardButton = resetGuardButton;
    }

    // ==================== 生成辅助 ====================

    private static Canvas UiCanvas(string name, int sortingOrder)
    {
        return CreateCanvas(name, sortingOrder);
    }

    private static RectTransform UiRect(Transform parent, string name)
    {
        return UiFactory.CreateRect(parent, name);
    }

    private static Text UiLabel(RectTransform parent, string name, string content, int size, Color color,
        Vector2 position, Vector2 dimensions)
    {
        Text text = UiFactory.CreateText(parent, name, content, size, color, TextAnchor.MiddleCenter);
        UiFactory.Center(text.rectTransform, position, dimensions);
        return text;
    }

    private static Button UiAudioButton(RectTransform parent, string name, string label, Vector2 position, float width)
    {
        Text ignored;
        Button button = UiFactory.CreateButton(parent, name, label, new Vector2(width, 36f), 16, out ignored);
        UiFactory.Place(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), position,
            new Vector2(width, 36f));
        return button;
    }

    /// <summary>整屏面板根：拉伸填满 + CanvasGroup + 暗化遮罩。</summary>
    private static RectTransform UiPanelRoot(Transform parent, string name)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        UiFactory.Stretch(rect);

        Image flat = UiFactory.CreateImage(rect, "Dim", UiFactory.KeyWhite, UiFactory.Alpha(UiFactory.Hex("08090F"), 0.82f), true);
        UiFactory.Stretch(flat.rectTransform);

        Image vignette = UiFactory.CreateImage(rect, "Dim Vignette", UiFactory.KeyVignette, UiFactory.Alpha(UiFactory.Hex("05060A"), 0.85f));
        UiFactory.Stretch(vignette.rectTransform);

        return rect;
    }

    /// <summary>带渐变底、金色边框、内描边、角花与扫光条的卡片。</summary>
    private static RectTransform UiCard(RectTransform parent, string name, Vector2 size, out RectTransform shine)
    {
        RectTransform card = UiRect(parent, name);
        UiFactory.Center(card, Vector2.zero, size);

        Image shadow = UiFactory.CreateSliced(card, "Shadow", UiFactory.KeyRound24, UiFactory.Alpha(UiFactory.Hex("04050A"), 0.55f));
        UiFactory.Center(shadow.rectTransform, new Vector2(0f, -14f), size + new Vector2(36f, 36f));

        Image back = UiFactory.CreateSliced(card, "Back", UiFactory.KeyRound24, UiFactory.Alpha(UiFactory.DarkCard, 0.98f), true);
        UiFactory.Center(back.rectTransform, Vector2.zero, size);

        Image topGlow = UiFactory.CreateImage(card, "Top Glow", UiFactory.KeyGradientV, UiFactory.Alpha(UiFactory.Gold, 0.10f));
        UiFactory.Place(topGlow.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f),
            new Vector2(size.x - 6f, size.y * 0.45f));

        Image innerLine = UiFactory.CreateSliced(card, "Inner Line", UiFactory.KeyRound16Frame, UiFactory.Alpha(UiFactory.Gold, 0.16f));
        UiFactory.Center(innerLine.rectTransform, Vector2.zero, size - new Vector2(20f, 20f));

        Image border = UiFactory.CreateSliced(card, "Frame", UiFactory.KeyRound24Frame, UiFactory.Alpha(UiFactory.Gold, 0.9f));
        UiFactory.Center(border.rectTransform, Vector2.zero, size);

        UiCardCorners(card);
        shine = UiShine(card, size);
        return card;
    }

    /// <summary>四角金色角花（用镜像而不是旋转，避免位移）。</summary>
    private static void UiCardCorners(RectTransform card)
    {
        Vector2[] anchors =
        {
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f)
        };
        for (int i = 0; i < anchors.Length; i++)
        {
            Image corner = UiFactory.CreateImage(card, "Corner " + i, UiFactory.KeyCorner, UiFactory.Alpha(UiFactory.Gold, 0.75f));
            Vector2 anchor = anchors[i];
            Vector2 position = new Vector2(anchor.x == 0f ? 12f : -12f, anchor.y == 1f ? -12f : 12f);
            UiFactory.Place(corner.rectTransform, anchor, anchor, position, new Vector2(34f, 34f));
            corner.rectTransform.localScale = new Vector3(anchor.x == 0f ? 1f : -1f, anchor.y == 1f ? 1f : -1f, 1f);
        }
    }

    /// <summary>斜向扫光条（挂在带 RectMask2D 的容器里，避免溢出卡片）。</summary>
    private static RectTransform UiShine(RectTransform card, Vector2 size)
    {
        RectTransform mask = UiRect(card, "Shine Mask");
        UiFactory.Center(mask, Vector2.zero, size - new Vector2(6f, 6f));
        mask.gameObject.AddComponent<RectMask2D>();

        RectTransform shine = UiRect(mask, "Shine");
        UiFactory.Center(shine, Vector2.zero, new Vector2(size.x * 0.42f, size.y * 1.5f));
        Image bar = UiFactory.CreateImage(shine, "Bar", UiFactory.KeyGradientH, UiFactory.Alpha(Color.white, 0.12f));
        UiFactory.Stretch(bar.rectTransform);
        shine.localRotation = Quaternion.Euler(0f, 0f, -18f);
        return shine;
    }

    /// <summary>分隔线 + 中心菱形。</summary>
    private static void UiDivider(RectTransform parent, float y, float width)
    {
        Image line = UiFactory.CreateImage(parent, "Divider", UiFactory.KeyWhite, UiFactory.Alpha(UiFactory.Gold, 0.5f));
        UiFactory.Center(line.rectTransform, new Vector2(0f, y), new Vector2(width, 1.5f));
        Image diamond = UiFactory.CreateImage(parent, "Divider Diamond", UiFactory.KeyRhombus, UiFactory.Gold);
        UiFactory.Center(diamond.rectTransform, new Vector2(0f, y), new Vector2(12f, 12f));
    }

    /// <summary>
    /// 渐变描边标题：底层深金全字 + 上层亮金顶部裁切带（RectMask2D），合起来读出竖向渐变。
    /// </summary>
    private static Text UiGradientTitle(RectTransform parent, string name, string content, int size,
        Vector2 position, Vector2 dimensions, Color topColor, Color bottomColor)
    {
        RectTransform area = UiRect(parent, name);
        UiFactory.Center(area, position, dimensions);

        Text bottom = UiFactory.CreateText(area, name + " Base", content, size, bottomColor);
        UiFactory.Stretch(bottom.rectTransform);
        UiFactory.AddOutline(bottom, UiFactory.Alpha(UiFactory.Hex("2A1506"), 0.95f), 2.5f);
        UiFactory.AddShadow(bottom, UiFactory.Alpha(Color.black, 0.5f), 4f);

        RectTransform band = UiRect(area, name + " Band");
        UiFactory.Place(band, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
            new Vector2(dimensions.x, dimensions.y * 0.46f));
        band.gameObject.AddComponent<RectMask2D>();

        Text top = UiFactory.CreateText(band, name + " Top", content, size, topColor);
        UiFactory.Place(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, dimensions);
        return bottom;
    }
}
