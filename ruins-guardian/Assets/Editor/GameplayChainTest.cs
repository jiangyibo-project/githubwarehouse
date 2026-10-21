
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 机关链路测试：批处理进入播放模式，验证「符文石碑 → 石门开启 → 门后宝石可拾取 → 通关」
/// 这条 V2 新增链路在真实运行时的行为，结果写入 Logs/chain_result.txt。
/// </summary>
[InitializeOnLoad]
public static class GameplayChainTest
{
    private const string PendingKey = "RuinsChainTestPending";
    private static string ResultPath { get { return Application.dataPath + "/../Logs/chain_result.txt"; } }

    static GameplayChainTest()
    {
        if (Application.isBatchMode && EditorPrefs.GetInt(PendingKey, 0) == 1)
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Ruins Guardian/运行机关链路测试")]
    public static void Start()
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
            if (manager != null) manager.AddComponent<GameplayChainRunner>();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorPrefs.DeleteKey(PendingKey);
            bool pass = File.Exists(ResultPath) && File.ReadAllText(ResultPath).Contains("CHAIN PASS");
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}

public sealed class GameplayChainRunner : MonoBehaviour
{
    private readonly List<string> errors = new List<string>();
    private string ResultPath { get { return Application.dataPath + "/../Logs/chain_result.txt"; } }

    private void Start()
    {
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception)
            errors.Add("[Exception] " + condition);
    }

    private IEnumerator Run()
    {
        yield return null;

        Transform player = GameObject.FindGameObjectWithTag("Player").transform;
        GameManager.Instance.StartGame();
        yield return WaitFrames(20);

        // 关掉敌人 AI，避免测试过程被守卫打死干扰断言
        EnemyWanderAI[] guards = FindObjectsOfType<EnemyWanderAI>();
        for (int i = 0; i < guards.Length; i++) guards[i].SetAIEnabled(false);

        RunePillar[] pillars = FindObjectsOfType<RunePillar>();
        RuneGate gate = FindObjectOfType<RuneGate>();
        if (pillars.Length == 0) errors.Add("场景中没有找到 RunePillar");
        if (gate == null) errors.Add("场景中没有找到 RuneGate");

        if (gate != null && gate.TotalPillars != pillars.Length)
            errors.Add("石门登记符文数量 " + gate.TotalPillars + " 与场景石碑数量 " + pillars.Length + " 不一致");

        for (int i = 0; i < pillars.Length; i++)
        {
            Vector3 p = pillars[i].transform.position;
            player.position = new Vector3(p.x, p.y + 0.05f, p.z - 1.6f);
            yield return WaitFrames(8);

            if (pillars[i].IsActivated)
                errors.Add("石碑在未调用 Activate 时就已激活：" + pillars[i].Label);

            pillars[i].Activate();
            yield return WaitFrames(8);

            if (!pillars[i].IsActivated)
                errors.Add("调用 Activate 后石碑仍未激活：" + pillars[i].Label);
        }

        yield return new WaitForSecondsRealtime(2.8f);
        if (gate != null && !gate.IsOpen)
            errors.Add("3 块石碑全部激活后石门仍未开启");

        Collectible[] gems = FindObjectsOfType<Collectible>();
        for (int i = 0; i < gems.Length; i++)
        {
            if (gems[i].IsCollected) continue;
            Vector3 g = gems[i].transform.position;
            player.position = new Vector3(g.x, g.y, g.z);
            yield return WaitFrames(20);
            if (!gems[i].IsCollected)
                errors.Add("宝石无法拾取：" + gems[i].name);
        }

        yield return new WaitForSecondsRealtime(2.4f);
        if (GameManager.Instance.State != GameManager.GameState.Won)
            errors.Add("集齐全部宝石后未进入胜利状态，当前状态：" + GameManager.Instance.State);

        Finish();
    }

    private void Finish()
    {
        string report = errors.Count == 0
            ? "CHAIN PASS\n符文链路正常：3 块石碑激活 → 石门开启 → 门后宝石可拾取 → 通关。"
            : "CHAIN FAIL\n" + string.Join("\n---\n", errors);
        File.WriteAllText(ResultPath, report);
        Application.logMessageReceived -= OnLog;
        StartCoroutine(QuitNextFrame());
    }

    private IEnumerator QuitNextFrame()
    {
        yield return null;
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }
}
