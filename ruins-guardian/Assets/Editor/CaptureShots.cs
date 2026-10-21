
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>临时工具：把当前场景从若干视角渲染成 PNG，用于评估画面现状。</summary>
public static class CaptureShots
{
    private static string OutDir { get { return Application.dataPath + "/../Logs/shots"; } }

    [MenuItem("Tools/Ruins Guardian/渲染截图")]
    public static void Capture()
    {
        Directory.CreateDirectory(OutDir);
        EditorSceneManager.OpenScene("Assets/Generated/RuinsDemo.unity");

        Camera main = null;
        foreach (Camera c in Object.FindObjectsOfType<Camera>())
            if (c.CompareTag("MainCamera")) main = c;
        if (main == null) main = Object.FindObjectOfType<Camera>();

        // 编辑模式下后处理层未初始化，先关掉避免渲染异常
        foreach (Behaviour b in Object.FindObjectsOfType<Behaviour>())
            if (b != null && b.GetType().Name == "PostProcessLayer") b.enabled = false;

        GameObject player = GameObject.FindWithTag("Player");

        // 1) 原样（含全部 UI 面板）
        Render(main, "shot_01_edit_ui.png");

        // 2) 关闭所有 Canvas，纯 3D 画面
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>();
        foreach (Canvas c in canvases) c.enabled = false;
        Render(main, "shot_02_edit_noui.png");

        // 3) 高位全景
        Camera overview = CreateTemp("OverviewCam", new Vector3(0f, 26f, -22f), new Vector3(0f, 0f, 2f));
        Render(overview, "shot_03_overview.png");

        // 4) 玩家近景
        if (player != null)
        {
            Vector3 p = player.transform.position;
            Camera closeup = CreateTemp("PlayerCam", p + new Vector3(2.6f, 1.6f, -3.2f), p + Vector3.up * 1.1f);
            Render(closeup, "shot_04_player.png");
            Object.DestroyImmediate(closeup.gameObject);

            // 5) 守卫近景（取场景中第一个守卫）
            GameObject guard = GameObject.Find("Ruins Guard 1");
            if (guard != null)
            {
                Vector3 g = guard.transform.position;
                Camera gc = CreateTemp("GuardCam", g + new Vector3(2.2f, 1.4f, -2.8f), g + Vector3.up * 1.1f);
                Render(gc, "shot_05_guard.png");
                Object.DestroyImmediate(gc.gameObject);
            }
        }

        foreach (Canvas c in canvases) c.enabled = true;
        Object.DestroyImmediate(overview.gameObject);
        Debug.Log("CAPTURE DONE -> " + OutDir);
    }

    private static Camera CreateTemp(string name, Vector3 position, Vector3 lookAt)
    {
        GameObject go = new GameObject(name);
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 300f;
        go.transform.position = position;
        go.transform.LookAt(lookAt);
        return cam;
    }

    private static void Render(Camera cam, string file)
    {
        const int w = 1600, h = 900;
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Debug.Log("SHOT " + file);
    }
}
