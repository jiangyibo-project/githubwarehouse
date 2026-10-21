using UnityEngine;

/// <summary>
/// 波次与补给调度：
///   1) 每拾取一颗宝石回复 15 HP（Health.Heal）；
///   2) 拾取第 3 颗宝石时，南侧拱门涌入 2 名远程哨兵（Sentinel）。
/// 只读 GameManager.CollectedGems，不改 GameManager / Collectible，避免动到别人的模块。
/// </summary>
public sealed class WaveDirector : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Health playerHealth;
    [SerializeField] private float healPerGem = 15f;
    [SerializeField] private int waveTriggerGem = 3;
    [SerializeField] private GameObject[] reinforcementTemplates = new GameObject[0];
    [SerializeField] private Transform[] reinforcementSpawns = new Transform[0];

    private int lastGemCount;

    /// <summary>由生成器注入引用（哨兵模板是场景里未激活的预制对象）。</summary>
    public void Configure(Transform playerTransform, GameObject[] templates, Transform[] spawnPoints)
    {
        player = playerTransform;
        playerHealth = playerTransform != null ? playerTransform.GetComponent<Health>() : null;
        reinforcementTemplates = templates ?? new GameObject[0];
        reinforcementSpawns = spawnPoints ?? new Transform[0];
    }

    private void Update()
    {
        GameManager manager = GameManager.Instance;
        if (manager == null) return;

        int gems = manager.CollectedGems;
        while (lastGemCount < gems)
        {
            lastGemCount++;
            HandleGemCollected(lastGemCount);
        }
    }

    private void HandleGemCollected(int index)
    {
        if (playerHealth != null && !playerHealth.IsDead)
        {
            playerHealth.Heal(healPerGem);
            HudBridge.RefreshHealth(playerHealth);
        }

        if (index == waveTriggerGem) SpawnReinforcements();
    }

    private void SpawnReinforcements()
    {
        int spawned = 0;
        for (int i = 0; i < reinforcementTemplates.Length; i++)
        {
            GameObject template = reinforcementTemplates[i];
            if (template == null) continue;

            Vector3 position = template.transform.position;
            Quaternion rotation = template.transform.rotation;
            if (reinforcementSpawns.Length > 0)
            {
                Transform point = reinforcementSpawns[i % reinforcementSpawns.Length];
                if (point != null)
                {
                    position = point.position;
                    rotation = point.rotation;
                }
            }

            // 出生朝向：面向玩家（玩家引用同时用于"没有增援点时就近生成"）
            if (player != null)
            {
                Vector3 toPlayer = player.position - position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.01f) rotation = Quaternion.LookRotation(toPlayer);
            }

            GameObject enemy = Instantiate(template, position, rotation);
            enemy.SetActive(true);
            spawned++;
        }

        if (spawned <= 0) return;
        HudBridge.SetObjective("南侧拱门涌入增援：" + spawned + " 名远程哨兵（保持移动）");
        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.Alert);
    }
}
