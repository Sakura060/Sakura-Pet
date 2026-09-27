using EriiPet.Core.Models;
using Microsoft.Win32;

namespace EriiPet.App;

/// <summary>开机自启（HKCU Run 键，无需管理员权限）。真源是注册表，取消勾选即删除。</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EriiPet";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v
                && v.Contains("Sakura-v3", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("AutoStart", $"设置自启失败: {ex.Message}");
        }
    }

    /// <summary>路径自愈：exe 被移动/改名后，启动时自动修正注册表指向</summary>
    public static void RepairIfStale()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string v && !v.Contains(exe, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, $"\"{exe}\"");
                LogService.Info("AutoStart", $"自启路径已修正: {v} → {exe}");
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("AutoStart", $"自启路径修正失败: {ex.Message}");
        }
    }
}
