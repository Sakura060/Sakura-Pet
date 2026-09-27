namespace EriiPet.App;

/// <summary>交互注意力共享状态（交互控制器与闲置/随机调度共用）</summary>
public sealed class AttentionState
{
    public DateTime LastInteraction { get; set; } = DateTime.Now;
    public DateTime LastRandomAction { get; set; } = DateTime.Now;
    public bool StretchPlayed { get; set; }
    public bool ChatActive { get; set; }

    /// <summary>发生任意用户交互：重置闲置计时与睡前伸懒腰标记</summary>
    public void MarkInteracted()
    {
        LastInteraction = DateTime.Now;
        LastRandomAction = DateTime.Now;
        StretchPlayed = false;
    }
}
