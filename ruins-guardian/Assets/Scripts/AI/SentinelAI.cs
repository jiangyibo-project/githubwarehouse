using UnityEngine;

/// <summary>
/// 远程哨兵（Sentinel）：3 HP，复用 EnemyWanderAI 的 Patrol/Investigate/Chase/Attack/Return 状态机，
/// 只覆写「追击」与「攻击」两个状态：
///   追击 —— 距离大于 9m 才靠近；
///   攻击 —— 保持 6~9m，玩家贴近则后撤，每 2.2s 一轮，前摇 0.5s（蓄力发光）后射出能量弹（12 伤害）。
/// </summary>
public sealed class SentinelAI : EnemyWanderAI
{
    [Header("哨兵：射击节奏")]
    [SerializeField] private float chargeTime = 0.5f;
    [SerializeField] private float minEngageRange = 6f;
    [SerializeField] private float maxEngageRange = 9f;
    [SerializeField] private float retreatSpeed = 2.6f;
    [SerializeField] private float boltDamage = 12f;

    [Header("哨兵：引用")]
    [SerializeField] private GameObject boltTemplate;
    [SerializeField] private Transform muzzle;
    [SerializeField] private Transform chargeOrb;
    [SerializeField] private MeshRenderer chargeRenderer;
    [SerializeField] private Material dormantMaterial;
    [SerializeField] private Material chargedMaterial;
    [SerializeField] private Light chargeLight;

    private float charge;

    /// <summary>由生成器调用，一次性把巡逻点、发射点与材质接好。</summary>
    public void Configure(Transform playerTransform, Transform[] waypoints, GameObject bolt, Transform muzzlePoint,
        Transform orb, MeshRenderer orbRenderer, Material dormant, Material charged, Light orbLight)
    {
        player = playerTransform;
        patrolPoints = waypoints;
        boltTemplate = bolt;
        muzzle = muzzlePoint;
        chargeOrb = orb;
        chargeRenderer = orbRenderer;
        dormantMaterial = dormant;
        chargedMaterial = charged;
        chargeLight = orbLight;
        ApplyTuning();
    }

    protected override void OnAwake()
    {
        ApplyTuning();
        charge = 0f;
        ApplyCharge(0f, true);
    }

    private void ApplyTuning()
    {
        // 射程 = 攻击距离：进入 9m 就停下开火，而不是继续贴脸
        attackRange = maxEngageRange;
        attackInterval = 2.2f;
        windup = chargeTime;
        chaseSpeed = 2.4f;
        patrolSpeed = 1.6f;
        detectionRange = 11f;
        proximityRange = 3.5f;
        viewAngle = 150f;
        turnSpeed = 7f;
    }

    protected override void EnterAttack()
    {
        attackTimer = attackInterval;
        hasStruck = false;
    }

    protected override void TickChase(bool canSee)
    {
        if (!canSee)
        {
            ChangeState(State.Investigate);
            return;
        }

        lastKnownPosition = player.position;
        if (FlatDistance(player.position) > maxEngageRange) MoveTo(player.position, chaseSpeed);
        else ChangeState(State.Attack);
    }

    protected override void TickAttack()
    {
        if (player == null || playerHealth == null)
        {
            ChangeState(State.Return);
            return;
        }

        float distance = FlatDistance(player.position);
        if (distance > attackRange + 1.5f)
        {
            ChangeState(State.Chase);
            return;
        }

        // 玩家贴近：后撤保持射击距离，但始终面向玩家
        if (distance < minEngageRange) MoveAway(player.position, retreatSpeed);
        Face(player.position);

        attackTimer -= Time.deltaTime;
        if (!hasStruck)
        {
            float chargeElapsed = attackInterval - attackTimer;
            if (chargeElapsed < chargeTime)
            {
                ApplyCharge(chargeElapsed / Mathf.Max(0.01f, chargeTime), false);
            }
            else
            {
                hasStruck = true;
                FireBolt();
            }
        }
        else
        {
            ApplyCharge(0f, false);
        }

        if (attackTimer <= 0f)
        {
            hasStruck = false;
            attackTimer = attackInterval;
        }
    }

    protected override void OnStateChanged(State previous, State next)
    {
        if (previous == State.Attack && next != State.Attack) ApplyCharge(0f, false);
    }

    private void FireBolt()
    {
        ApplyCharge(0f, true);
        if (boltTemplate == null || player == null) return;

        Vector3 origin = muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.35f;
        Vector3 direction = (player.position + Vector3.up * 1.0f) - origin;
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
        direction.Normalize();

        GameObject bolt = Instantiate(boltTemplate, origin, Quaternion.LookRotation(direction));
        bolt.SetActive(true);
        EnergyBolt projectile = bolt.GetComponent<EnergyBolt>();
        if (projectile != null) projectile.Launch(player, direction, boltDamage);

        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.Swing, 0.5f);
    }

    /// <summary>蓄力表现：球体放大 + 材质换到高亮 Emissive + 点光渐亮。</summary>
    private void ApplyCharge(float target, bool immediate)
    {
        float next = Mathf.Clamp01(target);
        charge = immediate ? next : Mathf.MoveTowards(charge, next, Time.deltaTime * 6f);

        if (chargeOrb != null)
        {
            bool show = charge > 0.02f;
            if (chargeOrb.gameObject.activeSelf != show) chargeOrb.gameObject.SetActive(show);
            if (show) chargeOrb.localScale = Vector3.one * Mathf.Lerp(0.16f, 0.42f, charge);
        }

        if (chargeRenderer != null)
        {
            Material desired = charge > 0.5f && chargedMaterial != null ? chargedMaterial : dormantMaterial;
            if (desired != null && chargeRenderer.sharedMaterial != desired) chargeRenderer.sharedMaterial = desired;
        }

        if (chargeLight != null)
        {
            bool lit = charge > 0.02f;
            if (chargeLight.enabled != lit) chargeLight.enabled = lit;
            if (lit) chargeLight.intensity = Mathf.Lerp(0f, 7f, charge);
        }
    }
}
