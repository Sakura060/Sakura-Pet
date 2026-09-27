using System.Diagnostics;

namespace EriiPet.Core.Models;

/// <summary>
/// 桌面感知服务 — 检测用户当前在做什么
/// 只读窗口标题和进程名，不做屏幕截图。隐私安全。
/// </summary>
public class DesktopWatcher
{
    private static readonly Dictionary<string, ActivityType> ProcessMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // IDE / 编辑器
        ["devenv"] = ActivityType.Coding, ["code"] = ActivityType.Coding,
        ["rider64"] = ActivityType.Coding, ["idea64"] = ActivityType.Coding,
        ["pycharm64"] = ActivityType.Coding, ["webstorm64"] = ActivityType.Coding,
        ["notepad++"] = ActivityType.Coding, ["sublime_text"] = ActivityType.Coding,
        ["cursor"] = ActivityType.Coding, ["windsurf"] = ActivityType.Coding,

        // 视频 / 直播
        ["vlc"] = ActivityType.Video, ["potplayer"] = ActivityType.Video,
        ["mpc-hc64"] = ActivityType.Video, ["bilibili"] = ActivityType.Video,

        // 游戏
        ["steam"] = ActivityType.Gaming, ["valorant"] = ActivityType.Gaming,
        ["league of legends"] = ActivityType.Gaming, ["genshinimpact"] = ActivityType.Gaming,
        ["honkai"] = ActivityType.Gaming, ["eldenring"] = ActivityType.Gaming,
        ["cs2"] = ActivityType.Gaming, ["dota2"] = ActivityType.Gaming,
        ["overwatch"] = ActivityType.Gaming, ["minecraft"] = ActivityType.Gaming,
        ["fortnite"] = ActivityType.Gaming, ["pubg"] = ActivityType.Gaming,

        // 办公
        ["winword"] = ActivityType.Document, ["excel"] = ActivityType.Document,
        ["powerpnt"] = ActivityType.Document, ["wps"] = ActivityType.Document,
        ["outlook"] = ActivityType.Document,

        // 浏览器
        ["chrome"] = ActivityType.Browser, ["msedge"] = ActivityType.Browser,
        ["firefox"] = ActivityType.Browser, ["opera"] = ActivityType.Browser,
        ["brave"] = ActivityType.Browser,

        // 音乐
        ["spotify"] = ActivityType.Music, ["music"] = ActivityType.Music,
        ["foobar2000"] = ActivityType.Music, ["netease"] = ActivityType.Music,
        ["qqmusic"] = ActivityType.Music,
    };

    public ActivityType CurrentActivity { get; private set; } = ActivityType.Desktop;
    public string CurrentWindowTitle { get; private set; } = "";
    public string CurrentProcessName { get; private set; } = "";

    public void Tick()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) { CurrentActivity = ActivityType.Desktop; return; }

        // 检测全屏
        GetWindowRect(hwnd, out var rect);
        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);
        bool isFullscreen = rect.Left <= 0 && rect.Top <= 0
            && rect.Right >= screenW - 5 && rect.Bottom >= screenH - 5;

        // 进程名
        GetWindowThreadProcessId(hwnd, out uint pid);
        CurrentProcessName = "";
        try
        {
            var proc = Process.GetProcessById((int)pid);
            CurrentProcessName = proc.ProcessName;
        }
        catch { }

        // 窗口标题
        var title = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, title, 256);
        CurrentWindowTitle = title.ToString();

        // 分类
        if (isFullscreen && CurrentProcessName != "explorer")
        {
            // 全屏可能是视频或游戏
            CurrentActivity = ProcessMap.GetValueOrDefault(CurrentProcessName, ActivityType.Fullscreen);
        }
        else
        {
            CurrentActivity = ProcessMap.GetValueOrDefault(CurrentProcessName, ActivityType.Desktop);
        }
    }

    /// <summary>最近 N 次 Tick 内检测过的活动类型</summary>
    public List<ActivityType> RecentActivities(int ticks = 5)
    {
        // 简化实现：只返回当前活动
        return [CurrentActivity];
    }

    // ─── Win32 ────────────────────────────────

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
}

public enum ActivityType
{
    Desktop,    // 桌面/空闲
    Coding,     // 写代码
    Video,      // 看视频/全屏视频
    Gaming,     // 打游戏
    Fullscreen, // 未知全屏应用
    Document,   // 文档/表格
    Browser,    // 浏览器
    Music,      // 音乐播放器
    Unknown,    // 其他
}
