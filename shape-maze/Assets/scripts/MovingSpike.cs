using UnityEngine;

public class MovingSpike : MonoBehaviour
{
    private Vector3 _center;
    private Vector3 _axis;
    private float _range = 0.85f;
    private float _speed = 1.7f;
    private float _offset;
    private int _direction = 1;
    public void Configure(Vector3 center, Vector3 axis) { _center = center; _axis = axis.normalized; }
    void Update()
    {
        _offset += _direction * _speed * Time.deltaTime;
        if (_offset >= _range) { _offset = _range; _direction = -1; }
        else if (_offset <= -_range) { _offset = -_range; _direction = 1; }
        transform.position = _center + _axis * _offset;
    }
    void OnTriggerEnter(Collider other)
    {
        PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
        if (health != null) health.TakeDamage();
    }
}
