using UnityEngine;

/// <summary>
/// 程序化音频合成核心：波形、包络、噪声滤波、混响与乐理音高工具。
/// 全部使用 44100 Hz 单声道/立体声浮点缓冲，不依赖任何外部素材。
/// 约定：所有对外生成的 clip 在写入 AudioClip 前都会做首尾 5 ms 淡入淡出，杜绝爆音。
/// </summary>
public static class AudioSynth
{
    /// <summary>统一采样率。</summary>
    public const int SampleRate = 44100;

    /// <summary>每个 clip 首尾的淡入淡出时长（秒）。</summary>
    public const float EdgeFade = 0.005f;

    /// <summary>相位波形：入参 0..1，返回大致 -1..1。</summary>
    public delegate float WaveFunction(float phase01);

    // ================= 波形 =================

    public static float Sine(float p) { return Mathf.Sin(p * Mathf.PI * 2f); }

    public static float Triangle(float p)
    {
        if (p < 0.25f) return p * 4f;
        if (p < 0.75f) return 2f - p * 4f;
        return p * 4f - 4f;
    }

    public static float Saw(float p) { return 2f * (p - Mathf.Floor(p + 0.5f)); }

    public static float Square(float p) { return p < 0.5f ? 1f : -1f; }

    /// <summary>带二次谐波塑形的锯齿：弦垫用，比纯锯齿柔和，不会刺耳。</summary>
    public static float WarmSaw(float p) { return Saw(p) * 0.72f + Saw(p * 2f) * 0.16f + Sine(p) * 0.12f; }

    /// <summary>管笛音色：正弦为主，掺少量三角与八度泛音，用于稀疏旋律。</summary>
    public static float Flute(float p) { return Sine(p) + Triangle(p) * 0.16f + Sine(p * 2f) * 0.07f; }

    // ================= 乐理：音高 / 音阶 / 和弦 =================

    /// <summary>MIDI 音高转频率（A4 = 69 = 440 Hz）。</summary>
    public static float Hz(int midi) { return 440f * Mathf.Pow(2f, (midi - 69) / 12f); }

    /// <summary>按半音移调（±2 半音之类的音高随机化）。</summary>
    public static float Transpose(float frequency, float semitones) { return frequency * Mathf.Pow(2f, semitones / 12f); }

    // 音阶（相对根音的半音偏移）
    /// <summary>D 小调五声：旋律只在这五个音里走，天然不会跑调。</summary>
    public static readonly int[] MinorPentatonic = { 0, 3, 5, 7, 10 };

    // 和弦堆叠（相对根音的半音）
    /// <summary>纯五度连续堆叠：D-A-D-A，遗迹的空旷感来源。</summary>
    public static readonly int[] FifthStack = { 0, 7, 12, 19 };
    /// <summary>二度加五度堆叠：紧张层用，制造不协和的"警觉"色彩。</summary>
    public static readonly int[] SecondStack = { 0, 2, 7, 12 };

    /// <summary>取音阶第 degree 级（可跨八度，负数向下），返回 MIDI 音高。</summary>
    public static int ScaleDegree(int[] scale, int rootMidi, int degree)
    {
        int n = scale.Length;
        int octave = Mathf.FloorToInt(degree / (float)n);
        int index = degree - octave * n;
        return rootMidi + scale[index] + octave * 12;
    }

    // ================= 缓冲工具 =================

    public static int Samples(float seconds) { return Mathf.Max(1, Mathf.CeilToInt(seconds * SampleRate)); }

    /// <summary>1 - exp(-2πf/sr)，一阶滤波器系数。</summary>
    private static float FilterAlpha(float cutoff)
    {
        cutoff = Mathf.Clamp(cutoff, 20f, SampleRate * 0.45f);
        return 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / SampleRate);
    }

    /// <summary>简易混响：4 路并联梳状 + 2 级全通，湿声按 amount 叠加回原缓冲。</summary>
    public static void Reverb(float[] data, float amount, float feedback, float lengthScale = 1f)
    {
        if (amount <= 0f) return;
        int[] combDelays = { 1557, 1617, 1422, 1277 };
        float[] wet = new float[data.Length];
        for (int c = 0; c < combDelays.Length; c++)
        {
            int delay = Mathf.Max(1, Mathf.RoundToInt(combDelays[c] * lengthScale));
            float[] line = new float[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                float delayed = i >= delay ? line[i - delay] : 0f;
                line[i] = data[i] + delayed * feedback;
            }
            for (int i = 0; i < data.Length; i++) wet[i] += line[i] * 0.25f;
        }
        // 两级全通，把梳状的回声梳齿抹平
        AllPass(wet, Mathf.RoundToInt(225 * lengthScale), 0.5f);
        AllPass(wet, Mathf.RoundToInt(556 * lengthScale), 0.5f);
        for (int i = 0; i < data.Length; i++) data[i] += wet[i] * amount;
    }

    private static void AllPass(float[] data, int delay, float feedback)
    {
        if (delay <= 0) return;
        float[] line = new float[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            float delayed = i >= delay ? line[i - delay] : 0f;
            float input = data[i];
            line[i] = input + delayed * feedback;
            data[i] = -input * feedback + delayed;
        }
    }

    /// <summary>首尾淡入淡出，同时硬限幅，防止 24bit/float 溢出与爆音。</summary>
    public static void Fade(float[] data)
    {
        int fade = Mathf.Max(1, Mathf.RoundToInt(EdgeFade * SampleRate));
        int last = data.Length - 1;
        for (int i = 0; i < data.Length; i++)
        {
            float gain = 1f;
            if (i < fade) gain = i / (float)fade;
            int tail = last - i;
            if (tail < fade) gain = Mathf.Min(gain, tail / (float)fade);
            data[i] = Mathf.Clamp(data[i] * gain, -1f, 1f);
        }
    }

    public static void Fade(float[] left, float[] right)
    {
        Fade(left);
        if (right != null) Fade(right);
    }

    /// <summary>按峰值归一化（仅放大路径不做，避免把噪声抬到失真）。</summary>
    public static void Normalize(float[] data, float peak)
    {
        float max = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float abs = Mathf.Abs(data[i]);
            if (abs > max) max = abs;
        }
        if (max <= 0.0001f) return;
        float gain = Mathf.Min(peak / max, 4f);
        for (int i = 0; i < data.Length; i++) data[i] *= gain;
    }

    public static void Normalize(float[] left, float[] right, float peak)
    {
        float max = 0f;
        for (int i = 0; i < left.Length; i++)
        {
            float abs = Mathf.Abs(left[i]);
            if (abs > max) max = abs;
            if (right != null)
            {
                abs = Mathf.Abs(right[i]);
                if (abs > max) max = abs;
            }
        }
        if (max <= 0.0001f) return;
        float gain = Mathf.Min(peak / max, 4f);
        for (int i = 0; i < left.Length; i++)
        {
            left[i] *= gain;
            if (right != null) right[i] *= gain;
        }
    }

    // ================= 音色写入 =================

    /// <summary>单音参数。未赋值的字段为 0（即无衰减、无颤音、无声像偏移）。</summary>
    public struct ToneSpec
    {
        public WaveFunction wave;
        public float amplitude;
        public float attack;
        public float release;
        /// <summary>指数衰减时间常数（秒），&lt;= 0 表示不衰减。</summary>
        public float decay;
        /// <summary>声像 -1（左）..1（右），仅立体声缓冲有效。</summary>
        public float pan;
        public float detuneCents;
        public float vibratoHz;
        public float vibratoDepth;
        /// <summary>&gt; 0 时在持续时间内线性滑到该频率（鼓、撞击音高下滑）。</summary>
        public float glideTo;
    }

    /// <summary>噪声参数。</summary>
    public struct NoiseSpec
    {
        public float amplitude;
        public float attack;
        public float release;
        public float decay;
        /// <summary>低通截止频率初值。</summary>
        public float lowCut;
        /// <summary>&gt; 0 时低通截止扫到该值（风声、挥动的"呼啸"来自这里）。</summary>
        public float lowSweepTo;
        /// <summary>高通截止频率（去除轰隆感，保留"沙"的部分）。</summary>
        public float highCut;
        public float pan;
        public int seed;
    }

    private static float Envelope(float t, float duration, float attack, float release, float decay)
    {
        float env = 1f;
        if (attack > 0f && t < attack) env = t / attack;
        if (decay > 0f) env *= Mathf.Exp(-t / decay);
        if (release > 0f && t > duration - release) env *= Mathf.Clamp01((duration - t) / release);
        return env;
    }

    public static void Tone(float[] mono, float startSec, float durationSec, float frequency, ToneSpec spec, bool wrapLoop)
    {
        ToneCore(mono, null, startSec, durationSec, frequency, spec, wrapLoop);
    }

    public static void Tone(float[] left, float[] right, float startSec, float durationSec, float frequency, ToneSpec spec, bool wrapLoop)
    {
        ToneCore(left, right, startSec, durationSec, frequency, spec, wrapLoop);
    }

    private static void ToneCore(float[] left, float[] right, float startSec, float durationSec, float frequency, ToneSpec spec, bool wrapLoop)
    {
        int length = left.Length;
        int start = Mathf.RoundToInt(startSec * SampleRate);
        int count = Mathf.Max(2, Mathf.CeilToInt(durationSec * SampleRate));
        WaveFunction wave = spec.wave ?? Sine;
        float detune = Mathf.Pow(2f, spec.detuneCents / 1200f);
        float gainL = 1f - Mathf.Max(0f, spec.pan);
        float gainR = 1f + Mathf.Min(0f, spec.pan);
        // 用 double 累加相位：长音（12 秒铺底）用 float 会累积出可闻失谐
        double phase = 0.0;
        double vibratoPhase = 0.0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float freq = frequency;
            if (spec.glideTo > 0f) freq = Mathf.Lerp(frequency, spec.glideTo, Mathf.Clamp01(t / durationSec));
            if (spec.vibratoDepth > 0f)
            {
                vibratoPhase += spec.vibratoHz / (double)SampleRate;
                if (vibratoPhase >= 1.0) vibratoPhase -= 1.0;
                freq *= 1f + spec.vibratoDepth * Mathf.Sin((float)vibratoPhase * Mathf.PI * 2f);
            }
            freq *= detune;
            phase += freq / (double)SampleRate;
            if (phase >= 1.0) phase -= 1.0;

            float sample = wave((float)phase) * spec.amplitude * Envelope(t, durationSec, spec.attack, spec.release, spec.decay);
            int index = start + i;
            if (wrapLoop) index %= length;
            else if (index >= length) break;
            if (index < 0) continue;
            if (right == null)
            {
                left[index] += sample;
            }
            else
            {
                left[index] += sample * gainL;
                right[index] += sample * gainR;
            }
        }
    }

    public static void Noise(float[] mono, float startSec, float durationSec, NoiseSpec spec, bool wrapLoop)
    {
        NoiseCore(mono, null, startSec, durationSec, spec, wrapLoop);
    }

    public static void Noise(float[] left, float[] right, float startSec, float durationSec, NoiseSpec spec, bool wrapLoop)
    {
        NoiseCore(left, right, startSec, durationSec, spec, wrapLoop);
    }

    private static void NoiseCore(float[] left, float[] right, float startSec, float durationSec, NoiseSpec spec, bool wrapLoop)
    {
        int length = left.Length;
        int start = Mathf.RoundToInt(startSec * SampleRate);
        int count = Mathf.Max(2, Mathf.CeilToInt(durationSec * SampleRate));
        System.Random random = new System.Random(spec.seed);
        float gainL = 1f - Mathf.Max(0f, spec.pan);
        float gainR = 1f + Mathf.Min(0f, spec.pan);
        float low = 0f;
        float highState = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float white = (float)(random.NextDouble() * 2.0 - 1.0);

            float cutoff = spec.lowCut > 0f ? spec.lowCut : SampleRate * 0.45f;
            if (spec.lowSweepTo > 0f) cutoff = Mathf.Lerp(cutoff, spec.lowSweepTo, Mathf.Clamp01(t / durationSec));
            low += FilterAlpha(cutoff) * (white - low);

            float sample = low;
            if (spec.highCut > 0f)
            {
                highState += FilterAlpha(spec.highCut) * (sample - highState);
                sample -= highState;
            }

            sample *= spec.amplitude * Envelope(t, durationSec, spec.attack, spec.release, spec.decay);
            int index = start + i;
            if (wrapLoop) index %= length;
            else if (index >= length) break;
            if (index < 0) continue;
            if (right == null)
            {
                left[index] += sample;
            }
            else
            {
                left[index] += sample * gainL;
                right[index] += sample * gainR;
            }
        }
    }

    // ================= AudioClip 组装 =================

    public static AudioClip ToMonoClip(string name, float[] mono)
    {
        Fade(mono);
        AudioClip clip = AudioClip.Create(name, mono.Length, 1, SampleRate, false);
        clip.SetData(mono, 0);
        return clip;
    }

    public static AudioClip ToStereoClip(string name, float[] left, float[] right)
    {
        Fade(left, right);
        int frames = Mathf.Min(left.Length, right.Length);
        float[] interleaved = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }
        AudioClip clip = AudioClip.Create(name, frames, 2, SampleRate, false);
        clip.SetData(interleaved, 0);
        return clip;
    }
}
