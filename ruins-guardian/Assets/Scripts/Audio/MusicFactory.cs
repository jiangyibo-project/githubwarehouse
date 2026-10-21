using UnityEngine;

/// <summary>
/// 分层背景音乐合成（立体声）。三层共用 80 BPM / 4 小节 / 12 秒的同一时间网格，
/// 因此可以在运行时按音量插值任意交叉淡化，拼接处相位连续、循环无缝。
///
/// 乐理：D 自然小调。探索层走 i(Dm) - VI(Bb) 两小节一换；紧张层在其上叠鼓点、
/// 八分音符琶音与二度/五度堆叠的不协和音；环境层是风噪与远处钟声。
/// </summary>
public static class MusicFactory
{
    public const float Bpm = 80f;
    public const float BeatSeconds = 60f / Bpm;              // 0.75 秒
    public const int BarsPerLoop = 4;
    /// <summary>单次循环时长（秒）：4 小节 × 4 拍 × 0.75 秒 = 12 秒，整数秒便于对齐。</summary>
    public const float LoopSeconds = BarsPerLoop * 4 * BeatSeconds;

    private const int D2 = 38;
    private const int A2 = 45;
    private const int Bb2 = 46;
    private const int D3 = 50;
    private const int F3 = 53;
    private const int A3 = 57;
    private const int Bb3 = 58;
    private const int D4 = 62;
    private const int A4 = 69;
    private const int D5 = 74;

    private static float[] LoopBuffer() { return new float[AudioSynth.Samples(LoopSeconds)]; }

    /// <summary>把 src 按偏移混入 dst（越界部分回卷到开头，保证循环连续）。</summary>
    private static void MixAt(float[] dst, float[] src, float startSec, float gain, bool wrapLoop)
    {
        int offset = Mathf.RoundToInt(startSec * AudioSynth.SampleRate);
        int length = dst.Length;
        for (int i = 0; i < src.Length; i++)
        {
            int index = offset + i;
            if (wrapLoop) index %= length;
            else if (index >= length) break;
            if (index < 0) continue;
            dst[index] += src[i] * gain;
        }
    }

    // ================= 探索层：低沉弦垫 + 稀疏旋律 =================

    /// <summary>探索层：D 小调弦垫（五度堆叠）+ 低音持续 + 稀疏的笛式旋律。</summary>
    public static AudioClip BuildExploreLayer()
    {
        float[] left = LoopBuffer();
        float[] right = LoopBuffer();

        // --- 弦垫：i(Dm) 两小节，VI(Bb) 两小节，第二段尾巴回卷到开头，接缝无凹陷 ---
        WritePad(left, right, 0f, 7f, new[] { D2, A2, D3, F3 }, 0.10f, 911);
        WritePad(left, right, 5.5f, 8f, new[] { Bb2, F3, Bb3, D4 }, 0.095f, 913);

        // --- 低音：比弦垫更慢的呼吸 ---
        WriteBass(left, right, 0f, 7f, D2, 0.20f);
        WriteBass(left, right, 5.5f, 8f, Bb2, 0.18f);

        // --- 高层微光：纯五度 D5-A5，营造遗迹的空旷 ---
        WriteShimmer(left, right, 0f, LoopSeconds, D5, 0.045f);
        WriteShimmer(left, right, 0f, LoopSeconds, A4 + 12, 0.035f);

        // --- 稀疏旋律：D 小调五声，每半小节决策一次，音级跳进不超过 2 度 ---
        WriteSparseMelody(left, right);

        AudioSynth.Normalize(left, right, 0.55f);
        return AudioSynth.ToStereoClip("BGM Explore", left, right);
    }

    private static void WritePad(float[] left, float[] right, float start, float duration,
        int[] chordNotes, float amplitude, int seed)
    {
        // 每个和弦音用三条微失谐的三角波，左右各偏一点，得到宽而有厚度的弦垫
        float[] detunes = { -7f, 0f, 7f };
        float[] pans = { -0.55f, 0f, 0.55f };
        for (int n = 0; n < chordNotes.Length; n++)
        {
            for (int v = 0; v < detunes.Length; v++)
            {
                AudioSynth.Tone(left, right, start, duration, AudioSynth.Hz(chordNotes[n]), new AudioSynth.ToneSpec
                {
                    wave = AudioSynth.Triangle,
                    amplitude = amplitude / (1f + n * 0.35f) / detunes.Length,
                    attack = 1.2f,
                    release = 1.6f,
                    pan = pans[v],
                    detuneCents = detunes[v] + (seed % 3) - 1f
                }, true);
            }
            // 每个音的八度泛音，让低频不至于糊成一团
            AudioSynth.Tone(left, right, start, duration, AudioSynth.Hz(chordNotes[n] + 12), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = amplitude * 0.12f,
                attack = 1.6f,
                release = 1.8f,
                pan = (n % 2 == 0) ? -0.2f : 0.2f
            }, true);
        }
    }

    private static void WriteBass(float[] left, float[] right, float start, float duration, int midi, float amplitude)
    {
        AudioSynth.Tone(left, right, start, duration, AudioSynth.Hz(midi), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = amplitude,
            attack = 0.35f,
            release = 0.9f
        }, true);
        AudioSynth.Tone(left, right, start, duration, AudioSynth.Hz(midi + 12), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = amplitude * 0.28f,
            attack = 0.5f,
            release = 1.1f,
            pan = 0.12f
        }, true);
    }

    private static void WriteShimmer(float[] left, float[] right, float start, float duration, int midi, float amplitude)
    {
        AudioSynth.Tone(left, right, start, duration, AudioSynth.Hz(midi), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = amplitude,
            attack = 2.5f,
            release = 2.5f,
            vibratoHz = 0.28f,
            vibratoDepth = 0.006f,
            pan = -0.35f
        }, true);
    }

    private static void WriteSparseMelody(float[] left, float[] right)
    {
        System.Random random = new System.Random(20240607);
        float step = BeatSeconds * 2f;                 // 半小节一个决策点
        int steps = Mathf.RoundToInt(LoopSeconds / step);
        int degree = 2;                                 // 从音阶第 3 级起，别一上来就跳
        for (int i = 0; i < steps; i++)
        {
            if (random.NextDouble() < 0.42) continue;   // 休止：稀疏感的来源
            degree = Mathf.Clamp(degree + random.Next(-2, 3), 0, 7);
            int midi = AudioSynth.ScaleDegree(AudioSynth.MinorPentatonic, D4, degree);
            float start = i * step + (float)random.NextDouble() * 0.08f;   // 人味的微延迟
            float duration = 1.25f + (float)random.NextDouble() * 0.5f;
            float pan = ((i % 2) == 0) ? -0.25f : 0.25f;
            AudioSynth.Tone(left, right, start, duration, AudioSynth.Hz(midi), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Flute,
                amplitude = 0.14f,
                attack = 0.07f,
                decay = 0.55f,
                release = 0.55f,
                vibratoHz = 4.8f,
                vibratoDepth = 0.004f,
                pan = pan
            }, true);
            // 轻泛音：让旋律在低音量下也听得见
            AudioSynth.Tone(left, right, start, duration * 0.8f, AudioSynth.Hz(midi + 12), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = 0.035f,
                attack = 0.12f,
                decay = 0.4f,
                release = 0.4f,
                pan = -pan * 0.5f
            }, true);
        }
    }

    // ================= 紧张层：鼓点 + 琶音 + 不协和堆叠 =================

    /// <summary>紧张层：四四拍鼓组、八分音符低音、Dm7 琶音、二度堆叠的紧张音簇。</summary>
    public static AudioClip BuildTensionLayer()
    {
        float[] left = LoopBuffer();
        float[] right = LoopBuffer();
        float beat = BeatSeconds;

        for (int bar = 0; bar < BarsPerLoop; bar++)
        {
            float barStart = bar * 4 * beat;

            // 底鼓：1、3 拍
            WriteKick(left, right, barStart);
            WriteKick(left, right, barStart + beat * 2f);
            // 军鼓：2、4 拍
            WriteSnare(left, right, barStart + beat);
            WriteSnare(left, right, barStart + beat * 3f);
            // 踩镲：每个八分音符的反拍
            for (int i = 0; i < 4; i++)
                WriteHat(left, right, barStart + beat * (i + 0.5f), 0.5f + i * 0.06f);
        }

        // 八分音符低音固定音型：i - i - V - i（D 小调的根音与五度交替）
        int[] bassPattern = { D2, D2, A2 - 12, D2, D2, F3 - 12, A2 - 12, D2 };
        for (int bar = 0; bar < BarsPerLoop; bar++)
        {
            for (int i = 0; i < bassPattern.Length; i++)
            {
                float start = bar * 4 * beat + i * beat * 0.5f;
                AudioSynth.Tone(left, right, start, 0.42f, AudioSynth.Hz(bassPattern[i]), new AudioSynth.ToneSpec
                {
                    wave = AudioSynth.WarmSaw,
                    amplitude = 0.16f,
                    attack = 0.008f,
                    decay = 0.16f,
                    release = 0.12f
                }, true);
            }
        }

        // 琶音：Dm7 分解（D4-F4-A4-C5），左右交替声像
        int[] arp = { D4, F3 + 12, A3 + 12, 72 };   // D4 F4 A4 C5
        for (int bar = 0; bar < BarsPerLoop; bar++)
        {
            for (int i = 0; i < 8; i++)
            {
                int index = (i < 4) ? i : 7 - i;         // 上行再下行，避免机械循环
                int midi = arp[index] + (bar % 2 == 0 ? 0 : 12);
                float start = bar * 4 * beat + i * beat * 0.5f;
                AudioSynth.Tone(left, right, start, 0.3f, AudioSynth.Hz(midi), new AudioSynth.ToneSpec
                {
                    wave = AudioSynth.Triangle,
                    amplitude = 0.085f,
                    attack = 0.004f,
                    decay = 0.12f,
                    release = 0.1f,
                    pan = (i % 2 == 0) ? -0.4f : 0.4f
                }, true);
            }
        }

        // 紧张音簇：二度 + 五度堆叠（D-A-E），长时间持续制造不安
        int[] cluster = AudioSynth.SecondStack;
        for (int i = 0; i < cluster.Length; i++)
        {
            AudioSynth.Tone(left, right, 0f, LoopSeconds, AudioSynth.Hz(D3 + cluster[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.WarmSaw,
                amplitude = 0.055f / (1f + i * 0.3f),
                attack = 1.4f,
                release = 1.4f,
                pan = -0.3f + i * 0.2f,
                vibratoHz = 5.6f,
                vibratoDepth = 0.003f
            }, true);
        }

        AudioSynth.Normalize(left, right, 0.78f);
        return AudioSynth.ToStereoClip("BGM Tension", left, right);
    }

    private static void WriteKick(float[] left, float[] right, float start)
    {
        AudioSynth.Tone(left, right, start, 0.22f, 120f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.62f,
            attack = 0.002f,
            decay = 0.09f,
            release = 0.1f,
            glideTo = 46f
        }, true);
        AudioSynth.Noise(left, right, start, 0.05f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.16f,
            attack = 0.001f,
            release = 0.03f,
            decay = 0.012f,
            lowCut = 900f,
            highCut = 60f,
            seed = 3001
        }, true);
    }

    private static void WriteSnare(float[] left, float[] right, float start)
    {
        AudioSynth.Noise(left, right, start, 0.16f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.3f,
            attack = 0.002f,
            release = 0.09f,
            decay = 0.045f,
            lowCut = 3200f,
            highCut = 900f,
            seed = 3101
        }, true);
        AudioSynth.Tone(left, right, start, 0.12f, 190f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Triangle,
            amplitude = 0.18f,
            attack = 0.002f,
            decay = 0.05f,
            release = 0.07f,
            glideTo = 150f
        }, true);
    }

    private static void WriteHat(float[] left, float[] right, float start, float amplitude)
    {
        AudioSynth.Noise(left, right, start, 0.06f, new AudioSynth.NoiseSpec
        {
            amplitude = 0.1f * amplitude,
            attack = 0.001f,
            release = 0.03f,
            decay = 0.012f,
            lowCut = 9000f,
            highCut = 6500f,
            seed = 3201,
            pan = 0.35f
        }, true);
    }

    // ================= 环境层：风噪 + 远处钟声 =================

    /// <summary>环境层：分层阵风（左右声道用不同噪声源，天然去相关）+ 低音长鸣 + 两声远钟。</summary>
    public static AudioClip BuildAmbientLayer()
    {
        float[] left = LoopBuffer();
        float[] right = LoopBuffer();

        // 阵风：四段交叠的噪声，各自不同的带宽与包络，形成起伏
        float[] starts = { 0f, 4f, 7.5f, 10.5f };
        float[] durations = { 4.5f, 4.2f, 4.6f, 3.2f };
        float[] cutStart = { 300f, 420f, 250f, 380f };
        float[] cutEnd = { 520f, 260f, 480f, 300f };
        for (int i = 0; i < starts.Length; i++)
        {
            AudioSynth.NoiseSpec windL = new AudioSynth.NoiseSpec
            {
                amplitude = 0.55f,
                attack = 1.4f,
                release = 1.4f,
                lowCut = cutStart[i],
                lowSweepTo = cutEnd[i],
                pan = -0.7f,
                seed = 9001 + i * 37
            };
            AudioSynth.Noise(left, right, starts[i], durations[i], windL, true);

            AudioSynth.NoiseSpec windR = new AudioSynth.NoiseSpec
            {
                amplitude = 0.55f,
                attack = 1.5f,
                release = 1.3f,
                lowCut = cutStart[(i + 2) % starts.Length],
                lowSweepTo = cutEnd[(i + 2) % starts.Length],
                pan = 0.7f,
                seed = 9501 + i * 41
            };
            AudioSynth.Noise(left, right, starts[i], durations[i], windR, true);
        }

        // 低音长鸣：两个微失谐的 D1 正弦，产生缓慢的拍频，像地底的回响
        AudioSynth.Tone(left, right, 0f, LoopSeconds, AudioSynth.Hz(26), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.16f,
            attack = 3f,
            release = 3f,
            pan = -0.15f
        }, true);
        AudioSynth.Tone(left, right, 0f, LoopSeconds, AudioSynth.Hz(26) * 1.005f, new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.13f,
            attack = 3.5f,
            release = 3.5f,
            pan = 0.15f
        }, true);

        // 远处钟声：单独渲染再叠入，才能挂上混响尾巴
        WriteDistantBell(left, right, 2.1f, 0.5f);
        WriteDistantBell(left, right, 8.4f, 0.34f);

        AudioSynth.Normalize(left, right, 0.42f);
        return AudioSynth.ToStereoClip("BGM Ambient", left, right);
    }

    private static void WriteDistantBell(float[] left, float[] right, float start, float amplitude)
    {
        float[] bell = new float[AudioSynth.Samples(4f)];
        float baseHz = AudioSynth.Hz(D4);
        float[] ratios = { 1f, 2f, 2.76f, 5.4f };
        float[] amps = { 0.5f, 0.24f, 0.14f, 0.07f };
        for (int i = 0; i < ratios.Length; i++)
        {
            float decay = 1.6f / (1f + i * 0.45f);
            AudioSynth.Tone(bell, 0f, 3.6f, baseHz * ratios[i], new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Sine,
                amplitude = amps[i] * amplitude,
                attack = 0.01f,
                decay = decay,
                release = 1.2f
            }, false);
        }
        AudioSynth.Reverb(bell, 0.55f, 0.78f, 1.6f);
        MixAt(left, bell, start, 1f, true);
        MixAt(right, bell, start, 1f, true);
    }

    // ================= 胜利 / 失败短段 =================

    /// <summary>胜利段：小调转大调的上行，D 大三和弦铺满，约 5 秒。</summary>
    public static AudioClip BuildVictorySegment()
    {
        float duration = 5.5f;
        float[] left = new float[AudioSynth.Samples(duration)];
        float[] right = new float[AudioSynth.Samples(duration)];

        int[] melody = { 62, 69, 74, 78, 86 };      // D4 A4 D5 F#5 D6
        for (int i = 0; i < melody.Length; i++)
        {
            float start = i * 0.18f;
            AudioSynth.Tone(left, right, start, 1.6f, AudioSynth.Hz(melody[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Triangle,
                amplitude = 0.28f,
                attack = 0.01f,
                decay = 0.5f,
                release = 0.7f,
                pan = -0.3f + i * 0.15f
            }, false);
        }

        // 收尾的 D 大三和弦（D-A-F#-D）
        int[] chord = { 50, 57, 62, 66, 74 };
        for (int i = 0; i < chord.Length; i++)
        {
            AudioSynth.Tone(left, right, 0.9f, 4.3f, AudioSynth.Hz(chord[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.WarmSaw,
                amplitude = 0.13f,
                attack = 0.35f,
                decay = 1.6f,
                release = 1.4f,
                pan = -0.5f + i * 0.25f,
                detuneCents = (i % 2 == 0) ? -5f : 5f
            }, false);
        }
        AudioSynth.Tone(left, right, 0.9f, 4.3f, AudioSynth.Hz(26), new AudioSynth.ToneSpec
        {
            wave = AudioSynth.Sine,
            amplitude = 0.3f,
            attack = 0.3f,
            decay = 1.8f,
            release = 1.5f
        }, false);

        AudioSynth.Normalize(left, right, 0.8f);
        return AudioSynth.ToStereoClip("BGM Victory", left, right);
    }

    /// <summary>失败段：D 小调下行的长音，配以低沉的地底共鸣，约 6 秒。</summary>
    public static AudioClip BuildDefeatSegment()
    {
        float duration = 6.5f;
        float[] left = new float[AudioSynth.Samples(duration)];
        float[] right = new float[AudioSynth.Samples(duration)];

        int[] melody = { 62, 60, 57, 53, 50 };      // D4 C4 A3 F3 D3（含小调的自然七度 C）
        for (int i = 0; i < melody.Length; i++)
        {
            float start = i * 0.55f;
            AudioSynth.Tone(left, right, start, 2.2f, AudioSynth.Hz(melody[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.Flute,
                amplitude = 0.2f,
                attack = 0.12f,
                decay = 0.9f,
                release = 1f,
                vibratoHz = 4.2f,
                vibratoDepth = 0.004f,
                pan = 0.3f - i * 0.12f
            }, false);
        }

        int[] chord = { 38, 45, 50, 53 };           // D2 A2 D3 F3：小三和弦铺底
        for (int i = 0; i < chord.Length; i++)
        {
            AudioSynth.Tone(left, right, 1.6f, 4.8f, AudioSynth.Hz(chord[i]), new AudioSynth.ToneSpec
            {
                wave = AudioSynth.WarmSaw,
                amplitude = 0.16f,
                attack = 0.8f,
                decay = 1.8f,
                release = 1.6f,
                pan = -0.4f + i * 0.25f,
                detuneCents = (i % 2 == 0) ? 6f : -6f
            }, false);
        }

        AudioSynth.Normalize(left, right, 0.75f);
        return AudioSynth.ToStereoClip("BGM Defeat", left, right);
    }
}
