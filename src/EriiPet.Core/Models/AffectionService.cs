using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>好感度系统（0-100，自动持久化）</summary>
public class AffectionService
{
    private readonly string _dataPath;

    public AffectionData Data { get; private set; }

    /// <param name="dataPathOverride">数据文件路径（测试隔离用；null=跟随数据目录设置）</param>
    public AffectionService(string? dataPathOverride = null)
    {
        _dataPath = dataPathOverride ?? DataPaths.Get("affection.json");
        Data = Load(_dataPath);
    }

    /// <summary>修改好感度（正=加好感，负=减）</summary>
    public void Modify(float delta, string reason)
    {
        var old = Data.Score;
        Data.Score = Math.Clamp(Data.Score + delta, 0, 100);
        Data.LastModified = DateTime.Now;

        // 记录事件
        Data.RecentEvents.Add(new AffectionEvent
        {
            Time = DateTime.Now,
            Delta = delta,
            Reason = reason,
        });
        // 只保留最近 50 条
        if (Data.RecentEvents.Count > 50)
            Data.RecentEvents = Data.RecentEvents.TakeLast(50).ToList();

        Save();
    }

    /// <summary>每日签到（每天只计一次）</summary>
    public void TryDailyCheckIn()
    {
        if (Data.LastCheckIn.Date < DateTime.Today)
        {
            Modify(1, "每日签到");
            Data.DaysSinceInstall++;
            Data.InteractionCountToday = 0;
            Data.LastCheckIn = DateTime.Today;
        }
    }

    /// <summary>记录一次互动</summary>
    public void RecordInteraction(string type)
    {
        Data.InteractionCountToday++;
        Data.TotalInteractions++;

        var delta = type switch
        {
            "click" => 0.1f,
            "doubleclick" => 0.2f,
            "tripleclick" => 0.3f,
            "cuddle" => 0.5f,
            "chat" => 0.2f,
            // 绘梨衣最喜欢被 Sakura 牵着走：拖拽=牵手，加少量好感
            "drag" => 0.05f,
            "drag_rage" => -0.5f,
            // 投喂小零食（可乐饼/棉花糖/波板糖）
            "feed" => 0.2f,
            _ => 0f,
        };
        Modify(delta, type);
    }

    /// <summary>好感度等级</summary>
    public string LevelName => Data.Score switch
    {
        < 20 => "陌生",
        < 40 => "认识",
        < 60 => "朋友",
        < 80 => "亲密",
        _ => "羁绊",
    };

    public bool CanCuddle => Data.Score >= 30;
    public bool DragShowsSmirk => Data.Score >= 40;
    public bool DragShowsBlush => Data.Score >= 70;

    private static AffectionData Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AffectionData>(File.ReadAllText(path)) ?? new();
        }
        catch { }
        return new();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dataPath)!);
        File.WriteAllText(_dataPath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public class AffectionData
{
    public float Score { get; set; } = 30; // 初始好感 30（认识阶段）
    public int DaysSinceInstall { get; set; }
    public int TotalInteractions { get; set; }
    public int InteractionCountToday { get; set; }
    public DateTime LastCheckIn { get; set; } = DateTime.Today;
    public DateTime LastModified { get; set; } = DateTime.Now;
    public List<AffectionEvent> RecentEvents { get; set; } = new();
}

public class AffectionEvent
{
    public DateTime Time { get; set; }
    public float Delta { get; set; }
    public string Reason { get; set; } = "";
}
