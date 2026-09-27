namespace EriiPet.Core.Models;

/// <summary>
/// 成就系统 — 基于现有好感统计实时计算，无需额外存储。
/// 长线陪伴的目标感：认识天数 / 累计互动 / 好感档位。
/// </summary>
public static class AchievementService
{
    public sealed record Achievement(string Name, string Desc, bool Unlocked);

    public static List<Achievement> Evaluate(AffectionService aff)
    {
        var d = aff.Data;
        bool Days(int n) => d.DaysSinceInstall >= n;
        bool Inter(int n) => d.TotalInteractions >= n;
        bool Score(int n) => d.Score >= n;

        return
        [
            new("初次相遇", "打开桌宠的第一天", true), // 引导型徽章，常驻解锁
            new("一周之约", "认识第 7 天", Days(7)),
            new("一个月了", "认识第 30 天", Days(30)),
            new("百日羁绊", "认识第 100 天", Days(100)),
            new("一周年", "认识第 365 天", Days(365)),
            new("百次心动", "累计互动 100 次", Inter(100)),
            new("千次陪伴", "累计互动 1000 次", Inter(1000)),
            new("朋友以上", "好感达到 60", Score(60)),
            new("羁绊", "好感达到 80（最高档）", Score(80)),
        ];
    }

    public static string Format(List<Achievement> list)
        => string.Join("\n", list.Select(a =>
            (a.Unlocked ? "✅ " : "🔒 ") + $"{a.Name} — {a.Desc}"));

    /// <summary>
    /// 找出"本次新解锁"的成就（并把它们登记进 knownIds）。
    /// 首次调用会把当前全部已解锁项登记为已知（老用户升级不刷屏）。
    /// </summary>
    public static List<Achievement> NewlyUnlocked(AffectionService aff, ISet<string> knownIds)
    {
        var fresh = new List<Achievement>();
        foreach (var a in Evaluate(aff))
        {
            if (!a.Unlocked) continue;
            if (knownIds.Add(a.Name))
                fresh.Add(a);
        }
        return fresh;
    }
}
