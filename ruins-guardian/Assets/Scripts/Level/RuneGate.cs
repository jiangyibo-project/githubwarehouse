using System.Collections;
using UnityEngine;

/// <summary>
/// 符文石门：三块符文石碑全部激活后，程序化石门向下沉入地面 1.6m 开启，
/// 同时放出北侧高台门后的宝石与精英守卫（Brute），并在 HUD 目标提示里播报进度。
/// </summary>
public sealed class RuneGate : MonoBehaviour
{
    [SerializeField] private RunePillar[] pillars = new RunePillar[0];
    [SerializeField] private Transform slab;
    [SerializeField] private Collider slabCollider;
    [SerializeField] private float sinkDepth = 1.6f;
    [SerializeField] private float sinkDuration = 1.9f;
    [SerializeField] private GameObject[] revealOnOpen = new GameObject[0];
    [SerializeField] private Light gateLight;
    [SerializeField] private float litIntensity = 2.4f;

    private Vector3 slabStart;
    private float openProgress;
    private bool opening;
    private bool opened;

    public bool IsOpen { get { return opened; } }
    public int TotalPillars { get { return pillars != null ? pillars.Length : 0; } }

    /// <summary>由生成器注入引用。</summary>
    public void Configure(RunePillar[] runePillars, Transform doorSlab, Collider doorCollider,
        GameObject[] reveals, Light light)
    {
        pillars = runePillars ?? new RunePillar[0];
        slab = doorSlab;
        slabCollider = doorCollider;
        revealOnOpen = reveals ?? new GameObject[0];
        gateLight = light;
    }

    /// <summary>石碑建好后回填（生成器先建石门再建石碑）。</summary>
    public void SetPillars(RunePillar[] runePillars)
    {
        pillars = runePillars ?? new RunePillar[0];
    }

    private void Awake()
    {
        if (slab != null) slabStart = slab.position;
        if (gateLight != null)
        {
            gateLight.intensity = 0f;
            gateLight.enabled = false;
        }
    }

    private void Start()
    {
        StartCoroutine(IntroObjectiveRoutine());
    }

    private IEnumerator IntroObjectiveRoutine()
    {
        // 等 GameManager 真正开局（菜单阶段 timeScale = 0，用真实时间等待）
        float guard = 0f;
        while (guard < 60f)
        {
            GameManager manager = GameManager.Instance;
            if (manager != null && manager.State == GameManager.GameState.Playing) break;
            guard += Time.unscaledDeltaTime;
            yield return null;
        }
        RefreshObjective();
    }

    /// <summary>刷新 HUD 目标提示（石碑进度）。</summary>
    public void RefreshObjective()
    {
        if (opened)
        {
            HudBridge.SetObjective("石门已开启：门后是精英守卫与宝石");
            return;
        }
        HudBridge.SetObjective("激活 3 块符文石碑打开北侧石门（" + CountActivated() + "/" + TotalPillars + "）");
    }

    /// <summary>石碑激活回调。</summary>
    public void NotifyPillarActivated(RunePillar pillar)
    {
        int activated = CountActivated();
        if (activated >= TotalPillars && TotalPillars > 0)
        {
            BeginOpen();
            return;
        }
        HudBridge.SetObjective("符文石碑已激活 " + activated + "/" + TotalPillars + "，去找剩下的");
    }

    private int CountActivated()
    {
        if (pillars == null) return 0;
        int count = 0;
        for (int i = 0; i < pillars.Length; i++)
        {
            if (pillars[i] != null && pillars[i].IsActivated) count++;
        }
        return count;
    }

    private void BeginOpen()
    {
        if (opening || opened) return;
        opening = true;
        openProgress = 0f;

        HudBridge.SetObjective("三符点亮：北侧石门开启，门后有精英守卫与宝石");
        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.DoorOpen);

        // 放出门后内容（精英守卫一开始是未激活状态，避免提前跑 AI）
        for (int i = 0; i < revealOnOpen.Length; i++)
        {
            if (revealOnOpen[i] != null) revealOnOpen[i].SetActive(true);
        }
    }

    private void Update()
    {
        if (!opening || opened) return;

        openProgress = Mathf.Clamp01(openProgress + Time.deltaTime / Mathf.Max(0.1f, sinkDuration));
        float eased = openProgress * openProgress * (3f - 2f * openProgress);

        if (slab != null) slab.position = slabStart + Vector3.down * (sinkDepth * eased);
        // 门体沉到一半就撤掉碰撞，避免玩家被"半开门"卡住
        if (slabCollider != null && slabCollider.enabled && openProgress >= 0.55f) slabCollider.enabled = false;

        if (gateLight != null)
        {
            if (!gateLight.enabled) gateLight.enabled = true;
            gateLight.intensity = Mathf.Lerp(0f, litIntensity, eased);
        }

        if (openProgress >= 1f)
        {
            opened = true;
            if (gateLight != null) gateLight.intensity = litIntensity;
        }
    }
}
