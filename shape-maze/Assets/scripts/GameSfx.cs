using System.Collections.Generic;
using UnityEngine;

public enum GameSound { Transform, Jump, Shot, Hit, Hurt, EnemyHit, EnemyDown, Pickup, Hammer, Fly, Shield, ShieldHit, Heal, Mark, LevelStart, Goal }

[RequireComponent(typeof(AudioSource))]
public class GameSfx : MonoBehaviour
{
    private static Dictionary<GameSound, AudioClip> _clips;
    private AudioSource _source;

    void Awake()
    {
        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
        _source.volume = 0.45f;
    }

    public static void Play(GameObject owner, GameSound sound)
    {
        if (owner == null) return;
        if (_clips == null) BuildClips();
        GameSfx player = owner.GetComponent<GameSfx>();
        if (player == null) player = owner.AddComponent<GameSfx>();
        AudioClip clip;
        if (player._source != null && _clips.TryGetValue(sound, out clip)) player._source.PlayOneShot(clip);
    }

    static void BuildClips()
    {
        _clips = new Dictionary<GameSound, AudioClip>();
        GameSound[] sounds = (GameSound[])System.Enum.GetValues(typeof(GameSound));
        for (int i = 0; i < sounds.Length; i++)
        {
            float frequency = 280f + i * 73f;
            float duration = sounds[i] == GameSound.Fly ? 0.45f : 0.16f;
            const int rate = 22050;
            int sampleCount = Mathf.RoundToInt(rate * duration);
            float[] samples = new float[sampleCount];
            for (int n = 0; n < sampleCount; n++)
            {
                float t = (float)n / rate;
                float envelope = Mathf.Pow(1f - (float)n / sampleCount, 2f);
                float sweep = frequency * (1f + 0.18f * Mathf.Sin(t * 9f));
                samples[n] = Mathf.Sin(t * sweep * Mathf.PI * 2f) * envelope * 0.28f;
            }
            AudioClip clip = AudioClip.Create("MazeSfx_" + sounds[i], sampleCount, 1, rate, false);
            clip.SetData(samples, 0);
            _clips.Add(sounds[i], clip);
        }
    }
}
