using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>游戏总控：菜单 / 游玩 / 胜利 / 失败状态机，统计数据，并转发所有音效与特效事件。</summary>
public sealed class GameManager : MonoBehaviour
{
    public enum GameState { Menu, Playing, Paused, Won, Lost }

    public static GameManager Instance { get; private set; }

    [Header("场景引用")]
    [SerializeField] private Health playerHealth;
    [SerializeField] private HUDController hud;
    [SerializeField] private GameMenuController menu;
    [SerializeField] private ProceduralAudioBank audioBank;

    private Collectible[] gems;
    private EnemyWanderAI[] guards;

    private GameState state = GameState.Menu;
    private int collectedGems;
    private int totalGems;
    private int killedGuards;
    private float playTime;
    private float endDelay;

    // 新战斗事件缓存：HUDController 目前没有连击 / 锁定 / 闪避控件，
    // 这里先缓存状态并提供只读属性，T5 的 UI 直接拉取即可
    private int comboCount;
    private float dodgeReadiness = 1f;
    private Transform lockTarget;
    private int hitCount;

    public GameState State { get { return state; } }
    public int CollectedGems { get { return collectedGems; } }
    public int TotalGems { get { return totalGems; } }
    public int KilledGuards { get { return killedGuards; } }
    public float PlayTime { get { return playTime; } }
    public int ComboCount { get { return comboCount; } }
    public float DodgeReadiness { get { return dodgeReadiness; } }
    public Transform LockTarget { get { return lockTarget; } }
    public int HitCount { get { return hitCount; } }

    private void Awake()
    {
        Instance = this;
        gems = FindObjectsOfType<Collectible>();
        guards = FindObjectsOfType<EnemyWanderAI>();
        totalGems = gems.Length;
    }

    private void OnEnable()
    {
        CombatEvents.ComboChanged += HandleComboChanged;
        CombatEvents.DodgeCooldown += HandleDodgeCooldown;
        CombatEvents.HitLanded += HandleHitLanded;
        CombatEvents.TargetChanged += HandleTargetChanged;
    }

    private void OnDisable()
    {
        CombatEvents.ComboChanged -= HandleComboChanged;
        CombatEvents.DodgeCooldown -= HandleDodgeCooldown;
        CombatEvents.HitLanded -= HandleHitLanded;
        CombatEvents.TargetChanged -= HandleTargetChanged;
    }

    private void Start()
    {
        if (playerHealth != null) playerHealth.Died += HandlePlayerDied;
        EnterMenu();
    }

    private void OnDestroy()
    {
        if (playerHealth != null) playerHealth.Died -= HandlePlayerDied;
    }

    private void Update()
    {
        if (state == GameState.Playing)
        {
            playTime += Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.Escape)) PauseGame();
        }
        else if (state == GameState.Paused && Input.GetKeyDown(KeyCode.Escape))
        {
            ResumeGame();
        }
    }

    private void EnterMenu()
    {
        state = GameState.Menu;
        Time.timeScale = 0f;
        if (menu != null) menu.ShowStartMenu();
        if (hud != null) hud.HideHud();
    }

    public void StartGame()
    {
        state = GameState.Playing;
        playTime = 0f;
        collectedGems = 0;
        killedGuards = 0;
        comboCount = 0;
        hitCount = 0;
        dodgeReadiness = 1f;
        lockTarget = null;
        Time.timeScale = 1f;
        if (menu != null) menu.HideAllPanels();
        if (hud != null)
        {
            hud.ShowHud();
            hud.RefreshHealth(playerHealth);
            hud.RefreshGems(collectedGems, totalGems);
            hud.SetObjective("激活 3 块符文石碑开启北门，集齐 6 颗遗迹宝石");
        }
        PlaySfx(ProceduralSfx.UiClick);
    }

    public void PauseGame()
    {
        state = GameState.Paused;
        Time.timeScale = 0f;
        if (menu != null) menu.ShowPauseMenu();
    }

    public void ResumeGame()
    {
        state = GameState.Playing;
        Time.timeScale = 1f;
        if (menu != null) menu.HideAllPanels();
        PlaySfx(ProceduralSfx.UiClick);
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        PlaySfx(ProceduralSfx.UiClick);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void HandlePlayerDied()
    {
        if (state != GameState.Playing) return;
        state = GameState.Lost;
        PlaySfx(ProceduralSfx.PlayerDeath);
        StartCoroutine(EndSequenceRoutine(false));
    }

    private IEnumerator EndSequenceRoutine(bool won)
    {
        yield return new WaitForSecondsRealtime(1.6f);
        Time.timeScale = 0f;
        if (won)
        {
            if (menu != null) menu.ShowWinMenu(playTime, killedGuards);
            PlaySfx(ProceduralSfx.Victory);
        }
        else
        {
            if (menu != null) menu.ShowLoseMenu(playTime, collectedGems, totalGems);
            PlaySfx(ProceduralSfx.Defeat);
        }
    }

    // ---- 各系统的事件转发 ----

    public void NotifyPlayerSwing()
    {
        PlaySfx(ProceduralSfx.Swing);
    }

    public void NotifyGuardSwing(Vector3 position)
    {
        PlaySfx(ProceduralSfx.Swing, 0.6f);
        if (hud != null) hud.SpawnSlashArc(position);
    }

    public void NotifyHit(Vector3 hitPoint, Health enemy, int damage)
    {
        PlaySfx(ProceduralSfx.Hit);
        if (hud != null)
        {
            hud.SpawnHitBurst(hitPoint, enemy != null && enemy.IsPlayer);
            hud.SpawnDamageNumber(hitPoint, damage.ToString());
        }
    }

    public void NotifyPlayerHurt(Vector3 position)
    {
        PlaySfx(ProceduralSfx.Hurt);
        if (hud != null)
        {
            hud.FlashDamageOverlay();
            hud.SpawnHitBurst(position, true);
            hud.RefreshHealth(playerHealth);
        }
    }

    public void NotifyGuardDied(Vector3 position)
    {
        killedGuards++;
        PlaySfx(ProceduralSfx.EnemyDeath);
        if (hud != null) hud.SpawnDeathBurst(position);
    }

    public void NotifyGemCollected(Vector3 position)
    {
        collectedGems++;
        PlaySfx(ProceduralSfx.Pickup);
        if (hud != null)
        {
            hud.RefreshGems(collectedGems, totalGems);
            hud.SpawnPickupBurst(position);
            if (collectedGems >= totalGems)
            {
                state = GameState.Won;
                StartCoroutine(EndSequenceRoutine(true));
            }
        }
    }

    // ---- 新战斗事件：缓存状态；命中音效/飘字仍走 NotifyHit，避免重复播放 ----

    private void HandleComboChanged(int combo)
    {
        comboCount = combo;
    }

    private void HandleDodgeCooldown(float readiness)
    {
        dodgeReadiness = readiness;
    }

    private void HandleHitLanded(Vector3 hitPoint, int damage)
    {
        hitCount++;
    }

    private void HandleTargetChanged(Transform target)
    {
        lockTarget = target;
    }

    public void PlaySfx(ProceduralSfx type, float volumeScale = 1f)
    {
        if (audioBank != null) audioBank.Play(type, volumeScale);
    }
}
