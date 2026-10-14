using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 3;
    public int CurrentHealth { get; private set; } = 3;
    public bool Shielded => _shieldTime > 0f;
    public float ShieldTimeLeft => _shieldTime;
    public float HitFlash => Mathf.Clamp01(_hitFlash / 0.22f);
    private float _invulnerableTime;
    private float _shieldTime;
    private float _hitFlash;
    private GameObject _shieldVisual;
    private Renderer[] _renderers;
    private Color[] _originalColors;
    private MaterialPropertyBlock _block;

    void Awake()
    {
        EnsurePropertyBlock();
        CurrentHealth = maxHealth;
        _renderers = GetComponentsInChildren<Renderer>(true);
        _originalColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
            _originalColors[i] = _renderers[i] != null && _renderers[i].sharedMaterial != null ? _renderers[i].sharedMaterial.color : Color.white;
    }
    void Start()
    {
        EnsurePropertyBlock();
        // ShapeChanger applies its blue property block from Awake; recapture after all Awake calls.
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            _renderers[i].GetPropertyBlock(_block);
            Color blockColor = _block.GetColor("_Color");
            if (blockColor.maxColorComponent > 0.01f) _originalColors[i] = blockColor;
        }
    }
    void Update()
    {
        _invulnerableTime = Mathf.Max(0f, _invulnerableTime - Time.deltaTime);
        _shieldTime = Mathf.Max(0f, _shieldTime - Time.deltaTime);
        _hitFlash = Mathf.Max(0f, _hitFlash - Time.deltaTime);
        UpdateHitFlash();
        if (_shieldVisual != null) _shieldVisual.SetActive(_shieldTime > 0f);
    }
    public void SetStartingHealth(int amount) { maxHealth = 3; _startingHealth = Mathf.Max(1, amount); CurrentHealth = _startingHealth; }
    private int _startingHealth = 3;
    public void ResetHealth() { CurrentHealth = _startingHealth; _invulnerableTime = 0f; _shieldTime = 0f; _hitFlash = 0f; if (_shieldVisual != null) _shieldVisual.SetActive(false); }
    public void Heal(int amount) { if (CurrentHealth > 0) CurrentHealth += Mathf.Max(1, amount); }
    public void ActivateShield(float seconds)
    {
        _shieldTime = seconds;
        if (_shieldVisual == null) CreateShieldVisual();
        _shieldVisual.SetActive(true);
    }
    public void TakeDamage(int amount = 1)
    {
        if (CurrentHealth <= 0 || _invulnerableTime > 0f) return;
        if (_shieldTime > 0f) return;
        ApplyDamage(amount);
    }

    public void TakeFallDamage(int amount = 1)
    {
        if (CurrentHealth <= 0) return;
        // Falling is environmental damage and intentionally bypasses both shield and
        // the brief post-hit grace period. The shield timer itself keeps running.
        ApplyDamage(amount);
    }

    void ApplyDamage(int amount)
    {
        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        _invulnerableTime = 1.1f;
        _hitFlash = 0.22f;
        GameSfx.Play(gameObject, GameSound.Hurt);
        ThirdPersonCam cameraMode = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCam>() : null;
        if (cameraMode != null) cameraMode.Shake(0.22f);
        if (CurrentHealth == 0)
        {
            PlayerController controller = GetComponent<PlayerController>();
            if (controller != null) controller.SetDead(true);
        }
    }

    void CreateShieldVisual()
    {
        _shieldVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _shieldVisual.name = "PlayerShieldEffect";
        _shieldVisual.transform.SetParent(transform, false);
        _shieldVisual.transform.localPosition = Vector3.up * 0.38f;
        _shieldVisual.transform.localScale = Vector3.one * 1.75f;
        Collider collider = _shieldVisual.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
        Renderer renderer = _shieldVisual.GetComponent<Renderer>();
        Material material = new Material(Shader.Find("Standard"));
        material.SetFloat("_Mode", 3f); material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0); material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000; material.color = new Color(0.12f, 0.82f, 1f, 0.24f);
        renderer.sharedMaterial = material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    void UpdateHitFlash()
    {
        if (_renderers == null) return;
        EnsurePropertyBlock();
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null || renderer.gameObject == _shieldVisual) continue;
            Color flash = Color.Lerp(_originalColors[i], new Color(1f, 0.12f, 0.12f), HitFlash);
            renderer.GetPropertyBlock(_block); _block.SetColor("_Color", flash); _block.SetColor("_BaseColor", flash); renderer.SetPropertyBlock(_block);
        }
    }

    void EnsurePropertyBlock()
    {
        if (_block == null) _block = new MaterialPropertyBlock();
    }

    void OnGUI()
    {
        if (_hitFlash <= 0f) return;
        Color old = GUI.color; GUI.color = new Color(1f, 0.08f, 0.08f, HitFlash * 0.22f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;
    }
}
