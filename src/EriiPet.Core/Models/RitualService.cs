namespace EriiPet.Core.Models;

/// <summary>
/// 时间仪式引擎 — 让绘梨衣有自己的生活节奏
/// 早安/晚安/周末/周一/安装周年等特殊时刻的独特反应
/// </summary>
public class RitualService
{
    private readonly AffectionService _affection;
    private readonly RelationshipService _relation;
    private DateTime _lastRitualCheck = DateTime.MinValue;
    private readonly HashSet<string> _todayTriggers = new();

    // 外部天气（雨声彩蛋用）：0=未知，其余=open-meteo 天气码
    public int WeatherCode { get; set; }

    // 用户生日（MM-dd，空=未设置），由外部同步
    public string UserBirthday { get; set; } = "";

    // 2026-2035 年农历正月初一（春节）与七月初七（七夕）公历日期
    private static readonly Dictionary<int, DateTime> LunarNewYear = new()
    {
        [2026] = new(2026, 2, 17), [2027] = new(2027, 2, 6), [2028] = new(2028, 1, 26),
        [2029] = new(2029, 2, 13), [2030] = new(2030, 2, 3), [2031] = new(2031, 1, 23),
        [2032] = new(2032, 2, 11), [2033] = new(2033, 1, 31), [2034] = new(2034, 2, 19),
        [2035] = new(2035, 2, 8),
    };
    private static readonly Dictionary<int, DateTime> Qixi = new()
    {
        [2026] = new(2026, 8, 19), [2027] = new(2027, 8, 8), [2028] = new(2028, 8, 26),
        [2029] = new(2029, 8, 16), [2030] = new(2030, 8, 5), [2031] = new(2031, 8, 24),
        [2032] = new(2032, 8, 12), [2033] = new(2033, 8, 2), [2034] = new(2034, 8, 22),
        [2035] = new(2035, 8, 11),
    };

    public RitualService(AffectionService affection, RelationshipService relation)
    {
        _affection = affection;
        _relation = relation;
    }

    // ═══════════════════════════════════════════════
    //  每日重置
    // ═══════════════════════════════════════════════

    public void OnNewDay()
    {
        _todayTriggers.Clear();
    }

    // ═══════════════════════════════════════════════
    //  检查并返回匹配的仪式事件
    // ═══════════════════════════════════════════════

    public RitualEvent? Check(DateTime now)
    {
        // 每天重置
        if (now.Date != _lastRitualCheck.Date)
            OnNewDay();

        // 冷却：每分钟最多一次
        if ((now - _lastRitualCheck).TotalMinutes < 5)
            return null;

        _lastRitualCheck = now;

        // 按优先级检查
        return CheckFirstInteraction(now)
            ?? CheckBirthday(now)
            ?? CheckUserBirthday(now)
            ?? CheckFestival(now)
            ?? CheckAnniversary(now)
            ?? CheckWeekDayRitual(now)
            ?? CheckTimeRitual(now)
            ?? CheckBrotherSong(now);
    }

    // ═══════════════════════════════════════════════
    //  今日首次互动
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckFirstInteraction(DateTime now)
    {
        if (_todayTriggers.Contains("first")) return null;
        _todayTriggers.Add("first");

        bool isWeekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        if (now.Hour >= 6 && now.Hour < 10)
        {
            return new RitualEvent
            {
                Type = RitualType.MorningGreeting,
                Message = isWeekend
                    ? "早…再睡一会…"
                    : "早…Sakura。今天也一起。",
                Emotion = PetState.Smirk,
                StretchAnimation = true,
            };
        }
        if (now.Hour >= 10 && now.Hour < 14)
        {
            return new RitualEvent
            {
                Type = RitualType.FirstGreeting,
                Message = isWeekend ? "Sakura醒了…周末快乐。" : "Sakura来了。",
                Emotion = PetState.Happy,
            };
        }
        if (now.Hour >= 14)
        {
            return new RitualEvent
            {
                Type = RitualType.FirstGreeting,
                Message = "Sakura…今天才来呢。",
                Emotion = PetState.Acknowledge,
            };
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    //  时段仪式
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckTimeRitual(DateTime now)
    {
        // 午餐（可乐饼日）
        if (now.Hour == 12 && now.Minute < 10 && !_todayTriggers.Contains("lunch"))
        {
            _todayTriggers.Add("lunch");
            return new RitualEvent
            {
                Type = RitualType.LunchReminder,
                Message = QuoteLibrary.Pick("ritual_lunch"),
                Emotion = PetState.Acknowledge,
            };
        }
        // 午后
        if (now.Hour == 14 && now.Minute < 10 && !_todayTriggers.Contains("afternoon"))
        {
            _todayTriggers.Add("afternoon");
            return new RitualEvent
            {
                Type = RitualType.AfternoonSleepy,
                Message = "午后…有点困呢。",
                Emotion = PetState.Sleepy,
            };
        }
        // 傍晚
        if (now.Hour == 18 && now.Minute < 10 && !_todayTriggers.Contains("evening"))
        {
            _todayTriggers.Add("evening");
            return new RitualEvent
            {
                Type = RitualType.EveningReflection,
                Message = "傍晚了…世界很温柔。",
                Emotion = PetState.Idle,
            };
        }
        // 深夜
        if (now.Hour == 23 && now.Minute < 10 && !_todayTriggers.Contains("night"))
        {
            _todayTriggers.Add("night");
            return new RitualEvent
            {
                Type = RitualType.LateNightWorry,
                Message = "夜深了…Sakura该睡了。",
                Emotion = PetState.Blush,
            };
        }
        if (now.Hour == 1 || now.Hour == 2)
        {
            if (!_todayTriggers.Contains("late"))
            {
                _todayTriggers.Add("late");
                return new RitualEvent
                {
                    Type = RitualType.LateNightWorry,
                    Message = "……Sakura？这么晚…",
                    Emotion = PetState.Blush,
                };
            }
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    //  星期仪式
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckWeekDayRitual(DateTime now)
    {
        // 周一早晨
        if (now.DayOfWeek == DayOfWeek.Monday && now.Hour >= 7 && now.Hour < 10
            && !_todayTriggers.Contains("monday"))
        {
            _todayTriggers.Add("monday");
            return new RitualEvent
            {
                Type = RitualType.MondayMotivation,
                Message = "新的一周…Sakura加油。",
                Emotion = PetState.Happy,
            };
        }
        // 周五傍晚
        if (now.DayOfWeek == DayOfWeek.Friday && now.Hour >= 17 && now.Hour < 20
            && !_todayTriggers.Contains("friday"))
        {
            _todayTriggers.Add("friday");
            return new RitualEvent
            {
                Type = RitualType.FridayEvening,
                Message = "明天可以休息了…Sakura开心吗？",
                Emotion = PetState.Happy,
            };
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    //  安装周年
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckAnniversary(DateTime now)
    {
        int days = _affection.Data.DaysSinceInstall;
        string? key = null;
        string? msg = null;

        if (days == 7 && !_todayTriggers.Contains("anniv7"))
        {
            key = "anniv7";
            msg = "Sakura…和你在一起7天了。";
        }
        else if (days == 30 && !_todayTriggers.Contains("anniv30"))
        {
            key = "anniv30";
            msg = "一个月了呢…和Sakura。";
        }
        else if (days == 100 && !_todayTriggers.Contains("anniv100"))
        {
            key = "anniv100";
            msg = "100天…谢谢Sakura一直在这里。";
        }
        else if (days > 0 && days % 365 == 0 && !_todayTriggers.Contains("annivY"))
        {
            key = "annivY";
            msg = $"已经{days / 365}年了…Sakura。世界很温柔。";
        }

        if (key != null)
        {
            _todayTriggers.Add(key);
            return new RitualEvent
            {
                Type = RitualType.Anniversary,
                Message = msg!,
                Emotion = PetState.Cuddle,
                SpawnPetals = true, // 相处里程碑也撒一次花
            };
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    //  绘梨衣的生日（7月11日）— 一年中最重要的一天
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckBirthday(DateTime now)
    {
        if (now.Month != 7 || now.Day != 11) return null;
        if (_todayTriggers.Contains("birthday")) return null;
        _todayTriggers.Add("birthday");

        return new RitualEvent
        {
            Type = RitualType.Birthday,
            Message = QuoteLibrary.Pick("birthday"),
            Emotion = PetState.Happy,
            SpawnPetals = true,   // 生日樱花雨
            PlayVoice = true,     // 播放原声配音
        };
    }

    // ═══════════════════════════════════════════════
    //  Sakura 的生日 — 陪伴是双向的
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckUserBirthday(DateTime now)
    {
        var m = System.Text.RegularExpressions.Regex.Match(UserBirthday ?? "",
            @"^\s*(\d{1,2})\s*[-/月]\s*(\d{1,2})\s*日?\s*$");
        if (!m.Success) return null;
        var month = int.Parse(m.Groups[1].Value);
        var day = int.Parse(m.Groups[2].Value);
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;

        if (now.Month != month || now.Day != day) return null;
        if (_todayTriggers.Contains("user_birthday")) return null;
        _todayTriggers.Add("user_birthday");

        return new RitualEvent
        {
            Type = RitualType.UserBirthday,
            Message = QuoteLibrary.Pick("user_birthday"),
            Emotion = PetState.Happy,
            SpawnPetals = true,
        };
    }

    // ═══════════════════════════════════════════════
    //  节日（公历 + 农历查表）
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckFestival(DateTime now)
    {
        string? key = null;

        if (now.Month == 1 && now.Day == 1) key = "festival_newyear";
        else if (now.Month == 2 && now.Day == 14) key = "festival_valentine";
        else if (now.Month == 6 && now.Day == 1) key = "festival_children";
        else if (now.Month == 12 && now.Day == 24) key = "festival_christmas_eve";
        else if (now.Month == 12 && now.Day == 25) key = "festival_christmas";
        else if (IsLunarMatch(LunarNewYear, now)) key = "festival_spring";
        else if (IsLunarMatch(Qixi, now)) key = "festival_qixi";

        if (key == null || _todayTriggers.Contains(key)) return null;
        _todayTriggers.Add(key);

        return new RitualEvent
        {
            Type = RitualType.Festival,
            Message = QuoteLibrary.Pick(key),
            Emotion = PetState.Happy,
            SpawnPetals = key == "festival_spring", // 春节也来一场花雨
        };
    }

    private static bool IsLunarMatch(Dictionary<int, DateTime> table, DateTime now)
        => table.TryGetValue(now.Year, out var d) && d == now.Date;

    // ═══════════════════════════════════════════════
    //  哥哥的歌 — 雨天/深夜低概率彩蛋，点到即止
    // ═══════════════════════════════════════════════

    private RitualEvent? CheckBrotherSong(DateTime now)
    {
        // 每天最多想起一次，且只有 1/3 概率真的想起
        if (_todayTriggers.Contains("brother")) return null;
        _todayTriggers.Add("brother");
        if (Random.Shared.Next(3) != 0) return null;

        bool isRain = WeatherCode is 51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82;
        bool isLateNight = now.Hour >= 23 || now.Hour < 5;

        if (isRain)
        {
            return new RitualEvent
            {
                Type = RitualType.BrotherSong,
                Message = QuoteLibrary.Pick("brother_rain"),
                Emotion = PetState.Idle,
            };
        }
        if (isLateNight)
        {
            return new RitualEvent
            {
                Type = RitualType.BrotherSong,
                Message = QuoteLibrary.Pick("brother_song"),
                Emotion = PetState.Sleepy,
            };
        }
        return null;
    }
}

public class RitualEvent
{
    public RitualType Type { get; set; }
    public string Message { get; set; } = "";
    public PetState? Emotion { get; set; }
    public bool StretchAnimation { get; set; }
    /// <summary>触发樱花粒子雨（生日/春节）</summary>
    public bool SpawnPetals { get; set; }
    /// <summary>触发原声配音（生日）</summary>
    public bool PlayVoice { get; set; }
}

public enum RitualType
{
    MorningGreeting,   // 早安
    FirstGreeting,     // 今天第一次互动
    LunchReminder,     // 午餐
    AfternoonSleepy,   // 午后困
    EveningReflection, // 傍晚
    LateNightWorry,    // 深夜催促
    MondayMotivation,  // 周一加油
    FridayEvening,     // 周五快乐
    Anniversary,       // 安装周年
    Birthday,          // 绘梨衣的生日（7月11日）
    UserBirthday,      // Sakura（用户）的生日
    Festival,          // 节日
    BrotherSong,       // 哥哥的歌（低频彩蛋）
}
