namespace EriiPet.Core.Models;

/// <summary>
/// 绘梨衣的小天地 — 管理画布内的位置、边界、道具和原地动作
/// 所有动画都在 SkiaSharp 画布内完成，不依赖窗口移动
/// </summary>
public class PetWorld
{
    // ─── 画布内位置（像素，相对画布左上角） ──
    public float PetX { get; private set; }
    public float PetY { get; private set; }
    public float TargetX { get; private set; }
    public float TargetY { get; private set; }
    public bool IsMoving { get; private set; }

    // ─── 画布尺寸（由外部设置） ──
    public float CanvasWidth { get; set; } = 380;
    public float CanvasHeight { get; set; } = 420;
    public float GroundY => CanvasHeight * 0.78f; // 地面线 Y 坐标

    // ─── 当前动作 ──
    public WorldAction CurrentAction { get; private set; } = WorldAction.Stand;
    public float ActionTimer { get; private set; }
    public float ActionProgress => ActionDuration > 0 ? ActionTimer / ActionDuration : 1f;
    public float ActionDuration { get; private set; } = 3000;

    // ─── 地面线偏移（走路的左右摆动） ──
    public float FootOffset { get; private set; }

    // ─── 状态 ──
    private DateTime _lastActionChange = DateTime.MinValue;
    private float _idleTimer;
    private float _transitionTimer;
    private WorldAction _nextAction = WorldAction.Stand;
    private bool _inTransition;
    private readonly Random _rng = new();

    public PetWorld()
    {
        // 初始位置：画布中央偏下
        PetX = CanvasWidth / 2f;
        PetY = GroundY - 120;
        TargetX = PetX;
        TargetY = PetY;
    }

    // ═══════════════════════════════════════════════
    //  每帧更新
    // ═══════════════════════════════════════════════

    public void Update(float deltaMs, ActivityType userActivity)
    {
        float dt = deltaMs / 1000f;

        // ─── 平滑移动到目标 ──
        float dx = TargetX - PetX;
        float dy = TargetY - PetY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > 0.5f)
        {
            IsMoving = true;
            float speed = 60f; // 像素/秒
            float step = speed * dt;
            float ratio = Math.Min(step / dist, 1f);
            PetX += dx * ratio;
            PetY += dy * ratio;
            // 走路摆幅
            FootOffset = MathF.Sin(PetX * 0.1f) * 3f;
        }
        else
        {
            IsMoving = false;
            PetX = TargetX;
            PetY = TargetY;
            FootOffset *= 0.9f;
        }

        // ─── 动作计时 ──
        ActionTimer += deltaMs;
        _idleTimer += deltaMs;

        // ─── 自动换动作 ──
        if (ActionTimer >= ActionDuration)
        {
            PickNextAction(userActivity);
        }
    }

    // ═══════════════════════════════════════════════
    //  移动
    // ═══════════════════════════════════════════════

    public void WalkTo(float x, float y)
    {
        // 限制在活动范围内（地面线上方 40-180px，水平居中 ±140px）
        float cx = CanvasWidth / 2f;
        TargetX = Math.Clamp(x, cx - 140, cx + 140);
        TargetY = Math.Clamp(y, GroundY - 180, GroundY - 40);
        CurrentAction = WorldAction.Walk;
        ActionDuration = 3000 + _rng.Next(2000);
        ActionTimer = 0;
    }

    public void WalkRandom()
    {
        float cx = CanvasWidth / 2f;
        WalkTo(cx + (_rng.NextSingle() - 0.5f) * 200, GroundY - 100 - _rng.NextSingle() * 80);
    }

    public void GoToEdge(EdgeDirection edge)
    {
        float cx = CanvasWidth / 2f;
        float x = edge switch
        {
            EdgeDirection.Left => cx - 130,
            EdgeDirection.Right => cx + 130,
            _ => cx,
        };
        WalkTo(x, GroundY - 100);
    }

    // ═══════════════════════════════════════════════
    //  动作
    // ═══════════════════════════════════════════════

    public void DoAction(WorldAction action, float duration = 3000)
    {
        CurrentAction = action;
        ActionDuration = duration;
        ActionTimer = 0;
    }

    private void PickNextAction(ActivityType userActivity)
    {
        // 先回 Stand 过渡 500ms，再切新动作
        if (!_inTransition && CurrentAction != WorldAction.Stand && CurrentAction != WorldAction.Walk)
        {
            _inTransition = true;
            _nextAction = DecideAction(userActivity);
            ActionDuration = 500;
            ActionTimer = 0;
            CurrentAction = WorldAction.Stand;
            // 过渡中微微飘动
            return;
        }
        _inTransition = false;

        var roll = _rng.NextSingle();
        WorldAction next;

        // 每 2-3 个动作穿插一次散步
        if (_rng.Next(4) == 0)
        {
            WalkRandom();
            return;
        }

        next = _nextAction != WorldAction.Stand ? _nextAction : DecideAction(userActivity);
        DoAction(next, 2500 + _rng.Next(3000));
    }

    private WorldAction DecideAction(ActivityType userActivity)
    {
        var r = _rng.NextSingle();
        return userActivity switch
        {
            ActivityType.Coding or ActivityType.Document => r switch
            {
                < 0.5f => WorldAction.Sit,
                < 0.7f => WorldAction.LieDown,
                < 0.85f => WorldAction.HeadTilt,
                _ => WorldAction.Peek,
            },
            ActivityType.Gaming or ActivityType.Fullscreen => WorldAction.Hide,
            ActivityType.Video => WorldAction.Peek,
            ActivityType.Music => WorldAction.Sway,
            ActivityType.Desktop => r switch
            {
                < 0.3f => RandomAction(),
                < 0.55f => WorldAction.Walk,
                < 0.7f => WorldAction.Spin,
                < 0.8f => WorldAction.Bounce,
                _ => WorldAction.DuckPet,
            },
            _ => RandomAction(),
        };
    }

    private WorldAction RandomAction() => _rng.Next(12) switch
    {
        0 => WorldAction.Bounce, 1 => WorldAction.Spin, 2 => WorldAction.Sit,
        3 => WorldAction.LieDown, 4 => WorldAction.Stretch, 5 => WorldAction.HeadTilt,
        6 => WorldAction.Wave, 7 => WorldAction.CircleDraw, 8 => WorldAction.DuckPet,
        9 => WorldAction.PlayDead, 10 => WorldAction.Tail, _ => WorldAction.Bounce,
    };
}

public enum WorldAction
{
    Stand,      // 站立呼吸
    Walk,       // 走路
    Bounce,     // 小跳步
    Spin,       // 踮脚转圈
    Sit,        // 坐下翻本子
    LieDown,    // 趴下晃腿
    Stretch,    // 伸懒腰
    HeadTilt,   // 歪头思考
    Wave,       // 对着屏幕外挥手
    Peek,       // 探头张望
    Hide,       // 躲起来
    Sway,       // 摇摆（听音乐）
    CircleDraw, // 蹲下画圈圈
    DuckPet,    // 摸摸鸭子头
    PlayDead,   // 装死 2s → 翻起来偷笑
    Tail,       // 追自己尾巴
}

public enum EdgeDirection { Left, Right, Top }
