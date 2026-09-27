using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>每日日记</summary>
public class DiaryService
{
    private static string DataPath => DataPaths.Get("diary.json");

    public List<DiaryEntry> Entries { get; private set; } = new();

    public DiaryService()
    {
        Entries = Load();
    }

    /// <summary>生成/更新今日日记</summary>
    public DiaryEntry UpdateToday(AffectionService aff, int clicks, int chats, int drags)
    {
        var today = DateTime.Today;
        var existing = Entries.Find(e => e.Date.Date == today);

        if (existing == null)
        {
            existing = new DiaryEntry { Date = today };
            Entries.Add(existing);
        }

        existing.AffectionScore = aff.Data.Score;
        existing.Level = aff.LevelName;
        existing.Clicks = clicks;
        existing.Chats = chats;
        existing.Drags = drags;
        existing.DaysSinceInstall = aff.Data.DaysSinceInstall;
        Save();
        return existing;
    }

    /// <summary>获取今天的日记摘要文字</summary>
    public string GetTodaySummary(AffectionService aff, int clicks, int chats, int drags)
    {
        var e = UpdateToday(aff, clicks, chats, drags);
        var parts = new List<string>();

        parts.Add($"📅 {DateTime.Today:yyyy年M月d日}");
        parts.Add($"💛 好感度：{e.AffectionScore:F0}/100（{e.Level}）");
        if (clicks > 0) parts.Add($"🖱️ 点了 {clicks} 次");
        if (chats > 0) parts.Add($"💬 聊了 {chats} 次");
        if (drags > 0) parts.Add($"✊ 拖了 {drags} 次");
        parts.Add($"📆 认识第 {e.DaysSinceInstall} 天");

        return string.Join("\n", parts);
    }

    private static List<DiaryEntry> Load()
    {
        try
        {
            if (File.Exists(DataPath))
                return JsonSerializer.Deserialize<List<DiaryEntry>>(File.ReadAllText(DataPath)) ?? new();
        }
        catch { }
        return new();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        File.WriteAllText(DataPath, JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public class DiaryEntry
{
    public DateTime Date { get; set; }
    public float AffectionScore { get; set; }
    public string Level { get; set; } = "";
    public int Clicks { get; set; }
    public int Chats { get; set; }
    public int Drags { get; set; }
    public int DaysSinceInstall { get; set; }
}
