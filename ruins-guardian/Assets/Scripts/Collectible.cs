using UnityEngine;

/// <summary>遗迹宝石：持续旋转浮动，玩家靠近时拾取。</summary>
public sealed class Collectible : MonoBehaviour
{
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private float floatHeight = 0.18f;
    [SerializeField] private float floatSpeed = 2f;
    [SerializeField] private float pickupRange = 1.3f;
    [SerializeField] private Transform player;
    [SerializeField] private Light gemLight;
    [SerializeField] private Transform gemModel;

    private Vector3 basePosition;
    private bool collected;

    public bool IsCollected { get { return collected; } }

    private void Start()
    {
        basePosition = transform.position;
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null) player = playerObject.transform;
        }
    }

    private void Update()
    {
        if (collected) return;

        float t = Time.time * floatSpeed;
        Vector3 position = basePosition;
        position.y += Mathf.Sin(t) * floatHeight;
        transform.position = position;
        Transform model = gemModel != null ? gemModel : transform;
        model.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        if (gemLight != null)
            gemLight.intensity = 1.2f + Mathf.Sin(t * 1.5f) * 0.4f;

        if (player != null && Vector3.Distance(transform.position, player.position) < pickupRange)
            Collect();
    }

    private void Collect()
    {
        collected = true;
        GameManager.Instance.NotifyGemCollected(transform.position);
        gameObject.SetActive(false);
    }

    public void Restore()
    {
        collected = false;
        gameObject.SetActive(true);
        basePosition = transform.position;
    }
}
