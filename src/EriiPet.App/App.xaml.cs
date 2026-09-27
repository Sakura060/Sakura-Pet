using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using EriiPet.Core.Models;

namespace EriiPet.App;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ─── 全局异常兜底：记日志、不闪退 ──
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Error("UI", "UI 线程未处理异常", args.Exception);
            System.Windows.MessageBox.Show(
                "绘梨衣遇到了一点小意外，但已经稳住了。\n详情见日志：" +
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EriiPet", "logs"),
                "绘梨衣桌宠", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogService.Error("APP", "非 UI 线程未处理异常", args.ExceptionObject as Exception);
        System.Windows.Forms.Application.ThreadException += (_, args) =>
            LogService.Error("WINFORMS", "托盘线程异常", args.Exception);

        // ─── 单实例锁 ──
        var existing = FindWindow(null, "绘梨衣桌宠");
        if (existing != IntPtr.Zero)
        {
            ShowWindow(existing, 9);
            SetForegroundWindow(existing);
            Shutdown();
            return;
        }

        var cfg = ConfigService.Load();
        LogService.Info("App", $"启动（{typeof(App).Assembly.GetName().Version}）");

        // 修复历史 bug：数据目录设置从未对好感/记忆/日记等服务生效，迁移旧数据（复制式，无损）
        DataPaths.MigrateFromLegacy(
            "affection.json", "relationship.json", "diary.json",
            "todos.json", "memories.dat", "memories.vec");

        if (cfg.AutoStart) AutoStartService.RepairIfStale();
        AutoBackupService.RunIfDue();

        // ─── 合规：首次启动年龄确认 ──
        if (!cfg.AgeConfirmed)
        {
            var result = System.Windows.MessageBox.Show(
                "绘梨衣桌宠 Pro — 年龄确认\n\n" +
                "本软件包含 AI 对话功能，根据相关法律法规：\n" +
                "· 需年满 18 周岁方可使用完整功能\n" +
                "· 本软件不会收集或上传您的对话记录\n" +
                "· 对话数据仅存储在您的本地设备\n\n" +
                "点击「是」确认您已年满 18 周岁并同意使用。\n" +
                "点击「否」将退出程序。",
                "绘梨衣桌宠 · 年龄确认",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Information);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                cfg.AgeConfirmed = true;
                ConfigService.Save(cfg);
            }
            else
            {
                Shutdown();
                return;
            }
        }

        _mainWindow = new MainWindow(cfg);
        _mainWindow.Show();
        SetupTray();
        LogService.Info("App", "主窗口与托盘就绪");

        // 首次启动欢迎引导
        if (!cfg.WelcomeShown)
        {
            var welcome = new Views.WelcomeWindow();
            welcome.ShowDialog();
            cfg.WelcomeShown = true;
            if (welcome.DontShowAgainChecked)
                ConfigService.Save(cfg);
        }
    }

    private void SetupTray()
    {
        var icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "绘梨衣桌宠",
            Visible = true,
        };

        var iconPath = FindAsset("erii_idle.png");
        if (File.Exists(iconPath))
        {
            using var bmp = new Bitmap(iconPath);
            icon.Icon = Icon.FromHandle(new Bitmap(bmp, 32, 32).GetHicon());
        }

        // 隐藏的 WinForms Form——ContextMenuStrip 需要前台窗口锚点
        var anchor = new System.Windows.Forms.Form
        {
            WindowState = System.Windows.Forms.FormWindowState.Minimized,
            ShowInTaskbar = false,
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None,
            Size = new System.Drawing.Size(1, 1),
            Opacity = 0,
        };
        anchor.Show(); // 创建 Handle
        anchor.Hide();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("👁 显示/隐藏", null, (_, _) =>
        {
            if (_mainWindow?.IsVisible == true) _mainWindow.Hide();
            else { _mainWindow?.Show(); _mainWindow?.Focus(); }
        });
        menu.Items.Add("🎭 随机表情", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.DoRandomExpression();
        });
        menu.Items.Add("🚶 让她散步", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.ForceWander());
        });
        menu.Items.Add("🌤️ 天气", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.TellWeather());
        });
        menu.Items.Add("🎧 倾听绘梨衣", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.ToggleListenVoice());
        });
        menu.Items.Add("🎤 启动语音服务", null, (_, _) =>
        {
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.StartVoiceService());
        });
        var muteItem = new System.Windows.Forms.ToolStripMenuItem("🔇 禁音");
        muteItem.Click += (_, _) =>
        {
            TtsService.Muted = !TtsService.Muted;
            muteItem.Text = TtsService.Muted ? "🔊 开音" : "🔇 禁音";
            if (TtsService.Muted) SoundService.Stop(); // 禁音同时停掉原声
        };
        menu.Items.Add(muteItem);
        menu.Items.Add("💤 让她睡觉", null, (_, _) => _mainWindow?.DoForceSleep());
        menu.Items.Add("💬 聊天", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.OpenChat());
        });

        // ─── 快速提醒（子菜单含预设） ──
        var remindMenu = new System.Windows.Forms.ToolStripMenuItem("📋 提醒");
        foreach (var preset in TodoService.Presets)
        {
            var p = preset; // 闭包捕获
            remindMenu.DropDownItems.Add(p.Name, null, (_, _) =>
            {
                _mainWindow?.Show();
                _mainWindow?.Dispatcher.Invoke(() => _mainWindow.AddQuickReminder(p));
            });
        }
        remindMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
        remindMenu.DropDownItems.Add("✏️ 自定义…", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.AddTodo());
        });
        remindMenu.DropDownItems.Add("🗑 取消提醒…", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.CancelReminders());
        });
        menu.Items.Add(remindMenu);

        // ─── 🍬 投喂 ──
        var feedMenu = new System.Windows.Forms.ToolStripMenuItem("🍬 投喂");
        foreach (var food in new[] { "可乐饼", "棉花糖", "波板糖" })
        {
            var f = food;
            feedMenu.DropDownItems.Add(food, null, (_, _) =>
            {
                _mainWindow?.Show();
                _mainWindow?.Dispatcher.Invoke(() => _mainWindow.Feed(f));
            });
        }
        menu.Items.Add(feedMenu);

        // ─── 🍅 专注陪伴 ──
        var focusMenu = new System.Windows.Forms.ToolStripMenuItem("🍅 专注陪伴");
        foreach (var min in new[] { 25, 45, 60 })
        {
            var m = min;
            focusMenu.DropDownItems.Add($"专注 {min} 分钟", null, (_, _) =>
            {
                _mainWindow?.Show();
                _mainWindow?.Dispatcher.Invoke(() => _mainWindow.StartFocus(m));
            });
        }
        focusMenu.DropDownItems.Add("✏️ 自定义时长…", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.StartFocusCustom());
        });
        focusMenu.DropDownItems.Add("⏹ 结束专注", null, (_, _) =>
        {
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.StopFocus());
        });
        menu.Items.Add(focusMenu);
        menu.Items.Add("⚙️ 设置", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.OpenSettings());
        });
        menu.Items.Add("🔮 今日一签", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.ShowFortune());
        });
        menu.Items.Add("🏆 成就", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.ShowAchievements());
        });
        menu.Items.Add("🧠 她记住的事", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.ShowMemoryManager());
        });
        menu.Items.Add("📂 打开日志", null, (_, _) =>
        {
            try
            {
                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "EriiPet", "logs");
                System.IO.Directory.CreateDirectory(logDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) { LogService.Warn("Tray", $"打开日志失败: {ex.Message}"); }
        });
        menu.Items.Add("📔 今日日记", null, (_, _) =>
        {
            _mainWindow?.Show();
            _mainWindow?.Dispatcher.Invoke(() => _mainWindow.ViewDiary());
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("❌ 退出", null, (_, _) =>
        {
            icon.Visible = false;
            anchor.Dispose();
            _mainWindow?.Close();
            Shutdown();
        });

        icon.MouseClick += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Right)
            {
                // 关键：强制前台 + 显示菜单
                SetForegroundWindow(anchor.Handle);
                menu.Show(System.Windows.Forms.Control.MousePosition);
            }
        };

        icon.DoubleClick += (_, _) =>
        {
            if (_mainWindow?.IsVisible == true) _mainWindow.Hide();
            else { _mainWindow?.Show(); _mainWindow?.Focus(); }
        };
    }

    private static string FindAsset(string filename)
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var t = Path.Combine(dir, "assets", "sprites", filename);
            if (File.Exists(t)) return t;
            var p = Path.GetDirectoryName(dir);
            if (p == null || p == dir) break;
            dir = p;
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "sprites", filename);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? c, string w);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);
}
