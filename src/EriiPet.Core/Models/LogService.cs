using System.Text;

namespace EriiPet.Core.Models;

/// <summary>
/// 滚动文件日志：%LocalAppData%\EriiPet\logs\erii_YYYYMMDD.log
/// 单文件超过 2MB 轮转为 .old（只保留一份），自身异常静默（绝不影响主流程）。
/// </summary>
public static class LogService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EriiPet", "logs");
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object _lock = new();

    public static void Info(string tag, string message) => Write("INFO", tag, message, null);
    public static void Warn(string tag, string message) => Write("WARN", tag, message, null);
    public static void Error(string tag, string message, Exception? ex = null) => Write("ERROR", tag, message, ex);

    private static void Write(string level, string tag, string message, Exception? ex)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Dir);
                var file = Path.Combine(Dir, $"erii_{DateTime.Now:yyyyMMdd}.log");
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                {
                    var old = Path.Combine(Dir, "erii_old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(file, old);
                }

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ')
                  .Append(level).Append(" [").Append(tag).Append("] ").AppendLine(message);
                if (ex != null)
                    sb.AppendLine(ex.ToString());
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 日志本身绝不抛异常
        }
    }
}
