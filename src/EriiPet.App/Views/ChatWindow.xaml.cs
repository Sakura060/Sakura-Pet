using System.IO;
using System.Windows;
using System.Windows.Input;
using EriiPet.Animation;
using EriiPet.Core.Models;

namespace EriiPet.App.Views;

public partial class ChatWindow : Window
{
    private readonly PetConfig _cfg;
    private readonly MemoryService? _memory;
    private readonly UserMoodService? _userMood;
    private readonly Action<string>? _onReply;
    private readonly Action<PetState>? _onEmotion;
    private readonly string? _stateContext;
    private readonly List<(string role, string content)> _history = new();
    private bool _loading;

    public ChatWindow(PetConfig cfg, MemoryService? memory, UserMoodService? userMood = null,
        Action<string>? onReply = null, Action<PetState>? onEmotion = null, string? stateContext = null,
        string? portraitPath = null)
    {
        InitializeComponent();
        _cfg = cfg;
        _memory = memory;
        _userMood = userMood;
        _onReply = onReply;
        _onEmotion = onEmotion;
        _stateContext = stateContext;

        // 左侧立绘（当前皮肤的 idle 图）
        try
        {
            if (!string.IsNullOrEmpty(portraitPath) && File.Exists(portraitPath))
                Portrait.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(portraitPath));
            else
                Portrait.Visibility = Visibility.Collapsed;
        }
        catch { Portrait.Visibility = Visibility.Collapsed; }

        if (string.IsNullOrWhiteSpace(cfg.AiEndpoint) || string.IsNullOrWhiteSpace(cfg.AiApiKey))
        {
            ReplyText.Text = "⚠️ 请先在设置里配置 API 端点 和密钥。";
            InputBox.IsEnabled = false;
        }

        InputBox.Focus();
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        InputBox.Focus();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_loading)
            _ = DoSend();
        else if (e.Key == Key.Escape)
            Close();
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await DoSend();
    }

    private async Task DoSend()
    {
        var msg = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(msg)) return;

        // ─── 合规：敏感词过滤 ──
        if (_cfg.ContentFilterEnabled && !SensitiveWordFilter.IsSafe(msg))
        {
            InputBox.Text = "";
            ReplyText.Text = "……（绘梨衣摇了摇头）";
            InputBox.IsEnabled = true;
            InputBox.Focus();
            return;
        }

        InputBox.IsEnabled = false;
        _loading = true;
        ReplyText.Text = "……";
        InputBox.Text = "";

        // ─── 检索相关记忆并注入上下文 ──
        string contextMsg = msg;
        if (_memory != null)
        {
            var memories = await _memory.RetrieveAsync(msg);
            if (memories.Count > 0)
            {
                contextMsg = "[以下是你们之前的对话记忆，请自然地融入回复中]\n"
                    + string.Join("\n", memories)
                    + "\n\n[现在用户说：]" + msg;
            }
        }

        _history.Add(("user", msg));

        // ─── v2.0 用户情绪分析 ──
        _userMood?.Analyze(msg);

        var reply = await ChatService.SendAsync(_cfg, contextMsg, _history, _stateContext);

        if (reply != null)
        {
            // ─── 合规：AI 回复安全检测 ──
            if (_cfg.ContentFilterEnabled && !SensitiveWordFilter.IsReplySafe(reply))
            {
                reply = "……（摇了摇头）";
            }

            _history.Add(("assistant", reply));
            ReplyText.Text = reply;
            _onReply?.Invoke(reply);

            // AI 回复情绪 → 切换表情
            var emotion = SkiaRenderer.DetectEmotion(reply);
            if (emotion != PetState.Idle)
                _onEmotion?.Invoke(emotion);

            // ─── 存入长期记忆 ──
            if (_memory != null)
                _ = _memory.RememberAsync(msg, reply);
        }
        else
        {
            ReplyText.Text = "……（出错了，请检查设置里的 API 配置）";
        }

        InputBox.IsEnabled = true;
        _loading = false;
        InputBox.Focus();
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
