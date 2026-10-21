using UnityEngine;

/// <summary>
/// 角色脚下的柔和接触阴影：程序化 64×64 径向渐变贴图 + 一个朝上的 Plane，
/// 每帧向下打射线找地面贴地，并按离地高度淡出（超过 1.5m 完全消失）。
/// 不参与实时阴影、不改 Shader、不新增贴图资源，每个角色只多 1 个半透明面片。
/// 挂载方式：生成器调用静态入口 GroundContactShadow.Attach(角色根物体)；
/// 另外有一个运行时兜底，场景加载时给所有带 Health 的角色自动补挂。
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class GroundContactShadow : MonoBehaviour
{
    [Header("阴影片")]
    [SerializeField] private float diameter = 0.95f;
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("贴地")]
    [SerializeField] private float rayStartHeight = 0.4f;
    [SerializeField] private float rayLength = 3f;
    [SerializeField] private float surfaceOffset = 0.01f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("淡出")]
    [SerializeField] private float fadeStartHeight = 0.4f;
    [SerializeField] private float fadeEndHeight = 1.5f;

    private const int TextureSize = 64;

    private static Texture2D sharedTexture;
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[8];
    private static bool sceneHookInstalled;

    private Transform shadowTransform;
    private Renderer shadowRenderer;
    private Material shadowMaterial;
    private Health health;
    private bool usesColorProperty;
    private float baseDiameter;

    /// <summary>
    /// 给角色根物体挂一个接触阴影（玩家 / 守卫）。重复调用安全，没有 Health 的物体会被忽略。
    /// </summary>
    public static GroundContactShadow Attach(GameObject character)
    {
        if (character == null) return null;
        GroundContactShadow existing = character.GetComponent<GroundContactShadow>();
        if (existing != null) return existing;
        if (character.GetComponent<Health>() == null) return null;
        return character.AddComponent<GroundContactShadow>();
    }

    /// <summary>给当前场景里所有带 Health 的角色补挂阴影。</summary>
    public static void AttachToAllCharacters()
    {
        Health[] characters = FindObjectsOfType<Health>();
        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] != null) Attach(characters[i].gameObject);
        }
    }

    /// <summary>运行时兜底：首个场景加载时自动挂载，并监听后续场景（重开局）加载。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallRuntimeFallback()
    {
        if (!Application.isPlaying) return;
        if (!sceneHookInstalled)
        {
            sceneHookInstalled = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        }
        AttachToAllCharacters();
    }

    private static void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        AttachToAllCharacters();
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        baseDiameter = Mathf.Max(0.2f, diameter);
        BuildShadowObject();
    }

    private void OnDestroy()
    {
        if (shadowMaterial != null) Destroy(shadowMaterial);
    }

    private void LateUpdate()
    {
        if (shadowTransform == null) return;

        if (health != null && health.IsDead)
        {
            if (shadowRenderer != null) shadowRenderer.enabled = false;
            return;
        }

        Vector3 origin = transform.position + Vector3.up * rayStartHeight;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, Mathf.Max(0.1f, rayLength), groundMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearest = float.MaxValue;
        Vector3 groundPoint = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (hit.collider == null) continue;
            // 忽略自己与其它角色（站在别人头上不该有影子）
            if (hit.collider.GetComponentInParent<Health>() != null) continue;
            if (hit.distance >= nearest) continue;
            nearest = hit.distance;
            groundPoint = hit.point;
            found = true;
        }

        if (!found)
        {
            if (shadowRenderer != null) shadowRenderer.enabled = false;
            return;
        }

        float heightAboveGround = Mathf.Max(0f, transform.position.y - groundPoint.y);
        float fade = 1f - Mathf.InverseLerp(fadeStartHeight, Mathf.Max(fadeStartHeight + 0.01f, fadeEndHeight), heightAboveGround);
        if (fade <= 0.01f)
        {
            if (shadowRenderer != null) shadowRenderer.enabled = false;
            return;
        }

        if (shadowRenderer != null) shadowRenderer.enabled = true;
        shadowTransform.position = new Vector3(transform.position.x, groundPoint.y + surfaceOffset, transform.position.z);
        // 始终水平朝上，不跟随角色旋转（圆形贴图正合适），也不贴合坡面法线
        shadowTransform.rotation = Quaternion.identity;

        // 离地越高片越小，视觉上像影子被"拉远"
        float spread = Mathf.Lerp(1f, 0.65f, Mathf.InverseLerp(fadeStartHeight, Mathf.Max(fadeStartHeight + 0.01f, fadeEndHeight), heightAboveGround));
        shadowTransform.localScale = Vector3.one * (baseDiameter * spread / 10f);

        if (shadowMaterial != null && usesColorProperty)
        {
            Color color = shadowColor;
            color.a = shadowColor.a * fade;
            shadowMaterial.color = color;
        }
    }

    private void BuildShadowObject()
    {
        Material material = CreateShadowMaterial();
        if (material == null) return;
        shadowMaterial = material;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "Ground Contact Shadow";
        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * (baseDiameter / 10f);

        shadowRenderer = go.GetComponent<Renderer>();
        if (shadowRenderer != null)
        {
            shadowRenderer.sharedMaterial = material;
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;
            shadowRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            shadowRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            shadowRenderer.enabled = false;
        }
        shadowTransform = go.transform;
    }

    private Material CreateShadowMaterial()
    {
        Texture2D texture = GetSharedTexture();
        if (texture == null) return null;

        // 优先规范要求的 Unlit/Transparent；它若没有 _Color（无法按实例调 alpha），
        // 退回到同样「无光照 + 透明混合 + 支持 _Color」的 Sprites/Default，保证淡出不失真。
        Material material = null;
        Shader unlit = Shader.Find("Unlit/Transparent");
        if (unlit != null)
        {
            material = new Material(unlit);
            if (!material.HasProperty("_Color"))
            {
                Destroy(material);
                material = null;
            }
        }
        if (material == null)
        {
            Shader fallback = Shader.Find("Sprites/Default");
            if (fallback == null) fallback = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (fallback == null) return null;
            material = new Material(fallback);
        }

        material.mainTexture = texture;
        material.renderQueue = 3000;
        usesColorProperty = material.HasProperty("_Color");
        if (usesColorProperty) material.color = shadowColor;
        return material;
    }

    /// <summary>共享的 64×64 径向渐变贴图：中心黑 alpha 0.55 → 边缘 alpha 0。</summary>
    private static Texture2D GetSharedTexture()
    {
        if (sharedTexture != null) return sharedTexture;

        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        texture.name = "Ground Contact Shadow Gradient";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;

        Color[] pixels = new Color[TextureSize * TextureSize];
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = (x + 0.5f) / TextureSize * 2f - 1f;
                float dy = (y + 0.5f) / TextureSize * 2f - 1f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = 0.55f * Mathf.Pow(Mathf.Clamp01(1f - distance), 1.8f);
                pixels[y * TextureSize + x] = new Color(0f, 0f, 0f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        sharedTexture = texture;
        return sharedTexture;
    }
}
