using UnityEngine;

/// <summary>
/// 全局手感导演：顿帧（hit stop）、击杀慢动作、trauma 模型屏震。
/// 时间缩放的归属：游戏暂停 / 菜单 / 结算期间本组件完全不写 Time.timeScale，
/// 由 GameManager 独占（Time.timeScale = 0）；恢复游玩后按当前手感请求接管。
/// 所有计时都用 unscaledDeltaTime，因此顿帧本身不会被自己减速。
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class JuiceDirector : MonoBehaviour
{
    [Header("顿帧")]
    [SerializeField] private float hitStopScale = 0.05f;
    [SerializeField] private float hitStopDuration = 0.08f;
    [SerializeField] private float hitStopRecover = 0.06f;

    [Header("击杀慢动作")]
    [SerializeField] private float slowMotionScale = 0.25f;
    [SerializeField] private float slowMotionDuration = 0.2f;
    [SerializeField] private float slowMotionRecover = 0.18f;

    [Header("屏震")]
    [SerializeField] private float traumaDecayPerSecond = 1.5f;
    [SerializeField] private float maxTrauma = 1f;
    [SerializeField] private float shakeFrequency = 22f;

    private static JuiceDirector instance;

    private float hitStopTimer;
    private float hitStopRecoverTimer;
    private float hitStopActiveScale;
    private float slowTimer;
    private float slowRecoverTimer;
    private float slowActiveScale;
    private float lastAppliedScale = -1f;

    /// <summary>全局实例；不存在时在播放模式下自动创建一个（场景生成器不必接线）。</summary>
    public static JuiceDirector Instance
    {
        get
        {
            if (instance == null && Application.isPlaying) instance = CreateInstance();
            return instance;
        }
    }

    /// <summary>受击震动的累积值 0..1，由 AddTrauma 累加、每帧衰减。</summary>
    public float Trauma { get; private set; }

    /// <summary>当前是否处于顿帧中。</summary>
    public bool IsHitStopping { get { return hitStopTimer > 0f; } }

    private static JuiceDirector CreateInstance()
    {
        GameObject go = new GameObject("Juice Director");
        return go.AddComponent<JuiceDirector>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        hitStopActiveScale = hitStopScale;
        slowActiveScale = slowMotionScale;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        // 只回收自己写进去的缩放，别覆盖 GameManager 的暂停
        if (lastAppliedScale > 0f && Mathf.Approximately(Time.timeScale, lastAppliedScale))
            Time.timeScale = 1f;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        Trauma = Mathf.Max(0f, Trauma - traumaDecayPerSecond * dt);

        if (IsExternallyPaused()) return;

        if (hitStopTimer > 0f)
        {
            hitStopTimer -= dt;
            if (hitStopTimer <= 0f) hitStopRecoverTimer = hitStopRecover;
        }
        else if (hitStopRecoverTimer > 0f)
        {
            hitStopRecoverTimer = Mathf.Max(0f, hitStopRecoverTimer - dt);
        }

        if (slowTimer > 0f)
        {
            slowTimer -= dt;
            if (slowTimer <= 0f) slowRecoverTimer = slowMotionRecover;
        }
        else if (slowRecoverTimer > 0f)
        {
            slowRecoverTimer = Mathf.Max(0f, slowRecoverTimer - dt);
        }

        float scale = 1f;
        if (hitStopTimer > 0f) scale = Mathf.Min(scale, hitStopActiveScale);
        else if (hitStopRecoverTimer > 0f)
            scale = Mathf.Min(scale, RecoverScale(hitStopActiveScale, hitStopRecoverTimer, hitStopRecover));

        if (slowTimer > 0f) scale = Mathf.Min(scale, slowActiveScale);
        else if (slowRecoverTimer > 0f)
            scale = Mathf.Min(scale, RecoverScale(slowActiveScale, slowRecoverTimer, slowMotionRecover));

        Time.timeScale = scale;
        lastAppliedScale = scale;
    }

    /// <summary>顿帧：压到极低时间缩放，短暂保持后平滑恢复。</summary>
    public void HitStop()
    {
        HitStop(hitStopScale, hitStopDuration);
    }

    /// <summary>自定义强度的顿帧（scale 建议 0.03~0.08）。</summary>
    public void HitStop(float scale, float duration)
    {
        if (duration <= 0f) return;
        hitStopActiveScale = Mathf.Clamp(scale, 0.01f, 0.9f);
        hitStopTimer = Mathf.Max(hitStopTimer, duration);
        hitStopRecoverTimer = 0f;
    }

    /// <summary>击杀慢动作（默认 0.25x 持续 0.2s）。</summary>
    public void SlowMotion()
    {
        SlowMotion(slowMotionScale, slowMotionDuration);
    }

    /// <summary>自定义慢动作。</summary>
    public void SlowMotion(float scale, float duration)
    {
        if (duration <= 0f) return;
        slowActiveScale = Mathf.Clamp(scale, 0.05f, 0.9f);
        slowTimer = Mathf.Max(slowTimer, duration);
        slowRecoverTimer = 0f;
    }

    /// <summary>累加受击震动（0..1）。</summary>
    public void AddTrauma(float amount)
    {
        if (amount <= 0f) return;
        Trauma = Mathf.Clamp(Trauma + amount, 0f, maxTrauma);
    }

    /// <summary>立即清空震动。</summary>
    public void ClearTrauma()
    {
        Trauma = 0f;
    }

    /// <summary>按 trauma² 曲线给出位移型屏震偏移，供相机叠加。</summary>
    public Vector3 GetShakeOffset(float amplitude)
    {
        if (Trauma <= 0f || amplitude <= 0f) return Vector3.zero;
        float strength = Trauma * Trauma;
        float t = Time.unscaledTime * shakeFrequency;
        float x = Mathf.PerlinNoise(0.37f, t) * 2f - 1f;
        float y = Mathf.PerlinNoise(3.71f, t) * 2f - 1f;
        float z = Mathf.PerlinNoise(7.13f, t) * 2f - 1f;
        return new Vector3(x, y, z) * (amplitude * strength);
    }

    /// <summary>按 trauma² 曲线给出轻微 roll（度），供相机叠加。</summary>
    public float GetShakeRoll(float maxDegrees)
    {
        if (Trauma <= 0f || maxDegrees <= 0f) return 0f;
        float strength = Trauma * Trauma;
        float noise = Mathf.PerlinNoise(11.9f, Time.unscaledTime * shakeFrequency * 0.5f) * 2f - 1f;
        return noise * maxDegrees * strength;
    }

    private static bool IsExternallyPaused()
    {
        GameManager manager = GameManager.Instance;
        return manager != null && manager.State != GameManager.GameState.Playing;
    }

    private static float RecoverScale(float from, float remaining, float duration)
    {
        if (duration <= 0f) return 1f;
        float t = 1f - Mathf.Clamp01(remaining / duration);
        return Mathf.Lerp(from, 1f, t * t);
    }
}
