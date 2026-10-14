using UnityEngine;

public class ShapeRequirementGate : MonoBehaviour
{
    public bool requireCube;

    private ShapeChanger _shapeChanger;
    private Collider _barrier;
    private Renderer _renderer;
    private Transform _player;
    private bool _opened;

    public void Configure(bool cubeRequired)
    {
        requireCube = cubeRequired;
    }

    void Start()
    {
        _shapeChanger = FindObjectOfType<ShapeChanger>();
        if (_shapeChanger != null) _player = _shapeChanger.transform;
        _barrier = GetComponent<Collider>();
        _renderer = GetComponent<Renderer>();
    }

    void Update()
    {
        if (_opened || _shapeChanger == null || _player == null) return;
        if ((transform.position - _player.position).sqrMagnitude > 2.1f * 2.1f) return;
        bool correctShape = _shapeChanger.IsCube == requireCube;
        if (!correctShape) return;

        _opened = true;
        if (_barrier != null) _barrier.enabled = false;
        if (_renderer != null) _renderer.enabled = false;
    }

    public void ResetGate()
    {
        _opened = false;
        if (_barrier == null) _barrier = GetComponent<Collider>();
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_barrier != null) _barrier.enabled = true;
        if (_renderer != null) _renderer.enabled = true;
    }
}
