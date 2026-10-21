using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 击杀探针：批处理播放模式下用 TryHit 反射结算 8 次攻击，验证守卫确实可被杀，
/// 并把每次结算的命中/血量/相对位置写入 Logs/killprobe_result.txt（供无头排查）。
/// 架构复制 GameplayChainTest（已验证可在批处理进入播放模式）。
/// </summary>
[InitializeOnLoad]
public static class KillGuardProbe
{
    private const string PendingKey = "RuinsKillProbePending";
    private static string ResultPath { get { return Application.dataPath + "/../Logs/killprobe_result.txt"; } }

    static KillGuardProbe()
    {
        if (Application.isBatchMode && EditorPrefs.GetInt(PendingKey, 0) == 1)
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Ruins Guardian/运行击杀探针")]
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
            if (manager != null) manager.AddComponent<KillGuardProbeRunner>();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorPrefs.DeleteKey(PendingKey);
            bool pass = File.Exists(ResultPath) && File.ReadAllText(ResultPath).Contains("PROBE PASS");
            EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}

public sealed class KillGuardProbeRunner : MonoBehaviour
{
    private string ResultPath { get { return Application.dataPath + "/../Logs/killprobe_result.txt"; } }

    private void Start()
    {
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return null;
        Transform player = GameObject.FindGameObjectWithTag("Player").transform;
        PlayerCombat combat = player.GetComponent<PlayerCombat>();
        EnemyWanderAI[] guards = FindObjectsOfType<EnemyWanderAI>();
        MethodInfo tryHit = typeof(PlayerCombat).GetMethod("TryHit", BindingFlags.Instance | BindingFlags.NonPublic);

        string report = "";
        GameManager.Instance.StartGame();
        yield return new WaitForSeconds(0.5f);

        EnemyWanderAI guard = guards[0];
        guard.SetAIEnabled(false);
        for (int i = 0; i < guards.Length; i++) guards[i].SetAIEnabled(false);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Health guardHealth = guard.GetComponent<Health>();
            if (guardHealth.IsDead) break;

            Vector3 toGuard = guard.transform.position - player.position;
            toGuard.y = 0f;
            if (toGuard.sqrMagnitude > 0.01f)
            {
                player.position = guard.transform.position - toGuard.normalized * 1.2f;
                player.rotation = Quaternion.LookRotation(toGuard.normalized);
            }
            yield return new WaitForSeconds(0.15f);
            bool hit = (bool)tryHit.Invoke(combat, null);
            Collider[] nearby = Physics.OverlapSphere(player.position + Vector3.up * 0.5f, 2.5f);
            report += "attempt=" + attempt + " hit=" + hit + " hp=" + guardHealth.CurrentHealth.ToString("0.0")
                + " player=" + player.position.ToString("0.00") + " guard=" + guard.transform.position.ToString("0.00")
                + " near=" + nearby.Length + "\n";
        }

        bool dead = guard.GetComponent<Health>().IsDead;
        report = (dead ? "PROBE PASS\n" : "PROBE FAIL\n") + report;
        File.WriteAllText(ResultPath, report);
        EditorApplication.ExitPlaymode();
    }
}
