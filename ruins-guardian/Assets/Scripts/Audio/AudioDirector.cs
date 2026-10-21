using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 音频导演：单例。负责分层 BGM 的交叉淡化、威胁驱动的战斗强度、脚步节拍（按移动速度）、
/// 低血量心跳、环境层（风噪 + 远处钟声），并对外暴露战斗与 UI 可直接调用的播报接口。
///
/// 设计要点：
/// 1. 音量/播放暂停由既有的 BGM AudioSource（音频面板控制）当"传输载体"——
///    本组件把它的 mute 置真当哑载体，读它的 volume 当用户音量，读它的 isPlaying 当播放状态，
///    因此无需改动 AudioPanelController 就能让面板继续控制整条音乐总线。
/// 2. 三个音乐层各有自己的 AudioSource，音量 = 用户音量 × 层权重，逐帧插值完成交叉淡化。
/// 3. 所有时间推进都用 Time.unscaledDeltaTime，暂停（timeScale = 0）时音乐仍能正确淡变。
/// </summary>
public sealed class AudioDirector : MonoBehaviour
{
    /// <summary>低血量阈值：血量比例低于该值时开始心跳。</summary>
    public const float LowHealthThreshold = 0.3f;

    public static AudioDirector Instance { get; private set; }

    [Header("引用（可由生成器注入，也可运行时自动查找）")]
    [SerializeField] private ProceduralAudioBank bank;
    [SerializeField] private AudioSource masterBgmSource;
    [SerializeField] private Health playerHealth;
    [SerializeField] private Transform player;

    [Header("调音")]
    [SerializeField] private float exploreLayerVolume = 0.9f;
    [SerializeField] private float tensionLayerVolume = 0.95f;
    [SerializeField] private float ambientLayerVolume = 0.55f;
    [SerializeField] private float tensionFadeInSeconds = 1.6f;
    [SerializeField] private float tensionFadeOutSeconds = 3.5f;
    [SerializeField] private float endSegmentVolume = 0.8f;
    [SerializeField] private bool autoStartBgm = true;
    [SerializeField] private bool hookUiSounds = true;

    [Header("脚步")]
    [SerializeField] private float walkStepInterval = 0.62f;
    [SerializeField] private float runStepInterval = 0.34f;
    [SerializeField] private float footstepVolume = 0.55f;

    [Header("环境")]
    [SerializeField] private float bellIntervalMin = 22f;
    [SerializeField] private float bellIntervalMax = 44f;
    [SerializeField] private float bellVolume = 0.3f;

    private AudioSource exploreSource;
    private AudioSource tensionSource;
    private AudioSource ambientSource;
    private AudioSource endSource;

    private EnemyWanderAI[] guards = new EnemyWanderAI[0];
    private bool[] guardAlerted = new bool[0];
    private Health[] guardHealths = new Health[0];
    private float nextGuardRefresh;

    private AudioMusicMode musicMode = AudioMusicMode.Explore;
    private float combatIntensity;          // 平滑后的战斗强度 0..1
    private float combatOverride = -1f;     // >= 0 时外部接管强度
    private float musicDuck = 1f;           // 胜利/失败时整条音乐总线淡出
    private bool transportStarted;
    private bool layersPlaying;
    private bool layersAssigned;

    private Vector3 lastPlayerPosition;
    private float smoothedSpeed;
    private float stepTimer;
    private SurfaceKind surface = SurfaceKind.Stone;
    private float nextSurfaceCheck;

    private float heartbeatTimer;
    private float bellTimer;
    private float lastDodgeTime = -99f;
    private float nextReferenceRetry;
    private float lastUiClickTime = -99f;
    private bool sprinting;
    private bool victoryCuePlayed;
    private bool defeatCuePlayed;
    private GameManager.GameState lastGameState = GameManager.GameState.Menu;

    private enum SurfaceKind { Stone, Grass }

    // ================= 对外只读状态 =================

    /// <summary>当前战斗强度（0 = 平静，1 = 紧贴敌人且被发现）。</summary>
    public float CombatIntensity { get { return combatIntensity; } }
    /// <summary>最近存活守卫的平面距离（无存活守卫时为 float.MaxValue）。</summary>
    public float NearestThreatDistance { get; private set; }
    /// <summary>是否有守卫处于追击/攻击状态。</summary>
    public bool IsThreatAlerted { get; private set; }
    public AudioMusicMode MusicMode { get { return musicMode; } }
    /// <summary>玩家血量比例 0..1（无玩家时为 1）。</summary>
    public float PlayerHealthRatio { get; private set; }
    public bool IsLowHealth { get { return PlayerHealthRatio < LowHealthThreshold; } }
    /// <summary>当前脚下地表类型，供其他系统查询。</summary>
    public bool OnGrass { get { return surface == SurfaceKind.Grass; } }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        NearestThreatDistance = float.MaxValue;
        PlayerHealthRatio = 1f;
        exploreSource = CreateLayerSource("BGM Explore Layer");
        tensionSource = CreateLayerSource("BGM Tension Layer");
        ambientSource = CreateLayerSource("BGM Ambient Layer");
        endSource = CreateLayerSource("BGM End Segment");
        endSource.loop = false;
        // 生成器按层做淡入淡出的边界：先都静音，由 Update 拉起来
        if (exploreSource != null) exploreSource.volume = 0f;
        if (tensionSource != null) tensionSource.volume = 0f;
        if (ambientSource != null) ambientSource.volume = 0f;
    }

    private void Start()
    {
        EnsureReferences();
        TryAssignLayers();
        HookUiButtons();
        RefreshThreats();
        if (player != null) lastPlayerPosition = player.position;
        bellTimer = Random.Range(bellIntervalMin, bellIntervalMax);
        if (GameManager.Instance != null) lastGameState = GameManager.Instance.State;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private AudioSource CreateLayerSource(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    // ================= 绑定 =================

    /// <summary>由场景生成器或外部系统注入引用（可只传需要覆盖的项）。</summary>
    public void Bind(ProceduralAudioBank audioBank, AudioSource bgmSource, Health playerHealth_)
    {
        if (audioBank != null) bank = audioBank;
        if (bgmSource != null) masterBgmSource = bgmSource;
        if (playerHealth_ != null)
        {
            playerHealth = playerHealth_;
            player = playerHealth_.transform;
        }
        EnsureReferences();
    }

    private void EnsureReferences()
    {
        if (bank == null) bank = FindObjectOfType<ProceduralAudioBank>();
        if (playerHealth == null)
        {
            Health[] all = FindObjectsOfType<Health>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].IsPlayer) { playerHealth = all[i]; break; }
            }
        }
        if (player == null && playerHealth != null) player = playerHealth.transform;
        if (masterBgmSource == null && bank != null) masterBgmSource = bank.BgmSource;
    }

    /// <summary>BGM 是分帧合成的，等 ProceduralAudioBank 报告就绪后再把 clip 接上。</summary>
    private void TryAssignLayers()
    {
        if (bank == null || !bank.BgmReady) return;
        SetupLayers();
        layersAssigned = true;
        // clip 是后来才赋上的：强制下一帧重新同步一次播放状态，否则层源永远停在暂停
        layersPlaying = false;
    }

    private void SetupLayers()
    {
        if (bank == null) return;
        if (exploreSource != null) exploreSource.clip = bank.BgmExploreClip;
        if (tensionSource != null) tensionSource.clip = bank.BgmTensionClip;
        if (ambientSource != null) ambientSource.clip = bank.BgmAmbientClip;

        // 传输载体：既有 BGM AudioSource 继续承载"播放/暂停 + 用户音量"，
        // 但把它静音，避免探索层被播两遍。
        if (masterBgmSource != null)
        {
            if (masterBgmSource.clip == null) masterBgmSource.clip = bank.BgmExploreClip;
            masterBgmSource.loop = true;
            masterBgmSource.playOnAwake = false;
            masterBgmSource.mute = true;
        }
    }

    // ================= 主循环 =================

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;
        if ((bank == null || player == null) && Time.unscaledTime >= nextReferenceRetry)
        {
            nextReferenceRetry = Time.unscaledTime + 1f;
            EnsureReferences();
        }

        if (!layersAssigned) TryAssignLayers();
        TrackGameState();
        UpdateTransport(dt);
        UpdateThreat(dt);
        UpdateLayerVolumes(dt);
        UpdateFootsteps(dt);
        UpdateHeartbeat(dt);
        UpdateAmbient(dt);
    }

    /// <summary>跟随 GameManager 的状态机，自动切到胜利/失败音乐。</summary>
    private void TrackGameState()
    {
        GameManager manager = GameManager.Instance;
        if (manager == null) return;
        GameManager.GameState state = manager.State;
        if (state == lastGameState) return;
        lastGameState = state;
        if (state == GameManager.GameState.Won) PlayVictoryCue();
        else if (state == GameManager.GameState.Lost) PlayDefeatCue();
    }

    private void UpdateTransport(float dt)
    {
        if (masterBgmSource == null) return;

        if (autoStartBgm && !transportStarted)
        {
            transportStarted = true;
            if (!masterBgmSource.isPlaying) masterBgmSource.Play();
        }

        // 面板的播放 / 暂停直接决定层源是否发声
        bool shouldPlay = masterBgmSource.isPlaying;
        if (shouldPlay != layersPlaying)
        {
            layersPlaying = shouldPlay;
            SetLayerPlayState(exploreSource, shouldPlay);
            SetLayerPlayState(tensionSource, shouldPlay);
            SetLayerPlayState(ambientSource, shouldPlay);
        }
    }

    private static void SetLayerPlayState(AudioSource source, bool play)
    {
        if (source == null || source.clip == null) return;
        if (play)
        {
            if (!source.isPlaying) source.Play();
        }
        else if (source.isPlaying)
        {
            source.Pause();
        }
    }

    // ================= 战斗强度 =================

    /// <summary>外部强制设定战斗强度（&lt; 0 表示交还给自动计算）。</summary>
    public void SetCombatIntensityOverride(float value)
    {
        combatOverride = value < 0f ? -1f : Mathf.Clamp01(value);
    }

    /// <summary>守卫数量变化（生成、复活）后调用，重新收集威胁目标。</summary>
    public void RefreshThreats()
    {
        EnemyWanderAI[] previousGuards = guards;
        bool[] previousAlerted = guardAlerted;
        guards = FindObjectsOfType<EnemyWanderAI>();
        guardHealths = new Health[guards.Length];
        guardAlerted = new bool[guards.Length];
        for (int i = 0; i < guards.Length; i++)
        {
            if (guards[i] == null) continue;
            guardHealths[i] = guards[i].GetComponent<Health>();
            // 同一个守卫沿用上一帧的警觉状态，否则会吞掉"刚发现玩家"的报警音
            bool alerted = IsAlertState(guards[i]);
            if (previousGuards != null)
            {
                for (int k = 0; k < previousGuards.Length; k++)
                {
                    if (previousGuards[k] == guards[i]) { alerted = previousAlerted[k]; break; }
                }
            }
            guardAlerted[i] = alerted;
        }
        nextGuardRefresh = Time.unscaledTime + 1.5f;
    }

    private static bool IsAlertState(EnemyWanderAI guard)
    {
        if (guard == null) return false;
        EnemyWanderAI.State state = guard.CurrentState;
        return state == EnemyWanderAI.State.Chase || state == EnemyWanderAI.State.Attack;
    }

    private void UpdateThreat(float dt)
    {
        if (Time.unscaledTime >= nextGuardRefresh) RefreshThreats();

        float nearest = float.MaxValue;
        bool alerted = false;
        if (player != null)
        {
            for (int i = 0; i < guards.Length; i++)
            {
                EnemyWanderAI guard = guards[i];
                if (guard == null) continue;
                Health health = guardHealths[i];
                if (health != null && health.IsDead) continue;

                Vector3 delta = guard.transform.position - player.position;
                delta.y = 0f;
                float distance = delta.magnitude;
                if (distance < nearest) nearest = distance;

                bool alert = IsAlertState(guard);
                if (alert) alerted = true;
                // 守卫从"未发现"切到"追击"时播报警觉音（EnemyWanderAI 没有音频引用，由本组件补上）
                if (alert && !guardAlerted[i]) PlaySfx(ProceduralSfx.Alert, 0.7f);
                guardAlerted[i] = alert;
            }
        }

        NearestThreatDistance = nearest;
        IsThreatAlerted = alerted;

        float target = 0f;
        if (nearest < float.MaxValue)
        {
            // 16 米外几乎不紧张，3 米内拉满；被发现时额外加成
            float proximity = 1f - Mathf.Clamp01((nearest - 3f) / 13f);
            target = Mathf.Clamp01(proximity * (alerted ? 1f : 0.55f));
            if (alerted) target = Mathf.Max(target, 0.3f);
        }
        if (combatOverride >= 0f) target = combatOverride;
        if (musicMode == AudioMusicMode.Combat) target = Mathf.Max(target, 0.85f);
        if (musicMode == AudioMusicMode.Victory || musicMode == AudioMusicMode.Defeat) target = 0f;

        float rate = target > combatIntensity ? 1f / Mathf.Max(0.1f, tensionFadeInSeconds)
                                              : 1f / Mathf.Max(0.1f, tensionFadeOutSeconds);
        combatIntensity = Mathf.MoveTowards(combatIntensity, target, rate * dt);
    }

    private void UpdateLayerVolumes(float dt)
    {
        if (masterBgmSource == null) return;

        bool musicActive = musicMode != AudioMusicMode.Victory && musicMode != AudioMusicMode.Defeat;
        float duckTarget = musicActive ? 1f : 0f;
        musicDuck = Mathf.MoveTowards(musicDuck, duckTarget, dt / 0.9f);

        // 面板的音量滑杆与静音按钮都落在 volume 上（mute 被本组件占用作哑载体），所以统一读 volume
        float userVolume = Mathf.Clamp01(masterBgmSource.volume);

        float exploreWeight = exploreLayerVolume * (1f - 0.35f * combatIntensity) * musicDuck;
        float tensionWeight = tensionLayerVolume * combatIntensity * musicDuck;
        float ambientWeight = ambientLayerVolume * (1f - 0.2f * combatIntensity) * Mathf.Max(musicDuck, 0.35f);

        if (musicMode == AudioMusicMode.Silent)
        {
            exploreWeight = 0f;
            tensionWeight = 0f;
        }

        SmoothVolume(exploreSource, userVolume * exploreWeight, dt);
        SmoothVolume(tensionSource, userVolume * tensionWeight, dt);
        SmoothVolume(ambientSource, userVolume * ambientWeight, dt);
    }

    private static void SmoothVolume(AudioSource source, float target, float dt)
    {
        if (source == null) return;
        source.volume = Mathf.MoveTowards(source.volume, Mathf.Clamp01(target), dt * 2.2f);
    }

    // ================= 脚步 =================

    private void UpdateFootsteps(float dt)
    {
        if (player == null) return;

        Vector3 delta = player.position - lastPlayerPosition;
        delta.y = 0f;
        lastPlayerPosition = player.position;
        float speed = delta.magnitude / dt;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-dt * 12f));

        // 起跑瞬间补一记冲刺风声（PlayerExplorer 只是加速，没有专门的闪避事件）
        if (smoothedSpeed > 4.8f && !sprinting)
        {
            sprinting = true;
            if (Time.unscaledTime - lastDodgeTime > 1.5f) ReportDodge(player.position);
        }
        else if (smoothedSpeed < 3.2f)
        {
            sprinting = false;
        }

        if (smoothedSpeed < 0.6f)
        {
            stepTimer = 0f;
            return;
        }

        if (Time.unscaledTime >= nextSurfaceCheck) ProbeSurface();

        // 速度越快，步频越密：这正是"按移动速度控制间隔"
        float t = Mathf.InverseLerp(1.2f, 6.5f, smoothedSpeed);
        float interval = Mathf.Lerp(walkStepInterval, runStepInterval, t);
        stepTimer += dt;
        if (stepTimer < interval) return;
        stepTimer -= interval;
        if (stepTimer > interval) stepTimer = 0f;   // 长时间掉帧后不追赶，避免连响

        ProceduralSfx step = surface == SurfaceKind.Grass ? ProceduralSfx.FootstepGrass : ProceduralSfx.FootstepStone;
        float volume = footstepVolume * Mathf.Lerp(0.7f, 1.15f, t) * Random.Range(0.9f, 1.08f);
        PlaySfx(step, volume);
    }

    private void ProbeSurface()
    {
        nextSurfaceCheck = Time.unscaledTime + 0.2f;
        if (player == null) return;
        RaycastHit hit;
        if (!Physics.Raycast(player.position + Vector3.up * 0.6f, Vector3.down, out hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return;
        surface = DetectSurface(hit) ? SurfaceKind.Grass : SurfaceKind.Stone;
    }

    /// <summary>用碰撞体上的材质名判断地表：名字里带 grass/foliage 的算草地，其余算石材。</summary>
    private static bool DetectSurface(RaycastHit hit)
    {
        Collider collider = hit.collider;
        if (collider == null) return false;
        string tag = collider.gameObject.tag;
        if (!string.IsNullOrEmpty(tag) && tag.ToLowerInvariant() == "grass") return true;

        Renderer renderer = collider.GetComponent<Renderer>();
        if (renderer == null) renderer = collider.GetComponentInChildren<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null) return false;
        string name = renderer.sharedMaterial.name.ToLowerInvariant();
        return name.Contains("grass") || name.Contains("foliage") || name.Contains("moss");
    }

    // ================= 低血量心跳 =================

    private void UpdateHeartbeat(float dt)
    {
        if (playerHealth == null) return;
        float ratio = playerHealth.MaxHealth > 0f ? playerHealth.CurrentHealth / playerHealth.MaxHealth : 1f;
        PlayerHealthRatio = Mathf.Clamp01(ratio);

        if (playerHealth.IsDead || ratio >= LowHealthThreshold)
        {
            heartbeatTimer = 0f;
            return;
        }

        // 血量越低，心跳越急促：30% 时约 1 秒一次，0% 时约 0.5 秒一次
        float interval = Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(ratio / LowHealthThreshold));
        heartbeatTimer += dt;
        if (heartbeatTimer < interval) return;
        heartbeatTimer = 0f;
        float volume = Mathf.Lerp(0.9f, 0.45f, Mathf.Clamp01(ratio / LowHealthThreshold));
        PlaySfx(ProceduralSfx.Heartbeat, volume);
    }

    // ================= 环境层：远处钟声 =================

    private void UpdateAmbient(float dt)
    {
        if (musicMode == AudioMusicMode.Victory || musicMode == AudioMusicMode.Defeat) return;
        bellTimer -= dt;
        if (bellTimer > 0f) return;
        bellTimer = Random.Range(bellIntervalMin, bellIntervalMax);
        PlaySfx(ProceduralSfx.BellToll, bellVolume * Random.Range(0.75f, 1.1f));
    }

    // ================= 对外播报接口 =================

    /// <summary>直接转发一个音效（音量已在 bank 内做总控）。</summary>
    public void PlaySfx(ProceduralSfx type, float volumeScale = 1f)
    {
        if (bank != null) bank.Play(type, volumeScale);
    }

    /// <summary>播报挥击：heavy = 守卫战锤，否则玩家剑。</summary>
    public void ReportSwing(bool heavy = false)
    {
        PlaySfx(heavy ? ProceduralSfx.HammerSwing : ProceduralSfx.Swing, heavy ? 0.85f : 1f);
    }

    /// <summary>播报命中：fleshy = 血肉钝击，否则金属撞击（内部随机音高）。</summary>
    public void ReportHit(Vector3 point, bool fleshy = false, float volumeScale = 1f)
    {
        PlaySfx(fleshy ? ProceduralSfx.HitFlesh : ProceduralSfx.Hit, volumeScale);
    }

    /// <summary>播报闪避/冲刺风声。</summary>
    public void ReportDodge(Vector3 position)
    {
        if (Time.unscaledTime - lastDodgeTime < 0.35f) return;
        lastDodgeTime = Time.unscaledTime;
        PlaySfx(ProceduralSfx.Dodge, 0.85f);
    }

    /// <summary>播报宝石拾取（3D 定位，配合 HUD 的拾取特效）。</summary>
    public void ReportGemPickup(Vector3 position)
    {
        if (bank != null) bank.PlayAt(ProceduralSfx.Pickup, position, 0.9f);
    }

    /// <summary>播报符文激活。</summary>
    public void ReportRune(Vector3 position)
    {
        if (bank != null) bank.PlayAt(ProceduralSfx.RuneActivate, position, 0.9f);
    }

    /// <summary>播报开门 / 关门。</summary>
    public void ReportDoor(Vector3 position, bool open)
    {
        if (bank != null) bank.PlayAt(open ? ProceduralSfx.DoorOpen : ProceduralSfx.DoorClose, position, 0.85f);
    }

    public void ReportUiHover()
    {
        PlaySfx(ProceduralSfx.UiHover, 0.7f);
    }

    /// <summary>UI 点击：80 毫秒内的重复调用合并成一次，避免和 GameManager 自带的点击音叠成双响。</summary>
    public void ReportUiClick()
    {
        if (Time.unscaledTime - lastUiClickTime < 0.08f) return;
        lastUiClickTime = Time.unscaledTime;
        PlaySfx(ProceduralSfx.UiClick, 0.85f);
    }

    /// <summary>手动切换音乐模式（菜单 / 战斗 / 胜利 / 失败）。</summary>
    public void SetMusicMode(AudioMusicMode mode)
    {
        if (musicMode == mode) return;
        musicMode = mode;
        if (mode == AudioMusicMode.Victory) PlayVictoryCue();
        else if (mode == AudioMusicMode.Defeat) PlayDefeatCue();
    }

    /// <summary>播放胜利短段：音乐层淡出，另起一条播放带鼓点的胜利段。</summary>
    public void PlayVictoryCue()
    {
        if (victoryCuePlayed) return;
        victoryCuePlayed = true;
        defeatCuePlayed = true;
        musicMode = AudioMusicMode.Victory;
        PlayEndSegment(ProceduralSfx.Victory);
    }

    /// <summary>播放失败短段。</summary>
    public void PlayDefeatCue()
    {
        if (defeatCuePlayed) return;
        defeatCuePlayed = true;
        victoryCuePlayed = true;
        musicMode = AudioMusicMode.Defeat;
        PlayEndSegment(ProceduralSfx.Defeat);
    }

    private void PlayEndSegment(ProceduralSfx stinger)
    {
        if (bank == null) return;
        if (endSource == null) return;
        // 菜单短曲（Victory / Defeat 音效）由 GameManager 自己播，这里只负责起音乐段，避免三层叠响
        AudioClip segment = stinger == ProceduralSfx.Victory ? bank.BgmVictoryClip : bank.BgmDefeatClip;
        if (segment == null) return;
        float userVolume = masterBgmSource != null ? Mathf.Clamp01(masterBgmSource.volume) : 1f;
        endSource.Stop();
        endSource.clip = segment;
        endSource.loop = false;
        endSource.volume = endSegmentVolume * userVolume;
        endSource.Play();
    }

    /// <summary>静音 / 恢复整条音乐总线（等价于操作音频面板的静音按钮）。</summary>
    public void SetMusicBusVolume(float volume)
    {
        if (masterBgmSource == null) return;
        masterBgmSource.volume = Mathf.Clamp01(volume);
    }

    // ================= UI 自动接线 =================

    /// <summary>给场景里所有按钮挂上悬停音与点击音，不改动 UI 脚本本身（AddListener 是叠加而非替换）。</summary>
    private void HookUiButtons()
    {
        if (!hookUiSounds) return;
        Button[] buttons = FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null) continue;
            button.onClick.AddListener(ReportUiClick);

            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>();
            bool hasHover = false;
            for (int e = 0; e < trigger.triggers.Count; e++)
            {
                if (trigger.triggers[e] != null && trigger.triggers[e].eventID == EventTriggerType.PointerEnter) { hasHover = true; break; }
            }
            if (hasHover) continue;
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = EventTriggerType.PointerEnter;
            entry.callback.AddListener(HandleHover);
            trigger.triggers.Add(entry);
        }
    }

    private void HandleHover(BaseEventData data)
    {
        ReportUiHover();
    }
}
