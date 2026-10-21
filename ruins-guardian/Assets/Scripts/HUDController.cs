using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 抬头显示总控（视觉 V2）。
/// 对外保留 GameManager 依赖的全部 public 方法签名：
/// ShowHud / HideHud / RefreshHealth / RefreshGems / SetObjective / FlashDamageOverlay /
/// SpawnHitBurst / SpawnDeathBurst / SpawnPickupBurst / SpawnSlashArc / SpawnDamageNumber。
/// 内部把表现交给 HudAnimator（血条延迟掉血、抖动、滚动、连击、冷却环）、
/// UiFeedback（拾取提示、目标条、锁定框、按键提示）与 DamagePopup（伤害数字）。
/// 战斗状态一律通过 CombatEventBridge 订阅规范 5.4 的 CombatEvents，不直接引用战斗脚本。
/// </summary>
public sealed class HUDController : MonoBehaviour
{
    [Header("HUD 控件")]
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private Image healthFill;
    [SerializeField] private Text healthText;
    [SerializeField] private Text gemText;
    [SerializeField] private Text objectiveText;
    [SerializeField] private Image damageOverlay;
    [SerializeField] private RectTransform floatingTextLayer;

    [Header("飘字")]
    [SerializeField] private Font font;
    [SerializeField] private Camera worldCamera;

    [Header("V2 视觉驱动")]
    [SerializeField] private HudAnimator hudAnimator;
    [SerializeField] private UiFeedback feedback;
    [SerializeField] private RectTransform crosshairRoot;

    private ParticleSystem hitSparks;
    private ParticleSystem hurtSparks;
    private ParticleSystem deathDust;
    private ParticleSystem pickupSparkles;
    private float lastHealthRatio = 1f;
    private bool lastHitHostile;

    private void Awake()
    {
        hitSparks = CreateBurst(new Color(1f, 0.78f, 0.32f), 14, 4.2f, 0.1f, 0.45f, 0.35f);
        hurtSparks = CreateBurst(UiFactory.Blood, 16, 3.2f, 0.12f, 0.5f, 0.5f);
        deathDust = CreateBurst(new Color(0.62f, 0.58f, 0.5f), 26, 2.4f, 0.16f, 0.95f, 0.9f);
        pickupSparkles = CreateBurst(UiFactory.Gem, 20, 2.6f, 0.09f, 0.7f, 0.4f);

        if (damageOverlay != null) damageOverlay.color = UiFactory.Alpha(UiFactory.Blood, 0f);
        if (worldCamera == null) worldCamera = Camera.main;
        if (font == null) font = UiFactory.DefaultFont;
        if (hudAnimator == null) hudAnimator = GetComponentInChildren<HudAnimator>(true);
        if (feedback == null) feedback = GetComponentInChildren<UiFeedback>(true);
    }

    private void OnEnable()
    {
        CombatEventBridge.EnsureInitialized();
        CombatEventBridge.ComboChanged += HandleComboChanged;
        CombatEventBridge.DodgeCooldown += HandleDodgeCooldown;
        CombatEventBridge.HitLanded += HandleHitLanded;
        CombatEventBridge.TargetChanged += HandleTargetChanged;
    }

    private void OnDisable()
    {
        CombatEventBridge.ComboChanged -= HandleComboChanged;
        CombatEventBridge.DodgeCooldown -= HandleDodgeCooldown;
        CombatEventBridge.HitLanded -= HandleHitLanded;
        CombatEventBridge.TargetChanged -= HandleTargetChanged;
    }

    // ==================== 显隐 ====================

    public void ShowHud()
    {
        if (hudRoot != null) hudRoot.SetActive(true);
        if (feedback != null)
        {
            feedback.SetKeyHint("Q", "闪避");
            feedback.SetKeyHintHold(-1f);
        }
    }

    public void HideHud()
    {
        if (hudRoot != null) hudRoot.SetActive(false);
    }

    /// <summary>更换右下角按键提示（默认 SPACE / 闪避）。</summary>
    public void SetDodgeKeyHint(string key, string description)
    {
        if (feedback != null) feedback.SetKeyHint(key, description);
    }

    // ==================== 数值 ====================

    public void RefreshHealth(Health playerHealth)
    {
        if (playerHealth == null) return;
        float max = Mathf.Max(1f, playerHealth.MaxHealth);
        float ratio = Mathf.Clamp01(playerHealth.CurrentHealth / max);

        if (healthFill != null) healthFill.fillAmount = ratio;
        if (hudAnimator != null) hudAnimator.ApplyHealth(playerHealth.CurrentHealth, max);
        else if (healthText != null)
            healthText.text = Mathf.CeilToInt(playerHealth.CurrentHealth) + " / " + Mathf.CeilToInt(max);

        if (ratio < lastHealthRatio - 0.001f && hudAnimator != null) hudAnimator.PlayDamageShake();
        lastHealthRatio = ratio;
    }

    public void RefreshGems(int collected, int total)
    {
        if (gemText != null) gemText.text = collected + " / " + total;
        if (hudAnimator != null) hudAnimator.ApplyGems(collected, total);
    }

    public void SetObjective(string text)
    {
        if (objectiveText != null) objectiveText.text = text;
        if (feedback != null) feedback.SetObjective(text);
    }

    // ==================== 受击 ====================

    public void FlashDamageOverlay()
    {
        StartCoroutine(DamageOverlayRoutine());
    }

    private IEnumerator DamageOverlayRoutine()
    {
        if (damageOverlay == null) yield break;
        float duration = 0.55f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float alpha = t < 0.12f ? Mathf.Lerp(0f, 0.62f, t / 0.12f) : Mathf.Lerp(0.62f, 0f, (t - 0.12f) / 0.88f);
            damageOverlay.color = UiFactory.Alpha(UiFactory.Blood, alpha);
            yield return null;
        }
        damageOverlay.color = UiFactory.Alpha(UiFactory.Blood, 0f);
    }

    // ==================== 世界反馈 ====================

    /// <summary>命中特效。player 为 true 表示挨打的是玩家。</summary>
    public void SpawnHitBurst(Vector3 position, bool player)
    {
        lastHitHostile = player;
        PlayBurst(player ? hurtSparks : hitSparks, position);
        if (player)
        {
            if (hudAnimator != null) hudAnimator.PlayDamageShake();
        }
        else
        {
            if (hudAnimator != null) hudAnimator.PlayCrosshairHit();
            SpawnRingFlourish(position, UiFactory.Gold);
        }
    }

    public void SpawnDeathBurst(Vector3 position)
    {
        PlayBurst(deathDust, position);
        if (floatingTextLayer != null)
            DamagePopup.Spawn(floatingTextLayer, worldCamera, position + Vector3.up, "击倒", false, true);
    }

    public void SpawnPickupBurst(Vector3 position)
    {
        PlayBurst(pickupSparkles, position);
        if (feedback != null) feedback.ShowPickup("遗迹宝石 +1", UiFactory.Gem);
        SpawnRingFlourish(position, UiFactory.Gem);
    }

    /// <summary>守卫挥砍轨迹：面向相机的加法感光弧，放大并淡出。</summary>
    public void SpawnSlashArc(Vector3 guardPosition)
    {
        StartCoroutine(SlashArcRoutine(guardPosition));
    }

    /// <summary>世界坐标伤害数字。敌人挨打为白色 / 金色，玩家挨打为血红。</summary>
    public void SpawnDamageNumber(Vector3 worldPosition, string value)
    {
        if (floatingTextLayer == null) return;

        int parsed;
        int.TryParse(value, out parsed);
        bool critical = parsed >= 20;
        DamagePopup.Spawn(floatingTextLayer, worldCamera, worldPosition, value, lastHitHostile, critical);

        // 一次命中只影响紧接着弹出的这一个数字
        lastHitHostile = false;
    }

    /// <summary>只做屏幕环状扩散的命中烟花（会衰减，不用粒子）。</summary>
    private void SpawnRingFlourish(Vector3 worldPosition, Color tint)
    {
        if (floatingTextLayer == null) return;
        StartCoroutine(RingFlourishRoutine(worldPosition, tint));
    }

    private IEnumerator RingFlourishRoutine(Vector3 worldPosition, Color tint)
    {
        GameObject go = new GameObject("Hit Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(floatingTextLayer, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(96f, 96f);

        Image image = go.GetComponent<Image>();
        image.sprite = UiFactory.Get(UiFactory.KeyRing);
        image.raycastTarget = false;
        image.color = UiFactory.Alpha(tint, 0.9f);

        Camera camera = worldCamera != null ? worldCamera : Camera.main;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, worldPosition);
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(floatingTextLayer, screenPoint, null, out localPoint);
        rect.anchoredPosition = localPoint;

        float duration = 0.3f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(0.35f, 1.5f, t);
            rect.localScale = new Vector3(scale, scale, 1f);
            image.color = UiFactory.Alpha(tint, Mathf.Lerp(0.85f, 0f, t));
            yield return null;
        }
        Destroy(go);
    }

    private IEnumerator SlashArcRoutine(Vector3 position)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Slash Arc";
        Collider collider = quad.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        Material material = new Material(Shader.Find("Sprites/Default"));
        material.mainTexture = UiFactory.Get(UiFactory.KeyRing).texture;
        material.color = UiFactory.Alpha(UiFactory.Gold, 0.55f);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.material = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        quad.transform.position = position + Vector3.up * 0.9f;

        Camera camera = worldCamera != null ? worldCamera : Camera.main;
        float duration = 0.3f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float size = Mathf.Lerp(0.4f, 2.6f, t);
            quad.transform.localScale = new Vector3(size, size, 1f);
            if (camera != null) quad.transform.rotation = camera.transform.rotation;
            quad.transform.Rotate(Vector3.forward, 220f * Time.unscaledDeltaTime);
            material.color = UiFactory.Alpha(UiFactory.Gold, Mathf.Lerp(0.6f, 0f, t));
            yield return null;
        }
        Destroy(quad);
        Destroy(material);
    }

    private void PlayBurst(ParticleSystem system, Vector3 position)
    {
        if (system == null) return;
        system.transform.position = position;
        system.Play();
    }

    // ==================== 战斗事件（规范 5.4） ====================

    private void HandleComboChanged(int combo)
    {
        if (hudAnimator != null) hudAnimator.SetCombo(combo);
    }

    private void HandleDodgeCooldown(float readiness)
    {
        if (hudAnimator != null) hudAnimator.SetDodgeReadiness(readiness);
        if (readiness >= 0.999f && feedback != null) feedback.PulseKeyHint();
    }

    private void HandleHitLanded(Vector3 point, int damage)
    {
        // 伤害数字由 GameManager.NotifyHit -> SpawnDamageNumber 负责，这里只补准心与烟花反馈
        if (hudAnimator != null) hudAnimator.PlayCrosshairHit();
        SpawnRingFlourish(point, UiFactory.GoldHot);
    }

    private void HandleTargetChanged(Transform target)
    {
        if (feedback != null) feedback.SetLockTarget(target);
    }

    // ==================== 程序化粒子 ====================

    private ParticleSystem CreateBurst(Color color, int count, float speed, float size, float life, float radius)
    {
        GameObject go = new GameObject("Burst " + color);
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startColor = color;
        main.startSpeed = speed;
        main.startSize = size;
        main.startLifetime = life;
        main.gravityModifier = 1.1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.burstCount = 1;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)count));

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;

        ParticleSystem.ColorOverLifetimeModule colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(color, 0f), new GradientColorKey(color * 0.35f, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;

        ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

        return ps;
    }
}
