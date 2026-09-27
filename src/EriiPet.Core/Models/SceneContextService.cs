namespace EriiPet.Core.Models;

/// <summary>
/// 场景上下文引擎 — 感知用户所处环境和状态
/// 驱动存在感调节、闲逛行为、场景触发对话
/// </summary>
public class SceneContextService
{
    public SceneContext Current { get; private set; } = new();

    private DateTime _workSessionStart = DateTime.Now;
    private DateTime _lastInputTime = DateTime.Now;
    private WeatherInfo? _weather;

    // ─── 更新周期（由 MainWindow 定时器驱动） ──

    public void Tick(WeatherInfo? weather)
    {
        _weather = weather;

        var now = DateTime.Now;

        // 时段
        Current.TimeOfDay = now.Hour switch
        {
            >= 6 and < 9  => TimeOfDay.Morning,
            >= 9 and < 12 => TimeOfDay.Morning,  // 上午都算 morning
            >= 12 and < 14 => TimeOfDay.Noon,
            >= 14 and < 18 => TimeOfDay.Afternoon,
            >= 18 and < 22 => TimeOfDay.Evening,
            >= 22 or < 4   => TimeOfDay.LateNight,
            _              => TimeOfDay.Night,
        };

        Current.IsWeekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        // 用户空闲检测（用 GetLastInputInfo 替代，这里做粗略估算）
        Current.UserIdleDuration = now - _lastInputTime;
        Current.UserJustReturned = Current.UserIdleDuration.TotalMinutes > 30;

        // 连续工作
        if (Current.UserIdleDuration.TotalMinutes < 3)
        {
            Current.ContinuousWorkDuration = now - _workSessionStart;
        }
        else if (Current.UserIdleDuration.TotalMinutes > 10)
        {
            _workSessionStart = now;
            Current.ContinuousWorkDuration = TimeSpan.Zero;
        }

        Current.Weather = _weather;
    }

    public void RecordInput()
    {
        var now = DateTime.Now;
        if ((now - _lastInputTime).TotalMinutes > 30)
        {
            // 用户刚回来
            Current.UserJustReturned = true;
        }
        _lastInputTime = now;
    }

    /// <summary>获取当前应触发的场景事件（优先级排序）</summary>
    public List<SceneEvent> GetActiveEvents()
    {
        var events = new List<SceneEvent>();
        var now = DateTime.Now;

        // 用户刚回来
        if (Current.UserJustReturned && (now - _lastInputTime).TotalMinutes > 30
            && (now - _lastInputTime).TotalMinutes < 35)
        {
            events.Add(new SceneEvent
            {
                Type = SceneEventType.ReturnGreeting,
                Priority = 10,
                Message = Current.TimeOfDay switch
                {
                    TimeOfDay.Morning => "你回来啦…早上好。",
                    TimeOfDay.LateNight => "这么晚了才回来…担心。",
                    _ => "你去哪了…绘梨衣一直在等你。",
                },
            });
        }

        // 久未休息
        if (Current.ContinuousWorkDuration.TotalHours > 2)
        {
            events.Add(new SceneEvent
            {
                Type = SceneEventType.WorkBreakReminder,
                Priority = 7,
                Message = $"已经工作了{(int)Current.ContinuousWorkDuration.TotalHours}小时了…休息一下吧？",
                AutoExpireSec = 30,
            });
        }

        // 午间提醒
        if (Current.TimeOfDay == TimeOfDay.Noon)
        {
            events.Add(new SceneEvent
            {
                Type = SceneEventType.LunchReminder,
                Priority = 5,
                Message = "12点了…该吃饭了。",
                AutoExpireSec = 60,
            });
        }

        // 深夜催促
        if (Current.TimeOfDay == TimeOfDay.LateNight)
        {
            events.Add(new SceneEvent
            {
                Type = SceneEventType.LateNightWorry,
                Priority = 6,
                Message = now.Hour switch
                {
                    >= 0 and < 3 => "凌晨了…还不睡吗？Sakura。",
                    _ => "夜深了…该睡了。",
                },
                AutoExpireSec = 0,
            });
        }

        // 天气联动
        if (_weather != null)
        {
            if (_weather.WeatherCode is 61 or 63 or 65 or 80 or 81 or 82)
            {
                events.Add(new SceneEvent
                {
                    Type = SceneEventType.RainComfort,
                    Priority = 4,
                    Message = "下雨了呢…要记得带伞。",
                    AutoExpireSec = 60,
                    EmotionHint = PetState.Cuddle,
                });
            }
            if (_weather.Temperature > 35)
            {
                events.Add(new SceneEvent
                {
                    Type = SceneEventType.HotDay,
                    Priority = 3,
                    Message = $"好热…{_weather.Temperature:F0}°C…要化了…",
                    AutoExpireSec = 60,
                });
            }
        }

        // 周末更活泼（通过行为概率影响，不单独弹消息）
        if (Current.IsWeekend && Current.TimeOfDay == TimeOfDay.Morning)
        {
            events.Add(new SceneEvent
            {
                Type = SceneEventType.WeekendPlayful,
                Priority = 2,
                Message = null, // 不说话，只影响行为
            });
        }

        return events.OrderByDescending(e => e.Priority).ToList();
    }
}

public class SceneContext
{
    public TimeOfDay TimeOfDay { get; set; } = TimeOfDay.Morning;
    public bool IsWeekend { get; set; }
    public WeatherInfo? Weather { get; set; }
    public bool IsFullscreen { get; set; }
    public TimeSpan ContinuousWorkDuration { get; set; }
    public TimeSpan UserIdleDuration { get; set; }
    public bool UserJustReturned { get; set; }
}

public enum TimeOfDay
{
    Morning,    // 6-12
    Noon,       // 12-14
    Afternoon,  // 14-18
    Evening,    // 18-22
    Night,      // 22-4 (大部分睡眠时间)
    LateNight,  // 22-4 (主动深夜)
}

public enum SceneEventType
{
    ReturnGreeting,
    WorkBreakReminder,
    LunchReminder,
    LateNightWorry,
    RainComfort,
    HotDay,
    WeekendPlayful,
}

public class SceneEvent
{
    public SceneEventType Type { get; set; }
    public int Priority { get; set; }
    public string? Message { get; set; }
    public int AutoExpireSec { get; set; } = 0; // 0=不自动过期，>0=过期秒数
    public PetState? EmotionHint { get; set; } // 伴随表情
}
