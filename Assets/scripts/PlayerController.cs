using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("移动参数")]
    public float moveSpeed = 5f;
    public float flightMoveSpeed = 7f;
    public float jumpForce = 4f;
    public float groundCheckDistance = 0.2f;

    [Header("地面检测")]
    public LayerMask groundLayer;

    private Rigidbody _rb;
    private Vector2 _moveInput;
    private bool _jumpInput;
    private Collider[] _playerColliders;
    private bool _flightJumpActive;
    public bool IsFlying => _flightJumpActive;
    private bool _dead;
    private float _nextShotTime;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _playerColliders = GetComponentsInChildren<Collider>(true);
        _rb.freezeRotation = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    // Input系统：移动输入回调
    public void OnMove(InputValue value)
    {
        _moveInput = value.Get<Vector2>();
    }

    // Input系统：跳跃按键回调
    public void OnJump(InputValue value)
    {
        if(value.isPressed)
        {
            _jumpInput = true;
        }
    }

    void Update()
    {
        if (MazeKeybinds.WasPressed(MazeAction.ToggleCursor) && MazeGenerator.Instance != null)
            MazeGenerator.Instance.ToggleCursor(new Vector2(-1f, -1f));
        if (MazeGenerator.Instance != null && !MazeGenerator.Instance.GameStarted)
        { _moveInput = Vector2.zero; _jumpInput = false; return; }
        bool mouseShot = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool keyboardShot = !mouseShot && MazeKeybinds.WasPressed(MazeAction.Shoot);
        if (mouseShot || keyboardShot)
        {
            if (Cursor.visible)
            {
                if (mouseShot && MazeGenerator.Instance != null)
                    MazeGenerator.Instance.ToggleCursor(Mouse.current.position.ReadValue());
            }
            else
            {
                Shoot();
            }
        }
        if (_dead) { _moveInput = Vector2.zero; _jumpInput = false; return; }
        ThirdPersonCam cameraMode = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCam>() : null;
        if (cameraMode != null && cameraMode.LimitedGodViewActive) { _moveInput = Vector2.zero; _jumpInput = false; return; }
        PlayerInventory inventory = GetComponent<PlayerInventory>();
        if (MazeKeybinds.WasPressed(MazeAction.Mark) && MazeGenerator.Instance != null)
            MazeGenerator.Instance.MarkPlayerFoot(transform.position);
        if (MazeKeybinds.WasPressed(MazeAction.UseItem) && inventory != null &&
            (cameraMode == null || !cameraMode.LimitedGodViewActive)) inventory.UseItem();
        float horizontal = (MazeKeybinds.IsPressed(MazeAction.MoveRight) ? 1f : 0f) -
                           (MazeKeybinds.IsPressed(MazeAction.MoveLeft) ? 1f : 0f);
        float vertical = (MazeKeybinds.IsPressed(MazeAction.MoveForward) ? 1f : 0f) -
                         (MazeKeybinds.IsPressed(MazeAction.MoveBackward) ? 1f : 0f);
        _moveInput = new Vector2(horizontal, vertical).normalized;

        if (MazeKeybinds.WasPressed(MazeAction.Jump)) _jumpInput = true;
    }

    void FixedUpdate()
    {
        if (_dead) { _rb.velocity = Vector3.zero; return; }
        MoveLogic();
        JumpLogic();
        if (_flightJumpActive && _rb.velocity.y <= 0f && IsGrounded()) _flightJumpActive = false;
    }

    void MoveLogic()
    {
        // 获取相机前后方向（忽略Y轴，保证平面移动）
        Camera mainCam = Camera.main;
        Vector3 camForward = mainCam != null ? mainCam.transform.forward : Vector3.forward;
        Vector3 camRight = mainCam != null ? mainCam.transform.right : Vector3.right;
        if (Mathf.Abs(camForward.y) > 0.95f && mainCam != null)
            camForward = mainCam.transform.up;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        // 计算移动方向
        Vector3 moveDir = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;
        // Keep clear, responsive air control while the flight pickup's high jump is active.
        Vector3 velocity = moveDir * (_flightJumpActive ? flightMoveSpeed : moveSpeed);
        velocity.y = _rb.velocity.y; // Y轴保留重力，不覆盖

        _rb.velocity = velocity;
    }

    void JumpLogic()
    {
        ShapeChanger shape = GetComponent<ShapeChanger>();
        if (_jumpInput && shape != null && shape.IsCube && _rb.velocity.y <= 0.05f && IsGrounded())
        {
            Vector3 velocity = _rb.velocity;
            velocity.y = jumpForce;
            _rb.velocity = velocity;
            GameSfx.Play(gameObject, GameSound.Jump);
        }
        _jumpInput = false;
    }

    // 地面检测
    public bool IsGrounded()
    {
        int layerMask = groundLayer.value == 0 ? Physics.DefaultRaycastLayers : groundLayer.value;
        float probeDistance = Mathf.Max(groundCheckDistance + 0.1f, 0.4f);

        for (int i = 0; i < _playerColliders.Length; i++)
        {
            Collider playerCollider = _playerColliders[i];
            if (playerCollider == null || !playerCollider.enabled || !playerCollider.gameObject.activeInHierarchy)
                continue;

            Bounds bounds = playerCollider.bounds;
            Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.08f, bounds.center.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, probeDistance, layerMask,
                QueryTriggerInteraction.Ignore);
            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                Transform hitTransform = hits[hitIndex].collider.transform;
                if (hitTransform != transform && !hitTransform.IsChildOf(transform))
                    return true;
            }
        }

        return false;
    }

    // 【给变形脚本调用】开启/关闭旋转锁定
    public void SetRotationLock(bool lockRot)
    {
        _rb.freezeRotation = lockRot;
    }

    public void ResetMotion()
    {
        _moveInput = Vector2.zero;
        _jumpInput = false;
        if (_rb == null) return;
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _flightJumpActive = false;
        _dead = false;
    }

    public bool BeginFlight()
    {
        if (_flightJumpActive || _dead || !IsGrounded()) return false;
        _flightJumpActive = true;
        // A high, steerable launch works in either shape and keeps the third-person camera.
        Vector3 velocity = _rb.velocity;
        velocity.y = 11.5f;
        _rb.velocity = velocity;
        return true;
    }

    public void SetDead(bool dead) { _dead = dead; if (dead) _moveInput = Vector2.zero; }

    void Shoot()
    {
        ShapeChanger shape = GetComponent<ShapeChanger>();
        if (shape == null || shape.IsCube || Time.time < _nextShotTime) return;
        _nextShotTime = Time.time + 0.42f;
        Camera cam = Camera.main;
        Vector3 direction = cam != null ? cam.transform.forward : transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        direction.Normalize();
        GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bullet.name = "PlayerProjectile";
        bullet.transform.position = transform.position + Vector3.up * 0.42f + direction * 0.62f;
        bullet.transform.localScale = Vector3.one * 0.18f;
        Collider collider = bullet.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
        Renderer renderer = bullet.GetComponent<Renderer>();
        MaterialPropertyBlock color = new MaterialPropertyBlock(); renderer.GetPropertyBlock(color);
        color.SetColor("_Color", new Color(0.25f, 0.9f, 1f)); color.SetColor("_BaseColor", new Color(0.25f, 0.9f, 1f)); renderer.SetPropertyBlock(color);
        PlayerProjectile projectile = bullet.AddComponent<PlayerProjectile>();
        projectile.Launch(direction, transform);
        if (MazeGenerator.Instance != null && MazeGenerator.Instance.GeneratedRoot != null)
            bullet.transform.SetParent(MazeGenerator.Instance.GeneratedRoot, true);
        StarEnemy.AlertByShot(transform.position, 18f);
        GameSfx.Play(gameObject, GameSound.Shot);
    }
}
