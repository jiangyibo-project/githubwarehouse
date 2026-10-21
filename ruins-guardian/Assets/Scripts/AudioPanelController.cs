using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 第 5 章 UGUI 音频控制：播放、暂停、音量、静音；
/// 并附带 AI 总开关与全部守卫复位按钮。
/// </summary>
public sealed class AudioPanelController : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Button playButton;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button muteButton;
    [SerializeField] private Button aiToggleButton;
    [SerializeField] private Button resetGuardButton;
    [SerializeField] private EnemyWanderAI[] guards;

    private bool muted;
    private bool aiEnabled = true;
    private float volumeBeforeMute = 1f;

    private void Awake()
    {
        if (guards == null || guards.Length == 0)
            guards = Object.FindObjectsOfType<EnemyWanderAI>();

        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.SetValueWithoutNotify(audioSource != null ? audioSource.volume : 0.7f);
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }
        if (playButton != null) playButton.onClick.AddListener(Play);
        if (pauseButton != null) pauseButton.onClick.AddListener(Pause);
        if (muteButton != null) muteButton.onClick.AddListener(ToggleMute);
        if (aiToggleButton != null) aiToggleButton.onClick.AddListener(ToggleAI);
        if (resetGuardButton != null) resetGuardButton.onClick.AddListener(ResetGuards);
    }

    private void OnDestroy()
    {
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(SetVolume);
        if (playButton != null) playButton.onClick.RemoveListener(Play);
        if (pauseButton != null) pauseButton.onClick.RemoveListener(Pause);
        if (muteButton != null) muteButton.onClick.RemoveListener(ToggleMute);
        if (aiToggleButton != null) aiToggleButton.onClick.RemoveListener(ToggleAI);
        if (resetGuardButton != null) resetGuardButton.onClick.RemoveListener(ResetGuards);
    }

    public void Play() { if (audioSource != null) audioSource.Play(); }
    public void Pause() { if (audioSource != null) audioSource.Pause(); }

    public void SetVolume(float value)
    {
        if (audioSource == null) return;
        audioSource.volume = muted ? 0f : Mathf.Clamp01(value);
        if (!muted && value > 0f) volumeBeforeMute = value;
    }

    public void ToggleMute()
    {
        if (audioSource == null) return;
        muted = !muted;
        if (muted)
        {
            volumeBeforeMute = Mathf.Max(audioSource.volume, volumeSlider != null ? volumeSlider.value : 1f);
            audioSource.volume = 0f;
            if (muteButton != null) SetButtonLabel(muteButton, "取消静音");
        }
        else
        {
            float restored = Mathf.Clamp01(volumeSlider != null ? volumeSlider.value : volumeBeforeMute);
            if (restored <= 0f) restored = Mathf.Clamp01(volumeBeforeMute);
            audioSource.volume = restored;
            if (muteButton != null) SetButtonLabel(muteButton, "静音");
        }
    }

    public void ToggleAI()
    {
        aiEnabled = !aiEnabled;
        if (guards != null)
        {
            for (int i = 0; i < guards.Length; i++)
            {
                if (guards[i] != null) guards[i].SetAIEnabled(aiEnabled);
            }
        }
        if (aiToggleButton != null)
            SetButtonLabel(aiToggleButton, aiEnabled ? "AI：开" : "AI：关");
    }

    public void ResetGuards()
    {
        if (guards == null) return;
        for (int i = 0; i < guards.Length; i++)
        {
            if (guards[i] != null) guards[i].ResetGuard();
        }
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null) return;
        Text text = button.GetComponentInChildren<Text>();
        if (text != null) text.text = label;
    }
}
