using System.IO.Compression;

namespace EriiPet.Core.Models;

/// <summary>
/// 皮肤管理 — 自动扫描 + ZIP 导入
/// </summary>
public static class SkinService
{
    private static readonly string[] RequiredSprites =
    [
        "idle", "happy", "surprised", "blush", "sleepy",
        "smirk", "reject", "annoyed", "acknowledge", "cuddle",
    ];

    /// <summary>扫描 sprites 目录下所有可用皮肤</summary>
    public static List<SkinInfo> ScanSkins(string assetsDir)
    {
        var spritesDir = Path.Combine(assetsDir, "sprites");
        var skins = new List<SkinInfo>();

        // 原皮永远在第一位
        skins.Add(new SkinInfo { Id = "default", Name = "🎀 原皮", Count = 10 });

        if (Directory.Exists(spritesDir))
        {
            var dirs = Directory.GetDirectories(spritesDir)
                .Select(d => new DirectoryInfo(d))
                .Where(d => d.Name != "default")
                .OrderBy(d => d.Name);

            foreach (var dir in dirs)
            {
                var files = Directory.GetFiles(dir.FullName, "erii_*.png");
                if (files.Length >= 5)
                {
                    skins.Add(new SkinInfo
                    {
                        Id = dir.Name,
                        Name = SkinNameMap.GetValueOrDefault(dir.Name, $"🎨 {dir.Name}"),
                        Count = files.Length,
                    });
                }
            }
        }

        return skins;
    }

    /// <summary>从 ZIP 导入皮肤</summary>
    /// <returns>错误消息，null 表示成功</returns>
    public static string? ImportFromZip(string zipPath, string assetsDir)
    {
        var spritesDir = Path.Combine(assetsDir, "sprites");
        Directory.CreateDirectory(spritesDir);

        // 用 ZIP 文件名（去掉扩展）作为皮肤目录名
        var skinName = Path.GetFileNameWithoutExtension(zipPath);
        // 干掉非法字符
        skinName = string.Join("_", skinName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(skinName)) skinName = $"imported_{DateTime.Now:yyyyMMddHHmmss}";

        var targetDir = Path.Combine(spritesDir, skinName);

        // 检查是否重名
        if (Directory.Exists(targetDir))
            return $"皮肤「{skinName}」已存在，请先删除再导入。";

        using var zip = ZipFile.OpenRead(zipPath);
        var pngFiles = zip.Entries
            .Where(e => e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pngFiles.Count == 0)
            return "ZIP 中没有找到 PNG 图片。";

        // 尝试匹配文件名
        var matched = new Dictionary<string, ZipArchiveEntry>();
        foreach (var sprite in RequiredSprites)
        {
            var entry = pngFiles.FirstOrDefault(e =>
                e.Name.Contains(sprite, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
                matched[$"erii_{sprite}"] = entry;
        }

        if (matched.Count < 5)
            return $"ZIP 中只找到了 {matched.Count} 个匹配表情（需要至少 5 个）。\n需要包含：idle, happy, surprised 等 10 个状态。";

        // 解压
        Directory.CreateDirectory(targetDir);
        foreach (var (targetName, entry) in matched)
        {
            var targetPath = Path.Combine(targetDir, targetName + ".png");
            entry.ExtractToFile(targetPath, true);
        }

        // 把未匹配的 PNG 也复制过去
        foreach (var entry in pngFiles)
        {
            if (!matched.ContainsValue(entry))
            {
                var targetPath = Path.Combine(targetDir, $"erii_{entry.Name}");
                entry.ExtractToFile(targetPath, true);
            }
        }

        return null; // 成功
    }

    /// <summary>删除皮肤</summary>
    public static void DeleteSkin(string skinId, string assetsDir)
    {
        if (skinId == "default") return; // 不删默认
        var dir = Path.Combine(assetsDir, "sprites", skinId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }

    private static readonly Dictionary<string, string> SkinNameMap = new()
    {
        ["default"] = "🎀 原版",
        ["sakura"] = "🌸 樱花粉",
        ["moonlight"] = "🌙 月光蓝",
        ["lavender"] = "💜 薰衣草紫",
        ["noir"] = "🖤 暗夜黑",
        ["wafuu"] = "🍵 和风",
    };
}

public class SkinInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Count { get; set; }
}
