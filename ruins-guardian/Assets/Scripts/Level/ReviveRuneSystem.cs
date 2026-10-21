using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 复活符文：玩家第一次被击杀时不会真正失败——在 Health 的致命结算之前把它救回来，
/// 进入"倒地"状态（输入关闭、敌人暂停、慢动作），按 R 原地复活并消耗符文；
/// 第二次死亡不再拦截，走 GameManager 现有的失败流程。
/// 关键点：Health.TakeDamage 是「先触发 Damaged 事件、再判定 CurrentHealth &lt;= 0 触发 Died」，
/// 因此在 Damaged 回调里 Restore 可以合法地取消这一次死亡，且不修改 Health.cs。
/// </summary>
public sealed class ReviveRuneSystem : MonoBehaviour
{
    [SerializeField] private Health playerHealth;
    [SerializeField] private PlayerExplorer explorer;
    [SerializeField] private int reviveCharges = 1;
    [SerializeField] private float reviveInvulnerability = 1.5f;

    private bool downed;
    private Vector3 downPosition;
    private readonly List<EnemyWanderAI> pausedEnemies = new List<EnemyWanderAI>();

    public int RemainingCharges { get { return reviveCharges; } }
    public bool IsDowned { get { return downed; } }

    /// <summary>由生成器注入引用。</summary>
    public void Configure(Health health, PlayerExplorer playerExplorer, int charges)
    {
        playerHealth = health;
        explorer = playerExplorer;
        reviveCharges = charges;
    }

    private void OnEnable()
    {
        if (playerHealth != null) playerHealth.Damaged += HandleDamaged;
    }

    private void OnDisable()
    {
        if (playerHealth != null) playerHealth.Damaged -= HandleDamaged;
    }

    private void HandleDamaged(float amount, Vector3 direction)
    {
        if (downed || reviveCharges <= 0 || playerHealth == null) return;
        if (playerHealth.CurrentHealth > 0f) return;   // 只拦致命一击

        reviveCharges--;
        downed = true;
        downPosition = playerHealth.transform.position;

        // 赶在 TakeDamage 判定死亡之前救回来
        playerHealth.Restore();
        playerHealth.GrantInvulnerability(999f);

        if (explorer != null) explorer.SetInputEnabled(false);
        PauseEnemies(true);

        if (JuiceDirector.Instance != null)
        {
            JuiceDirector.Instance.SlowMotion(0.25f, 0.7f);
            JuiceDirector.Instance.AddTrauma(0.5f);
        }
        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.RuneActivate);

        HudBridge.SetObjective("你倒下了：按 R 使用复活符文");
    }

    private void Update()
    {
        if (!downed) return;
        if (Input.GetKeyDown(KeyCode.R)) Revive();
    }

    private void Revive()
    {
        if (playerHealth == null) return;

        downed = false;
        playerHealth.transform.position = downPosition;   // 原地复活
        playerHealth.Restore();                           // 清掉倒地期间的 999s 无敌
        playerHealth.GrantInvulnerability(reviveInvulnerability);

        if (explorer != null) explorer.SetInputEnabled(true);
        PauseEnemies(false);

        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.RuneActivate);
        HudBridge.SetObjective("复活符文已消耗：再次倒下将真正失败");
    }

    private void PauseEnemies(bool pause)
    {
        if (pause)
        {
            pausedEnemies.Clear();
            EnemyWanderAI[] all = Object.FindObjectsOfType<EnemyWanderAI>();
            for (int i = 0; i < all.Length; i++)
            {
                EnemyWanderAI enemy = all[i];
                if (enemy == null || !enemy.enabled) continue;          // 已被别的系统关掉的不动
                Health enemyHealth = enemy.GetComponent<Health>();
                if (enemyHealth != null && enemyHealth.IsDead) continue; // 尸体不要被重新启用
                pausedEnemies.Add(enemy);
                enemy.SetAIEnabled(false);
            }
        }
        else
        {
            for (int i = 0; i < pausedEnemies.Count; i++)
            {
                if (pausedEnemies[i] != null) pausedEnemies[i].SetAIEnabled(true);
            }
            pausedEnemies.Clear();
        }
    }
}
