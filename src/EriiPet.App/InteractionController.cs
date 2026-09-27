using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EriiPet.Animation;
using EriiPet.Core.Models;

namespace EriiPet.App;

/// <summary>
/// 鼠标交互控制器：拖拽（=牵着绘梨衣走）、单击/双击/三击防抖、小黄鸭彩蛋命中。
/// 坐标经 Win32 GetCursorPos 直读，绕过 WPF 布局延迟。
/// </summary>
public sealed class InteractionController
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private readonly Func<PetConfig> _config;
    private readonly PetStateMachine _stateMachine;
    private readonly AffectionService _affection;
    private readonly RelationshipService _relation;
    private readonly SkiaRenderer _renderer;
    private readonly AttentionState _attention;
    private readonly VoicePipeline _voice;
    private readonly IWindowContext _win;
    private readonly Dispatcher _dispatcher;

    private bool _isDragging;
    private double _dragBaseX, _dragBaseY;
    private double _dragBaseMouseX, _dragBaseMouseY;
    private readonly List<DateTime> _dragHistory = new();
    private int _pendingClickCount;
    private DispatcherTimer? _clickDebounceTimer;

    public int ClickCountToday { get; private set; }
    public int DragCountToday { get; private set; }
    public int ChatCountToday { get; private set; }

    public bool IsDragging => _isDragging;

    public InteractionController(Func<PetConfig> config, PetStateMachine stateMachine,
        AffectionService affection, RelationshipService relation, SkiaRenderer renderer,
        AttentionState attention, VoicePipeline voice, IWindowContext win, Dispatcher dispatcher)
    {
        _config = config;
        _stateMachine = stateMachine;
        _affection = affection;
        _relation = relation;
        _renderer = renderer;
        _attention = attention;
        _voice = voice;
        _win = win;
        _dispatcher = dispatcher;
    }

    public void RecordChat() => ChatCountToday++;

    // ═══════════════════════════════════════════════
    //  按下 → 睡觉唤醒 / 停止闲逛 / 开始拖拽
    // ═══════════════════════════════════════════════

    public void OnLeftButtonDown()
    {
        _attention.MarkInteracted();

        if (_stateMachine.Current == PetState.Sleepy)
        {
            _stateMachine.WakeUp();
            return;
        }
        if (_stateMachine.Current == PetState.Wandering)
            _stateMachine.ForceIdle();

        _isDragging = true;
        _dragBaseX = _win.Left;
        _dragBaseY = _win.Top;
        GetCursorPos(out var pt);
        var (sx, sy) = _win.DpiScale;
        _dragBaseMouseX = pt.X / sx;
        _dragBaseMouseY = pt.Y / sy;
        _win.CaptureMouse();
    }

    // ═══════════════════════════════════════════════
    //  移动 → 窗口跟随光标 + 牵手表情
    // ═══════════════════════════════════════════════

    public void OnMouseMove()
    {
        if (!_isDragging || Mouse.LeftButton != MouseButtonState.Pressed) return;

        GetCursorPos(out var pt);
        var (sx, sy) = _win.DpiScale;
        var cursorDipX = pt.X / sx;
        var cursorDipY = pt.Y / sy;
        var motionPx = Math.Abs(cursorDipX - _dragBaseMouseX) + Math.Abs(cursorDipY - _dragBaseMouseY);
        if (motionPx <= 5) return;

        _pendingClickCount = 0;

        if (_stateMachine.Current != PetState.Reject && _stateMachine.Current != PetState.Smirk
            && _stateMachine.Current != PetState.Blush)
        {
            // 牵手表情随好感度升温：初识乖巧点头 → 熟悉偷笑 → 亲密害羞
            if (_affection.DragShowsBlush)
                _stateMachine.Transition(PetState.Blush);
            else if (_affection.DragShowsSmirk)
                _stateMachine.Transition(PetState.Smirk);
            else
                _stateMachine.Transition(PetState.Acknowledge);
        }

        _win.Left = _dragBaseX + (cursorDipX - _dragBaseMouseX);
        _win.Top = _dragBaseY + (cursorDipY - _dragBaseMouseY);
    }

    // ═══════════════════════════════════════════════
    //  抬起 → 拖拽结算 / 鸭子猜拳 / 点击防抖
    // ═══════════════════════════════════════════════

    public void OnLeftButtonUp(System.Windows.Point pos)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _win.ReleaseMouseCapture();

        GetCursorPos(out var pt);
        var (sx, sy) = _win.DpiScale;
        var cursorDipX = pt.X / sx;
        var cursorDipY = pt.Y / sy;
        var totalMovement = Math.Abs(cursorDipX - _dragBaseMouseX) + Math.Abs(cursorDipY - _dragBaseMouseY);

        if (totalMovement > 5)
        {
            // ─── 拖拽结束（= 牵着绘梨衣走） ──
            var now = DateTime.Now;
            _dragHistory.Add(now);
            _dragHistory.RemoveAll(t => (now - t).TotalSeconds > 3);

            if (_dragHistory.Count >= 3)
            {
                // 3 秒内连拖 3 次 = 被甩来甩去，真的生气了
                _stateMachine.Transition(PetState.Annoyed);
                _affection.RecordInteraction("drag_rage");
                DragCountToday++;
                _voice.Speak(QuoteLibrary.Pick("drag_rage"));
            }
            else if (_stateMachine.Current != PetState.Annoyed)
            {
                // 牵着手带她走 → 好感小幅增加 + 专属台词
                _affection.RecordInteraction("drag");
                DragCountToday++;
                _stateMachine.ForceIdle();
                _voice.Speak(QuoteLibrary.Pick("carry"));
            }
            return;
        }

        if (_renderer.DuckActive)
        {
            // ─── 点击鸭子 → 石头剪刀布（画布坐标适配高 DPI） ──
            var (dsx, dsy) = _win.DpiScale;
            var cx = (float)(pos.X * dsx);
            var cy = (float)(pos.Y * dsy);
            if (_renderer.HitTestDuck(cx, cy))
            {
                _voice.Speak(_renderer.PlayRPS());
                return;
            }
        }

        // ─── 头顶点击 = 摸头（立即害羞反应，不走防抖） ──
        if (pos.Y < _win.Height * 0.42)
        {
            _stateMachine.ForceIdle();
            _stateMachine.Transition(PetState.Blush);
            _affection.RecordInteraction("click");
            ClickCountToday++;
            _relation.RecordInteraction(RelationEventType.UserInitiated);
            _voice.Speak(QuoteLibrary.Pick("headpat"));
            return;
        }

        // ─── 正常点击 ──
        DebounceClick();
    }

    // ═══════════════════════════════════════════════
    //  点击防抖（延迟判定法：单双击三击互不干扰）
    // ═══════════════════════════════════════════════

    private void DebounceClick()
    {
        _pendingClickCount++;
        _clickDebounceTimer ??= new DispatcherTimer(
            DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromMilliseconds(350) };
        _clickDebounceTimer.Stop();
        _clickDebounceTimer.Tick -= OnClickSettle;
        _clickDebounceTimer.Tick += OnClickSettle;
        _clickDebounceTimer.Start();
    }

    private void OnClickSettle(object? s, EventArgs e)
    {
        _clickDebounceTimer?.Stop();
        var count = _pendingClickCount;
        _pendingClickCount = 0;

        switch (count)
        {
            case 1:
                // 单点：直接惊讶——这是即时反应，需要打断当前状态
                _stateMachine.Transition(PetState.Surprised);
                _affection.RecordInteraction("click");
                ClickCountToday++;
                break;
            case 2:
                // 双击：前置 ForceIdle 确保从 Idle 开始开心
                _stateMachine.ForceIdle();
                _stateMachine.Transition(PetState.Happy);
                _affection.RecordInteraction("doubleclick");
                ClickCountToday++;
                // 30% 概率使出绘梨衣的招牌绝技
                if (Random.Shared.Next(10) < 3)
                    _voice.Speak(QuoteLibrary.Pick("power"));
                break;
            default:
                // 三击：ForceIdle 保证害羞表情从头开始播放
                _stateMachine.ForceIdle();
                _stateMachine.Transition(PetState.Blush);
                _affection.RecordInteraction("tripleclick");
                ClickCountToday++;
                break;
        }
        _relation.RecordInteraction(RelationEventType.UserInitiated);
    }
}
