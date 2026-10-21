
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>验收工具：批处理进入播放模式，把 HUD/菜单等 Overlay UI 改为相机渲染后截图。</summary>
[InitializeOnLoad]
public static class PlayCapture
{
    private const string PendingKey = "RuinsCapturePending";

    static PlayCapture()
    {
        if (Application.isBatchMode && EditorPrefs.GetInt(PendingKey, 0) == 1)
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Ruins Guardian/渲染游玩截图")]
    public static void Capture()
    {
        EditorSceneManager.OpenScene("Assets/Generated/RuinsDemo.unity");
        EditorPrefs.SetInt(PendingKey, 1);
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            GameObject manager = GameObject.Find("GameManager");
            if (manager != null) manager.AddComponent<PlayCaptureRunner>();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorPrefs.DeleteKey(PendingKey);
            EditorApplication.Exit(0);
        }
    }
}

public sealed class PlayCaptureRunner : MonoBehaviour
{
    private string outDir;

    private IEnumerator Start()
    {
        outDir = Application.dataPath + "/../Logs/shots";
        Directory.CreateDirectory(outDir);
        yield return new WaitForSecondsRealtime(0.5f);

        // Overlay 画布不参与相机渲染，改由相机渲染才能截到
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        Camera cam = Camera.main;
        foreach (Canvas c in canvases)
        {
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 0.5f;
            }
        }

        yield return new WaitForSecondsRealtime(0.4f);
        Shot(cam, "play_01_menu.png");

        if (GameManager.Instance != null) GameManager.Instance.StartGame();
        yield return new WaitForSecondsRealtime(0.8f);
        Shot(cam, "play_02_hud.png");

        // 侧后方近景，观察角色与场景质感
        Transform player = GameObject.FindWithTag("Player").transform;
        OrbitFollowCamera follow = cam.GetComponent<OrbitFollowCamera>();
        Shot(cam, "play_03_chase.png");

        PlayCaptureUtil.MoveCamera(cam, player.position + new Vector3(-2.2f, 2.4f, -3.6f), player.position + Vector3.up * 1.2f);
        yield return new WaitForSecondsRealtime(0.2f);
        Shot(cam, "play_04_closeup.png");

        GameObject guard = GameObject.Find("Ruins Guard 1");
        if (guard != null)
        {
            PlayCaptureUtil.MoveCamera(cam, guard.transform.position + new Vector3(1.8f, 1.5f, -2.4f), guard.transform.position + Vector3.up * 1.1f);
            yield return new WaitForSecondsRealtime(0.2f);
            Shot(cam, "play_05_guard.png");
        }

        // 边界检查 A：玩家站到南侧围墙边、面朝墙外，用游戏相机自然跟随
        if (follow != null) follow.enabled = true;
        player.position = new Vector3(0f, 0f, -8.4f);
        player.rotation = Quaternion.LookRotation(Vector3.back);
        yield return new WaitForSecondsRealtime(0.9f);
        Shot(cam, "play_07_edge_look_out.png");

        // 边界检查 B：相机抬到墙顶高度再朝外看，检查远景层是否挡住地平线
        PlayCaptureUtil.MoveCamera(cam, new Vector3(0f, 14f, -8.4f), new Vector3(0f, 4f, 30f));
        yield return new WaitForSecondsRealtime(0.25f);
        Shot(cam, "play_08_edge_far.png");

        // 暂停面板
        if (GameManager.Instance != null) GameManager.Instance.PauseGame();
        yield return new WaitForSecondsRealtime(0.5f);
        Shot(cam, "play_06_pause.png");

        yield return new WaitForSecondsRealtime(0.1f);
        Debug.Log("CAPTURE DONE");
        EditorApplication.ExitPlaymode();
    }

    private void Shot(Camera cam, string file)
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
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Destroy(rt);
        Destroy(tex);
    }
}

public static class PlayCaptureUtil
{
    public static void MoveCamera(Camera cam, Vector3 position, Vector3 lookAt)
    {
        OrbitFollowCamera follow = cam.GetComponent<OrbitFollowCamera>();
        if (follow != null) follow.enabled = false;
        cam.transform.position = position;
        cam.transform.LookAt(lookAt);
    }
}
