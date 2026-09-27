using System.Security.Cryptography;

namespace EriiPet.Core.Models;

/// <summary>DPAPI 本地加密（绑定当前 Windows 用户 + 设备）</summary>
public static class DataProtection
{
    public static byte[] Protect(byte[] data)
        => ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data)
    {
        try { return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser); }
        catch { throw; }
    }

    public static bool TryProtectFile(string jsonPath, string datPath)
    {
        try
        {
            if (!File.Exists(jsonPath)) return false;
            var plain = File.ReadAllBytes(jsonPath);
            var encrypted = Protect(plain);
            File.WriteAllBytes(datPath, encrypted);
            File.Delete(jsonPath);
            return true;
        }
        catch { return false; }
    }

    public static string? TryReadFile(string datPath)
    {
        try
        {
            if (File.Exists(datPath))
            {
                var encrypted = File.ReadAllBytes(datPath);
                var plain = Unprotect(encrypted);
                return System.Text.Encoding.UTF8.GetString(plain);
            }
        }
        catch { }
        return null;
    }

    public static void WriteFile(string datPath, string content)
    {
        var plain = System.Text.Encoding.UTF8.GetBytes(content);
        var encrypted = Protect(plain);
        var dir = Path.GetDirectoryName(datPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(datPath, encrypted);
    }
}
