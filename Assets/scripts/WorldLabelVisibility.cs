using UnityEngine;

/// <summary>Hides generated 3D instruction labels until the player is nearby.</summary>
public class WorldLabelVisibility : MonoBehaviour
{
    public float maxVisibleDistance = 7f;

    private Renderer[] _labelRenderers;
    private Transform _cameraTransform;
    private Vector3 _facingNormal;

    void Awake()
    {
        _labelRenderers = GetComponentsInChildren<Renderer>(true);
    }

    void LateUpdate()
    {
        if (_cameraTransform == null && Camera.main != null)
            _cameraTransform = Camera.main.transform;
        if (_labelRenderers == null || _cameraTransform == null) return;

        Vector3 toCamera = _cameraTransform.position - transform.position;
        float distanceSqr = toCamera.sqrMagnitude;
        bool closeEnough = distanceSqr <= maxVisibleDistance * maxVisibleDistance;
        bool onFrontSide = _facingNormal.sqrMagnitude < 0.01f || Vector3.Dot(toCamera.normalized, _facingNormal) > 0.05f;
        bool visible = closeEnough && onFrontSide;
        for (int i = 0; i < _labelRenderers.Length; i++)
            if (_labelRenderers[i] != null) _labelRenderers[i].enabled = visible;

        if (visible)
        {
            Vector3 faceDirection = toCamera;
            faceDirection.y = 0f;
            if (faceDirection.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(faceDirection.normalized, Vector3.up);
        }
    }

    public void SetFacing(Vector3 normal)
    {
        _facingNormal = normal.normalized;
    }
}
