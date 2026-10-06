using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    public MazeItem item;

    private Vector3 _basePosition;
    private Renderer _itemRenderer;
    private GameObject _beacon;
    private float _phase;
    private MaterialPropertyBlock _block;
    private bool _fullNoticeShown;

    void Start()
    {
        _basePosition = transform.position;
        _itemRenderer = GetComponent<Renderer>();
        _block = new MaterialPropertyBlock();
        _phase = (transform.position.x * 0.73f + transform.position.z * 0.41f) % 6.28f;

        Color color = GetItemColor();
        _beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _beacon.name = "ItemBeacon";
        _beacon.transform.SetParent(transform.parent, true);
        _beacon.transform.position = _basePosition + Vector3.down * 0.56f;
        _beacon.transform.localScale = new Vector3(0.88f, 0.025f, 0.88f);
        Collider beaconCollider = _beacon.GetComponent<Collider>();
        if (beaconCollider != null) beaconCollider.enabled = false;
        SetColor(_beacon.GetComponent<Renderer>(), Color.Lerp(color, Color.white, 0.3f));
    }

    void Update()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f + _phase);
        transform.position = _basePosition + Vector3.up * (0.12f + pulse * 0.12f);
        transform.Rotate(Vector3.up, 55f * Time.deltaTime, Space.World);
        if (_beacon != null)
        {
            float size = 0.82f + pulse * 0.2f;
            _beacon.transform.localScale = new Vector3(size, 0.025f, size);
        }
        if (_itemRenderer != null)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            _itemRenderer.GetPropertyBlock(_block);
            Color glow = Color.Lerp(GetItemColor(), Color.white, pulse * 0.38f);
            _block.SetColor("_Color", glow);
            _block.SetColor("_BaseColor", glow);
            _itemRenderer.SetPropertyBlock(_block);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    void OnTriggerStay(Collider other)
    {
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory != null && inventory.HeldItem == MazeItem.None) TryCollect(other);
    }

    void TryCollect(Collider other)
    {
        PlayerInventory inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null) return;
        if (inventory.TryPickup(item))
        {
            GameSfx.Play(inventory.gameObject, GameSound.Pickup);
            if (MazeGenerator.Instance != null) MazeGenerator.Instance.ShowNotice("获得道具 · " + inventory.ItemLabel + "  |  按 E 使用");
            Destroy(gameObject);
        }
        else if (!_fullNoticeShown && MazeGenerator.Instance != null)
        {
            _fullNoticeShown = true;
            MazeGenerator.Instance.ShowNotice("道具栏已满 · 按 E 用掉当前道具后再拾取");
        }
    }

    void OnDestroy()
    {
        if (_beacon != null) Destroy(_beacon);
    }

    Color GetItemColor()
    {
        switch (item)
        {
            case MazeItem.Hammer: return new Color(1f, 0.55f, 0.18f);
            case MazeItem.Fly: return new Color(0.2f, 0.85f, 1f);
            case MazeItem.Shield: return new Color(0.6f, 0.42f, 1f);
            case MazeItem.Heal: return new Color(0.24f, 1f, 0.48f);
            default: return Color.white;
        }
    }

    static void SetColor(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetColor("_Color", color);
        properties.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(properties);
    }
}
