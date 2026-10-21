using System.Collections.Generic;
using UnityEngine;

/// <summary>程序化音效类型。前 10 个是 v1 既有成员（GameManager 已按名字调用，顺序不可变），其后为 v2 新增。</summary>
public enum ProceduralSfx
{
    // ---- v1 既有成员：语义保留，音色已升级 ----
    Swing,          // 剑挥风（内部随机 3 个变体）
    Hit,            // 命中金属（内部随机 ±2 半音）
    Hurt,           // 玩家受击
    Pickup,         // 宝石拾取（明亮上行音阶）
    EnemyDeath,     // 守卫倒地
    PlayerDeath,    // 玩家死亡
    UiClick,        // UI 点击
    Alert,          // 守卫发现玩家
    Victory,        // 胜利短曲
    Defeat,         // 失败短曲

    // ---- v2 新增 ----
    FootstepStone,  // 石地脚步（3 个变体）
    FootstepGrass,  // 草地脚步（3 个变体）
    HammerSwing,    // 守卫战锤挥击
    HitFlesh,       // 命中血肉（低沉钝击）
    Dodge,          // 闪避冲刺风声
    RuneActivate,   // 符文激活
    DoorOpen,       // 石门开启
    DoorClose,      // 石门关闭
    UiHover,        // UI 悬停
    Heartbeat,      // 单次心跳（低血量时由 AudioDirector 循环触发）
    BellToll        // 远处钟声（环境层偶发点缀）
}

/// <summary>
/// 运行时程序化生成全部音效与分层背景音乐，零外部素材。
/// 音效通过 <see cref="Play"/> 以 2D 单声道播放；BGM 拆成探索 / 紧张 / 环境三层，
/// 由 <see cref="AudioDirector"/> 取走 clip 并做音量交叉淡化。
/// </summary>
public sealed class ProceduralAudioBank : MonoBehaviour
{
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private float sfxVolume = 0.8f;

    /// <summary>同一个枚举名下的随机变体（剑挥风 3 个、金属命中 5 个音高、脚步 3 个等）。</summary>
    private readonly Dictionary<ProceduralSfx, AudioClip[]> variants = new Dictionary<ProceduralSfx, AudioClip[]>();

    private AudioClip bgmExplore;
    private AudioClip bgmTension;
    private AudioClip bgmAmbient;
    private AudioClip bgmVictory;
    private AudioClip bgmDefeat;
    private bool bgmBuilt;

    /// <summary>探索层（低沉弦垫 + 稀疏旋律），也由 BGM AudioSource 承载，用作播放/暂停与总音量的传输载体。</summary>
    public AudioClip BgmExploreClip { get { return bgmExplore; } }
    /// <summary>紧张层（鼓点 + 琶音），随威胁渐入。</summary>
    public AudioClip BgmTensionClip { get { return bgmTension; } }
    /// <summary>环境层（风噪 + 远处钟声），常驻低音量。</summary>
    public AudioClip BgmAmbientClip { get { return bgmAmbient; } }
    public AudioClip BgmVictoryClip { get { return bgmVictory; } }
    public AudioClip BgmDefeatClip { get { return bgmDefeat; } }
    /// <summary>五段 BGM 是否已全部合成完毕（分帧生成，见 BuildBgmRoutine）。</summary>
    public bool BgmReady { get { return bgmBuilt; } }

    public AudioSource SfxSource { get { return sfxSource; } }
    public AudioSource BgmSource { get { return bgmSource; } }
    public float SfxVolume { get { return sfxVolume; } set { sfxVolume = Mathf.Clamp01(value); } }

    private void Awake()
    {
        EnsureSfxSource();
        BuildSfx();
    }

    private void Start()
    {
        if (!bgmBuilt) StartCoroutine(BuildBgmRoutine());
    }

    /// <summary>分帧合成 BGM：三层各约 12 秒立体声，一次性生成会在加载瞬间卡顿一秒以上。</summary>
    private System.Collections.IEnumerator BuildBgmRoutine()
    {
        bgmExplore = MusicFactory.BuildExploreLayer();
        yield return null;
        bgmTension = MusicFactory.BuildTensionLayer();
        yield return null;
        bgmAmbient = MusicFactory.BuildAmbientLayer();
        yield return null;
        bgmVictory = MusicFactory.BuildVictorySegment();
        bgmDefeat = MusicFactory.BuildDefeatSegment();
        bgmBuilt = true;

        if (bgmSource != null && bgmSource.clip == null)
        {
            bgmSource.clip = bgmExplore;
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
        }
    }

    private void EnsureSfxSource()
    {
        if (sfxSource == null)
        {
            sfxSource = gameObject.GetComponent<AudioSource>();
            if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        }
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
    }

    /// <summary>播放音效。同名多音色会在内部随机挑一个变体，避免重复感。</summary>
    public void Play(ProceduralSfx type, float volumeScale = 1f)
    {
        if (sfxSource == null) return;
        AudioClip clip = PickClip(type);
        if (clip == null) return;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * Mathf.Max(0f, volumeScale)));
    }

    /// <summary>在指定位置播放 3D 版本的音效（临时声源，适合宝石、开门等世界事件）。</summary>
    public void PlayAt(ProceduralSfx type, Vector3 position, float volumeScale = 1f)
    {
        AudioClip clip = PickClip(type);
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, Mathf.Clamp01(sfxVolume * Mathf.Max(0f, volumeScale)));
    }

    /// <summary>取某个音效的随机变体（供 AudioDirector / 战斗系统直接使用）。</summary>
    public AudioClip PickClip(ProceduralSfx type)
    {
        if (variants.Count == 0) BuildSfx();
        AudioClip[] list;
        if (!variants.TryGetValue(type, out list) || list == null || list.Length == 0) return null;
        if (list.Length == 1) return list[0];
        return list[Random.Range(0, list.Length)];
    }

    // ================= 生成 =================

    private void Register(ProceduralSfx type, params AudioClip[] clips)
    {
        variants[type] = clips;
    }

    private void BuildSfx()
    {
        if (variants.Count > 0) return;

        Register(ProceduralSfx.Swing, SfxFactory.BuildSwingVariants());
        Register(ProceduralSfx.Hit, SfxFactory.BuildMetalHitVariants());
        Register(ProceduralSfx.Hurt, SfxFactory.BuildPlayerHurt());
        Register(ProceduralSfx.Pickup, SfxFactory.BuildGemPickup());
        Register(ProceduralSfx.EnemyDeath, SfxFactory.BuildEnemyDeath());
        Register(ProceduralSfx.PlayerDeath, SfxFactory.BuildPlayerDeath());
        Register(ProceduralSfx.UiClick, SfxFactory.BuildUiClick());
        Register(ProceduralSfx.Alert, SfxFactory.BuildAlert());
        Register(ProceduralSfx.Victory, SfxFactory.BuildVictoryStinger());
        Register(ProceduralSfx.Defeat, SfxFactory.BuildDefeatStinger());

        Register(ProceduralSfx.FootstepStone, SfxFactory.BuildFootstepStoneVariants());
        Register(ProceduralSfx.FootstepGrass, SfxFactory.BuildFootstepGrassVariants());
        Register(ProceduralSfx.HammerSwing, SfxFactory.BuildHammerSwing());
        Register(ProceduralSfx.HitFlesh, SfxFactory.BuildFleshHit());
        Register(ProceduralSfx.Dodge, SfxFactory.BuildDodge());
        Register(ProceduralSfx.RuneActivate, SfxFactory.BuildRuneActivate());
        Register(ProceduralSfx.DoorOpen, SfxFactory.BuildDoorOpen());
        Register(ProceduralSfx.DoorClose, SfxFactory.BuildDoorClose());
        Register(ProceduralSfx.UiHover, SfxFactory.BuildUiHover());
        Register(ProceduralSfx.Heartbeat, SfxFactory.BuildHeartbeat());
        Register(ProceduralSfx.BellToll, SfxFactory.BuildBellToll());
    }

    /// <summary>需要立刻拿到 BGM 时同步合成（正常情况下走 Start 里的分帧协程）。</summary>
    public void EnsureBgm()
    {
        if (bgmBuilt) return;
        bgmBuilt = true;
        bgmExplore = MusicFactory.BuildExploreLayer();
        bgmTension = MusicFactory.BuildTensionLayer();
        bgmAmbient = MusicFactory.BuildAmbientLayer();
        bgmVictory = MusicFactory.BuildVictorySegment();
        bgmDefeat = MusicFactory.BuildDefeatSegment();
    }
}
