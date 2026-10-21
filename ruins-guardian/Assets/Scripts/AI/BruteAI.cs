using UnityEngine;

/// <summary>
/// 精英守卫（Brute）：8 HP，移动慢（2.2 m/s），复用 EnemyWanderAI 的五状态机，
/// 只覆写「攻击」为两段重锤：
///   一段：前摇 0.6s → 2.6m 内 20 伤害；
///   间隔 0.35s → 二段：前摇 0.8s → 3.4m 范围冲击波 26 伤害 + 击退；
///   收招 0.7s 后重新进入下一轮。
/// 前摇起手即给 0.25s 霸体（Health 没有独立的硬直抗性接口，因此用短暂无敌窗口实现「不被轻击打断」）。
/// 死亡：爆炸碎块粒子 + 掉落 1 瓶治疗。
/// </summary>
public sealed class BruteAI : EnemyWanderAI
{
    [Header("精英：两段重锤")]
    [SerializeField] private float firstWindup = 0.6f;
    [SerializeField] private float firstDamage = 20f;
    [SerializeField] private float firstRange = 2.6f;
    [SerializeField] private float comboGap = 0.35f;
    [SerializeField] private float secondWindup = 0.8f;
    [SerializeField] private float secondDamage = 26f;
    [SerializeField] private float shockRadius = 3.4f;
    [SerializeField] private float shockKnockback = 1.6f;
    [SerializeField] private float recoverTime = 0.7f;
    [SerializeField] private float armorWindow = 0.25f;

    [Header("精英：表现与掉落")]
    [SerializeField] private ShockwaveRing shockwave;
    [SerializeField] private GameObject healTemplate;
    [SerializeField] private Material debrisMaterial;
    [SerializeField] private float dropHealAmount = 30f;

    private enum ComboPhase { FirstWindup, Gap, SecondWindup, Recover }

    private ComboPhase comboPhase = ComboPhase.FirstWindup;
    private float comboTimer;

    /// <summary>由生成器调用：接好冲击波、掉落模板与碎块材质。</summary>
    public void Configure(Transform playerTransform, Transform[] waypoints, ShockwaveRing shock,
        GameObject healPickupTemplate, Material debris)
    {
        player = playerTransform;
        patrolPoints = waypoints;
        shockwave = shock;
        healTemplate = healPickupTemplate;
        debrisMaterial = debris;
        ApplyTuning();
    }

    protected override void OnAwake()
    {
        ApplyTuning();
    }

    private void ApplyTuning()
    {
        // 慢速精英：脚步慢、出招慢，但覆盖范围大
        chaseSpeed = 2.2f;
        patrolSpeed = 2.2f;
        attackRange = firstRange;
        attackInterval = firstWindup + comboGap + secondWindup + recoverTime;
        windup = firstWindup;
        damage = firstDamage;
        detectionRange = 9f;
        proximityRange = 4f;
        viewAngle = 140f;
        turnSpeed = 5.5f;
    }

    protected override void EnterAttack()
    {
        attackTimer = attackInterval;
        hasStruck = false;
        comboPhase = ComboPhase.FirstWindup;
        comboTimer = firstWindup;
        GrantArmor();
        if (animator != null) animator.SetTrigger("AttackTrigger");
        if (GameManager.Instance != null) GameManager.Instance.NotifyGuardSwing(transform.position);
    }

    protected override void TickAttack()
    {
        if (player == null || playerHealth == null)
        {
            ChangeState(State.Return);
            return;
        }

        Face(player.position);
        comboTimer -= Time.deltaTime;
        attackTimer -= Time.deltaTime;
        if (comboTimer > 0f) return;

        switch (comboPhase)
        {
            case ComboPhase.FirstWindup:
                PerformMeleeHit(firstDamage);
                comboPhase = ComboPhase.Gap;
                comboTimer = comboGap;
                break;

            case ComboPhase.Gap:
                comboPhase = ComboPhase.SecondWindup;
                comboTimer = secondWindup;
                GrantArmor();
                if (animator != null) animator.SetTrigger("AttackTrigger");
                break;

            case ComboPhase.SecondWindup:
                PerformShockwaveHit();
                comboPhase = ComboPhase.Recover;
                comboTimer = recoverTime;
                break;

            case ComboPhase.Recover:
                ChangeState(FlatDistance(player.position) <= firstRange ? State.Attack : State.Chase);
                break;
        }
    }

    private void PerformMeleeHit(float hitDamage)
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (FlatDistance(player.position) <= firstRange)
        {
            if (playerHealth.TakeDamage(hitDamage, direction.normalized) && GameManager.Instance != null)
                GameManager.Instance.NotifyPlayerHurt(player.position + Vector3.up);
        }
        if (GameManager.Instance != null) GameManager.Instance.NotifyGuardSwing(transform.position);
        if (JuiceDirector.Instance != null) JuiceDirector.Instance.AddTrauma(0.25f);
    }

    private void PerformShockwaveHit()
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (FlatDistance(player.position) <= shockRadius)
        {
            if (playerHealth.TakeDamage(secondDamage, direction.normalized))
            {
                playerHealth.ApplyKnockback(direction, shockKnockback);
                if (GameManager.Instance != null) GameManager.Instance.NotifyPlayerHurt(player.position + Vector3.up);
            }
        }

        if (shockwave != null) shockwave.Play(shockRadius);
        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.HammerSwing);
        if (JuiceDirector.Instance != null)
        {
            JuiceDirector.Instance.AddTrauma(0.55f);
            JuiceDirector.Instance.HitStop(0.12f, 0.06f);
        }
    }

    /// <summary>霸体帧：前摇起手的一小段窗口内不被打断。</summary>
    private void GrantArmor()
    {
        if (health != null) health.GrantInvulnerability(armorWindow);
    }

    protected override void OnDied()
    {
        base.OnDied();
        BurstFx.Play(transform.position + Vector3.up * 1f, debrisMaterial, 14, 4.5f);
        if (JuiceDirector.Instance != null) JuiceDirector.Instance.AddTrauma(0.7f);
        DropHeal();
    }

    private void DropHeal()
    {
        if (healTemplate == null) return;
        Vector3 position = transform.position + transform.forward * 0.6f + Vector3.up * 0.12f;
        GameObject pickup = Instantiate(healTemplate, position, Quaternion.identity);
        pickup.SetActive(true);
        HealPickup heal = pickup.GetComponent<HealPickup>();
        if (heal != null) heal.Configure(player, dropHealAmount);
        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.Pickup, 0.6f);
    }
}
