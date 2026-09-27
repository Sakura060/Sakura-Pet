using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EriiPet.Animation;
using EriiPet.Core.Models;
using static EriiPet.Core.Models.TtsService;

namespace EriiPet.App;

public partial class MainWindow : Window, IWindowContext
{
    // ─── Win32 原生光标（绕过 WPF PointToScreen 的布局延迟） ──
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // ─── DPI 转换 ───────────────────────────────
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    private PetConfig _config;
    private readonly SkiaRenderer _renderer;
    private readonly PetStateMachine _stateMachine = new();
    private readonly AffectionService _affection;
    private readonly DiaryService _diary;
    private readonly MemoryService _memory;
    private readonly TodoService _todo;
    private WeatherInfo? _lastWeather;
    // ─── 全屏检测 ─────────────────────────
    private bool _isFullscreen;


    // ─── 帧计时 ─────────────────────────────────
    private long _lastFrameTimestamp;

    // ─── v3.2 拆分出的协作组件 ──────────────────
    private readonly AttentionState _attention = new();
    private readonly TimerHost _timers = new();
    private VoicePipeline _voice = null!;
    private InteractionController _interaction = null!;
    private ReminderController _reminders = null!;

    // ─── v2.0 新系统 ────────────────────────────
    private readonly SceneContextService _sceneCtx = new();
    private readonly UserMoodService _userMood = new();
    private DateTime _lastSceneCheck = DateTime.MinValue;

    // ─── v3.0 小天地 + 桌面感知 ──────────────────
    private readonly PetWorld _world = new();
    private readonly DesktopWatcher _desktopWatcher = new();

    // ─── v3.1 关系 + 记忆回响 ────────────────────
    private readonly RelationshipService _relation = new();
    private MemoryEchoService? _memoryEcho;

    // ─── v3.2 时间仪式 ────────────────────────────
    private RitualService? _ritual;

    // ─── v3.3 专注陪伴 ────────────────────────────
    private readonly FocusService _focus = new();

    // ─── v3.6 成就播报 ────────────────────────────
    private readonly HashSet<string> _knownAchievements = new();
    private DateTime _lastAchievementCheck = DateTime.MinValue;
    private DateTime _fortuneShownDate = DateTime.MinValue;

    public MainWindow(PetConfig config)
    {
        _config = config;

        // 兜底：模型名只能是没有空格的标准名称
        if (string.IsNullOrWhiteSpace(_config.AiModel) || _config.AiModel.Contains(' '))
            _config.AiModel = "deepseek-chat";

        // 兼容旧配置：如果闲逛时间还是旧的 5/15min，强制用新值
        if (_config.WanderTriggerMinMs >= 5 * 60_000)
        {
            _config.WanderTriggerMinMs = 15_000;
            _config.WanderTriggerMaxMs = 25_000;
            _config.SleepTimeoutMs = Math.Max(_config.SleepTimeoutMs, 60_000);
        }

        InitializeComponent();

        _renderer = new SkiaRenderer(_config);

        _affection = new AffectionService();
        _affection.TryDailyCheckIn();
        _diary = new DiaryService();
        _memory = new MemoryService(_config);
        _todo = new TodoService();

        // ─── v3.6 协作组件 ──
        _voice = new VoicePipeline(() => _config, _renderer);
        _interaction = new InteractionController(() => _config, _stateMachine, _affection,
            _relation, _renderer, _attention, _voice, this, Dispatcher);
        _reminders = new ReminderController(_todo, _stateMachine, _renderer,
            _relation, _attention, this);

        // 成就播报基线：升级安装时已解锁的不刷屏
        foreach (var a in AchievementService.Evaluate(_affection))
            if (a.Unlocked) _knownAchievements.Add(a.Name);

        // v4.0 语音模型：启用但服务没开时提示一次（延迟 15s 等托盘出现）
        if (_config.VoiceModelEnabled)
        {
            _ = Task.Delay(15000).ContinueWith(async _ =>
            {
                var reachable = await VoiceModelService.PingAsync();
                if (!reachable)
                    Dispatcher.Invoke(() => _voice.Speak("语音服务没开…托盘「🎤 启动语音服务」可以启动哦。哒。"));
            });
        }

        // 首次运行提示下载本地记忆模型（零 API 消耗）
        if (!_memory.UseLocal)
        {
            _ = Task.Run(async () =>
            {
                await MemoryService.EnsureModelAsync();
                // 下次重启生效
            });
        }
        var assetsDir = FindAssetsDirectory();
        QuoteLibrary.Load(assetsDir); // 台词包（quotes.json 可覆盖）
        _renderer.LoadSprites(assetsDir, _config.CurrentSkin);
        _lastSkin = _config.CurrentSkin; // 同步实际的皮肤
        _renderer.SetBreathing(false);

        _stateMachine.StateChanged += (old, state) =>
        {
            _renderer.TransitionTo(state);
            if (state != PetState.Idle && state != PetState.Sleepy)
                _renderer.ShowBubble();
        };

        PositionAtBottomRight();

        // 应用保存的大小与透明度
        Width = _config.WindowWidth * _config.RenderScale;
        Height = _config.WindowHeight * _config.RenderScale;
        Opacity = Math.Clamp(_config.WindowOpacity, 0.3f, 1f);

        // 获取 DPI 缩放
        Loaded += (s, e) =>
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            PositionAtBottomRight(); // DPI 就绪后重定位（副屏换算需要真实 DPI）
        };

        _lastFrameTimestamp = Stopwatch.GetTimestamp();

        // ─── v3.2 定时器统一注册（TimerHost） ──
        _timers.Register("render", TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
            (s, e) => { if (IsVisible) Canvas.InvalidateVisual(); }); // 隐藏时不重绘，省 CPU
        _timers.Register("idle", TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnIdleCheck);
        // 随机微表情（每 10s 检测一次，45-90s 间隔随机触发）
        _timers.Register("random", TimeSpan.FromSeconds(_config.DuckIntervalSec), DispatcherPriority.Background,
            OnRandomActionCheck);
        // 小黄鸭彩蛋
        _timers.Register("duck", TimeSpan.FromSeconds(_config.DuckIntervalSec), DispatcherPriority.Background,
            (s, e) =>
            {
                if (_stateMachine.Current == PetState.Idle
                    && Random.Shared.NextDouble() < _config.DuckChance)
                    _renderer.SpawnDuck(); if (_renderer.DuckActive) _renderer.ShowBubbleRaw("🐤 嘎！");
            });
        _timers.Register("todo", TimeSpan.FromSeconds(30), DispatcherPriority.Background,
            (s, e) => _reminders.OnCheck());
        // 高精度秒级检查（临近提醒时 1s 精度 + 倒计时气泡）
        _timers.Register("precise", TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (s, e) => _reminders.OnPrecisionTick());
        _timers.Register("fullscreen", TimeSpan.FromSeconds(2), DispatcherPriority.Background,
            OnFullscreenCheck);

        // 天气：启动时异步获取，之后每小时重试（失败也会自动补上）
        _ = RefreshWeatherAsync();
        _timers.Register("weather", TimeSpan.FromMinutes(60), DispatcherPriority.Background,
            (s, e) => _ = RefreshWeatherAsync());

        // ─── v3.0 小天地引擎（16ms 帧更新） ──
        _renderer.World = _world;
        _world.CanvasWidth = (float)(_config.WindowWidth * _config.RenderScale);
        _world.CanvasHeight = (float)(_config.WindowHeight * _config.RenderScale);
        _timers.Register("world", TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, OnWorldTick);
        // ─── 桌面感知（每 3s） ──
        _timers.Register("desktop", TimeSpan.FromSeconds(3), DispatcherPriority.Background, OnDesktopTick);

        // ─── v3.2 时间仪式（每 2 分钟检查，替代旧整点报时） ──
        _ritual = new RitualService(_affection, _relation);
        _ritual.UserBirthday = _config.UserBirthday;
        _timers.Register("ritual", TimeSpan.FromMinutes(2), DispatcherPriority.Background, OnRitualTick);

        // ─── v3.1 记忆回响（每 5 分钟检查） ──
        _memoryEcho = new MemoryEchoService(_memory, _userMood);
        _timers.Register("echo", TimeSpan.FromMinutes(5), DispatcherPriority.Background, OnMemoryEchoTick);

        // ─── v2.0 场景感知（每 30s 检测） ──
        _timers.Register("scene", TimeSpan.FromSeconds(30), DispatcherPriority.Background, OnSceneTick);

        _voice.ApplyConfig();
    }

    // ═══════════════════════════════════════════════
    //  SkiaSharp 渲染
    // ═══════════════════════════════════════════════

    private void OnPaintSurface(object? sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var deltaMs = (float)((now - _lastFrameTimestamp) * 1000.0 / Stopwatch.Frequency);
        _lastFrameTimestamp = now;
        if (deltaMs > 100) deltaMs = 100f;

        _renderer.Render(e.Surface.Canvas, (float)e.Info.Width, (float)e.Info.Height, deltaMs);
    }

    // ═══════════════════════════════════════════════
    //  点击
    // ═══════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _sceneCtx.RecordInput();
        _interaction.OnLeftButtonDown();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _interaction.OnMouseMove();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _interaction.OnLeftButtonUp(e.GetPosition(this));
    }


    // ═══════════════════════════════════════════════
    //  闲置 → 睡觉
    // ═══════════════════════════════════════════════

    private void OnIdleCheck(object? sender, EventArgs e)
    {
        if (_memory.ShouldNotifyFull())
            _voice.Speak("记忆装满了呢…旧的事情，慢慢忘了。");

        // ─── v3.3 专注陪伴（半程加油 / 完成夸奖） ──
        var focusLine = _focus.Tick();
        if (focusLine != null)
        {
            _voice.Speak(focusLine);
            if (_focus.JustFinished)
            {
                _renderer.SpawnParticles(10, 2200);
                _affection.RecordInteraction("cuddle"); // 完成夸奖按贴贴级加好感
            }
        }

        // ─── 久坐关怀 ──
        var sedentary = _focus.CheckSedentary(_attention.LastInteraction, _config.SedentaryMinutes);
        if (sedentary != null)
            _voice.Speak(sedentary);

        // ─── 成就播报（每 60s 查一次，聊天中不打扰） ──
        if (!_attention.ChatActive && (DateTime.Now - _lastAchievementCheck).TotalSeconds >= 60)
        {
            _lastAchievementCheck = DateTime.Now;
            foreach (var a in AchievementService.NewlyUnlocked(_affection, _knownAchievements))
            {
                _voice.Speak($"解锁新成就…「{a.Name}」！嘿嘿。");
                _renderer.SpawnPetalsRain(12);
            }
        }

        if (_attention.ChatActive) return;
        if (_focus.IsActive) return; // 专注期间不睡觉、不闲置打扰

        var currentState = _stateMachine.Current;
        if (currentState != PetState.Idle && currentState != PetState.Sleepy && currentState != PetState.Wandering) return;

        var idleSec = (DateTime.Now - _attention.LastInteraction).TotalSeconds;
        var sleepSec = _config.SleepTimeoutMs / 1000.0;

        // 睡前伸懒腰
        if (!_attention.StretchPlayed && idleSec >= sleepSec - 5 && currentState == PetState.Idle)
        {
            _attention.StretchPlayed = true;
            _renderer.PlayStretch(() =>
            {
                Dispatcher.Invoke(() => _stateMachine.ForceSleep());
            });
        }
        else if (idleSec >= sleepSec)
        {
            _attention.StretchPlayed = false;
            _stateMachine.ForceSleep();
        }
    }

    // ═══════════════════════════════════════════════
    //  全屏检测
    // ═══════════════════════════════════════════════

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private void OnFullscreenCheck(object? sender, EventArgs e)
    {
        if (!_config.AutoHideFullscreen) return;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        GetWindowRect(hwnd, out var rect);
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        var isFullscreen = rect.Left <= 0 && rect.Top <= 0
            && rect.Right >= screenW - 5 && rect.Bottom >= screenH - 5;

        if (isFullscreen && !_isFullscreen)
        {
            _isFullscreen = true;
            Opacity = 0.25;
            Left = screenW - 60;
            Top = screenH - 80;
        }
        else if (!isFullscreen && _isFullscreen)
        {
            _isFullscreen = false;
            Opacity = Math.Clamp(_config.WindowOpacity, 0.3f, 1f);
            PositionAtBottomRight();
        }
    }

    // ═══════════════════════════════════════════════
    //  天气 + 整点报时
    // ═══════════════════════════════════════════════

    private async Task RefreshWeatherAsync()
    {
        var loc = await WeatherService.GetLocationByIpAsync();
        if (loc == null) return;
        var w = await WeatherService.GetCurrentAsync(loc.Value.lat, loc.Value.lon);
        if (w == null) return;

        _lastWeather = w;
        if (_ritual != null) _ritual.WeatherCode = w.WeatherCode;
        _renderer.SetAmbientWeather(w.WeatherCode); // 雨滴/雪花/花瓣氛围
        var msg = w.Temperature switch
        {
            > 35 => $"{w.Emoji} 今天{w.Description}，{w.Temperature:F0}°C…好热…要化了…",
            > 28 => $"{w.Emoji} {w.Description}，{w.Temperature:F0}°C…有点热呢。",
            < 5 => $"{w.Emoji} {w.Description}，{w.Temperature:F0}°C…好冷…Sakura要穿厚一点。",
            < 15 => $"{w.Emoji} {w.Description}，{w.Temperature:F0}°C…有点凉。",
            _ => $"{w.Emoji} {w.Description}，{w.Temperature:F0}°C…天气不错呢。",
        };

        // 只在 idle 时弹天气
        if (_stateMachine.Current == PetState.Idle)
            _voice.Speak(msg);
    }

    private void OnRandomActionCheck(object? sender, EventArgs e)
    {
        if (_config.InteractionLevel == 0) return;
        if (_focus.IsActive) return; // 专注期间保持安静
        var current = _stateMachine.Current;
        if (current == PetState.Wandering) return; // 小天地 tự管理动作
        if (current != PetState.Idle) return;

        var since = (DateTime.Now - _attention.LastRandomAction).TotalSeconds;
        var min = _config.RandomActionMinMs / 1000.0;
        var max = _config.RandomActionMaxMs / 1000.0;
        if (since < min) return;

        var range = max - min;
        var pos = since - min;
        var prob = pos / Math.Max(1, range);

        // 用户情绪影响触发概率和表情选择
        var talkative = Math.Clamp(_userMood.GetTalkativeness() + _relation.TalkativenessBonus, 0, 1);
        prob *= (0.5f + talkative * 0.5f);

        if (Random.Shared.NextDouble() < prob * 0.3)
        {
            _attention.LastRandomAction = DateTime.Now;
            // 使用场景感知选择更合适的表情
            var mood = _userMood.CurrentProfile;
            var r = Random.Shared.NextDouble();

            if (mood.Happiness < -0.3f)
            {
                // 用户情绪低落 → 安慰向
                _stateMachine.Transition(r < 0.6 ? PetState.Cuddle : PetState.Smirk);
            }
            else if (mood.Stress > 0.5f)
            {
                // 用户压力大 → 轻松向
                _stateMachine.Transition(r < 0.5 ? PetState.Smirk : PetState.Happy);
            }
            else if (r < 0.4)
            {
                _stateMachine.Transition(PetState.Smirk);
            }
            else if (r < 0.7)
            {
                _stateMachine.Transition(PetState.Happy);
            }
            else
            {
                _stateMachine.Transition(PetState.Blush);
            }
        }
    }

    // ═══════════════════════════════════════════════
    //  右键菜单
    // ═══════════════════════════════════════════════


    /// <summary>鼠标进入：熟络了她会偷笑一下（专注期不打扰）</summary>
    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (_stateMachine.Current == PetState.Idle && !_focus.IsActive)
            _stateMachine.Transition(PetState.Smirk);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        _attention.MarkInteracted();
        _stateMachine.Transition(PetState.Acknowledge);
    }

    public void DoRandomExpression()
    {
        _attention.MarkInteracted();
        var states = new[] { PetState.Happy, PetState.Surprised, PetState.Blush, PetState.Smirk, PetState.Cuddle };
        var r = states[Random.Shared.Next(states.Length)];
        _stateMachine.ForceIdle();
        _stateMachine.Transition(r);
    }

    /// <summary>手动触发闲逛</summary>
    public void ForceWander()
    {
        _attention.MarkInteracted();
        if (_stateMachine.Current == PetState.Sleepy)
            _stateMachine.WakeUp();
        _stateMachine.ForceIdle();
        StartWandering();
    }

    private void StartWandering()
    {
        if (!_config.WanderEnabled) return;
        if (_interaction.IsDragging) return;
        if (_stateMachine.Current != PetState.Idle) return;

        _stateMachine.Transition(PetState.Wandering);
        _world.WalkRandom();
    }

    private void StopWandering()
    {
        _stateMachine.ForceIdle();
    }

    // ═══════════════════════════════════════════════
    //  v3.0 小天地引擎
    // ═══════════════════════════════════════════════

    private WorldAction _lastWorldAction = (WorldAction)(-1);

    private void OnWorldTick(object? sender, EventArgs e)
    {
        if (_stateMachine.Current != PetState.Wandering) return;
        _world.Update(16f, _desktopWatcher.CurrentActivity);

        // 小天地动作切换时，为标志性动作挂上专属便签字幕
        var a = _world.CurrentAction;
        if (a != _lastWorldAction)
        {
            _lastWorldAction = a;
            var quoteKey = a switch
            {
                WorldAction.Spin => "wander_spin",     // 转圈 = 旋转木马
                WorldAction.Sit => "wander_sit",
                WorldAction.DuckPet => "wander_duckpet",
                _ => null,
            };
            if (quoteKey != null)
            {
                var line = QuoteLibrary.Pick(quoteKey);
                if (line.Length > 0)
                {
                    _renderer.ShowBubbleRaw(line);
                    _renderer.AddToNotepad(line);
                }
            }
        }
    }

    private ActivityType _lastActivity = ActivityType.Desktop;

    private void OnDesktopTick(object? sender, EventArgs e)
    {
        if (!_config.DesktopAwareEnabled) return;
        _desktopWatcher.Tick();
        var act = _desktopWatcher.CurrentActivity;

        // 活动变化时偶尔反馈
        if (act != _lastActivity && _stateMachine.Current == PetState.Idle)
        {
            _lastActivity = act;
            string? msg = act switch
            {
                ActivityType.Coding => "在写代码呢…Sakura加油。",
                ActivityType.Video => null, // 安静陪看
                ActivityType.Gaming => null, // 躲起来了
                ActivityType.Document => "在工作吗…不打扰。",
                // 听歌时低概率跟着哼一句（切到音乐那一刻）
                ActivityType.Music => Random.Shared.Next(100) < 15 ? "这首歌…好听。哒。" : null,
                _ => null,
            };
            if (msg != null)
            {
                _renderer.ShowBubbleRaw(msg);
                _relation.RecordInteraction(RelationEventType.PetInitiated);
            }
        }
        if (act == _lastActivity) return; // 不变不处理
        _lastActivity = act;
    }

    private async void OnMemoryEchoTick(object? sender, EventArgs e)
    {
        if (_focus.IsActive) return; // 专注期间保持安静
        if (_stateMachine.Current != PetState.Idle && _stateMachine.Current != PetState.Wandering) return;
        var echo = await _memoryEcho!.TryEchoAsync();
        if (echo != null && echo.Message.Length > 0)
        {
            _voice.Speak(echo.Message);
            _relation.RecordInteraction(RelationEventType.PetInitiated);
        }
    }

    private async void OnRitualTick(object? sender, EventArgs e)
    {
        var ritual = _ritual!.Check(DateTime.Now);
        if (ritual == null) return;

        // 仅 idle/wandering 时触发
        if (_stateMachine.Current != PetState.Idle && _stateMachine.Current != PetState.Wandering) return;

        if (ritual.StretchAnimation)
            _renderer.PlayStretch();

        // 生日/春节樱花雨
        if (ritual.SpawnPetals)
            _renderer.SpawnPetalsRain();

        // 生日播放原声配音（可在设置关闭）；原声期间不叠加 TTS，只写便签
        if (ritual.PlayVoice && _config.VoicePackEnabled)
        {
            _voice.SpeakWithPackagedVoice(ritual.Message, FindAssetsDirectory());
        }
        else
        {
            // 晨间首次问候 → 顺手递上今日一签（每天一次）
            if (ritual.Type is RitualType.MorningGreeting or RitualType.FirstGreeting
                && _fortuneShownDate != DateTime.Today)
            {
                _fortuneShownDate = DateTime.Today;
                var (tier, text) = FortuneService.GetDaily(DateTime.Today);
                ritual.Message = $"{ritual.Message} 今日一签…{tier}。{text}";
            }
            await _voice.SpeakAsync(ritual.Message);
        }

        if (ritual.Emotion != null)
            _stateMachine.Transition(ritual.Emotion.Value);

        _relation.RecordInteraction(RelationEventType.PetInitiated);
    }

    // ═══════════════════════════════════════════════
    //  场景感知
    // ═══════════════════════════════════════════════

    private void OnSceneTick(object? sender, EventArgs e)
    {
        if (_focus.IsActive) return; // 专注期间保持安静
        _sceneCtx.Tick(_lastWeather);
        _sceneCtx.Current.IsFullscreen = _isFullscreen;
        var events = _sceneCtx.GetActiveEvents();
        if (events.Count == 0) return;
        var top = events[0];
        if (_stateMachine.Current != PetState.Idle && _stateMachine.Current != PetState.Wandering) return;
        if (_userMood.Stress > 0.7f && top.Message != null && top.Priority < 8) return;
        if (top.Message != null)
        {
            _voice.Speak(top.Message);
            if (top.EmotionHint != null)
                _stateMachine.Transition(top.EmotionHint.Value);
        }
    }

    // ═══════════════════════════════════════════════
    //  日记 / 右键
    // ═══════════════════════════════════════════════

    public void ViewDiary()
    {
        var summary = _diary.GetTodaySummary(_affection,
            _interaction.ClickCountToday, _interaction.ChatCountToday, _interaction.DragCountToday);

        // 关系等级对应的一句"她想说的话"
        var closing = _affection.LevelName switch
        {
            "陌生" => "…还不太熟。但绘梨衣会慢慢记住你的。",
            "认识" => "嗯…今天也记得了你一点点。",
            "朋友" => "和朋友在一起的日子…不无聊。",
            "亲密" => "嘿嘿…绘梨衣今天也很开心。",
            _ => "羁绊…就算世界忘了，绘梨衣也不会忘。",
        };

        var warm = "🌸 绘梨衣的便签\n\n" + summary + "\n\n" + closing + "\n\n—— 世界很温柔。";
        System.Windows.MessageBox.Show(warm, "绘梨衣 · 今日日记");
    }

    public void TellWeather()
    {
        if (_lastWeather != null)
            _voice.Speak($"{_lastWeather.Emoji} {_lastWeather.Description}，{_lastWeather.Temperature:F0}°C");
        else
            _voice.Speak("还不太清楚天气呢…");
    }

    // ═══════════════════════════════════════════════
    //  提醒系统
    // ═══════════════════════════════════════════════

    public void AddQuickReminder(TodoPreset preset)
    {
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"「{preset.Text}」\n\n多久后提醒？\n例：30s / 1.5 / 2m30s / 1h",
            "⏰ 设置提醒时间", preset.DefaultMinutes > 0 ? $"{preset.DefaultMinutes}m" : "5m");
        var result = TodoService.ParseDuration(input?.Trim() ?? "");
        if (result == null) return;
        _todo.Add(preset.Text, DateTime.Now.AddSeconds(result.Value));
        var sec = result.Value;
        var label = sec < 60 ? $"{sec}秒" : sec < 3600 ? $"{(int)(sec / 60)}分钟" : $"{(int)(sec / 3600)}小时";
        _voice.Speak($"记住了…{label}后提醒你。");
    }

    public void AddTodo()
    {
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            "输入提醒内容，如：\n下午3点 开会\n或 30分钟后 喝水", "📋 绘梨衣的便签", "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var time = TodoService.ParseTime(input);
        if (time == null) time = DateTime.Now.AddMinutes(5);
        _todo.Add(input.Trim(), time.Value);
        _voice.Speak($"记住了…{time:HH:mm}提醒你。");
    }

    public void CancelReminders()
    {
        var pending = _todo.Items.Where(i => !i.Notified && i.RemindAt > DateTime.Now).OrderBy(i => i.RemindAt).ToList();
        if (pending.Count == 0) { _voice.Speak("现在没有等待中的提醒。"); return; }
        var lines = pending.Select((p, i) => $"{i + 1}. {p.Text} — {p.DueText}").ToList();
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            $"待处理的提醒：\n\n{string.Join("\n", lines)}\n\n输入序号取消，0=全部取消", "🗑 取消提醒", "");
        if (string.IsNullOrWhiteSpace(input)) return;
        if (int.TryParse(input.Trim(), out var idx))
        {
            if (idx == 0) { foreach (var p in pending) _todo.Items.Remove(p); _voice.Speak("全部取消了。"); }
            else if (idx >= 1 && idx <= pending.Count) { _todo.Items.Remove(pending[idx - 1]); _todo.Save(); _voice.Speak($"「{pending[idx - 1].Text}」…取消了。"); }
        }
    }

    public void DoForceSleep()
    {
        _attention.MarkInteracted();
        _stateMachine.ForceSleep();
    }

    public void ApplyConfig(PetConfig cfg)
    {
        _config = cfg;
        _renderer.SetBreathing(_stateMachine.Current == PetState.Sleepy);
        Opacity = Math.Clamp(cfg.WindowOpacity, 0.3f, 1f);
        _voice.ApplyConfig();
        if (_ritual != null) _ritual.UserBirthday = cfg.UserBirthday;

        // 更新窗口大小
        var dpiScale = _dpiScaleX;
        Width = _config.WindowWidth * cfg.RenderScale;
        Height = _config.WindowHeight * cfg.RenderScale;

        // 闲逛边界随缩放同步（修复：改缩放后画布尺寸不更新）
        _world.CanvasWidth = (float)(_config.WindowWidth * cfg.RenderScale);
        _world.CanvasHeight = (float)(_config.WindowHeight * cfg.RenderScale);

        // 皮肤变了 → 重新加载精灵，立即生效
        if (cfg.CurrentSkin != _lastSkin)
        {
            _lastSkin = cfg.CurrentSkin;
            var assetsDir = FindAssetsDirectory();
            _renderer.LoadSprites(assetsDir, cfg.CurrentSkin);
            _renderer.TransitionTo(_stateMachine.Current);
        }

        PositionAtBottomRight(); // 显示器选择可能变了
    }

    private string _lastSkin = "";

    public void OpenSettings()
    {
        var w = new Views.SettingsWindow(_config, cfg =>
        {
            Dispatcher.Invoke(() => ApplyConfig(cfg));
        }, FindAssetsDirectory());
        w.Show();
    }

    /// <summary>构造"此刻状态"摘要，注入 AI 聊天的系统提示词</summary>
    private string BuildChatContext()
    {
        var now = DateTime.Now;
        var hour = now.Hour switch
        {
            < 6 => "深夜", < 11 => "早上", < 14 => "中午",
            < 18 => "午后", < 23 => "晚上", _ => "深夜",
        };
        var weather = _lastWeather == null
            ? "" : $"，外面{_lastWeather.Description}{_lastWeather.Temperature:F0}°C";
        var interact = $"，今天你们已互动{_interaction.ClickCountToday + _interaction.DragCountToday}次";
        var focus = _focus.IsActive ? $"；Sakura正在专注（还剩约{_focus.RemainingMinutes}分钟），这时不要打扰他" : "";
        return $"现在是{hour}。你们认识{_affection.Data.DaysSinceInstall}天，"
             + $"现在的关系是「{_affection.LevelName}」（好感{_affection.Data.Score:F0}/100）"
             + $"{weather}{interact}{focus}。";
    }

    public void OpenChat()
    {
        if (_stateMachine.Current == PetState.Sleepy)
            _stateMachine.WakeUp();

        _attention.MarkInteracted();
        _attention.ChatActive = true;

        var w = new Views.ChatWindow(_config, _memory, _userMood,
            reply => { Dispatcher.Invoke(() => _voice.Speak(reply)); },
            emotion => { Dispatcher.Invoke(() => _stateMachine.Transition(emotion)); },
            BuildChatContext(),
            GetPortraitPath());
        w.Closed += (_, _) =>
        {
            _attention.ChatActive = false;
            _affection.RecordInteraction("chat");
            _interaction.RecordChat();
        };
        w.Show();
    }

    private static string FindAssetsDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var near = Path.Combine(baseDir, "assets");
        if (Directory.Exists(near)) return near;
        var dir = baseDir;
        for (int i = 0; i < 10; i++)
        {
            var test = Path.Combine(dir, "assets");
            if (Directory.Exists(test)) return test;
            var parent = Path.GetDirectoryName(dir);
            if (parent == null || parent == dir) break;
            dir = parent;
        }
        return baseDir;
    }

    private void PositionAtBottomRight()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var idx = Math.Clamp(_config.ScreenIndex, 0, screens.Length - 1);
        if (idx == 0)
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - _config.WindowWidth - 20;
            Top = workArea.Bottom - _config.WindowHeight - 60;
            return;
        }
        // 副屏：WorkingArea 是物理像素，按 DPI 换算成 DIP
        var dpiX = _dpiScaleX > 0 ? _dpiScaleX : 1.0;
        var dpiY = _dpiScaleY > 0 ? _dpiScaleY : 1.0;
        Left = screens[idx].WorkingArea.Right / dpiX - _config.WindowWidth - 20;
        Top = screens[idx].WorkingArea.Bottom / dpiY - _config.WindowHeight - 60;
    }

    /// <summary>当前皮肤 idle 精灵的路径（聊天窗立绘用）</summary>
    private string GetPortraitPath()
    {
        var assetsDir = FindAssetsDirectory();
        var spriteDir = _config.CurrentSkin == "default"
            ? Path.Combine(assetsDir, "sprites")
            : Path.Combine(assetsDir, "sprites", _config.CurrentSkin);
        return Path.Combine(spriteDir, "erii_idle.png");
    }

    /// <summary>🏆 成就（托盘调用）</summary>
    public void ShowAchievements()
    {
        _attention.MarkInteracted();
        var list = AchievementService.Evaluate(_affection);
        var unlocked = list.Count(a => a.Unlocked);
        var text = AchievementService.Format(list);
        System.Windows.MessageBox.Show(
            $"{text}\n\n—— 已解锁 {unlocked}/{list.Count}", "🏆 绘梨衣与你的成就");
    }

    /// <summary>🧠 记忆管理（托盘调用）：查看并删除她记住的事</summary>
    public void ShowMemoryManager()
    {
        _attention.MarkInteracted();
        while (true)
        {
            var recent = _memory.Recent(20);
            if (recent.Count == 0)
            {
                _voice.Speak("绘梨衣…还没记住什么。多聊聊吧。");
                return;
            }

            var lines = recent.Select((m, i) =>
            {
                var oneLine = m.Text.Replace("\n", " ");
                if (oneLine.Length > 44) oneLine = oneLine[..44] + "…";
                return $"{i + 1}. [{m.Time:MM-dd HH:mm}] {oneLine}";
            }).ToList();

            var input = Microsoft.VisualBasic.Interaction.InputBox(
                $"她记住的事（共 {_memory.Count} 条，显示最近 {recent.Count} 条）\n\n" +
                string.Join("\n", lines) +
                "\n\n输入序号删除该条记忆；留空或 0 = 退出",
                "🧠 她记住的事", "");
            if (string.IsNullOrWhiteSpace(input) || input.Trim() == "0") return;

            if (int.TryParse(input.Trim(), out var idx) && idx >= 1 && idx <= recent.Count)
            {
                var target = recent[idx - 1];
                var confirm = System.Windows.MessageBox.Show(
                    $"让绘梨衣忘掉这条记忆吗？\n\n{target.Text}",
                    "确认忘记", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (confirm == System.Windows.MessageBoxResult.Yes)
                {
                    _memory.DeleteById(target.Id);
                    _voice.Speak("嗯…这件事，绘梨衣忘了。");
                }
            }
        }
    }

    /// <summary>🔮 每日一签（托盘调用；同一天结果固定）</summary>
    public void ShowFortune()
    {
        _attention.MarkInteracted();
        if (_stateMachine.Current == PetState.Sleepy) _stateMachine.WakeUp();
        _stateMachine.ForceIdle();
        _stateMachine.Transition(PetState.Acknowledge);
        var (tier, text) = FortuneService.GetDaily(DateTime.Today);
        _voice.Speak($"今日一签…是{tier}。{text}");
    }

    /// <summary>开始专注陪伴（托盘调用）</summary>
    public void StartFocus(int minutes)
    {
        _attention.MarkInteracted();
        if (_stateMachine.Current == PetState.Sleepy) _stateMachine.WakeUp();
        _stateMachine.ForceIdle();
        _voice.Speak(_focus.Start(minutes) ?? "要开始专注了吗…绘梨衣会安静陪着你。");
        _renderer.ShowBubbleRaw($"🍅 专注 {minutes} 分钟…绘梨衣陪着你。");
    }

    /// <summary>自定义专注时长（5~240 分钟）</summary>
    public void StartFocusCustom()
    {
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            "专注多少分钟？(5~240)", "🍅 专注陪伴", "30");
        if (int.TryParse(input?.Trim(), out var m) && m is >= 5 and <= 240)
            StartFocus(m);
    }

    /// <summary>取消专注（托盘调用）</summary>
    public void StopFocus()
    {
        _voice.Speak(_focus.Stop() ?? "好…那先休息一下。");
    }

    /// <summary>托盘「🍬 投喂」：给绘梨衣小零食（原著吃货属性）</summary>
    public void Feed(string food)
    {
        _attention.MarkInteracted();
        if (_stateMachine.Current == PetState.Sleepy) _stateMachine.WakeUp();
        _stateMachine.ForceIdle();
        _stateMachine.Transition(PetState.Happy);
        _affection.RecordInteraction("feed");
        _voice.Speak(string.Format(QuoteLibrary.Pick("feed"), food));
        _renderer.SpawnParticles(5, 1600);
    }

    /// <summary>🎤 启动本地语音服务（托盘调用，执行交接文档的启动脚本）</summary>
    public void StartVoiceService()
    {
        var bat = _config.VoiceServiceBat;
        if (string.IsNullOrWhiteSpace(bat) || !File.Exists(bat))
        {
            _voice.Speak("没找到语音服务的启动脚本…检查设置里的路径。");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = bat,
                WorkingDirectory = Path.GetDirectoryName(bat) ?? "",
                UseShellExecute = true,
            });
            _voice.Speak("语音服务启动中…第一次要等一小会儿。哒。");
        }
        catch (Exception ex)
        {
            LogService.Warn("Voice", $"启动语音服务失败: {ex.Message}");
            _voice.Speak("启动失败了…看看日志。");
        }
    }

    /// <summary>托盘「倾听绘梨衣」：循环播放原声配音，再点一次停止</summary>
    public void ToggleListenVoice()
    {
        _attention.MarkInteracted();
        if (SoundService.IsPlaying)
        {
            SoundService.Stop();
            _renderer.ShowBubbleRaw("……听完了吗？");
        }
        else
        {
            TtsService.StopPlayback();
            SoundService.ToggleListen(FindAssetsDirectory());
            _stateMachine.ForceIdle();
            _stateMachine.Transition(PetState.Happy);
            _renderer.ShowBubbleRaw("要听绘梨衣说话吗？好呀…哒。");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timers.Dispose();
        SoundService.Stop();
        _renderer.Dispose();
        _memory.Dispose();
        base.OnClosed(e);
    }

    // ═══════════════════════════════════════════════
    //  IWindowContext（交互控制器对窗口的最小依赖）
    // ═══════════════════════════════════════════════

    (double SX, double SY) IWindowContext.DpiScale => (_dpiScaleX, _dpiScaleY);

    void IWindowContext.DispatcherInvoke(Action action) => Dispatcher.Invoke(action);
}
