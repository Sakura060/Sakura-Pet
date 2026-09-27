namespace EriiPet.Core.Models;

/// <summary>
/// 记忆回响引擎 — 让绘梨衣能"自己想起来的"
/// 不是被动检索，而是基于时间/情绪/随机触发主动回忆
/// </summary>
public class MemoryEchoService
{
    private readonly MemoryService _memory;
    private readonly UserMoodService _mood;
    private readonly DateTime _lastEchoTime = DateTime.MinValue;
    private readonly Random _rng = new();
    private readonly HashSet<string> _echoedIds = new(); // 已经回响过的记忆ID

    public MemoryEchoService(MemoryService memory, UserMoodService mood)
    {
        _memory = memory;
        _mood = mood;
    }

    // ═══════════════════════════════════════════════
    //  回响触发（每次调用检查是否该触发）
    // ═══════════════════════════════════════════════

    public async Task<MemoryEcho?> TryEchoAsync()
    {
        var now = DateTime.Now;

        // 检查频率：每15分钟最多一次
        if ((now - _lastEchoTime).TotalMinutes < 15) return null;

        // 三层触发，按优先级
        var echo = await TryTimeEchoAsync(now)
                ?? await TryEmotionEchoAsync(now)
                ?? TryRandomEcho(now);

        return echo;
    }

    // ═══════════════════════════════════════════════
    //  时间关联回响
    // ═══════════════════════════════════════════════

    private async Task<MemoryEcho?> TryTimeEchoAsync(DateTime now)
    {
        // 查找在当前时段（±1小时）存下的记忆
        var memories = await _memory.RetrieveByTimeRangeAsync(
            now.AddHours(-1), now.AddHours(1), 10);

        foreach (var m in memories)
        {
            if (_echoedIds.Contains(m.Id)) continue;

            var daysAgo = (now - m.Time).Days;
            if (daysAgo < 1 || daysAgo > 14) continue; // 1-14天前的记忆

            _echoedIds.Add(m.Id);
            return new MemoryEcho
            {
                Type = EchoType.TimeAssociation,
                Memory = m,
                Message = daysAgo switch
                {
                    1 => "昨天这个时候…",
                    <= 3 => $"前几天…也是这个时候。",
                    <= 7 => "想起上周…",
                    _ => "想起以前…这时候也在和Sakura说话呢。",
                },
            };
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    //  情绪关联回响
    // ═══════════════════════════════════════════════

    private async Task<MemoryEcho?> TryEmotionEchoAsync(DateTime now)
    {
        // 如果当前情绪有明显倾向，查找相似情绪时的旧记忆
        var profile = _mood.CurrentProfile;

        string query = profile.Profile switch
        {
            MoodProfile.Sad_Stressed or MoodProfile.Melancholy => "难过 累 压力",
            MoodProfile.Happy or MoodProfile.Excited_Anxious => "开心 高兴 好",
            _ => null,
        };
        if (query == null) return null;

        var memories = await _memory.RetrieveAsync(query, 5);
        foreach (var m in memories)
        {
            if (_echoedIds.Contains(m)) continue;
            _echoedIds.Add(m);
            return new MemoryEcho
            {
                Type = EchoType.EmotionAssociation,
                Memory = new MemoryEntry { Time = DateTime.Now, Text = m },
                Message = profile.Profile switch
                {
                    MoodProfile.Sad_Stressed => "上次Sakura也这样…是同一个原因吗？",
                    MoodProfile.Happy => "想起之前…Sakura也是这么开心呢。",
                    _ => "……想起一点事。",
                },
            };
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    //  随机闪回
    // ═══════════════════════════════════════════════

    private MemoryEcho? TryRandomEcho(DateTime now)
    {
        // 每天 1-3 次，概率约 5%
        if (_rng.NextDouble() > 0.05) return null;

        // 这里不实际检索——检索由时间/情绪触发完成
        // 纯随机闪回通过 TryEchoAsync 外层兜底
        return null;
    }

    // ═══════════════════════════════════════════════
    //  周年记忆
    // ═══════════════════════════════════════════════

    public async Task<MemoryEcho?> TryAnniversaryEchoAsync(DateTime now)
    {
        // 查找7天/30天前的记忆
        foreach (int days in new[] { 7, 30 })
        {
            var target = now.AddDays(-days);
            var memories = await _memory.RetrieveByTimeRangeAsync(
                target.AddHours(-2), target.AddHours(2), 5);

            foreach (var m in memories)
            {
                if (_echoedIds.Contains(m.Id)) continue;
                _echoedIds.Add(m.Id);
                return new MemoryEcho
                {
                    Type = EchoType.Anniversary,
                    Memory = m,
                    Message = days == 7
                        ? "一周前…Sakura说过类似的话。"
                        : "一个月了呢…Sakura还记得吗？",
                };
            }
        }
        return null;
    }
}

public class MemoryEcho
{
    public EchoType Type { get; set; }
    public MemoryEntry? Memory { get; set; }
    public string Message { get; set; } = "";
}

public enum EchoType
{
    TimeAssociation,    // 时间关联
    EmotionAssociation, // 情绪关联
    RandomFlashback,    // 随机闪回
    Anniversary,        // 周年
}
