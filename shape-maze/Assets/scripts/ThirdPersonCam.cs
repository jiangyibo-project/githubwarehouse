using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCam : MonoBehaviour
{
    [Header("相机跟随参数")]
    public Transform player;
    public Vector3 offset = new Vector3(0, 2.2f, -3.5f);
    [Header("鼠标转向")]
    public float mouseSensitivity = 0.15f;
    public float minPitch = 5f;
    public float maxPitch = 55f;
    [Header("上帝视角")]
    public float godViewHeight = 55f;
    public Vector3 mazeCenter;
    public float mazeSize = 48f;

    private float _yaw;
    private float _pitch = 24f;
    private bool _godView;
    private bool _limitedGodView;
    private bool _cheatGodView;
    private float _limitedViewTimer;
    private int _qUsesLeft = 3;
    private float _shakeTimer;
    private float _shakeAmount;
    private RaycastHit[] _cameraHits;
    private GUIStyle _playerMarkerStyle;
    public float cameraCollisionRadius = 0.22f;
    public float cameraWallPadding = 0.18f;
    public bool LimitedGodViewActive => _limitedGodView;
    public int QUsesLeft => _qUsesLeft;
    public float QTimeLeft => _limitedViewTimer;
    public bool CheatGodViewActive => _cheatGodView;

    void Awake()
    {
        _cameraHits = new RaycastHit[32];
    }

    void LateUpdate()
    {
        if (player == null) return;
        _shakeTimer = Mathf.Max(0f, _shakeTimer - Time.deltaTime);
        Vector3 shake = _shakeTimer > 0f ? Random.insideUnitSphere * (_shakeAmount * _shakeTimer / 0.22f) : Vector3.zero;

        if (MazeGenerator.Instance == null || MazeGenerator.Instance.GameStarted)
        {
            if (MazeKeybinds.WasPressed(MazeAction.CheatView)) ToggleCheatGodView();
            if (MazeKeybinds.WasPressed(MazeAction.GodView) && !_cheatGodView && !_limitedGodView && _qUsesLeft > 0)
            {
                _qUsesLeft--;
                _limitedGodView = _godView = true;
                _limitedViewTimer = 3f;
            }
        }

        if (_limitedGodView)
        {
            _limitedViewTimer -= Time.deltaTime;
            if (_limitedViewTimer <= 0f) { _limitedGodView = false; _godView = false; }
        }

        if (_godView)
        {
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.orthographic = true;
                float aspect = Mathf.Max(0.1f, cam.aspect);
                cam.orthographicSize = Mathf.Max(mazeSize * 0.55f, mazeSize / (2f * aspect) * 1.05f);
            }
            transform.position = mazeCenter + Vector3.up * godViewHeight + shake;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return;
        }

        if (!Cursor.visible && Mouse.current != null)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            _yaw += mouseDelta.x * mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch - mouseDelta.y * mouseSensitivity, minPitch, maxPitch);
        }

        Camera normalCam = GetComponent<Camera>();
        if (normalCam != null) normalCam.orthographic = false;
        float distance = new Vector2(offset.y, offset.z).magnitude;
        Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 lookTarget = player.position + Vector3.up * 0.5f;
        Vector3 fromTarget = orbit * (Vector3.back * distance);
        Vector3 cameraDirection = fromTarget.normalized;
        float safeDistance = distance;
        if (_cameraHits == null) _cameraHits = new RaycastHit[32];
        int hitCount = Physics.SphereCastNonAlloc(lookTarget, cameraCollisionRadius, cameraDirection,
            _cameraHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            if (_cameraHits[i].collider == null) continue;
            Transform hitTransform = _cameraHits[i].collider.transform;
            if (hitTransform == player || hitTransform.IsChildOf(player)) continue;
            safeDistance = Mathf.Min(safeDistance, _cameraHits[i].distance - cameraWallPadding);
        }
        safeDistance = Mathf.Clamp(safeDistance, 0.15f, distance);
        transform.position = lookTarget + cameraDirection * safeDistance + shake;
        transform.LookAt(lookTarget);
    }

    void OnGUI()
    {
        if (!_limitedGodView || player == null) return;
        Vector3 screen = GetComponent<Camera>().WorldToScreenPoint(player.position + Vector3.up * 0.5f);
        if (screen.z <= 0f) return;

        Vector2 point = new Vector2(screen.x, Screen.height - screen.y);
        Color previous = GUI.color;
        // A bright, outlined reticle stays easy to spot against both the maze floor and walls.
        GUI.color = new Color(0.1f, 0.92f, 1f, 1f);
        GUI.DrawTexture(new Rect(point.x - 15f, point.y - 15f, 30f, 3f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(point.x - 15f, point.y + 12f, 30f, 3f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(point.x - 15f, point.y - 12f, 3f, 24f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(point.x + 12f, point.y - 12f, 3f, 24f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(point.x - 2f, point.y - 7f, 4f, 14f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(point.x - 7f, point.y - 2f, 14f, 4f), Texture2D.whiteTexture);

        Rect labelRect = new Rect(point.x + 22f, point.y - 14f, 94f, 26f);
        GUI.color = new Color(0.02f, 0.08f, 0.12f, 0.92f);
        GUI.DrawTexture(labelRect, Texture2D.whiteTexture);
        if (_playerMarkerStyle == null)
        {
            _playerMarkerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }
        GUI.color = Color.white;
        GUI.Label(labelRect, "你在这里", _playerMarkerStyle);
        GUI.color = previous;
    }

    public void SetMazeView(Vector3 center, float size)
    {
        mazeCenter = center;
        mazeSize = size;
    }

    public void ReturnToThirdPerson()
    {
        _godView = false;
        _limitedGodView = false;
        _cheatGodView = false;
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.orthographic = false;
    }

    public void ResetQUses() { _qUsesLeft = 3; }
    public void SetQUsesLeft(int uses) { _qUsesLeft = Mathf.Clamp(uses, 0, 3); }
    public void Shake(float duration) { _shakeTimer = duration; _shakeAmount = 0.22f; }

    void ToggleCheatGodView()
    {
        _cheatGodView = !_cheatGodView;
        _limitedGodView = false;
        _godView = _cheatGodView;
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.orthographic = _godView;
    }
}
