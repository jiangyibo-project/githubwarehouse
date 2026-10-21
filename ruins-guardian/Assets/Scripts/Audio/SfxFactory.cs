using UnityEngine;

/// <summary>
/// 全部音效的程序化合成工厂（单声道）。每个方法返回已做 5 ms 首尾淡入淡出的 AudioClip。
/// 音高一律取自 D 小调 / D 五声音阶，音效之间的和谐度由乐理保证，而不是随机乱响。
/// </summary>
public static class SfxFactory
{
    private static float[] Buffer(float seconds) { return new float[AudioSynth.Samples(seconds)]; }

    private static AudioClip Finish(string name, float[] buffer, float peak, float reverbAmount = 0f)
    {
        if (reverbAmount > 0f) AudioSynth.Reverb(buffer, reverbAmount, 0.72f);
        AudioSynth.Normalize(buffer, peak);
        return AudioSynth.ToMonoClip(name, buffer);
    }

    // ================= 剑挥风：3 个变体 =================

    /// <summary>剑挥风三个变体：时长、气流带宽、起音点各不相同，避免连续挥动出现"复读机"感。</summary>
    public static AudioClip[] BuildSwingVariants()
    {
        AudioClip[] clips = new AudioClip[3];
        clips[0] = BuildSwing("Swing A", 0.22f, 480f, 4600f, 900f, 0.62f, 11);
        clips[1] = BuildSwing("Swing B", 0.27f, 360f, 3800f, 700f, 0.52f, 27);
        clips[2] = BuildSwing("Swing C", 0.18f, 620f, 5200f, 1100f, 0.70f, 43);
        return clips;
    }

    private static AudioClip BuildSwing(string name, float duration, float cutFrom, float cutTo, float highCut, float amp, int seed)
    {
        float[] buffer = Buffer(duration);
        // 气流上升段：噪声低通快速上扫，就是"呼"的那一下
        AudioSynth.NoiseSpec rise = new AudioSynth.NoiseSpec
        {
            amplitude = amp,
            attack = duration * 0.10f,
            release = duration * 0.55f,
            decay = duration * 0.22f,
            lowCut = cutFrom,
            lowSweepTo = cutTo,
            highCut = highCut,
            seed = seed
        };
        AudioSynth.Noise(buffer, 0f, duration, rise, false);

        // 气流收束段：高频迅速下扫，收尾干净
        AudioSynth.NoiseSpec fall = new AudioSynth.NoiseSpec
        {
            amplitude = amp * 0.55f,
            attack = duration * 0.35f,
            release = duration * 0.30f,
            decay = duration * 0.15f,
            lowCut = cutTo * 0.7f,
            lowSweepTo = cutFrom * 0.5f,
            highCut = highCut * 1.4f,
            seed = seed + 101
        };
        AudioSynth.Noise(buffer, 0f, duration * 0.8f, fall, false);

        // 极轻的低频体感：挥动时空气被推开的"嗡"
        AudioSynth.Tone(buffer, 0f, duration * 0.6f, 150f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = amp * 0.16f,
            attack = 0.02f,
            release = duration * 0.3f,
            decay = 0.08f,
            glideTo = 85f
        }, false);

        return Finish(name, buffer, 0.85f);
    }

    // ================= 命中：金属（±2 半音随机）/ 血肉 =================

    /// <summary>命中金属：5 个音高变体（-2、-1、0、+1、+2 半音），调用方随机取一个。</summary>
    public static AudioClip[] BuildMetalHitVariants()
    {
        float[] semitones = { -2f, -1f, 0f, 1f, 2f };
        AudioClip[] clips = new AudioClip[semitones.Length];
        for (int i = 0; i < semitones.Length; i++)
            clips[i] = BuildMetalHit("Hit Metal " + semitones[i].ToString("+0;-0;0"), semitones[i], 500 + i * 13);
        return clips;
    }

    private static AudioClip BuildMetalHit(string name, float semitoneShift, int seed)
    {
        float[] buffer = Buffer(0.55f);
        float baseHz = AudioSynth.Transpose(523.25f, semitoneShift); // 以 C5 为基频，避免和人声区打架

        // 非谐分音：1 / 2.76 / 5.4 是金属棒振动的经典比例
        float[] ratios = { 1f, 2.76f, 5.4f, 8.93f };
        float[] amps = { 0.55f, 0.34f, 0.18f, 0.09f };
        for (int i = 0; i < ratios.Length; i++)
        {
            float decay = 0.26f / (1f + i * 0.55f);
            AudioSynth.Tone(buffer, 0f, decay * 4f, baseHz * ratios[i], new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = amps[i],
                attack = 0.001f,
                decay = decay,
                release = decay * 0.8f
            }, false);
        }

        // 撞击瞬间的金属脆响
        AudioSynth.Noise(buffer, 0f, 0.05f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.55f,
            attack = 0.001f,
            release = 0.03f,
            decay = 0.012f,
            lowCut = 4200f,
            highCut = 2600f,
            seed = seed
        }, false);

        // 打击的"体重"：低频下沉
        AudioSynth.Tone(buffer, 0f, 0.16f, 190f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.42f,
            attack = 0.001f,
            decay = 0.05f,
            release = 0.08f,
            glideTo = 92f
        }, false);

        return Finish(name, buffer, 0.92f);
    }

    /// <summary>命中血肉：低沉钝击，没有金属高频，只有闷响与软组织摩擦。</summary>
    public static AudioClip BuildFleshHit()
    {
        float[] buffer = Buffer(0.4f);
        AudioSynth.Tone(buffer, 0f, 0.22f, 96f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.85f,
            attack = 0.002f,
            decay = 0.07f,
            release = 0.14f,
            glideTo = 48f
        }, false);
        AudioSynth.Noise(buffer, 0f, 0.16f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.5f,
            attack = 0.002f,
            release = 0.09f,
            decay = 0.045f,
            lowCut = 520f,
            highCut = 90f,
            seed = 7
        }, false);
        return Finish("Hit Flesh", buffer, 0.9f);
    }

    // ================= 守卫战锤挥击 =================

    /// <summary>守卫战锤：比剑更长、更低、更笨重，起音慢半拍，带重物移动的低频。</summary>
    public static AudioClip BuildHammerSwing()
    {
        float[] buffer = Buffer(0.62f);
        AudioSynth.Noise(buffer, 0f, 0.55f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.6f,
            attack = 0.12f,
            release = 0.3f,
            decay = 0.16f,
            lowCut = 240f,
            lowSweepTo = 1400f,
            highCut = 480f,
            seed = 61
        }, false);
        AudioSynth.Noise(buffer, 0.05f, 0.4f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.3f,
            attack = 0.16f,
            release = 0.18f,
            decay = 0.12f,
            lowCut = 1600f,
            lowSweepTo = 500f,
            highCut = 900f,
            seed = 62
        }, false);
        // 铁锤链条/木柄的低频晃动
        AudioSynth.Tone(buffer, 0f, 0.5f, 72f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.WarmSaw,
            amplitude = 0.22f,
            attack = 0.08f,
            release = 0.2f,
            decay = 0.22f,
            glideTo = 52f
        }, false);
        return Finish("Hammer Swing", buffer, 0.88f);
    }

    // ================= 玩家受击 =================

    /// <summary>玩家受击：短促的锯齿下坠 + 低频闷响，听感上"疼"而不刺耳。</summary>
    public static AudioClip BuildPlayerHurt()
    {
        float[] buffer = Buffer(0.45f);
        AudioSynth.Tone(buffer, 0f, 0.26f, AudioSynth.Hz(57), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Saw,
            amplitude = 0.5f,
            attack = 0.004f,
            release = 0.16f,
            decay = 0.09f,
            glideTo = AudioSynth.Hz(45)
        }, false);
        AudioSynth.Tone(buffer, 0f, 0.3f, 78f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.45f,
            attack = 0.002f,
            decay = 0.08f,
            release = 0.18f,
            glideTo = 46f
        }, false);
        AudioSynth.Noise(buffer, 0f, 0.2f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.3f,
            attack = 0.003f,
            release = 0.12f,
            decay = 0.05f,
            lowCut = 900f,
            highCut = 150f,
            seed = 91
        }, false);
        return Finish("Player Hurt", buffer, 0.9f);
    }

    // ================= 闪避冲刺风声 =================

    /// <summary>闪避冲刺：0.3 秒的气流暴涨后迅速收束，比剑挥风更"宽"更低。</summary>
    public static AudioClip BuildDodge()
    {
        float[] buffer = Buffer(0.36f);
        AudioSynth.Noise(buffer, 0f, 0.34f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.6f,
            attack = 0.03f,
            release = 0.16f,
            decay = 0.1f,
            lowCut = 500f,
            lowSweepTo = 2600f,
            highCut = 260f,
            seed = 131
        }, false);
        AudioSynth.Noise(buffer, 0.02f, 0.26f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.3f,
            attack = 0.06f,
            release = 0.12f,
            decay = 0.06f,
            lowCut = 3000f,
            lowSweepTo = 1200f,
            highCut = 1800f,
            seed = 132
        }, false);
        return Finish("Dodge", buffer, 0.8f);
    }

    // ================= 脚步：石材 / 草地 =================

    /// <summary>石地脚步三个变体：硬质鞋跟 + 短促石屑。</summary>
    public static AudioClip[] BuildFootstepStoneVariants()
    {
        AudioClip[] clips = new AudioClip[3];
        for (int i = 0; i < clips.Length; i++)
            clips[i] = BuildFootstepStone("Footstep Stone " + (i + 1), 133 + i * 17, 1f - i * 0.06f);
        return clips;
    }

    private static AudioClip BuildFootstepStone(string name, int seed, float pitchScale)
    {
        float[] buffer = Buffer(0.14f);
        AudioSynth.Noise(buffer, 0f, 0.08f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.5f,
            attack = 0.001f,
            release = 0.045f,
            decay = 0.014f,
            lowCut = 3600f,
            highCut = 700f,
            seed = seed
        }, false);
        AudioSynth.Tone(buffer, 0f, 0.05f, 2400f * pitchScale, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.22f,
            attack = 0.0005f,
            decay = 0.007f,
            release = 0.018f
        }, false);
        AudioSynth.Tone(buffer, 0f, 0.1f, 120f * pitchScale, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.34f,
            attack = 0.001f,
            decay = 0.028f,
            release = 0.06f,
            glideTo = 74f
        }, false);
        return Finish(name, buffer, 0.72f);
    }

    /// <summary>草地脚步三个变体：低通的"沙沙"，没有硬质泛音。</summary>
    public static AudioClip[] BuildFootstepGrassVariants()
    {
        AudioClip[] clips = new AudioClip[3];
        for (int i = 0; i < clips.Length; i++)
            clips[i] = BuildFootstepGrass("Footstep Grass " + (i + 1), 211 + i * 23, 1f - i * 0.05f);
        return clips;
    }

    private static AudioClip BuildFootstepGrass(string name, int seed, float pitchScale)
    {
        float[] buffer = Buffer(0.18f);
        AudioSynth.Noise(buffer, 0f, 0.16f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.42f,
            attack = 0.004f,
            release = 0.09f,
            decay = 0.035f,
            lowCut = 2400f,
            lowSweepTo = 900f,
            highCut = 380f,
            seed = seed
        }, false);
        AudioSynth.Noise(buffer, 0.012f, 0.1f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.18f,
            attack = 0.01f,
            release = 0.06f,
            decay = 0.02f,
            lowCut = 5200f,
            highCut = 2400f,
            seed = seed + 5
        }, false);
        AudioSynth.Tone(buffer, 0f, 0.09f, 88f * pitchScale, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.16f,
            attack = 0.006f,
            decay = 0.03f,
            release = 0.05f
        }, false);
        return Finish(name, buffer, 0.62f);
    }

    // ================= 宝石拾取：明亮上行音阶 =================

    /// <summary>宝石拾取：D 小调五声上行（D5-F5-A5-D6），铃音色加泛音，末尾留长衰减。</summary>
    public static AudioClip BuildGemPickup()
    {
        float[] buffer = Buffer(1.1f);
        int[] notes = { 74, 77, 81, 86 };   // D5, F5, A5, D6
        float spacing = 0.075f;
        for (int i = 0; i < notes.Length; i++)
        {
            float start = i * spacing;
            bool last = i == notes.Length - 1;
            float decay = last ? 0.45f : 0.12f;
            float amp = last ? 0.42f : 0.3f;
            float hz = AudioSynth.Hz(notes[i]);
            AudioSynth.Tone(buffer, start, decay * 3f, hz, new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = amp,
                attack = 0.004f,
                decay = decay,
                release = decay * 0.9f
            }, false);
            // 八度泛音做出"宝石"的玻璃质感
            AudioSynth.Tone(buffer, start, decay * 2f, hz * 2f, new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = amp * 0.35f,
                attack = 0.003f,
                decay = decay * 0.55f,
                release = decay * 0.5f
            }, false);
            AudioSynth.Tone(buffer, start, decay * 1.5f, hz * 3.01f, new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = amp * 0.16f,
                attack = 0.002f,
                decay = decay * 0.35f,
                release = decay * 0.3f
            }, false);
        }
        return Finish("Gem Pickup", buffer, 0.85f, 0.28f);
    }

    // ================= 符文激活 =================

    /// <summary>符文激活：低频咒语堆叠 + 上行五度琶音 + 高频微光，带混响尾巴。</summary>
    public static AudioClip BuildRuneActivate()
    {
        float[] buffer = Buffer(1.8f);
        int root = 50;                                  // D3
        int[] stack = AudioSynth.FifthStack;            // 纯五度堆叠
        for (int i = 0; i < stack.Length; i++)
        {
            float start = 0.045f * i;
            float hz = AudioSynth.Hz(root + stack[i]);
            AudioSynth.Tone(buffer, start, 1.2f, hz, new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Triangle,
                amplitude = 0.26f,
                attack = 0.02f,
                decay = 0.55f,
                release = 0.4f,
                vibratoHz = 5.2f,
                vibratoDepth = 0.004f,
                pan = 0f
            }, false);
        }
        // 咒语底噪：带通噪声缓慢上扫
        AudioSynth.Noise(buffer, 0f, 1.5f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.22f,
            attack = 0.25f,
            release = 0.5f,
            decay = 0.7f,
            lowCut = 300f,
            lowSweepTo = 2200f,
            highCut = 220f,
            seed = 307
        }, false);
        // 微光：极高的正弦点缀
        float[] shine = { 1046.5f, 1568f, 2093f };
        for (int i = 0; i < shine.Length; i++)
        {
            AudioSynth.Tone(buffer, 0.2f + i * 0.09f, 0.9f, shine[i], new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = 0.1f,
                attack = 0.05f,
                decay = 0.35f,
                release = 0.4f
            }, false);
        }
        return Finish("Rune Activate", buffer, 0.85f, 0.42f);
    }

    // ================= 开门 / 机关 =================

    /// <summary>石门开启：低频摩擦爬升 + 石屑，收尾留一点余震。</summary>
    public static AudioClip BuildDoorOpen()
    {
        float[] buffer = Buffer(1.5f);
        AudioSynth.Noise(buffer, 0f, 1.4f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.5f,
            attack = 0.18f,
            release = 0.5f,
            decay = 0.9f,
            lowCut = 180f,
            lowSweepTo = 420f,
            highCut = 60f,
            seed = 401
        }, false);
        AudioSynth.Noise(buffer, 0.15f, 1.1f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.24f,
            attack = 0.2f,
            release = 0.4f,
            decay = 0.6f,
            lowCut = 1400f,
            lowSweepTo = 800f,
            highCut = 500f,
            seed = 402
        }, false);
        AudioSynth.Tone(buffer, 0f, 1.3f, 62f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Saw,
            amplitude = 0.2f,
            attack = 0.25f,
            release = 0.4f,
            decay = 0.7f,
            glideTo = 44f
        }, false);
        return Finish("Door Open", buffer, 0.8f, 0.2f);
    }

    /// <summary>石门关闭：关门更重，末尾有落定的重击。</summary>
    public static AudioClip BuildDoorClose()
    {
        float[] buffer = Buffer(1.2f);
        AudioSynth.Noise(buffer, 0f, 0.9f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.42f,
            attack = 0.1f,
            release = 0.35f,
            decay = 0.5f,
            lowCut = 220f,
            lowSweepTo = 160f,
            highCut = 70f,
            seed = 421
        }, false);
        AudioSynth.Tone(buffer, 0.62f, 0.5f, 70f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.65f,
            attack = 0.003f,
            decay = 0.16f,
            release = 0.3f,
            glideTo = 42f
        }, false);
        AudioSynth.Noise(buffer, 0.62f, 0.3f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.35f,
            attack = 0.002f,
            release = 0.2f,
            decay = 0.07f,
            lowCut = 1200f,
            highCut = 200f,
            seed = 422
        }, false);
        return Finish("Door Close", buffer, 0.85f, 0.18f);
    }

    // ================= UI =================

    /// <summary>UI 悬停：极短极轻的高频点，不抢注意力。</summary>
    public static AudioClip BuildUiHover()
    {
        float[] buffer = Buffer(0.06f);
        AudioSynth.Tone(buffer, 0f, 0.05f, 1174.66f, new AudioSynth.ToneSpec   // D6
        {
            wave = AudioSynth.Sine,
            amplitude = 0.4f,
            attack = 0.003f,
            release = 0.03f,
            decay = 0.02f
        }, false);
        AudioSynth.Tone(buffer, 0f, 0.03f, 1760f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.12f,
            attack = 0.002f,
            release = 0.02f,
            decay = 0.01f
        }, false);
        return Finish("UI Hover", buffer, 0.3f);
    }

    /// <summary>UI 点击：短促的方波点 + 一点噪声边角，确认感清晰。</summary>
    public static AudioClip BuildUiClick()
    {
        float[] buffer = Buffer(0.1f);
        AudioSynth.Tone(buffer, 0f, 0.07f, 880f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Square,
            amplitude = 0.28f,
            attack = 0.001f,
            release = 0.045f,
            decay = 0.025f
        }, false);
        AudioSynth.Tone(buffer, 0f, 0.06f, 1320f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.3f,
            attack = 0.001f,
            release = 0.04f,
            decay = 0.02f
        }, false);
        AudioSynth.Noise(buffer, 0f, 0.03f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.2f,
            attack = 0.0005f,
            release = 0.02f,
            decay = 0.006f,
            lowCut = 6000f,
            highCut = 1500f,
            seed = 501
        }, false);
        return Finish("UI Click", buffer, 0.55f);
    }

    // ================= 心跳 =================

    /// <summary>单次心跳（lub-dub 双拍），由 AudioDirector 按血量低时以间隔重复触发。</summary>
    public static AudioClip BuildHeartbeat()
    {
        float[] buffer = Buffer(0.7f);
        WriteBeat(buffer, 0f, 58f, 0.75f);
        WriteBeat(buffer, 0.26f, 46f, 0.5f);
        return Finish("Heartbeat", buffer, 0.8f);
    }

    private static void WriteBeat(float[] buffer, float start, float frequency, float amp)
    {
        AudioSynth.Tone(buffer, start, 0.3f, frequency, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = amp,
            attack = 0.004f,
            decay = 0.07f,
            release = 0.18f,
            glideTo = frequency * 0.62f
        }, false);
        AudioSynth.Noise(buffer, start, 0.14f, new AudioSynth.NoiseSpec
        {
            amplitude = amp * 0.25f,
            attack = 0.004f,
            release = 0.09f,
            decay = 0.04f,
            lowCut = 220f,
            highCut = 40f,
            seed = 601
        }, false);
    }

    // ================= 远处钟声 =================

    /// <summary>远处钟声：非谐分音 + 长混响，作为遗迹环境层的偶发点缀。</summary>
    public static AudioClip BuildBellToll()
    {
        float[] buffer = Buffer(3.4f);
        float baseHz = AudioSynth.Hz(62);   // D4
        float[] ratios = { 1f, 2f, 2.76f, 5.4f, 8.1f };
        float[] amps = { 0.5f, 0.26f, 0.18f, 0.1f, 0.05f };
        for (int i = 0; i < ratios.Length; i++)
        {
            float decay = 1.5f / (1f + i * 0.5f);
            AudioSynth.Tone(buffer, 0f, decay * 2.4f, baseHz * ratios[i], new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = amps[i],
                attack = 0.006f,
                decay = decay,
                release = decay * 1.1f
            }, false);
        }
        return Finish("Bell Toll", buffer, 0.7f, 0.5f);
    }

    // ================= 守卫发现玩家 =================

    /// <summary>守卫警觉：上行五度双音（D5-A5），短促有指向性。</summary>
    public static AudioClip BuildAlert()
    {
        float[] buffer = Buffer(0.55f);
        AudioSynth.Tone(buffer, 0f, 0.16f, AudioSynth.Hz(74), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Triangle,
            amplitude = 0.4f,
            attack = 0.004f,
            decay = 0.07f,
            release = 0.08f
        }, false);
        AudioSynth.Tone(buffer, 0.11f, 0.4f, AudioSynth.Hz(81), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Triangle,
            amplitude = 0.42f,
            attack = 0.004f,
            decay = 0.16f,
            release = 0.2f
        }, false);
        AudioSynth.Noise(buffer, 0.11f, 0.2f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.14f,
            attack = 0.005f,
            release = 0.12f,
            decay = 0.05f,
            lowCut = 5000f,
            highCut = 1200f,
            seed = 701
        }, false);
        return Finish("Alert", buffer, 0.7f, 0.15f);
    }

    // ================= 敌人 / 玩家死亡 =================

    /// <summary>守卫倒地：石头碎裂 + 金属武器落地 + 低频下坠。</summary>
    public static AudioClip BuildEnemyDeath()
    {
        float[] buffer = Buffer(1.2f);
        AudioSynth.Tone(buffer, 0f, 0.9f, AudioSynth.Hz(55), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Saw,
            amplitude = 0.4f,
            attack = 0.01f,
            decay = 0.3f,
            release = 0.5f,
            glideTo = AudioSynth.Hz(33)
        }, false);
        // 碎裂：两段不同带宽的噪声，模拟石块崩落
        AudioSynth.Noise(buffer, 0f, 0.5f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.4f,
            attack = 0.002f,
            release = 0.3f,
            decay = 0.12f,
            lowCut = 2600f,
            highCut = 600f,
            seed = 801
        }, false);
        AudioSynth.Noise(buffer, 0.06f, 0.7f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.24f,
            attack = 0.01f,
            release = 0.4f,
            decay = 0.25f,
            lowCut = 900f,
            lowSweepTo = 300f,
            highCut = 180f,
            seed = 802
        }, false);
        // 武器落地的高频余音
        AudioSynth.Tone(buffer, 0.42f, 0.5f, 1480f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.12f,
            attack = 0.002f,
            decay = 0.14f,
            release = 0.2f
        }, false);
        return Finish("Enemy Death", buffer, 0.85f, 0.2f);
    }

    /// <summary>玩家死亡：缓慢下坠的低频 + 心跳停止 + 尘埃落定。</summary>
    public static AudioClip BuildPlayerDeath()
    {
        float[] buffer = Buffer(2.2f);
        AudioSynth.Tone(buffer, 0f, 2f, AudioSynth.Hz(50), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.55f,
            attack = 0.02f,
            decay = 0.9f,
            release = 1f,
            glideTo = AudioSynth.Hz(26)
        }, false);
        AudioSynth.Tone(buffer, 0f, 1.4f, AudioSynth.Hz(57), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Saw,
            amplitude = 0.22f,
            attack = 0.05f,
            decay = 0.6f,
            release = 0.7f,
            glideTo = AudioSynth.Hz(38)
        }, false);
        // 最后两下心跳（越来越弱）
        WriteBeat(buffer, 0.1f, 52f, 0.4f);
        WriteBeat(buffer, 0.72f, 46f, 0.24f);
        AudioSynth.Noise(buffer, 0f, 1.8f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.16f,
            attack = 0.2f,
            release = 1f,
            decay = 1f,
            lowCut = 700f,
            highCut = 120f,
            seed = 901
        }, false);
        return Finish("Player Death", buffer, 0.9f, 0.3f);
    }

    // ================= 菜单短曲：胜利 / 失败 =================

    /// <summary>胜利短曲：D 大调上行，最后由大三和弦收尾（小调转大调，情绪上扬）。</summary>
    public static AudioClip BuildVictoryStinger()
    {
        float[] buffer = Buffer(2.4f);
        int[] melody = { 62, 69, 74, 78 };   // D4 A4 D5 F#5
        float[] spacing = { 0f, 0.11f, 0.22f, 0.34f };
        for (int i = 0; i < melody.Length; i++)
        {
            AudioSynth.Tone(buffer, spacing[i], 0.9f, AudioSynth.Hz(melody[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Triangle,
                amplitude = 0.3f,
                attack = 0.01f,
                decay = 0.35f,
                release = 0.4f
            }, false);
        }
        // 收尾的 D 大三和弦（D-A-F#），五度堆叠做厚度
        int[] chord = { 62, 69, 66, 78 };
        for (int i = 0; i < chord.Length; i++)
        {
            AudioSynth.Tone(buffer, 0.34f, 1.9f, AudioSynth.Hz(chord[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Triangle,
                amplitude = 0.22f,
                attack = 0.05f,
                decay = 0.7f,
                release = 0.8f
            }, false);
        }
        return Finish("Victory Stinger", buffer, 0.85f, 0.35f);
    }

    /// <summary>失败短曲：D 小调下行，低音 D2 垫底，越走越低。</summary>
    public static AudioClip BuildDefeatStinger()
    {
        float[] buffer = Buffer(3f);
        int[] melody = { 62, 57, 53, 50 };   // D4 A3 F3 D3
        for (int i = 0; i < melody.Length; i++)
        {
            AudioSynth.Tone(buffer, i * 0.22f, 1.1f, AudioSynth.Hz(melody[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Triangle,
                amplitude = 0.3f,
                attack = 0.03f,
                decay = 0.4f,
                release = 0.6f,
                vibratoHz = 4.5f,
                vibratoDepth = 0.003f
            }, false);
        }
        AudioSynth.Tone(buffer, 0.6f, 2.3f, AudioSynth.Hz(38), new AudioSynth.ToneSpec   // D2
        {
            wave = AudioSynth.Sine,
            amplitude = 0.4f,
            attack = 0.1f,
            decay = 1f,
            release = 1f
        }, false);
        return Finish("Defeat Stinger", buffer, 0.85f, 0.4f);
    }
}
