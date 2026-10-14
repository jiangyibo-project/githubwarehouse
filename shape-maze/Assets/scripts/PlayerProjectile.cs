using UnityEngine;

public class PlayerProjectile : MonoBehaviour
{
    private Vector3 _direction;
    private Transform _shooter;
    private float _speed = 14f;
    private float _remainingDistance = 18f;

    public void Launch(Vector3 direction, Transform shooter) { _direction = direction.normalized; _shooter = shooter; }

    void Update()
    {
        float step = _speed * Time.deltaTime;
        RaycastHit[] hits = Physics.RaycastAll(transform.position, _direction, step + 0.12f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform) || hitTransform == _shooter || hitTransform.IsChildOf(_shooter)) continue;
            StarEnemy enemy = hits[i].collider.GetComponentInParent<StarEnemy>();
            if (enemy != null) enemy.TakeDamage(1);
            Destroy(gameObject);
            return;
        }
        transform.position += _direction * step;
        _remainingDistance -= step;
        if (_remainingDistance <= 0f) Destroy(gameObject);
    }
}
