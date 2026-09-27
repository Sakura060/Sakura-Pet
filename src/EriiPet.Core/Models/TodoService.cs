using System.Text.Json;
using System.Text.RegularExpressions;

namespace EriiPet.Core.Models;

/// <summary>
/// 绘梨衣提醒服务 v2.0
/// - 预设模板 + 自定义内容
/// - 多提醒排队（按时间排序）
/// - 确认统计（及时/超时）
/// - 忽略惩罚状态
/// </summary>
public class TodoService
{
    private static string DataPath => DataPaths.Get("todos.json");

    public List<TodoItem> Items { get; private set; } = new();
    public TodoStats Stats { get; private set; } = new();

    // 忽略惩罚：用户上次忽略提醒后，下一次互动绘梨衣变安静
    public bool IsPunished { get; set; }
    public string? PunishMessage { get; set; } // "哦。" / "Sakura下次早点来嘛。"

    public TodoService()
    {
        (Items, Stats) = Load();
    }

    // ═══════════════════════════════════════════════
    //  预设模板
    // ═══════════════════════════════════════════════

    public static readonly TodoPreset[] Presets =
    [
        new() { Name = "💧 喝水",     Text = "该喝水了",    DefaultMinutes = 60 },
        new() { Name = "☕ 休息一下", Text = "休息一下吧",  DefaultMinutes = 90 },
        new() { Name = "📋 开会",     Text = "要开会了",    DefaultMinutes = 10 },
        new() { Name = "💊 吃药",     Text = "该吃药了",    DefaultMinutes = 0 },
        new() { Name = "⏰ 倒计时",   Text = "时间到了",    DefaultMinutes = 5 },
    ];

    // ═══════════════════════════════════════════════
    //  添加
    // ═══════════════════════════════════════════════

    /// <summary>从预设创建提醒</summary>
    public TodoItem AddFromPreset(TodoPreset preset, DateTime? remindAt = null)
    {
        var at = remindAt ?? DateTime.Now.AddMinutes(preset.DefaultMinutes > 0 ? preset.DefaultMinutes : 10);
        return Add(preset.Text, at);
    }

    /// <summary>快速倒计时（如"5分钟后"）</summary>
    public TodoItem AddQuick(int minutes, string text = "时间到了")
    {
        return Add(text, DateTime.Now.AddMinutes(minutes));
    }

    /// <summary>添加一条提醒</summary>
    public TodoItem Add(string text, DateTime remindAt)
    {
        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString()[..8],
            Text = text,
            RemindAt = remindAt,
            CreatedAt = DateTime.Now,
        };
        Items.Add(item);
        Items = Items.OrderBy(t => t.RemindAt).ToList();
        Stats.TotalCreated++;
        Save();
        return item;
    }

    // ═══════════════════════════════════════════════
    //  触发
    // ═══════════════════════════════════════════════

    /// <summary>获取到期且未提醒的提醒列表</summary>
    public List<TodoItem> GetDueAndUnnotified()
    {
        var now = DateTime.Now;
        return Items.Where(t => !t.Notified && t.RemindAt <= now).ToList();
    }

    /// <summary>标记为已提醒（弹窗已弹出）</summary>
    public void MarkNotified(TodoItem item)
    {
        item.Notified = true;
        item.NotifiedAt = DateTime.Now;
        Save();
    }

    /// <summary>记录确认结果</summary>
    public void Confirm(TodoItem item, bool onTime)
    {
        item.Confirmed = true;
        item.OnTime = onTime;
        if (onTime) Stats.OnTimeCount++;
        Stats.TotalCompleted++;
        Save();
    }

    // ═══════════════════════════════════════════════
    //  忽略惩罚
    // ═══════════════════════════════════════════════

    public void ApplyPunish(bool wasOnTime)
    {
        if (!wasOnTime)
        {
            IsPunished = true;
            PunishMessage = "Sakura…下次早点来。";
        }
    }

    public void ClearPunish()
    {
        IsPunished = false;
        PunishMessage = null;
    }

    // ═══════════════════════════════════════════════
    //  统计
    // ═══════════════════════════════════════════════

    /// <summary>随机夸奖（基于确认率）</summary>
    public string? GetRandomPraise()
    {
        if (Stats.TotalCompleted < 3) return null;
        if (Random.Shared.NextDouble() > 0.2) return null; // 20% 概率触发

        var rate = (double)Stats.OnTimeCount / Math.Max(1, Stats.TotalCompleted);
        return rate switch
        {
            >= 0.9 => "Sakura…每次都很准时呢。",
            >= 0.7 => "Sakura真棒。",
            >= 0.5 => "还不错…",
            _ => "偶尔…也记得看看吧。",
        };
    }

    // ═══════════════════════════════════════════════
    //  I/O
    // ═══════════════════════════════════════════════

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        var data = new TodoPersist { Items = Items, Stats = Stats };
        File.WriteAllText(DataPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static (List<TodoItem>, TodoStats) Load()
    {
        try
        {
            if (File.Exists(DataPath))
            {
                var data = JsonSerializer.Deserialize<TodoPersist>(File.ReadAllText(DataPath));
                if (data != null) return (data.Items, data.Stats);
            }
        }
        catch { }
        return (new(), new());
    }

    /// <summary>从自然语言解析时间</summary>
    /// <summary>解析时长字符串，返回总秒数。支持：30s / 1.5 / 2m30s / 1h / 90秒
    /// null = 解析失败</summary>
    public static double? ParseDuration(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim().ToLowerInvariant();

        double total = 0;

        // "1h30m45s" 格式
        var hMatch = Regex.Match(input, @"([\d.]+)\s*(h|小时|时)");
        if (hMatch.Success) { total += double.Parse(hMatch.Groups[1].Value) * 3600; }

        var mMatch = Regex.Match(input, @"([\d.]+)\s*(m|分钟|分)(?!in)");
        if (mMatch.Success) { total += double.Parse(mMatch.Groups[1].Value) * 60; }

        var sMatch = Regex.Match(input, @"([\d.]+)\s*(s|秒)");
        if (sMatch.Success) { total += double.Parse(sMatch.Groups[1].Value); }

        // 纯数字→分钟
        if (!hMatch.Success && !mMatch.Success && !sMatch.Success)
        {
            if (double.TryParse(input, out var num) && num > 0)
            {
                // 如果 ≤ 5 且没有小数点，可能是秒（30 = 30秒不是30分钟）
                if (num <= 5 && !input.Contains('.'))
                    total = num * 60; // 小数字=分钟
                else if (num >= 60)
                    total = num; // 大数字=秒
                else
                    total = num * 60; // 默认分钟
            }
            else return null;
        }

        return total > 0 ? total : null;
    }

    /// <summary>从自然语言解析时间（用于自定义输入："下午3点"、"30分钟后"）</summary>
    public static DateTime? ParseTime(string input)
    {
        var now = DateTime.Now;

        var relMatch = Regex.Match(input,
            @"(\d+)\s*(分钟|小时|分|时|h|min|m)\s*后");
        if (relMatch.Success)
        {
            var num = int.Parse(relMatch.Groups[1].Value);
            return relMatch.Groups[2].Value switch
            {
                "小时" or "时" or "h" => now.AddHours(num),
                "分钟" or "分" or "min" or "m" => now.AddMinutes(num),
                _ => null,
            };
        }

        var absMatch = Regex.Match(input,
            @"(上午|下午|晚上|中午)?\s*(\d{1,2})\s*点\s*(半|(\d+)分)?");
        if (absMatch.Success)
        {
            var ampm = absMatch.Groups[1].Value;
            var hour = int.Parse(absMatch.Groups[2].Value);
            var half = absMatch.Groups[3].Value == "半";
            var min = half ? 30 : absMatch.Groups[4].Success ? int.Parse(absMatch.Groups[4].Value) : 0;

            if (ampm == "下午" || ampm == "晚上") hour += 12;
            if (ampm == "中午") hour = 12;

            var target = new DateTime(now.Year, now.Month, now.Day, hour % 24, min, 0);
            if (target <= now) target = target.AddDays(1);
            return target;
        }
        return null;
    }
}

// ═══════════════════════════════════════════════
//  数据模型
// ═══════════════════════════════════════════════

public class TodoItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime RemindAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Notified { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public bool Confirmed { get; set; }
    public bool OnTime { get; set; }

    /// <summary>用于显示的日程描述</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DueText => RemindAt.Date == DateTime.Today
        ? RemindAt.ToString("HH:mm")
        : RemindAt.ToString("MM-dd HH:mm");
}

public class TodoPreset
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
    public int DefaultMinutes { get; set; } = 10;
}

public class TodoStats
{
    public int TotalCreated { get; set; }
    public int TotalCompleted { get; set; }
    public int OnTimeCount { get; set; }
}

public class TodoPersist
{
    public List<TodoItem> Items { get; set; } = new();
    public TodoStats Stats { get; set; } = new();
}
