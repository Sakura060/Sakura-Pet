namespace EriiPet.Core.Models;

/// <summary>
/// 闲逛系统 — 定义绘梨衣的"自己活动"行为池
/// 状态机会在 Wandering 状态下从池中随机抽取动作
/// </summary>
public static class WanderActions
{
    /// <summary>所有闲逛微动作的定义</summary>
    public static readonly WanderAction[] All =
    [
        // ═══ 移动类 ═══
        new()
        {
            Name = "走到左边",    Id = "walk_left",
            Type = WanderType.Move,  DurationMs = 2000,
            Weight = 25,
            TargetPosition = WanderTarget.ScreenEdge(0.05f, 0.5f), // 左边缘偏中
            BubbleText = null,
        },
        new()
        {
            Name = "走到右边",    Id = "walk_right",
            Type = WanderType.Move,  DurationMs = 2000,
            Weight = 25,
            TargetPosition = WanderTarget.ScreenEdge(0.95f, 0.5f),
            BubbleText = null,
        },
        new()
        {
            Name = "走到上面",    Id = "walk_top",
            Type = WanderType.Move,  DurationMs = 1500,
            Weight = 15,
            TargetPosition = WanderTarget.ScreenEdge(0.5f, 0.05f),
            BubbleText = null,
        },
        new()
        {
            Name = "踱步",        Id = "pace",
            Type = WanderType.Pace,   DurationMs = 5000,
            Weight = 20,
            TargetPosition = null, // 来回走，不需要目标位
            BubbleText = null,
        },

        // ═══ 姿态类 ═══
        new()
        {
            Name = "蹦跶",        Id = "bounce",
            Type = WanderType.Bounce,  DurationMs = 2500,
            Weight = 18,
            Transform = new WanderTransform { ScaleOscillation = 0.08f, OscPeriodMs = 300 },
            BubbleText = null,
        },
        new()
        {
            Name = "转圈",        Id = "spin",
            Type = WanderType.Spin,    DurationMs = 2000,
            Weight = 12,
            Transform = new WanderTransform { RotationDeg = 360, UseEasing = true },
            BubbleText = null,
        },
        new()
        {
            Name = "伸懒腰（醒着）", Id = "stretch_awake",
            Type = WanderType.Stretch, DurationMs = 2200,
            Weight = 15,
            Transform = new WanderTransform { ScaleX = 1.06f, ScaleY = 0.94f, UseEasing = true },
            BubbleText = "嗯……♡",
        },
        new()
        {
            Name = "坐下望天",    Id = "sit_gaze",
            Type = WanderType.Sit,     DurationMs = 5000,
            Weight = 10,
            Transform = new WanderTransform { OffsetY = 20f, ScaleOscillation = 0.03f, OscPeriodMs = 2000 },
            BubbleText = "天空好蓝…",
        },
        new()
        {
            Name = "趴下踢腿",    Id = "lie_down",
            Type = WanderType.LieDown, DurationMs = 4000,
            Weight = 8,
            Transform = new WanderTransform { OffsetY = 30f, ScaleX = 1.1f, ScaleY = 0.85f },
            BubbleText = "呼……",
        },
        new()
        {
            Name = "探头张望",    Id = "peek",
            Type = WanderType.Peek,    DurationMs = 3000,
            Weight = 10,
            Transform = new WanderTransform { ScaleOscillation = 0.04f, OscPeriodMs = 600 },
            BubbleText = "那边有什么呢…",
        },

        // ═══ 迷你剧情 ═══
        new()
        {
            Name = "追光标",      Id = "chase_cursor",
            Type = WanderType.ChaseCursor, DurationMs = 4000,
            Weight = 8, // 偶尔触发更有惊喜感
            TargetPosition = null, // 动态跟随鼠标
            BubbleText = "等等…",
        },
        new()
        {
            Name = "发现小虫子",  Id = "bug_watch",
            Type = WanderType.BugWatch, DurationMs = 3500,
            Weight = 6,
            Transform = new WanderTransform { OffsetY = -10f, ScaleOscillation = 0.02f, OscPeriodMs = 400 },
            BubbleText = "咦…有个小虫子。",
        },
        new()
        {
            Name = "踩水花",      Id = "splash",
            Type = WanderType.Bounce,  DurationMs = 3000,
            Weight = 5, Condition = ctx => ctx.Weather?.WeatherCode is 61 or 63 or 65 or 80 or 81 or 82,
            Transform = new WanderTransform { ScaleOscillation = 0.1f, OscPeriodMs = 250 },
            BubbleText = "下雨了呢…",
        },
    ];

    /// <summary>根据场景上下文过滤并加权随机选一个</summary>
    public static WanderAction? PickRandom(SceneContext? ctx = null)
    {
        var valid = All.Where(a => a.Condition == null || a.Condition(ctx ?? new SceneContext())).ToList();
        if (valid.Count == 0) return null;

        var totalWeight = valid.Sum(a => a.Weight);
        var roll = Random.Shared.NextDouble() * totalWeight;
        float cum = 0;
        foreach (var a in valid)
        {
            cum += a.Weight;
            if (roll <= cum) return a;
        }
        return valid[^1];
    }
}

public class WanderAction
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";
    public WanderType Type { get; set; }
    public int DurationMs { get; set; } = 3000;
    public int Weight { get; set; } = 10;
    public WanderTransform? Transform { get; set; }
    public WanderTarget? TargetPosition { get; set; }
    public string? BubbleText { get; set; }
    public Func<SceneContext, bool>? Condition { get; set; }
}

public enum WanderType
{
    Move,        // 平移到新位置
    Pace,        // 来回踱步
    Bounce,      // 原地弹跳
    Spin,        // 转圈
    Stretch,     // 伸懒腰
    Sit,         // 坐下
    LieDown,     // 趴下
    Peek,        // 探头
    ChaseCursor, // 追光标
    BugWatch,    // 看小虫子
}

/// <summary>闲逛动作的位置目标</summary>
public class WanderTarget
{
    /// <summary>屏幕横向比例（0=左边，1=右边）</summary>
    public float ScreenX { get; set; } = 0.5f;
    /// <summary>屏幕纵向比例（0=顶部，1=底部）</summary>
    public float ScreenY { get; set; } = 0.8f;
    public bool IsRandom { get; set; }

    public static WanderTarget ScreenEdge(float x, float y) => new() { ScreenX = x, ScreenY = y };
    public static WanderTarget Random() => new() { IsRandom = true };
}

/// <summary>闲逛动作的视觉变形参数</summary>
public class WanderTransform
{
    /// <summary>缩放振荡幅度（正弦波动的峰值偏差）</summary>
    public float ScaleOscillation { get; set; }
    /// <summary>振荡周期（毫秒）</summary>
    public float OscPeriodMs { get; set; } = 1000;
    /// <summary>旋转角度（度）</summary>
    public float RotationDeg { get; set; }
    /// <summary>X 轴拉伸系数</summary>
    public float ScaleX { get; set; } = 1f;
    /// <summary>Y 轴拉伸系数</summary>
    public float ScaleY { get; set; } = 1f;
    /// <summary>Y 轴偏移（像素，正=下移）</summary>
    public float OffsetY { get; set; }
    /// <summary>是否使用缓入缓出</summary>
    public bool UseEasing { get; set; }
}
