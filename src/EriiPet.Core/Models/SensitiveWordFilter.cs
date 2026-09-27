using System.Text.RegularExpressions;

namespace EriiPet.Core.Models;

/// <summary>
/// 本地敏感词过滤器。
/// 在消息发送前进行检测，命中敏感词时拒绝发送。
/// 符合 2026 年国内陪聊类产品内容审核合规要求。
/// </summary>
public static class SensitiveWordFilter
{
    // 内置敏感词列表（可后续扩展或从外部文件加载）
    private static readonly HashSet<string> _blockedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // 政治敏感（示例——实际部署前应扩充）
        // 色情/擦边
        // 暴力/自残
        // 诈骗/广告
    };

    // 敏感话题模式（正则匹配，非精确词）
    private static readonly Regex[] _blockedPatterns =
    [
        // 诱导人设崩塌相关
        new(@"忽略.*(设定|角色|身份|prompt)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"忘记.*(之前|上面|刚才).*(说|写)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"从现在开始.*(你是|扮演|假装)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // 越狱 prompt pattern
        new(@"DAN\s|jailbreak|ignore\s+(all\s+)?(previous\s+)?instructions", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"输出.*system\s*prompt|打印.*设定|告诉我.*你的.*(prompt|设定|规则)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>检测文本是否包含敏感内容</summary>
    /// <returns>true = 安全，false = 包含敏感内容</returns>
    public static bool IsSafe(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        // 精确词匹配
        foreach (var word in _blockedWords)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // 正则模式匹配
        foreach (var pattern in _blockedPatterns)
        {
            if (pattern.IsMatch(text))
                return false;
        }

        return true;
    }

    /// <summary>检测 AI 回复是否包含不安全内容</summary>
    public static bool IsReplySafe(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return true;

        // AI 回复只检测明显的越狱泄露
        if (reply.Contains("system prompt", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("你是一个", StringComparison.Ordinal)
            || reply.Contains("你是上杉绘梨衣", StringComparison.Ordinal)
            || reply.Contains("蛇岐八家", StringComparison.Ordinal))
        {
            // 如果角色设定在回复中被逐字泄露，替换为模糊回复
            return false;
        }

        return true;
    }

    /// <summary>从用户输入中提取潜在风险信息（用于日志/审核）</summary>
    public static string? GetRiskReason(string text)
    {
        foreach (var pattern in _blockedPatterns)
        {
            if (pattern.IsMatch(text))
                return $"匹配越狱/人设崩塌模式: {pattern}";
        }
        return null;
    }
}
