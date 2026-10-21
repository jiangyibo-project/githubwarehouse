using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 编辑模式击杀探针：不需要进入播放模式，直接打开生成场景、反射调用 TryHit，
/// 把 8 次结算与守卫血量写入 Logs/editorkill_result.txt，用于确认玩家伤害判定有效。
/// </summary>
public static class EditorKillProbe
{
    [MenuItem("Tools/Ruins Guardian/运行编辑模式击杀探针")]
    public static void Start()
    {
        EditorSceneManager.OpenScene("Assets/Generated/RuinsDemo.unity");
        Transform player = GameObject.FindGameObjectWithTag("Player").transform;
        PlayerCombat combat = player.GetComponent<PlayerCombat>();
        EnemyWanderAI[] guards = Object.FindObjectsOfType<EnemyWanderAI>();

        MethodInfo tryHit = typeof(PlayerCombat).GetMethod("TryHit", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo awake = typeof(Health).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);

        Health guardHealth = guards[0].GetComponent<Health>();
        if (guardHealth == null) { File.WriteAllText(ResultPath(), "PROBE FAIL\n未找到守卫 Health"); return; }
        awake.Invoke(guardHealth, null);

        string report = "";
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 toGuard = guardHealth.transform.position - player.position;
            toGuard.y = 0f;
            if (toGuard.sqrMagnitude > 0.01f)
            {
                player.position = guardHealth.transform.position - toGuard.normalized * 1.2f;
                player.rotation = Quaternion.LookRotation(toGuard.normalized);
            }
            // 编辑模式下 Time.time 冻结，守卫会一直卡在攻击后的无敌帧；清掉它来验证伤害链
            FieldInfo invuln = typeof(Health).GetField("invulnerableUntil", BindingFlags.Instance | BindingFlags.NonPublic);
            invuln.SetValue(guardHealth, 0f);
            Physics.SyncTransforms();
            bool hit = (bool)tryHit.Invoke(combat, null);
            Collider[] nearby = Physics.OverlapSphere(player.position + Vector3.up * 0.5f, 2.5f);
            report += "attempt=" + attempt + " hit=" + hit + " hp=" + guardHealth.CurrentHealth.ToString("0.0")
                + " player=" + player.position.ToString("0.00") + " guard=" + guardHealth.transform.position.ToString("0.00")
                + " near=" + nearby.Length + "\n";
        }

        bool dead = guardHealth.IsDead;
        report = (dead ? "PROBE PASS\n" : "PROBE FAIL\n") + report;
        File.WriteAllText(ResultPath(), report);
        Debug.Log("EditorKillProbe 完成：" + (dead ? "守卫被杀" : "守卫未死"));
    }

    private static string ResultPath()
    {
        return Application.dataPath + "/../Logs/editorkill_result.txt";
    }
}
