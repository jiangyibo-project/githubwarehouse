/// <summary>背景音乐的整体模式，由 AudioDirector.SetMusicMode 切换。</summary>
public enum AudioMusicMode
{
    /// <summary>只保留环境层（菜单、过场）。</summary>
    Silent,
    /// <summary>探索层 + 环境层，紧张层按威胁渐入。</summary>
    Explore,
    /// <summary>强制拉满紧张层（Boss 战、外部脚本接管）。</summary>
    Combat,
    /// <summary>淡出所有层并播放胜利短段。</summary>
    Victory,
    /// <summary>淡出所有层并播放失败短段。</summary>
    Defeat
}
