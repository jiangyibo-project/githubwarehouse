using UnityEngine;

public class FakeWall : MonoBehaviour
{
    private bool _mandatoryPath;

    public void Configure(bool mandatoryPath)
    {
        _mandatoryPath = mandatoryPath;
    }

    public void Break()
    {
        if (_mandatoryPath && MazeGenerator.Instance != null)
            MazeGenerator.Instance.NotifyMandatoryFakeWallBroken();

        Renderer wallRenderer = GetComponent<Renderer>();
        Color debrisColor = new Color(0.65f, 0.7f, 0.74f);
        if (wallRenderer != null)
        {
            MaterialPropertyBlock wallBlock = new MaterialPropertyBlock();
            wallRenderer.GetPropertyBlock(wallBlock);
            Color tint = wallBlock.GetColor("_Color");
            if (tint.maxColorComponent > 0.01f) debrisColor = tint;
        }
        for (int i = 0; i < 5; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "FakeWallDebris";
            shard.transform.position = transform.position + new Vector3(Random.Range(-0.65f, 0.65f), Random.Range(0.2f, 2.1f), Random.Range(-0.55f, 0.55f));
            if (transform.parent != null) shard.transform.SetParent(transform.parent, true);
            shard.transform.localScale = Vector3.one * Random.Range(0.12f, 0.24f);
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            shard.GetComponent<Renderer>().GetPropertyBlock(block);
            block.SetColor("_Color", debrisColor); block.SetColor("_BaseColor", debrisColor);
            shard.GetComponent<Renderer>().SetPropertyBlock(block);
            Rigidbody body = shard.AddComponent<Rigidbody>();
            body.velocity = Random.insideUnitSphere * 2f + Vector3.up * 1.6f;
            body.angularVelocity = Random.insideUnitSphere * 5f;
            Destroy(shard, 1.4f);
        }
        Destroy(gameObject);
    }
}
