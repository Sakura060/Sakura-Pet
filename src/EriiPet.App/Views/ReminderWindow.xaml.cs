using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EriiPet.Core.Models;
using static EriiPet.Core.Models.TtsService;

namespace EriiPet.App.Views;

/// <summary>
/// 强制提醒弹窗 — 无法 ESC/Alt+F4/点击空白关闭，必须点确认。
/// 全屏置顶，确保打游戏/看视频时也能看到。
/// </summary>
public partial class ReminderWindow : Window
{
    private readonly TodoItem _item;
    private readonly Action<bool>? _onConfirmed; // true = 及时, false = 超时忽略
    private readonly DateTime _triggerTime;
    private bool _confirmed;

    public ReminderWindow(TodoItem item, Action<bool>? onConfirmed = null, double left = 0, double top = 0)
    {
        InitializeComponent();
        _item = item;
        _onConfirmed = onConfirmed;
        _triggerTime = DateTime.Now;

        // 定位在指定位置（默认由 MainWindow 计算在绘梨衣上方）
        Left = left;
        Top = top;

        ContentText.Text = item.Text;
        DueTimeLabel.Text = $"📋 {item.DueText ?? "现在"}";

        // 入场动画
        Loaded += (_, _) => PlayEntranceAnimation();
    }

    // ═══════════════════════════════════════════════
    //  强制确认 — 禁用所有逃避方式
    // ═══════════════════════════════════════════════

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // ESC / Alt+F4 / 系统快捷键全部拦截
        if (e.Key == Key.Escape || e.Key == Key.System
            || (e.Key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt))
        {
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 未确认前禁止关闭（Alt+F4、任务栏关闭等全部拦截）
        if (!_confirmed)
        {
            e.Cancel = true;

            // 抖动反馈——让用户知道必须点按钮
            ShakeWindow();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 移除关闭按钮（Alt+F4 已在 OnKeyDown 拦截，但窗口装饰也去掉）
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~WS_SYSMENU; // 移除系统菜单（含关闭按钮）
            SetWindowLong(hwnd, GWL_STYLE, style);
        }
    }

    // ═══════════════════════════════════════════════
    //  确认
    // ═══════════════════════════════════════════════

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        // 防双击——如果已经确认过，直接 return
        if (_confirmed) return;
        _confirmed = true;
        ConfirmBtn.IsEnabled = false;

        var elapsed = (DateTime.Now - _triggerTime).TotalMinutes;
        var onTime = elapsed < 3; // 3分钟以内 = 及时

        // 替换按钮文案
        if (!onTime)
        {
            ConfirmBtn.Content = "……下次早点来。";
            ConfirmBtn.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x90, 0x80));
        }
        else
        {
            var quips = new[] {
                "帮到了Sakura…开心。",
                "提醒…送达了。",
                "Sakura真棒。",
                "……嗯。",
                "下次也要记得。",
            };
            ConfirmBtn.Content = quips[Random.Shared.Next(quips.Length)];
        }

        ConfirmBtn.IsEnabled = false;

        // 延迟关闭（让用户看到反馈文字）
        var timer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(1200),
            System.Windows.Threading.DispatcherPriority.Normal,
            (_, _) =>
            {
                _onConfirmed?.Invoke(onTime);
                Close();
            },
            Dispatcher);
        timer.Start();
    }

    // ═══════════════════════════════════════════════
    //  动画
    // ═══════════════════════════════════════════════

    private void PlayEntranceAnimation()
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void ShakeWindow()
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(Left - 8,  KeyTime.FromPercent(0.1)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(Left + 8,  KeyTime.FromPercent(0.2)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(Left - 6,  KeyTime.FromPercent(0.35)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(Left + 6,  KeyTime.FromPercent(0.5)));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(Left,      KeyTime.FromPercent(0.7)));
        BeginAnimation(Window.LeftProperty, anim);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    private const int GWL_STYLE = -16;
    private const int WS_SYSMENU = 0x80000;
}
