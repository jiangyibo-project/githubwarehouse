using UnityEngine;

/// <summary>
/// 符文石碑：玩家进入 2.5m 内按 E 激活（F 为备用键；相机旋转键已迁到 Z/C，不再冲突）。
/// 激活表现 = 换 Emissive 材质 + 光柱升起 + 石碑抬升动画 + 符文音效，并通知 RuneGate 计数。
/// </summary>
public sealed class RunePillar : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private RuneGate gate;
    [SerializeField] private string pillarLabel = "符文石碑";
    [SerializeField] private float activateRange = 2.5f;
    [SerializeField] private Renderer slabRenderer;
    [SerializeField] private Material dormantMaterial;
    [SerializeField] private Material activeMaterial;
    [SerializeField] private Transform riseRoot;
    [SerializeField] private float riseHeight = 0.45f;
    [SerializeField] private float riseDuration = 0.9f;
    [SerializeField] private Transform beam;
    [SerializeField] private Light runeLight;
    [SerializeField] private float litIntensity = 5.5f;

    private Vector3 riseStart;
    private Vector3 beamBaseScale = Vector3.one;
    private float riseProgress;
    private bool activated;
    private bool playerInRange;

    public bool IsActivated { get { return activated; } }
    public string Label { get { return pillarLabel; } }

    /// <summary>由生成器注入引用。</summary>
    public void Configure(Transform playerTransform, RuneGate owner, Renderer slab, Material dormant, Material active,
        Transform rising, Transform beamRoot, Light light, string label)
    {
        player = playerTransform;
        gate = owner;
        slabRenderer = slab;
        dormantMaterial = dormant;
        activeMaterial = active;
        riseRoot = rising;
        beam = beamRoot;
        runeLight = light;
        pillarLabel = label;
    }

    private void Awake()
    {
        if (riseRoot != null) riseStart = riseRoot.localPosition;
        if (beam != null)
        {
            beamBaseScale = beam.localScale;
            beam.gameObject.SetActive(false);
        }
        if (runeLight != null)
        {
            runeLight.intensity = 0f;
            runeLight.enabled = false;
        }
        if (slabRenderer != null && dormantMaterial != null) slabRenderer.sharedMaterial = dormantMaterial;
    }

    private void Update()
    {
        if (!activated)
        {
            UpdateProximity();
            return;
        }

        if (riseProgress < 1f)
        {
            riseProgress = Mathf.Clamp01(riseProgress + Time.deltaTime / Mathf.Max(0.05f, riseDuration));
            float eased = 1f - Mathf.Pow(1f - riseProgress, 3f);
            if (riseRoot != null) riseRoot.localPosition = riseStart + Vector3.up * (riseHeight * eased);
            if (beam != null)
            {
                float grow = Mathf.Clamp01(riseProgress * 1.4f);
                beam.localScale = new Vector3(beamBaseScale.x, Mathf.Lerp(0.05f, 1f, grow) * beamBaseScale.y, beamBaseScale.z);
            }
            if (runeLight != null) runeLight.intensity = Mathf.Lerp(0f, litIntensity, eased);
        }
        else if (runeLight != null && runeLight.enabled)
        {
            // 待机呼吸
            runeLight.intensity = litIntensity + Mathf.Sin(Time.time * 2.2f) * 0.6f;
        }
    }

    private void UpdateProximity()
    {
        bool inRange = false;
        if (player != null)
        {
            Vector3 delta = player.position - transform.position;
            delta.y = 0f;
            inRange = delta.magnitude <= activateRange;
        }

        if (inRange != playerInRange)
        {
            playerInRange = inRange;
            if (inRange) HudBridge.SetObjective("按 E 激活" + pillarLabel);
            else if (gate != null) gate.RefreshObjective();
        }

        if (!inRange) return;
        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.F)) Activate();
    }

    /// <summary>激活（也可由机关外部调用）。</summary>
    public void Activate()
    {
        if (activated) return;
        activated = true;
        riseProgress = 0f;

        if (slabRenderer != null && activeMaterial != null) slabRenderer.sharedMaterial = activeMaterial;
        if (beam != null)
        {
            beam.gameObject.SetActive(true);
            beam.localScale = new Vector3(beamBaseScale.x, 0.05f * beamBaseScale.y, beamBaseScale.z);
        }
        if (runeLight != null)
        {
            runeLight.enabled = true;
            runeLight.intensity = 0f;
        }

        if (GameManager.Instance != null) GameManager.Instance.PlaySfx(ProceduralSfx.RuneActivate);
        if (gate != null) gate.NotifyPillarActivated(this);
    }
}
