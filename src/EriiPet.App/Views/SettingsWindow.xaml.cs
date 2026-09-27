using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using EriiPet.Core.Models;
using Microsoft.Win32;

namespace EriiPet.App.Views;

public partial class SettingsWindow : Window
{
    private readonly PetConfig _cfg;
    private readonly Action<PetConfig> _onSaved;
    private bool _saved;
    private List<SkinInfo> _skinInfos = new();
    private string _assetsDir = "";

    public SettingsWindow(PetConfig cfg, Action<PetConfig> onSaved, string assetsDir = "")
    {
        InitializeComponent();
        _cfg = cfg;
        _onSaved = onSaved;
        _assetsDir = assetsDir;

        // 加载当前值
        AmpSlider.Value = (cfg.BreatheScaleMin + cfg.BreatheScaleMax) / 2f;
        UpdateAmpLabel();
        PeriodSlider.Value = cfg.BreathePeriodMs;
        PeriodLabel.Text = ((int)PeriodSlider.Value).ToString();
        SleepSlider.Value = cfg.SleepTimeoutMs / 1000.0;
        SleepLabel.Text = ((int)SleepSlider.Value).ToString();
        DuckSlider.Value = cfg.DuckChance;
        DuckLabel.Text = $"{cfg.DuckChance * 100:F0}%";
        NightCheck.IsChecked = cfg.NightStartHour < 24;

        // 实时更新标签
        AmpSlider.ValueChanged += (_, _) => UpdateAmpLabel();
        PeriodSlider.ValueChanged += (_, _) => PeriodLabel.Text = ((int)PeriodSlider.Value).ToString();
        SleepSlider.ValueChanged += (_, _) => SleepLabel.Text = ((int)SleepSlider.Value).ToString();
        DuckSlider.ValueChanged += (_, _) =>
            DuckLabel.Text = $"{DuckSlider.Value * 100:F0}%";
        ScaleSlider.Value = cfg.RenderScale;
        ScaleLabel.Text = $"{cfg.RenderScale * 100:F0}%";
        ScaleSlider.ValueChanged += (_, _) =>
            ScaleLabel.Text = $"{ScaleSlider.Value * 100:F0}%";
        DataPathBox.Text = cfg.DataPath ?? "";
        MuteCheck.IsChecked = TtsService.Muted;
        MuteCheck.Checked += (_, _) => TtsService.Muted = true;
        MuteCheck.Unchecked += (_, _) => TtsService.Muted = false;
        VoicePackCheck.IsChecked = cfg.VoicePackEnabled;
        VoicePackCheck.Checked += (_, _) => cfg.VoicePackEnabled = true;
        VoicePackCheck.Unchecked += (_, _) => cfg.VoicePackEnabled = false;
        InteractionSlider.Value = cfg.InteractionLevel;
        UpdateInteractionLabel();
        InteractionSlider.ValueChanged += (_, _) => UpdateInteractionLabel();
        OpacitySlider.Value = cfg.WindowOpacity;
        OpacityLabel.Text = $"{cfg.WindowOpacity * 100:F0}%";
        OpacitySlider.ValueChanged += (_, _) =>
            OpacityLabel.Text = $"{OpacitySlider.Value * 100:F0}%";
        VolumeSlider.Value = cfg.MasterVolume;
        VolumeLabel.Text = $"{cfg.MasterVolume * 100:F0}%";
        VolumeSlider.ValueChanged += (_, _) =>
            VolumeLabel.Text = $"{VolumeSlider.Value * 100:F0}%";
        AutoStartCheck.IsChecked = AutoStartService.IsEnabled();
        UserBirthdayBox.Text = cfg.UserBirthday ?? "";

        // 显示器列表
        var screens = System.Windows.Forms.Screen.AllScreens;
        ScreenCombo.ItemsSource = screens.Select((s, i) =>
            $"显示器 {i + 1} ({s.Bounds.Width}×{s.Bounds.Height}){(s.Primary ? " · 主屏" : "")}").ToList();
        ScreenCombo.SelectedIndex = Math.Clamp(cfg.ScreenIndex, 0, screens.Length - 1);
        FullscreenCheck.IsChecked = cfg.AutoHideFullscreen;
        FullscreenCheck.Checked += (_, _) => cfg.AutoHideFullscreen = true;
        FullscreenCheck.Unchecked += (_, _) => cfg.AutoHideFullscreen = false;

        WanderCheck.IsChecked = cfg.WanderEnabled;
        WanderCheck.Checked += (_, _) => cfg.WanderEnabled = true;
        WanderCheck.Unchecked += (_, _) => cfg.WanderEnabled = false;

        DesktopAwareCheck.IsChecked = cfg.DesktopAwareEnabled;
        DesktopAwareCheck.Checked += (_, _) => cfg.DesktopAwareEnabled = true;
        DesktopAwareCheck.Unchecked += (_, _) => cfg.DesktopAwareEnabled = false;

        PetalCheck.IsChecked = cfg.PetalEffectEnabled;
        PetalCheck.Checked += (_, _) => cfg.PetalEffectEnabled = true;
        PetalCheck.Unchecked += (_, _) => cfg.PetalEffectEnabled = false;

        NightDimCheck.IsChecked = cfg.NightDimEnabled;
        NightDimCheck.Checked += (_, _) => cfg.NightDimEnabled = true;
        NightDimCheck.Unchecked += (_, _) => cfg.NightDimEnabled = false;

        LocalModelCheck.IsChecked = cfg.UseLocalModel;
        LocalModelCheck.Checked += (_, _) => cfg.UseLocalModel = true;
        LocalModelCheck.Unchecked += (_, _) => cfg.UseLocalModel = false;

        // AI 配置
        AiEndpointBox.Text = string.IsNullOrWhiteSpace(cfg.AiEndpoint) ? "https://api.deepseek.com" : cfg.AiEndpoint;
        AiKeyBox.Text = cfg.AiApiKey ?? "";
        AiModelBox.Text = string.IsNullOrWhiteSpace(cfg.AiModel) ? "deepseek-chat" : cfg.AiModel;

        // 本地语音模型
        VoiceModelCheck.IsChecked = cfg.VoiceModelEnabled;
        VoiceModelEndpointBox.Text = string.IsNullOrWhiteSpace(cfg.VoiceModelEndpoint)
            ? "http://127.0.0.1:9880" : cfg.VoiceModelEndpoint;
        VoiceModelRefBox.Text = string.IsNullOrWhiteSpace(cfg.VoiceModelRefAudio)
            ? VoiceModelService.DefaultRefAudio : cfg.VoiceModelRefAudio;
        VoiceModelPromptBox.Text = string.IsNullOrWhiteSpace(cfg.VoiceModelPromptText)
            ? VoiceModelService.DefaultPromptText : cfg.VoiceModelPromptText;
        VoiceServiceBatBox.Text = string.IsNullOrWhiteSpace(cfg.VoiceServiceBat)
            ? @"D:\GPT-SoVITS\启动本地语音服务.bat" : cfg.VoiceServiceBat;

        // ─── 本地模型检测（失败自动重试 10 次，每 3 秒一次） ──
        _ = CheckOllamaWithRetryAsync();

        // ─── 皮肤：动态扫描 ──
        RefreshSkinList();
    }

    private void UpdateAmpLabel()
    {
        var c = (float)AmpSlider.Value;
        AmpLabel.Text = $"{c - 0.01f:F3} — {c + 0.01f:F3}";
    }

    private void UpdateInteractionLabel()
    {
        InteractionLabel.Text = ((int)InteractionSlider.Value) switch
        { 0 => "安静", 1 => "适度", 2 => "话痨", _ => "适度" };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var amp = (float)AmpSlider.Value;
        _cfg.BreatheScaleMin = amp - 0.01f;
        _cfg.BreatheScaleMax = amp + 0.01f;
        _cfg.BreathePeriodMs = (int)PeriodSlider.Value;
        _cfg.SleepTimeoutMs = (int)SleepSlider.Value * 1000;
        _cfg.DuckChance = Math.Round(DuckSlider.Value, 2);
        _cfg.RenderScale = (float)Math.Round(ScaleSlider.Value, 1);
        _cfg.InteractionLevel = (int)InteractionSlider.Value;
        _cfg.WindowOpacity = (float)Math.Round(OpacitySlider.Value, 2);
        _cfg.MasterVolume = (float)Math.Round(VolumeSlider.Value, 2);
        _cfg.UserBirthday = UserBirthdayBox.Text.Trim();
        _cfg.ScreenIndex = ScreenCombo.SelectedIndex >= 0 ? ScreenCombo.SelectedIndex : 0;
        _cfg.DataPath = DataPathBox.Text.Trim();

        // 开机自启：真源是注册表，保存时同步写入/删除
        var wantAutoStart = AutoStartCheck.IsChecked == true;
        AutoStartService.SetEnabled(wantAutoStart);
        _cfg.AutoStart = wantAutoStart;

        if (NightCheck.IsChecked != true) { _cfg.NightStartHour = 24; _cfg.NightEndHour = 24; }
        else { _cfg.NightStartHour = 18; _cfg.NightEndHour = 6; }

        _cfg.AiEndpoint = AiEndpointBox.Text.Trim();
        _cfg.AiApiKey = AiKeyBox.Text.Trim();
        _cfg.AiModel = AiModelBox.Text.Trim();

        _cfg.VoiceModelEnabled = VoiceModelCheck.IsChecked == true;
        _cfg.VoiceModelEndpoint = VoiceModelEndpointBox.Text.Trim();
        _cfg.VoiceModelRefAudio = VoiceModelRefBox.Text.Trim();
        _cfg.VoiceModelPromptText = VoiceModelPromptBox.Text.Trim();
        _cfg.VoiceServiceBat = VoiceServiceBatBox.Text.Trim();
        _cfg.CurrentSkin = SkinCombo.SelectedIndex >= 0 && SkinCombo.SelectedIndex < _skinInfos.Count
            ? _skinInfos[SkinCombo.SelectedIndex].Id : "default";

        ConfigService.Save(_cfg);
        _saved = true;
        _onSaved(_cfg);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnBrowseDataPath(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择记忆存储目录",
            ShowNewFolderButton = true,
        };
        if (!string.IsNullOrWhiteSpace(DataPathBox.Text))
            dlg.SelectedPath = DataPathBox.Text;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DataPathBox.Text = dlg.SelectedPath;
    }

    private async Task CheckOllamaWithRetryAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            var ok = await ChatService.IsOllamaAvailableAsync();
            if (ok)
            {
                Dispatcher.Invoke(() =>
                {
                    OllamaStatus.Text = "✅ 已连接 — 闲聊将使用本地模型";
                    OllamaHelpBtn.Visibility = Visibility.Collapsed;
                });
                return;
            }
            await Task.Delay(3000);
        }
        Dispatcher.Invoke(() =>
        {
            OllamaStatus.Text = "⚠️ 未检测到本地模型";
            OllamaHelpBtn.Visibility = Visibility.Visible;
        });
    }

    private async Task CheckOllamaAsync()
    {
        var ok = await ChatService.IsOllamaAvailableAsync();
        Dispatcher.Invoke(() =>
        {
            if (ok)
            {
                OllamaStatus.Text = "✅ 已连接 — 闲聊将使用本地模型";
                OllamaHelpBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                OllamaStatus.Text = "⚠️ 未检测到本地模型";
                OllamaHelpBtn.Visibility = Visibility.Visible;
            }
        });
    }

    private void OnOllamaHelp(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://ollama.com/download/windows",
            UseShellExecute = true,
        });
    }

    private async void OnVoiceModelTest(object sender, RoutedEventArgs e)
    {
        VoiceTestBtn.IsEnabled = false;
        VoiceTestStatus.Text = "⏳ 正在合成…（CPU 首句约 3~8 秒）";
        try
        {
        // 用界面当前值直接试听（不依赖保存）；留空走交接文档默认值
        VoiceModelService.Enabled = true;
        VoiceModelService.Endpoint = VoiceModelEndpointBox.Text.Trim();
        VoiceModelService.RefAudioPath = string.IsNullOrWhiteSpace(VoiceModelRefBox.Text)
            ? VoiceModelService.DefaultRefAudio : VoiceModelRefBox.Text.Trim();
        VoiceModelService.PromptText = string.IsNullOrWhiteSpace(VoiceModelPromptBox.Text)
            ? VoiceModelService.DefaultPromptText : VoiceModelPromptBox.Text.Trim();

            var wav = await VoiceModelService.SynthesizeAsync("世界很温柔。哒。");
            if (wav == null)
            {
                VoiceTestStatus.Text = "⚠️ 合成失败：请确认已运行「启动本地语音服务.bat」、服务地址与参考音频路径正确";
                return;
            }
            VoiceTestStatus.Text = "✅ 合成成功，播放中…";
            await VoiceModelService.PlayAsync(wav);
            VoiceTestStatus.Text = "✅ 试听完成 — 记得点「💾 保存」生效";
        }
        finally
        {
            VoiceTestBtn.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════════════════
    //  数据备份 / 恢复（好感度、记忆、日记）
    // ═══════════════════════════════════════════════

    private static readonly string[] BackupExtensions = [".json", ".dat"];

    private void OnBackupData(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ZIP 备份 (*.zip)|*.zip",
            FileName = $"erii_backup_{DateTime.Now:yyyyMMdd_HHmm}.zip",
            Title = "备份绘梨衣的数据",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var dataDir = DataPaths.BaseDir;
            var files = Directory.EnumerateFiles(dataDir)
                .Where(f => BackupExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (files.Count == 0)
            {
                System.Windows.MessageBox.Show("还没有可备份的数据。", "提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            using var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var f in files)
            {
                zip.CreateEntryFromFile(f, Path.GetFileName(f),
                    CompressionLevel.Optimal);
            }
            System.Windows.MessageBox.Show($"已备份 {files.Count} 个文件。\n{dlg.FileName}", "备份完成",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            LogService.Info("Backup", $"备份完成: {dlg.FileName} ({files.Count} 个文件)");
        }
        catch (Exception ex)
        {
            LogService.Error("Backup", "备份失败", ex);
            System.Windows.MessageBox.Show($"备份失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnRestoreData(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ZIP 备份 (*.zip)|*.zip",
            Title = "选择绘梨衣的备份文件",
        };
        if (dlg.ShowDialog() != true) return;

        var result = System.Windows.MessageBox.Show(
            "恢复会覆盖当前的好感度、记忆与日记。\n建议先点「📦 备份数据…」保存现状。\n\n确定继续吗？",
            "确认恢复", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var dataDir = DataPaths.BaseDir;
            Directory.CreateDirectory(dataDir);
            int count = 0;
            using (var fs = new FileStream(dlg.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // 跳过目录条目
                    var target = Path.Combine(dataDir, Path.GetFileName(entry.Name));
                    using var src = entry.Open();
                    using var dst = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                    src.CopyTo(dst);
                    count++;
                }
            }
            System.Windows.MessageBox.Show($"已恢复 {count} 个文件。\n重启桌宠后生效。", "恢复完成",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            LogService.Info("Backup", $"恢复完成: {dlg.FileName} ({count} 个文件)");
        }
        catch (Exception ex)
        {
            LogService.Error("Backup", "恢复失败", ex);
            System.Windows.MessageBox.Show($"恢复失败：{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshSkinList()
    {
        _skinInfos = SkinService.ScanSkins(_assetsDir);
        var names = _skinInfos.Select(s => $"{s.Name} ({s.Count}/10)").ToList();
        SkinCombo.ItemsSource = names;
        var idx = _skinInfos.FindIndex(s => s.Id == _cfg.CurrentSkin);
        SkinCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void OnImportSkin(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ZIP 皮肤包 (*.zip)|*.zip",
            Title = "选择绘梨衣皮肤 ZIP 包",
        };
        if (dlg.ShowDialog() != true) return;

        var err = SkinService.ImportFromZip(dlg.FileName, _assetsDir);
        if (err != null)
        {
            System.Windows.MessageBox.Show(err, "导入失败", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        else
        {
            RefreshSkinList();
            System.Windows.MessageBox.Show("皮肤导入成功！已添加到列表。", "导入成功",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    private void OnDeleteSkin(object sender, RoutedEventArgs e)
    {
        if (SkinCombo.SelectedIndex < 0) return;
        var skinId = _skinInfos[SkinCombo.SelectedIndex].Id;
        if (skinId == "default")
        {
            System.Windows.MessageBox.Show("原版皮肤不能删除。", "提示",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }
        var result = System.Windows.MessageBox.Show($"确定删除皮肤「{_skinInfos[SkinCombo.SelectedIndex].Name}」？",
            "确认删除", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        SkinService.DeleteSkin(skinId, _assetsDir);
        RefreshSkinList();
    }


}