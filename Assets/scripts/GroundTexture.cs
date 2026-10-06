using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class GroundTexture : MonoBehaviour
{
    [Min(1)] public int tilesAcross = 40;
    public Color tileA = new Color(0.24f, 0.29f, 0.34f, 1f);
    public Color tileB = new Color(0.28f, 0.33f, 0.38f, 1f);
    public Color gridColor = new Color(0.18f, 0.23f, 0.28f, 1f);

    void Awake()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        texture.name = "Procedural Ground Tiles";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Point;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool gridLine = x < 2 || y < 2;
            bool alternate = ((x / 32) + (y / 32)) % 2 == 0;
            texture.SetPixel(x, y, gridLine ? gridColor : (alternate ? tileA : tileB));
        }
        texture.Apply();

        Renderer targetRenderer = GetComponent<Renderer>();
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        targetRenderer.GetPropertyBlock(properties);
        properties.SetTexture("_MainTex", texture);
        properties.SetTexture("_BaseMap", texture);
        properties.SetVector("_MainTex_ST", new Vector4(tilesAcross, tilesAcross, 0f, 0f));
        properties.SetVector("_BaseMap_ST", new Vector4(tilesAcross, tilesAcross, 0f, 0f));
        targetRenderer.SetPropertyBlock(properties);
    }
}
