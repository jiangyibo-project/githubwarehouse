using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家闪避：Q 触发（空格让给跳跃）。0.22s 无敌帧 + 0.3s 位移（有输入朝输入方向，无输入朝后），
/// 沿途留下 0.2s 内淡出的残影，冷却 0.9s 并通过 CombatEvents.DodgeCooldown 广播就绪度。
/// 位移直接用 unscaledDeltaTime 推进，保证顿帧中依然干脆。
/// </summary>
[RequireComponent(typeof(Health))]
public sealed class PlayerDodge : MonoBehaviour
{
    [Header("闪避参数")]
    [SerializeField] private KeyCode dodgeKey = KeyCode.Q;
    [SerializeField] private float invulnerableTime = 0.22f;
    [SerializeField] private float dodgeDuration = 0.3f;
    [SerializeField] private float dodgeDistance = 3.4f;
    [SerializeField] private float cooldown = 0.9f;

    [Header("残影")]
    [SerializeField] private int afterimageCount = 3;
    [SerializeField] private float afterimageFade = 0.2f;
    [SerializeField] private Color afterimageColor = new Color(0.27f, 0.88f, 0.78f, 0.55f);

    [Header("引用")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private PlayerCombat combat;

    private Health health;
    private float dodgeTimer;
    private float cooldownTimer;
    private Vector3 dodgeDirection = Vector3.forward;
    private int ghostsSpawned;
    private float lastBroadcast = -1f;
    private readonly List<Ghost> ghosts = new List<Ghost>();

    /// <summary>是否正在闪避位移中（相机据此拉伸 FOV）。</summary>
    public bool IsDodging { get { return dodgeTimer > 0f; } }

    /// <summary>冷却就绪度 0..1。</summary>
    public float CooldownReadiness
    {
        get
        {
            if (cooldown <= 0f) return 1f;
            return 1f - Mathf.Clamp01(cooldownTimer / cooldown);
        }
    }

    /// <summary>冷却是否已经转好。</summary>
    public bool IsReady { get { return cooldownTimer <= 0f; } }

    private sealed class Ghost
    {
        public GameObject Root;
        public Material Material;
        public readonly List<Mesh> BakedMeshes = new List<Mesh>();
        public float Life;
        public float Duration;
        public Color Color;
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        if (combat == null) combat = GetComponent<PlayerCombat>();
    }

    private void Start()
    {
        RaiseCooldown(1f);
    }

    private void OnDisable()
    {
        dodgeTimer = 0f;
        ClearGhosts();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        UpdateGhosts(dt);
        UpdateCooldown(dt);

        if (health == null || health.IsDead)
        {
            dodgeTimer = 0f;
            return;
        }

        if (!IsGameplayActive()) return;

        if (dodgeTimer > 0f) AdvanceDodge(dt);

        if (dodgeTimer > 0f || cooldownTimer > 0f) return;
        if (Input.GetKeyDown(dodgeKey)) StartDodge();
    }

    private void StartDodge()
    {
        dodgeDirection = ResolveDirection();
        dodgeTimer = Mathf.Max(0.01f, dodgeDuration);
        ghostsSpawned = 0;
        cooldownTimer = Mathf.Max(0f, cooldown);
        if (health != null) health.GrantInvulnerability(invulnerableTime);

        SpawnGhost();
        ghostsSpawned = 1;

        GameManager manager = GameManager.Instance;
        if (manager != null) manager.PlaySfx(ProceduralSfx.Swing, 0.45f);

        RaiseCooldown(0f);
    }

    private void AdvanceDodge(float dt)
    {
        float duration = Mathf.Max(0.01f, dodgeDuration);
        float step = Mathf.Min(dt, dodgeTimer);
        float before = 1f - dodgeTimer / duration;
        dodgeTimer = Mathf.Max(0f, dodgeTimer - step);
        float after = 1f - dodgeTimer / duration;

        // ease-out：起步快、收尾慢，位移总量始终等于 dodgeDistance
        float travelled = EaseOutCubic(after) - EaseOutCubic(before);
        transform.position += dodgeDirection * (dodgeDistance * travelled);

        int wanted = Mathf.Clamp(Mathf.FloorToInt(after * Mathf.Max(1, afterimageCount)) + 1, 1, Mathf.Max(1, afterimageCount));
        while (ghostsSpawned < wanted)
        {
            SpawnGhost();
            ghostsSpawned++;
        }
    }

    private Vector3 ResolveDirection()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        Transform cam = ResolveCamera();
        if (cam != null)
        {
            forward = cam.forward;
            forward.y = 0f;
            right = cam.right;
            right.y = 0f;
            if (forward.sqrMagnitude > 0.001f) forward.Normalize(); else forward = Vector3.forward;
            if (right.sqrMagnitude > 0.001f) right.Normalize(); else right = Vector3.right;
        }

        Vector3 direction = forward * vertical + right * horizontal;
        if (direction.sqrMagnitude < 0.01f) direction = -transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = Vector3.back;
        return direction.normalized;
    }

    private Transform ResolveCamera()
    {
        if (cameraTransform == null)
        {
            Camera main = Camera.main;
            if (main != null) cameraTransform = main.transform;
        }
        return cameraTransform;
    }

    private void UpdateCooldown(float dt)
    {
        if (cooldownTimer > 0f) cooldownTimer = Mathf.Max(0f, cooldownTimer - dt);
        float readiness = CooldownReadiness;
        if (Mathf.Abs(readiness - lastBroadcast) > 0.01f) RaiseCooldown(readiness);
    }

    private void RaiseCooldown(float readiness)
    {
        lastBroadcast = readiness;
        CombatEvents.RaiseDodgeCooldown(readiness);
    }

    /// <summary>菜单 / 暂停 / 结算期间不响应闪避输入。</summary>
    private static bool IsGameplayActive()
    {
        GameManager manager = GameManager.Instance;
        return manager == null || manager.State == GameManager.GameState.Playing;
    }

    private static float EaseOutCubic(float t)
    {
        float inverse = 1f - Mathf.Clamp01(t);
        return 1f - inverse * inverse * inverse;
    }

    // ---------- 残影 ----------

    private void SpawnGhost()
    {
        Renderer[] sources = GetComponentsInChildren<Renderer>();
        if (sources == null || sources.Length == 0) return;

        Material material = CreateGhostMaterial();
        if (material == null) return;

        GameObject root = new GameObject("Dodge Afterimage");
        root.transform.SetPositionAndRotation(transform.position, transform.rotation);

        List<Mesh> bakedMeshes = new List<Mesh>();
        int parts = 0;
        for (int i = 0; i < sources.Length; i++)
        {
            Renderer source = sources[i];
            if (source == null || !source.enabled) continue;

            Mesh mesh = null;
            SkinnedMeshRenderer skinned = source as SkinnedMeshRenderer;
            if (skinned != null)
            {
                Mesh baked = new Mesh();
                skinned.BakeMesh(baked);
                bakedMeshes.Add(baked);
                mesh = baked;
            }
            else
            {
                MeshFilter filter = source.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }
            if (mesh == null) continue;

            GameObject part = new GameObject(source.name + " Ghost");
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = transform.InverseTransformPoint(source.transform.position);
            part.transform.localRotation = Quaternion.Inverse(transform.rotation) * source.transform.rotation;
            part.transform.localScale = source.transform.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer partRenderer = part.AddComponent<MeshRenderer>();
            partRenderer.sharedMaterial = material;
            partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = false;
            parts++;
        }

        if (parts == 0)
        {
            for (int i = 0; i < bakedMeshes.Count; i++) Destroy(bakedMeshes[i]);
            Destroy(material);
            Destroy(root);
            return;
        }

        Ghost ghost = new Ghost();
        ghost.Root = root;
        ghost.Material = material;
        ghost.BakedMeshes.AddRange(bakedMeshes);
        ghost.Color = afterimageColor;
        ghost.Duration = Mathf.Max(0.05f, afterimageFade);
        ghost.Life = ghost.Duration;
        ghosts.Add(ghost);
    }

    private void UpdateGhosts(float dt)
    {
        for (int i = ghosts.Count - 1; i >= 0; i--)
        {
            Ghost ghost = ghosts[i];
            ghost.Life -= dt;
            if (ghost.Root == null || ghost.Life <= 0f)
            {
                DestroyGhost(ghost);
                ghosts.RemoveAt(i);
                continue;
            }
            float t = Mathf.Clamp01(ghost.Life / ghost.Duration);
            Color color = ghost.Color;
            color.a = ghost.Color.a * t;
            if (ghost.Material != null) ghost.Material.color = color;
        }
    }

    private void ClearGhosts()
    {
        for (int i = 0; i < ghosts.Count; i++) DestroyGhost(ghosts[i]);
        ghosts.Clear();
    }

    private void DestroyGhost(Ghost ghost)
    {
        if (ghost.Root != null) Destroy(ghost.Root);
        if (ghost.Material != null) Destroy(ghost.Material);
        for (int i = 0; i < ghost.BakedMeshes.Count; i++)
        {
            if (ghost.BakedMeshes[i] != null) Destroy(ghost.BakedMeshes[i]);
        }
        ghost.BakedMeshes.Clear();
    }

    private Material CreateGhostMaterial()
    {
        Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;

        Material material = new Material(shader);
        if (material.HasProperty("_Color")) material.color = afterimageColor;
        material.renderQueue = 3000;
        return material;
    }
}
