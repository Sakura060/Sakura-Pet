using System.IO.Compression;

namespace EriiPet.Core.Models;

/// <summary>
/// 每周自动备份 — 用户忘记点备份时的最后防线。
/// 启动时检测：距上次自动备份超过 7 天 → 打包数据目录顶层 .json/.dat
/// 到 {数据目录}\backups\auto_YYYYMMDD_HHmm.zip，保留最近 4 份。
/// </summary>
public static class AutoBackupService
{
    private const int IntervalDays = 7;
    private const int KeepCount = 4;

    public static void RunIfDue()
    {
        try
        {
            var dataDir = DataPaths.BaseDir;
            var backupDir = Path.Combine(dataDir, "backups");
            Directory.CreateDirectory(backupDir);

            // 到期检查：无备份或最近一次超过 7 天
            var latest = Directory.Exists(backupDir)
                ? Directory.GetFiles(backupDir, "auto_*.zip")
                    .Select(File.GetLastWriteTime)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max()
                : DateTime.MinValue;
            if ((DateTime.Now - latest).TotalDays < IntervalDays) return;

            var files = Directory.EnumerateFiles(dataDir)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".json" or ".dat";
                })
                .ToList();
            if (files.Count == 0)
            {
                LogService.Warn("Backup", $"数据目录没有可备份的文件: {dataDir}");
                return;
            }

            var name = $"auto_{DateTime.Now:yyyyMMdd_HHmm}.zip";
            var target = Path.Combine(backupDir, name);
            using (var fs = new FileStream(target, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var f in files)
                    zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
            }

            // 只保留最近 KeepCount 份
            var stale = Directory.GetFiles(backupDir, "auto_*.zip")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .Skip(KeepCount);
            foreach (var f in stale) File.Delete(f);

            LogService.Info("Backup", $"每周自动备份完成: {name} ({files.Count} 个文件)");
        }
        catch (IOException ex)
        {
            LogService.Warn("Backup", $"自动备份跳过（IO）: {ex.Message}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Backup", $"自动备份失败: {ex.Message}");
        }
    }
}
