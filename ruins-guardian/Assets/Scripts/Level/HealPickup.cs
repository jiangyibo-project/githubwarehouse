using UnityEngine;

/// <summary>
/// 治疗瓶：精英守卫（Brute）死亡时掉落，玩家靠近自动拾取并回复生命。
/// 模板由生成器预先做好（材质/网格都是资源），运行时只 Instantiate，不新建材质。
/// </summary>
public sealed class HealPickup : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Health playerHealth;
    [SerializeField] private float healAmount = 30f;
    [SerializeField] private float pickupRange = 1.2f;
    [SerializeField] private float floatHeight = 0.12f;
    [SerializeField] private float floatSpeed = 2.4f;
    [SerializeField] private float spinSpeed = 120f;
    [SerializeField] private float lifeTime = 30f;

    private Vector3 basePosition;
    private float life;
    private bool collected;

    /// <summary>掉落时由 BruteAI 调用，把玩家与回复量接上。</summary>
    public void Configure(Transform playerTransform, float amount)
    {
        player = playerTransform;
        healAmount = amount;
        playerHealth = playerTransform != null ? playerTransform.GetComponent<Health>() : null;
    }

    private void Start()
    {
        basePosition = transform.position;
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<Health>();
    }

    private void Update()
    {
        if (collected) return;

        life += Time.deltaTime;
        if (life > lifeTime)
        {
            Destroy(gameObject);
            return;
        }

        float t = Time.time * floatSpeed;
        Vector3 position = basePosition;
        position.y += Mathf.Sin(t) * floatHeight;
        transform.position = position;
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        if (player != null && Vector3.Distance(transform.position, player.position) < pickupRange) Collect();
    }

    private void Collect()
    {
        collected = true;
        if (playerHealth != null && !playerHealth.IsDead)
        {
            playerHealth.Heal(healAmount);
            HudBridge.RefreshHealth(playerHealth);
        }
        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.Pickup);
        Destroy(gameObject);
    }
}
