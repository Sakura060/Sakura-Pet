namespace EriiPet.Core.Models;

/// <summary>
/// 用户情绪追踪服务 — 从聊天文字中分析用户情绪
/// 维护一个随时间衰减的情绪剖面，驱动绘梨衣的回应策略
/// </summary>
public class UserMoodService
{
    /// <summary>开心度：-1（很难过）到 +1（非常开心），0=中性</summary>
    public float Happiness { get; private set; }

    /// <summary>压力度：0（轻松）到 1（高度紧张）</summary>
    public float Stress { get; private set; }

    /// <summary>精力值：0（疲惫）到 1（精力充沛），从打字速度/长度推断</summary>
    public float Energy { get; private set; }

    public UserMoodProfile CurrentProfile => new()
    {
        Happiness = Happiness,
        Stress = Stress,
        Energy = Energy,
        Profile = Happiness switch
        {
            > 0.3f => Stress > 0.4f ? MoodProfile.Excited_Anxious : MoodProfile.Happy,
            < -0.3f => Stress > 0.4f ? MoodProfile.Sad_Stressed : MoodProfile.Melancholy,
            _ => Stress > 0.4f ? MoodProfile.Tense : MoodProfile.Neutral,
        },
    };

    private DateTime _lastMessageTime = DateTime.MinValue;

    // ─── 分析新消息 ──

    public void Analyze(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var now = DateTime.Now;

        // 自然衰减（距上一条消息每过 10 分钟衰减 20%）
        if (_lastMessageTime != DateTime.MinValue)
        {
            var gap = (now - _lastMessageTime).TotalMinutes;
            var decay = (float)Math.Exp(-gap / 30.0); // e^(-t/30min)
            Happiness *= decay;
            Stress *= decay;
            Energy *= decay;
        }

        // ─── 情绪关键词匹配 ──
        float happyDelta = 0, stressDelta = 0, energyDelta = 0;

        // 积极
        if (ContainsAny(message, "哈哈", "嘻嘻", "开心", "太好了", "棒", "喜欢", "爱", "谢谢", "nice", "good", "谢谢你", "嗯！", "耶"))
            happyDelta += 0.15f;
        if (ContainsAny(message, "？？？", "？？", "哈哈哈", "笑死", "草", "乐", "233"))
            happyDelta += 0.1f;

        // 消极
        if (ContainsAny(message, "累", "烦", "难", "不想", "崩溃", "救命", "丧", "emo", "难受", "哭"))
            { happyDelta -= 0.2f; stressDelta += 0.15f; energyDelta -= 0.1f; }
        if (ContainsAny(message, "好累", "累死了", "困", "想睡", "没精神", "疲惫"))
            { energyDelta -= 0.2f; }
        if (ContainsAny(message, "气死", "无语", "？？？？？？", "傻逼", "坑爹", "tmd", "草泥马"))
            { stressDelta += 0.25f; happyDelta -= 0.1f; }

        // 压力
        if (ContainsAny(message, "加班", "赶", "deadline", "着急", "来不及", "催", "忙"))
            { stressDelta += 0.2f; energyDelta -= 0.05f; }
        if (ContainsAny(message, "算了", "随便", "无所谓", "不说了"))
            { happyDelta -= 0.1f; energyDelta -= 0.1f; }

        // 精力
        if (message.Length > 50) energyDelta += 0.05f; // 长篇 = 有精力
        if (message.Length < 5) energyDelta -= 0.05f;  // 极短 = 不太想说话

        // 应用
        Happiness = Math.Clamp(Happiness + happyDelta, -1, 1);
        Stress = Math.Clamp(Stress + stressDelta, 0, 1);
        Energy = Math.Clamp(Energy + energyDelta, 0, 1);

        _lastMessageTime = now;
    }

    // ─── 获取回应策略 ──

    /// <summary>绘梨衣当前应该用什么情绪回应</summary>
    public PetState GetRecommendedEmotion()
    {
        if (Happiness < -0.4f) return PetState.Cuddle;     // 安慰
        if (Stress > 0.6f) return PetState.Acknowledge;     // 安静陪着
        if (Happiness > 0.5f) return PetState.Happy;         // 一起开心
        if (Energy < 0.3f && Happiness < 0) return PetState.Sleepy; // "我也困了…"
        return PetState.Idle;
    }

    /// <summary>绘梨衣主动说话的频率倾向（0=安静，1=话痨）</summary>
    public float GetTalkativeness()
    {
        if (Stress > 0.7f) return 0.2f;    // 压力大时绘梨衣安静陪伴
        if (Happiness > 0.5f) return 0.8f;  // 开心时更活泼
        if (Energy < 0.3f) return 0.3f;     // 用户累了就不打扰
        return 0.5f;
    }

    // ─── helper ──

    private static bool ContainsAny(string msg, params string[] keywords)
    {
        foreach (var kw in keywords)
            if (msg.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

public class UserMoodProfile
{
    public float Happiness { get; set; }
    public float Stress { get; set; }
    public float Energy { get; set; }
    public MoodProfile Profile { get; set; }
}

public enum MoodProfile
{
    Happy,           // 开心
    Excited_Anxious, // 兴奋但有点焦虑
    Neutral,          // 中性
    Tense,            // 紧张/专注
    Melancholy,       // 忧郁/低落
    Sad_Stressed,     // 难过+压力
}
