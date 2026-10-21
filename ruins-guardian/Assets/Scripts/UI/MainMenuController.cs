using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 主界面（运行期程序化生成，不需要重新生成场景）：
/// 深色半透明底 + 游戏标题「遗迹守卫」+「开始游戏」「退出游戏」。
/// 场景加载后自动出现，并压住旧的开始面板与 HUD；点开始才进入游戏。
/// 全部动画使用 unscaledDeltaTime（此时 Time.timeScale 可能是 0）。
/// </summary>
public sealed class MainMenuController : MonoBehaviour
{
    private const int CanvasOrder = 50;

    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform titleRoot;
    private RectTransform buttonRoot;
    private GameMenuController gameMenu;
    private HUDController hud;
    private bool visible;
    private Coroutine fadeRoutine;
    private float hideTimer = 0.3f;
    private bool built;

    /// <summary>场景加载后自动挂载主界面（不需要改生成脚本，也不需要改 GameManager）。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject host = new GameObject("Main Menu (Runtime)");
        host.AddComponent<MainMenuController>();
    }

    private void Awake()
    {
        // 防重复：同一时刻只允许一个主界面
        MainMenuController[] all = Object.FindObjectsOfType<MainMenuController>();
        if (all.Length > 1)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != this) Destroy(all[i].gameObject);
            }
        }

        UiFonts.ApplyAll();
        Build();
    }

    private void Start()
    {
        gameMenu = Object.FindObjectOfType<GameMenuController>();
        hud = Object.FindObjectOfType<HUDController>();
        // 挂到 GameManager 节点下，随场景一起卸载；找不到就留作根对象
        if (GameManager.Instance != null) transform.SetParent(GameManager.Instance.transform, true);
        Show();
    }

    private void Update()
    {
        if (!visible) return;
        // 主界面期间压住旧的开始面板与 HUD（GameManager.Start 里的 EnterMenu 会重新打开它们）。
        // 节流 0.3 秒，避免每帧重启淡出协程。
        hideTimer -= Time.unscaledDeltaTime;
        if (hideTimer > 0f) return;
        hideTimer = 0.3f;
        if (gameMenu != null) gameMenu.HideAllPanels();
        if (hud != null) hud.HideHud();
    }

    // ==================== 构建 ====================

    private void Build()
    {
        if (built) return;
        built = true;

        GameObject canvasObject = new GameObject("Main Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasOrder; // 高于菜单画布(20)与音频画布(10)
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = canvasObject.GetComponent<CanvasGroup>();

        RectTransform root = canvasObject.GetComponent<RectTransform>();

        // 深色半透明底
        Image backdrop = UiFactory.CreateImage(root, "Backdrop", UiFactory.KeyWhite, UiFactory.Alpha(UiFactory.Hex("08090F"), 0.92f), true);
        UiFactory.Stretch(backdrop.rectTransform);
        Image vignette = UiFactory.CreateImage(root, "Backdrop Vignette", UiFactory.KeyVignette, UiFactory.Alpha(UiFactory.Hex("04050A"), 0.85f));
        UiFactory.Stretch(vignette.rectTransform);

        // 标题
        titleRoot = UiFactory.CreateRect(root, "Title Group");
        UiFactory.Center(titleRoot, new Vector2(0f, 176f), new Vector2(900f, 260f));

        Image glow = UiFactory.CreateImage(titleRoot, "Title Glow", UiFactory.KeyGlow, UiFactory.Alpha(UiFactory.Gold, 0.18f));
        UiFactory.Center(glow.rectTransform, new Vector2(0f, 10f), new Vector2(820f, 300f));

        Text title = UiFactory.CreateText(titleRoot, "Title", "遗迹守卫", 92, UiFactory.Gold);
        UiFactory.Center(title.rectTransform, new Vector2(0f, 26f), new Vector2(820f, 130f));
        UiFonts.Polish(title);
        title.fontSize = 92;
        UiFactory.AddOutline(title, UiFactory.Alpha(UiFactory.Hex("2A1506"), 0.95f), 3f);
        UiFactory.AddShadow(title, UiFactory.Alpha(Color.black, 0.6f), 5f);

        Text subtitle = UiFactory.CreateText(titleRoot, "Subtitle", "R U I N S   G U A R D I A N", 22, UiFactory.TextDim);
        UiFactory.Center(subtitle.rectTransform, new Vector2(0f, -52f), new Vector2(820f, 30f));

        Image divider = UiFactory.CreateImage(titleRoot, "Divider", UiFactory.KeyWhite, UiFactory.Alpha(UiFactory.Gold, 0.55f));
        UiFactory.Center(divider.rectTransform, new Vector2(0f, -84f), new Vector2(420f, 1.5f));
        Image diamond = UiFactory.CreateImage(titleRoot, "Divider Diamond", UiFactory.KeyRhombus, UiFactory.Gold);
        UiFactory.Center(diamond.rectTransform, new Vector2(0f, -84f), new Vector2(14f, 14f));

        // 按钮
        buttonRoot = UiFactory.CreateRect(root, "Button Group");
        UiFactory.Center(buttonRoot, new Vector2(0f, -190f), new Vector2(420f, 220f));

        Button startButton = UiFactory.CreateButton(buttonRoot, "Start Button", "开 始 游 戏", new Vector2(360f, 76f), 28);
        UiFactory.Center(startButton.GetComponent<RectTransform>(), new Vector2(0f, 40f), new Vector2(360f, 76f));
        startButton.onClick.AddListener(HandleStart);

        Button quitButton = UiFactory.CreateButton(buttonRoot, "Quit Button", "退 出 游 戏", new Vector2(360f, 64f), 22);
        UiFactory.Center(quitButton.GetComponent<RectTransform>(), new Vector2(0f, -52f), new Vector2(360f, 64f));
        quitButton.onClick.AddListener(HandleQuit);
        // 退出按钮用低饱和的暗色底，避免和主按钮抢视觉
        Image quitBackground = quitButton.GetComponent<Image>();
        if (quitBackground != null) quitBackground.color = UiFactory.Alpha(UiFactory.ColdLight, 0.85f);
        Text quitLabel = quitButton.GetComponentInChildren<Text>();
        if (quitLabel != null) quitLabel.color = UiFactory.TextPrimary;

        Text hint = UiFactory.CreateText(root, "Hint", "空格 跳跃　Q 闪避　左键 攻击　Tab 锁定　E 激活符文　R 复活　Esc 暂停", 16, UiFactory.Alpha(UiFactory.TextDim, 0.9f));
        UiFactory.BottomCenter(hint.rectTransform, new Vector2(0f, 40f), new Vector2(1400f, 28f));

        Text version = UiFactory.CreateText(root, "Version", "程序化生成 · 无外部素材", 13, UiFactory.Alpha(UiFactory.TextDim, 0.75f), TextAnchor.LowerRight);
        UiFactory.Place(version.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 22f), new Vector2(400f, 22f));
    }

    // ==================== 显示 / 隐藏 ====================

    /// <summary>显示主界面。</summary>
    public void Show()
    {
        if (!built) Build();
        visible = true;
        hideTimer = 0.3f;
        group.alpha = 0f;
        group.blocksRaycasts = true;
        group.interactable = true;
        canvas.gameObject.SetActive(true);
        if (hud != null) hud.HideHud();
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeInRoutine());
    }

    /// <summary>隐藏主界面。</summary>
    public void Hide()
    {
        visible = false;
        if (group != null)
        {
            group.blocksRaycasts = false;
            group.interactable = false;
        }
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeOutRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        float duration = 0.28f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            group.alpha = t;
            float scale = Mathf.Lerp(0.94f, 1f, EaseOutBack(t));
            if (titleRoot != null) titleRoot.localScale = new Vector3(scale, scale, 1f);
            if (buttonRoot != null)
            {
                float buttonScale = Mathf.Lerp(0.96f, 1f, t);
                buttonRoot.localScale = new Vector3(buttonScale, buttonScale, 1f);
            }
            yield return null;
        }
        group.alpha = 1f;
        if (titleRoot != null) titleRoot.localScale = Vector3.one;
        if (buttonRoot != null) buttonRoot.localScale = Vector3.one;
        fadeRoutine = null;
    }

    private IEnumerator FadeOutRoutine()
    {
        float duration = 0.18f;
        float elapsed = 0f;
        float startAlpha = group != null ? group.alpha : 1f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(startAlpha, 0f, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        group.alpha = 0f;
        if (canvas != null) canvas.gameObject.SetActive(false);
        fadeRoutine = null;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // ==================== 按钮 ====================

    private void HandleStart()
    {
        if (!visible) return; // 防连点：已经在淡出时忽略重复点击
        Hide();
        if (gameMenu != null) gameMenu.HideAllPanels();
        if (hud != null) hud.ShowHud();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartGame();
        }
        else
        {
            // 没有 GameManager 时至少让游戏跑起来，避免卡在 timeScale = 0
            Time.timeScale = 1f;
        }
    }

    private void HandleQuit()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
