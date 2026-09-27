using System.Text.Json;

namespace EriiPet.Core.Models;

public static class ConfigService
{
    // 优先读加密文件 .dat，不存在则回退明文 .json
    private static readonly string DatPath = DataPaths.Get("config.json");
    private static readonly string DatSecure = DatPath.Replace(".json", ".dat");

    public static PetConfig Load()
    {
        PetConfig? cfg = null;

        // 1. 读加密 .dat
        var json = DataProtection.TryReadFile(DatSecure);
        if (json != null)
        {
            try { cfg = JsonSerializer.Deserialize<PetConfig>(json); }
            catch { }
        }

        // 2. 兜底读明文 .json（并迁移为加密）
        if (cfg == null && File.Exists(DatPath))
        {
            try
            {
                json = File.ReadAllText(DatPath);
                cfg = JsonSerializer.Deserialize<PetConfig>(json);
                // 迁移：删明文 .json，存加密 .dat
                DataProtection.WriteFile(DatSecure, json);
                File.Delete(DatPath);
            }
            catch { }
        }

        cfg ??= PetConfig.Default;
        if (!string.IsNullOrWhiteSpace(cfg.DataPath))
            DataPaths.SetBaseDir(cfg.DataPath);
        return cfg;
    }

    public static void Save(PetConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        DataProtection.WriteFile(DatSecure, json);
        // 删掉可能残留的明文
        if (File.Exists(DatPath)) File.Delete(DatPath);
    }
}
