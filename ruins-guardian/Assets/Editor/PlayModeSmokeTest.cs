using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// 冒烟测试：批处理模式下自动进入播放模式，模拟玩家受伤、守卫击杀、
/// 宝石收集与胜利流程，收集运行时异常并写入结果文件。
/// </summary>
[InitializeOnLoad]
public static class PlayModeSmokeTest
{
    private const string PendingKey = "RuinsSmokeTestPending";
    private static string ResultPath { get { return Application.dataPath + "/../Logs/smoke_result.txt"; } }

    static PlayModeSmokeTest()
    {
        if (Application.isBatchMode && EditorPrefs.GetInt(PendingKey, 0) == 1)
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
    }

    [MenuItem("Tools/Ruins Guardian/运行冒烟测试")]
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
            if (manager != null)
                manager.AddComponent<SmokeTestRunner>();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorPrefs.DeleteKey(PendingKey);
            bool pass = File.Exists(ResultPath) && File.ReadAllText(ResultPath).Contains("SMOKE PASS");
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}

/// <summary>播放模式下的自动测试驱动。</summary>
public sealed class SmokeTestRunner : MonoBehaviour
{
    private readonly List<string> errors = new List<string>();
    private string ResultPath { get { return Application.dataPath + "/../Logs/smoke_result.txt"; } }

    private Health playerHealth;
    private PlayerCombat playerCombat;
    private Transform player;
    private EnemyWanderAI[] guards;
    private Collectible[] gems;
    private MethodInfo tryHitMethod;

    private void Start()
    {
        Application.logMessageReceived += OnLog;
        StartCoroutine(Run());
    }

    private void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Assert)
            errors.Add("[" + type + "] " + condition + "\n" + stackTrace);
    }

    private IEnumerator Run()
    {
        yield return null;

        player = GameObject.FindGameObjectWithTag("Player").transform;
        playerHealth = player.GetComponent<Health>();
        playerCombat = player.GetComponent<PlayerCombat>();
        guards = FindObjectsOfType<EnemyWanderAI>();
        gems = FindObjectsOfType<Collectible>();
        tryHitMethod = typeof(PlayerCombat).GetMethod("TryHit",
            BindingFlags.Instance | BindingFlags.NonPublic);

        GameManager.Instance.StartGame();
        yield return WaitFrames(30);

        // 1. 让守卫攻击玩家：瞬移到第一个守卫面前
        EnemyWanderAI targetGuard = guards[0];
        player.position = targetGuard.transform.position + targetGuard.transform.forward * 1.5f;
        player.rotation = Quaternion.LookRotation(targetGuard.transform.position - player.position);
        yield return new WaitForSeconds(4f);
        if (playerHealth.CurrentHealth >= playerHealth.MaxHealth)
            errors.Add("玩家在守卫攻击 4 秒后仍未受伤，敌人攻击判定可能失效");
        else
            Debug.Log("冒烟测试：玩家受伤生效，剩余血量 " + playerHealth.CurrentHealth);

        // 2. 玩家击杀守卫：把玩家对准守卫，反射调用攻击判定
        yield return KillGuardRoutine(targetGuard);
        if (targetGuard.GetComponent<Health>().IsDead)
            Debug.Log("冒烟测试：守卫被击倒");
        else
            errors.Add("多次攻击后守卫仍未死亡，玩家伤害判定可能失效");

        // 3. 收集所有宝石
        for (int i = 0; i < gems.Length; i++)
        {
            if (gems[i].IsCollected) continue;
            Vector3 position = gems[i].transform.position;
            player.position = new Vector3(position.x, 0f, position.z);
            yield return WaitFrames(30);
            if (!gems[i].IsCollected)
                errors.Add("宝石未能被拾取：" + gems[i].name);
        }

        // 等待胜利流程
        yield return new WaitForSecondsRealtime(2.2f);
        if (GameManager.Instance.State != GameManager.GameState.Won)
            errors.Add("全部宝石收集后未进入胜利状态");

        yield return WaitFrames(10);
        Finish();
    }

    private IEnumerator KillGuardRoutine(EnemyWanderAI guard)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            if (guard.GetComponent<Health>().IsDead) yield break;
            Vector3 toGuard = guard.transform.position - player.position;
            toGuard.y = 0f;
            if (toGuard.sqrMagnitude > 0.01f)
            {
                player.position = guard.transform.position - toGuard.normalized * 1.2f;
                player.rotation = Quaternion.LookRotation(toGuard.normalized);
            }
            yield return WaitFrames(3);
            tryHitMethod.Invoke(playerCombat, null);
            yield return WaitFrames(25);
        }
    }

    private void Finish()
    {
        string report;
        if (errors.Count == 0)
        {
            report = "SMOKE PASS\n所有运行时流程正常：受伤、击杀、拾取、胜利。";
        }
        else
        {
            report = "SMOKE FAIL\n" + string.Join("\n---\n", errors);
        }
        File.WriteAllText(ResultPath, report);
        Application.logMessageReceived -= OnLog;
        StartCoroutine(QuitNextFrame());
    }

    private IEnumerator QuitNextFrame()
    {
        yield return null;
        // 保持 pending 标志，域重载后静态构造会重新注册回调，
        // 由 EnteredEditMode 统一关闭标志并退出批处理。
        EditorApplication.ExitPlaymode();
    }

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            yield return null;
    }
}
