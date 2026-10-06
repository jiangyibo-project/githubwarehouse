using System.Collections.Generic;
using UnityEngine;

public class StarEnemy : MonoBehaviour
{
    public int Health { get; private set; } = 3;
    private Transform _target;
    private Renderer _renderer;
    private bool _hasSeenTarget;
    private float _speed = 2.65f;
    private float _wallBlockedTime;
    private bool _recoveringFromWall;
    private Vector3 _wallRecoveryPoint;
    [SerializeField] private float healthBarVisibleDistance = 9f;
    private float _flash;
    private MaterialPropertyBlock _block;
    private readonly RaycastHit[] _wallHits = new RaycastHit[16];
    private static Mesh _starMesh;

    public void Configure(Transform target)
    {
        _target = target;
        EnsurePropertyBlock();
        MeshFilter filter = GetComponent<MeshFilter>();
        if (_starMesh == null) _starMesh = CreateStarMesh();
        filter.sharedMesh = _starMesh;
        _renderer = GetComponent<Renderer>();
        _renderer.sharedMaterial = GetComponent<Renderer>().sharedMaterial;
        SetColor(new Color(1f, 0.66f, 0.12f));
        SphereCollider body = GetComponent<SphereCollider>();
        if (body == null) body = gameObject.AddComponent<SphereCollider>();
        body.isTrigger = true; body.radius = 0.48f; body.center = new Vector3(0f, 0.42f, 0f);
    }

    void Update()
    {
        if (_target == null) return;
        _flash = Mathf.Max(0f, _flash - Time.deltaTime);
        SetColor(_flash > 0f ? Color.white : new Color(1f, 0.66f, 0.12f));
        float distance = Vector3.Distance(transform.position, _target.position);
        if (!_hasSeenTarget && distance < 16f && CanSeeTarget()) _hasSeenTarget = true;
        ThirdPersonCam cameraMode = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCam>() : null;
        bool topDownView = cameraMode != null && (cameraMode.LimitedGodViewActive || cameraMode.CheatGodViewActive);
        if (topDownView) transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        if (!_hasSeenTarget) return;

        Vector3 waypoint = _recoveringFromWall
            ? _wallRecoveryPoint
            : MazeGenerator.Instance != null
                ? MazeGenerator.Instance.GetChaseWaypoint(transform.position, _target.position)
                : _target.position;
        Vector3 direction = waypoint - transform.position; direction.y = 0f;
        if (direction.sqrMagnitude > 0.05f)
        {
            float step = Mathf.Min(_speed * Time.deltaTime, direction.magnitude);
            Vector3 moveDirection = direction.normalized;
            // Only maze walls stop the pursuer. Props such as trees and ramps should
            // not trap it in a narrow corridor or make it freeze beside the route.
            Vector3 castOrigin = transform.position + Vector3.up * 0.42f;
            int hitCount = Physics.SphereCastNonAlloc(castOrigin, 0.44f, moveDirection, _wallHits,
                step + 0.04f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool blockedByWall = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = _wallHits[i].collider;
                if (hitCollider == null) continue;
                Transform hitTransform = hitCollider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
                if (hitTransform.name == "MazeWalls" || hitCollider.GetComponentInParent<FakeWall>() != null)
                {
                    blockedByWall = true;
                    break;
                }
            }
            if (!blockedByWall)
            {
                transform.position += moveDirection * step;
                _wallBlockedTime = 0f;
                if (_recoveringFromWall && (transform.position - _wallRecoveryPoint).sqrMagnitude < 0.04f)
                    _recoveringFromWall = false;
            }
            else
            {
                _wallBlockedTime += Time.deltaTime;
                if (_wallBlockedTime > 0.35f && !_recoveringFromWall && MazeGenerator.Instance != null)
                {
                    // Turn back toward the current cell center and walk there; never
                    // teleport the enemy through the maze when it meets a wall corner.
                    _wallRecoveryPoint = MazeGenerator.Instance.GetChaseRecoveryPoint(transform.position);
                    _recoveringFromWall = (transform.position - _wallRecoveryPoint).sqrMagnitude > 0.04f;
                    _wallBlockedTime = 0f;
                }
            }
            if (!topDownView) transform.rotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        }
        else
        {
            _wallBlockedTime = 0f;
            if (_recoveringFromWall) _recoveringFromWall = false;
        }
    }

    bool CanSeeTarget()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Vector3 target = _target.position + Vector3.up * 0.4f;
        Vector3 delta = target - origin;
        RaycastHit[] hits = Physics.RaycastAll(origin, delta.normalized, delta.magnitude,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].collider.transform;
            if (hit == transform || hit.IsChildOf(transform) || hit == _target || hit.IsChildOf(_target)) continue;
            return false;
        }
        return true;
    }

    public void TakeDamage(int amount)
    {
        if (Health <= 0) return;
        Health = Mathf.Max(0, Health - amount);
        _flash = 0.14f;
        GameSfx.Play(_target != null ? _target.gameObject : gameObject, Health == 0 ? GameSound.EnemyDown : GameSound.EnemyHit);
        if (Health == 0) Destroy(gameObject);
    }

    public static void AlertByShot(Vector3 shotOrigin, float alertRadius)
    {
        StarEnemy[] enemies = FindObjectsOfType<StarEnemy>();
        float radiusSqr = alertRadius * alertRadius;
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] == null) continue;
            Vector3 delta = enemies[i].transform.position - shotOrigin;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSqr) enemies[i]._hasSeenTarget = true;
        }
    }

    void OnTriggerStay(Collider other)
    {
        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health != null) health.TakeDamage(1);
    }

    void OnGUI()
    {
        if (MazeGenerator.Instance != null && !MazeGenerator.Instance.GameStarted) return;
        if (Health <= 0 || Camera.main == null || _target == null ||
            Vector3.Distance(transform.position, _target.position) > healthBarVisibleDistance) return;
        Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.25f);
        if (screen.z <= 0f) return;
        Rect bar = new Rect(screen.x - 20f, Screen.height - screen.y, 40f, 7f);
        Color old = GUI.color;
        GUI.color = new Color(0.12f, 0.08f, 0.08f, 0.9f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.12f, 0.15f, 1f); GUI.DrawTexture(new Rect(bar.x + 1f, bar.y + 1f, 38f * Health / 3f, 5f), Texture2D.whiteTexture);
        GUI.color = old;
    }

    void SetColor(Color color)
    {
        if (_renderer == null) return;
        EnsurePropertyBlock();
        _renderer.GetPropertyBlock(_block); _block.SetColor("_Color", color); _block.SetColor("_BaseColor", color); _renderer.SetPropertyBlock(_block);
    }

    void EnsurePropertyBlock()
    {
        if (_block == null) _block = new MaterialPropertyBlock();
    }

    static Mesh CreateStarMesh()
    {
        const int points = 10;
        const float halfThickness = 0.24f;
        Vector3[] vertices = new Vector3[points * 2 + 2];
        int[] triangles = new int[points * 12];
        vertices[0] = Vector3.zero;
        for (int i = 0; i < points; i++)
        {
            float angle = (90f - i * 36f) * Mathf.Deg2Rad;
            float radius = i % 2 == 0 ? 0.78f : 0.36f;
            Vector3 outline = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            vertices[i + 1] = outline + Vector3.forward * halfThickness;
            vertices[points + 2 + i] = outline - Vector3.forward * halfThickness;
        }
        vertices[0] = Vector3.forward * halfThickness;
        vertices[points + 1] = Vector3.back * halfThickness;
        int triangle = 0;
        for (int i = 0; i < points; i++)
        {
            int currentFront = i + 1;
            int nextFront = (i + 1) % points + 1;
            int currentBack = points + 2 + i;
            int nextBack = points + 2 + (i + 1) % points;

            // Front and back caps.
            triangles[triangle++] = 0; triangles[triangle++] = nextFront; triangles[triangle++] = currentFront;
            triangles[triangle++] = points + 1; triangles[triangle++] = currentBack; triangles[triangle++] = nextBack;
            // Extruded side wall gives the star visible thickness from low angles.
            triangles[triangle++] = currentFront; triangles[triangle++] = nextFront; triangles[triangle++] = nextBack;
            triangles[triangle++] = currentFront; triangles[triangle++] = nextBack; triangles[triangle++] = currentBack;
        }
        Mesh mesh = new Mesh { name = "FivePointStarEnemyMesh" };
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
