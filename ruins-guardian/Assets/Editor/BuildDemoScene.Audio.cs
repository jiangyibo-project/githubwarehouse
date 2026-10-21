using UnityEngine;

/// <summary>
/// 生成器音频接线（partial）：把 v2 音频系统挂进程序化生成的场景。
/// 由 BuildDemoScene.Build() 在创建完 GameManager 与 BGM 声源之后调用一次：
///     BuildAudioV2(managerObject, bgmSource, playerHealth, player.transform);
/// 所有参数都可省略，缺省时会在当前场景里自动查找。
/// </summary>
public static partial class BuildDemoScene
{
    /// <summary>
    /// 挂载音频导演并接线。返回创建出来的 AudioDirector（挂不上时返回 null）。
    /// </summary>
    internal static AudioDirector BuildAudioV2(GameObject managerObject = null, GameObject bgmSource = null,
        Health playerHealth = null, Transform player = null)
    {
        if (managerObject == null) managerObject = GameObject.Find("GameManager");

        ProceduralAudioBank bank = managerObject != null ? managerObject.GetComponent<ProceduralAudioBank>() : null;
        if (bank == null) bank = Object.FindObjectOfType<ProceduralAudioBank>();

        if (bgmSource == null)
        {
            GameObject found = GameObject.Find("BGM Audio Source");
            bgmSource = found != null ? found : CreateBgmSource();
        }
        AudioSource masterBgm = bgmSource != null ? bgmSource.GetComponent<AudioSource>() : null;
        if (masterBgm != null)
        {
            // 音乐层的播放/暂停与总音量都由这条既有声源当传输载体，配置必须一致
            masterBgm.loop = true;
            masterBgm.playOnAwake = false;
            masterBgm.spatialBlend = 0f;
            if (masterBgm.volume <= 0.01f) masterBgm.volume = 0.45f;
        }

        if (playerHealth == null)
        {
            Health[] healths = Object.FindObjectsOfType<Health>();
            for (int i = 0; i < healths.Length; i++)
            {
                if (healths[i] != null && healths[i].IsPlayer) { playerHealth = healths[i]; break; }
            }
        }
        if (player == null && playerHealth != null) player = playerHealth.transform;

        GameObject directorObject = GameObject.Find("Audio Director");
        if (directorObject == null) directorObject = new GameObject("Audio Director");
        AudioDirector director = directorObject.GetComponent<AudioDirector>();
        if (director == null) director = directorObject.AddComponent<AudioDirector>();

        if (bank != null) SetPrivate(director, "bank", bank);
        if (masterBgm != null) SetPrivate(director, "masterBgmSource", masterBgm);
        if (playerHealth != null) SetPrivate(director, "playerHealth", playerHealth);
        if (player != null) SetPrivate(director, "player", player);
        return director;
    }

    /// <summary>兜底：场景里没有 "BGM Audio Source" 时临时建一个（正常流程由 BuildBgmSource 负责）。</summary>
    private static GameObject CreateBgmSource()
    {
        GameObject go = new GameObject("BGM Audio Source");
        AudioSource source = go.AddComponent<AudioSource>();
        source.volume = 0.45f;
        source.loop = true;
        source.playOnAwake = false;
        return go;
    }
}
