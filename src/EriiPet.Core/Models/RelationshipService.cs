using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>
/// 关系动态模型 — 不是线性好感度，而是多维关系
/// 亲密度/依赖度/默契度/活跃期 共同决定绘梨衣的行为
/// </summary>
public class RelationshipService
{
    private static string DataPath => DataPaths.Get("relationship.json");

    public RelationshipData Data { get; private set; } = new();

    public RelationshipService()
    {
        Data = Load();
    }

    // ═══════════════════════════════════════════════
    //  更新指标
    // ═══════════════════════════════════════════════

    public void RecordInteraction(RelationEventType type, int count = 1)
    {
        var now = DateTime.Now;

        switch (type)
        {
            case RelationEventType.Chat:
                Data.Closeness = Math.Clamp(Data.Closeness + 0.05f * count, 0, 100);
                break;
            case RelationEventType.UserInitiated:
                Data.Dependence = Math.Clamp(Data.Dependence + 0.08f, 0, 100);
                break;
            case RelationEventType.PetInitiated:
                Data.Dependence = Math.Clamp(Data.Dependence - 0.02f, 0, 100);
                break;
            case RelationEventType.ReminderOnTime:
                Data.Understanding = Math.Clamp(Data.Understanding + 0.1f, 0, 100);
                break;
            case RelationEventType.ReminderLate:
                Data.Understanding = Math.Clamp(Data.Understanding - 0.05f, 0, 100);
                break;
            case RelationEventType.EmotionMatch:
                Data.Understanding = Math.Clamp(Data.Understanding + 0.15f, 0, 100);
                break;
            case RelationEventType.EmotionMiss:
                Data.Understanding = Math.Clamp(Data.Understanding - 0.03f, 0, 100);
                break;
        }

        // 活跃时段记录
        int hour = now.Hour;
        if (!Data.HourlyActivity.ContainsKey(hour))
            Data.HourlyActivity[hour] = 0;
        Data.HourlyActivity[hour] += count;

        Data.LastInteraction = now;
        Data.TotalInteractions++;

        // 自然衰减——长时间不互动，关系慢慢变淡
        var daysSinceLast = (now - Data.LastDecay).TotalDays;
        if (daysSinceLast > 1)
        {
            float decayFactor = (float)Math.Pow(0.99, daysSinceLast); // 每天衰减1%
            Data.Closeness *= decayFactor;
            Data.LastDecay = now;
        }

        Save();
    }

    // ═══════════════════════════════════════════════
    //  查询
    // ═══════════════════════════════════════════════

    /// <summary>获取当前最活跃的时段</summary>
    public int GetPeakHour()
    {
        if (Data.HourlyActivity.Count == 0) return 10;
        return Data.HourlyActivity.OrderByDescending(kv => kv.Value).First().Key;
    }

    /// <summary>指定时段是否活跃</summary>
    public bool IsActiveHour(int hour)
    {
        if (!Data.HourlyActivity.TryGetValue(hour, out var count)) return false;
        int max = Data.HourlyActivity.Values.Max();
        return count >= max * 0.3f; // 至少是峰值30%
    }

    /// <summary>绘梨衣的话痨程度：亲密度高+依赖度高 → 话多</summary>
    public float TalkativenessBonus => (Data.Closeness + Data.Dependence) / 200f;

    /// <summary>默契度高时回复更简短（"不用说太多你也懂"）</summary>
    public float ConcisenessFactor => Data.Understanding > 70 ? 0.7f : 1f;

    /// <summary>亲密度等级</summary>
    public string ClosenessLevel => Data.Closeness switch
    {
        < 20 => "初识",
        < 40 => "熟悉",
        < 60 => "亲近",
        < 80 => "亲密",
        _ => "羁绊",
    };

    // ═══════════════════════════════════════════════
    //  I/O
    // ═══════════════════════════════════════════════

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        File.WriteAllText(DataPath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static RelationshipData Load()
    {
        try
        {
            if (File.Exists(DataPath))
                return JsonSerializer.Deserialize<RelationshipData>(File.ReadAllText(DataPath)) ?? new();
        }
        catch { }
        return new();
    }
}

public class RelationshipData
{
    public float Closeness { get; set; } = 20;      // 亲密度 0-100
    public float Dependence { get; set; } = 10;       // 依赖度 0-100
    public float Understanding { get; set; } = 5;     // 默契度 0-100
    public Dictionary<int, int> HourlyActivity { get; set; } = new();
    public DateTime LastInteraction { get; set; } = DateTime.Now;
    public DateTime LastDecay { get; set; } = DateTime.Now;
    public int TotalInteractions { get; set; }
}

public enum RelationEventType
{
    Chat,           // 聊天
    UserInitiated,  // 用户主动互动（点击/拖拽）
    PetInitiated,   // 绘梨衣主动发起
    ReminderOnTime, // 按时确认提醒
    ReminderLate,   // 延迟确认提醒
    EmotionMatch,   // 情绪判断准确
    EmotionMiss,    // 情绪判断不准
}
