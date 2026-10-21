using UnityEngine;

/// <summary>
/// 程序化爆裂碎块：死亡爆炸用。碎块由 CreatePrimitive 生成（不新增材质，复用传入的共享材质），
/// 带初速 + 重力 + 自旋，收缩到零后整体自毁。生命周期独立于施放者，施放者 Death 禁用后仍会播完。
/// </summary>
public sealed class BurstFx : MonoBehaviour
{
    private struct Shard
    {
        public Transform Transform;
        public Vector3 Velocity;
        public Vector3 Spin;
        public Vector3 Scale;
    }

    private Shard[] shards;
    private float elapsed;
    private float duration = 0.75f;
    private float gravity = -14f;

    /// <summary>在世界坐标生成一次爆裂。</summary>
    public static void Play(Vector3 position, Material material, int count, float power)
    {
        if (material == null || count <= 0) return;
        GameObject root = new GameObject("爆裂碎块");
        root.transform.position = position;
        BurstFx fx = root.AddComponent<BurstFx>();
        fx.Build(material, count, power);
    }

    private void Build(Material material, int count, float power)
    {
        shards = new Shard[count];
        for (int i = 0; i < count; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Collider shardCollider = shard.GetComponent<Collider>();
            if (shardCollider != null) Destroy(shardCollider);
            shard.name = "碎块";
            shard.transform.SetParent(transform, false);

            Vector3 scale = Vector3.one * Random.Range(0.07f, 0.18f);
            shard.transform.localScale = scale;
            shard.transform.localPosition = Random.insideUnitSphere * 0.18f;
            shard.transform.rotation = Random.rotation;

            Renderer shardRenderer = shard.GetComponent<Renderer>();
            if (shardRenderer != null) shardRenderer.sharedMaterial = material;

            Vector3 velocity = Random.insideUnitSphere;
            velocity.y = Mathf.Abs(velocity.y) * 1.4f + 0.35f;
            shards[i] = new Shard
            {
                Transform = shard.transform,
                Velocity = velocity.normalized * power * Random.Range(0.6f, 1.3f),
                Spin = Random.insideUnitSphere * 360f,
                Scale = scale
            };
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        elapsed += dt;
        float t = Mathf.Clamp01(elapsed / duration);

        if (shards != null)
        {
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i].Transform == null) continue;
                Vector3 velocity = shards[i].Velocity + Vector3.up * (gravity * dt);
                shards[i].Velocity = velocity;
                shards[i].Transform.position += velocity * dt;
                shards[i].Transform.Rotate(shards[i].Spin * dt, Space.World);
                shards[i].Transform.localScale = shards[i].Scale * Mathf.Lerp(1f, 0.12f, t * t);
            }
        }

        if (t >= 1f) Destroy(gameObject);
    }
}
