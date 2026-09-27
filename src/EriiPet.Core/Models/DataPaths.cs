namespace EriiPet.Core.Models;

/// <summary>统一数据路径管理</summary>
public static class DataPaths
{
    private static string _baseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EriiPet");

    /// <summary>设置自定义路径（空=用默认）</summary>
    public static void SetBaseDir(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            _baseDir = path;
        else
            _baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EriiPet");
    }

    public static string Get(string filename) => Path.Combine(_baseDir, filename);
    public static string BaseDir => _baseDir;

    /// <summary>旧版数据目录（v3.7 及之前各服务硬编码的位置）</summary>
    public static string LegacyDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EriiPet");

    /// <summary>
    /// 把旧目录的指定文件迁移到当前 BaseDir（复制式、不覆盖已有目标，无损）。
    /// 用于修复历史 bug：设置里改了数据目录，但好感/记忆/日记等服务从未跟随。
    /// </summary>
    public static void MigrateFromLegacy(params string[] filenames)
    {
        try
        {
            if (string.Equals(
                    Path.GetFullPath(_baseDir).TrimEnd('\\'),
                    Path.GetFullPath(LegacyDir).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase))
                return; // 自定义目录就是默认目录，无需迁移

            Directory.CreateDirectory(_baseDir);
            var moved = 0;
            foreach (var name in filenames)
            {
                var target = Path.Combine(_baseDir, name);
                if (File.Exists(target)) continue;             // 目标已有 → 不覆盖
                var src = Path.Combine(LegacyDir, name);
                if (File.Exists(src))
                {
                    File.Copy(src, target);                    // 复制，原位置保留作安全网
                    moved++;
                }
            }
            if (moved > 0)
                LogService.Info("Migrate", $"已从旧目录迁移 {moved} 个数据文件到 {Path.GetFullPath(_baseDir)}");
        }
        catch (Exception ex)
        {
            LogService.Warn("Migrate", $"数据迁移失败: {ex.Message}");
        }
    }
}
