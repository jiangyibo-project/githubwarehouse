using UnityEngine;

public class ShapeChanger : MonoBehaviour
{
    public GameObject sphereVisual;
    public SphereCollider sphereCollider;
    public GameObject cubeVisual;
    public BoxCollider boxCollider;
    public Color playerColor = new Color(0.12f, 0.42f, 1f, 1f);

    private Renderer _sphereRenderer;
    private Renderer _cubeRenderer;
    private Rigidbody _body;
    private Renderer _animatingRenderer;
    private float _animationTimer;
    public bool IsCube { get; private set; }

    void Awake()
    {
        _body = GetComponent<Rigidbody>();
        if (sphereVisual == null) sphereVisual = transform.Find("Sphere")?.gameObject;
        if (cubeVisual == null) cubeVisual = transform.Find("Cube")?.gameObject;
        if (sphereCollider == null && sphereVisual != null) sphereCollider = sphereVisual.GetComponent<SphereCollider>();
        if (boxCollider == null && cubeVisual != null) boxCollider = cubeVisual.GetComponent<BoxCollider>();

        if (boxCollider != null)
        {
            // Keep the square's visible size, with a small clearance on its sides
            // so its corners do not scrape maze corridors.
            Vector3 size = boxCollider.size;
            size.x *= 0.92f;
            size.z *= 0.92f;
            boxCollider.size = size;
        }
        if (sphereCollider != null)
        {
            // The low tunnel obstacles are deliberately passable only in sphere form.
            sphereCollider.radius = 0.4f;
            sphereCollider.center = new Vector3(0f, -0.1f, 0f);
        }

        PhysicMaterial lowFriction = new PhysicMaterial("Player Low Friction");
        lowFriction.staticFriction = 0f;
        lowFriction.dynamicFriction = 0f;
        lowFriction.bounciness = 0f;
        lowFriction.frictionCombine = PhysicMaterialCombine.Minimum;
        lowFriction.bounceCombine = PhysicMaterialCombine.Minimum;
        if (sphereCollider != null) sphereCollider.sharedMaterial = lowFriction;
        if (boxCollider != null) boxCollider.sharedMaterial = lowFriction;

        _sphereRenderer = sphereVisual != null ? sphereVisual.GetComponent<Renderer>() : null;
        _cubeRenderer = cubeVisual != null ? cubeVisual.GetComponent<Renderer>() : null;
        SetBlue(_sphereRenderer);
        SetBlue(_cubeRenderer);

        // Keep both shape roots alive. The visuals and matching collider change
        // together; this avoids disabling a collider indirectly via SetActive.
        if (sphereVisual != null) sphereVisual.SetActive(true);
        if (cubeVisual != null) cubeVisual.SetActive(true);
        SetShape(false);
    }

    void Update()
    {
        if (_animatingRenderer != null && _animationTimer > 0f)
        {
            _animationTimer = Mathf.Max(0f, _animationTimer - Time.deltaTime);
            float t = 1f - _animationTimer / 0.18f;
            Color pulse = Color.Lerp(playerColor, Color.white, Mathf.Sin(t * Mathf.PI) * 0.7f);
            MaterialPropertyBlock effect = new MaterialPropertyBlock();
            _animatingRenderer.GetPropertyBlock(effect);
            effect.SetColor("_Color", pulse); effect.SetColor("_BaseColor", pulse);
            _animatingRenderer.SetPropertyBlock(effect);
            if (_animationTimer <= 0f) { SetBlue(_animatingRenderer); _animatingRenderer = null; }
        }
        if (MazeGenerator.Instance != null && !MazeGenerator.Instance.GameStarted) return;
        if (!MazeKeybinds.WasPressed(MazeAction.Transform)) return;

        // Derive the next state from the visible renderer so the visual and
        // collider cannot drift out of sync after a physics or scene refresh.
        bool showCubeNext = _sphereRenderer != null && _sphereRenderer.enabled;
        SetShape(showCubeNext);
    }

    void SetShape(bool cube)
    {
        bool changed = IsCube != cube;
        IsCube = cube;
        Vector3 savedVelocity = _body != null ? _body.velocity : Vector3.zero;
        if (sphereVisual != null) sphereVisual.SetActive(true);
        if (cubeVisual != null) cubeVisual.SetActive(true);
        if (_sphereRenderer != null) _sphereRenderer.enabled = !cube;
        if (_cubeRenderer != null) _cubeRenderer.enabled = cube;
        _animatingRenderer = cube ? _cubeRenderer : _sphereRenderer;
        _animationTimer = changed ? 0.18f : 0f;
        // Switch just the collider components; the shape GameObjects stay active.
        if (sphereCollider != null) sphereCollider.enabled = !cube;
        if (boxCollider != null) boxCollider.enabled = cube;
        Physics.SyncTransforms();
        if (_body != null) _body.velocity = savedVelocity;
        if (changed) GameSfx.Play(gameObject, GameSound.Transform);
    }

    public void ResetToSphere()
    {
        SetShape(false);
    }

    void SetBlue(Renderer target)
    {
        if (target == null) return;
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        target.GetPropertyBlock(properties);
        properties.SetColor("_Color", playerColor);
        properties.SetColor("_BaseColor", playerColor);
        target.SetPropertyBlock(properties);
    }
}
