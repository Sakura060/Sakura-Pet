using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>AI 聊天服务（OpenAI 兼容 API）</summary>
public static class ChatService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>绘梨衣的角色设定 Prompt</summary>
    public const string SystemPrompt = """
        你是上杉绘梨衣，来自江南小说《龙族》。
        你是蛇岐八家上杉家主，言灵「审判」持有者。
        你平时用便签本写字交流，不说话。你的回复很短——一句话，不超过20个字。
        你纯真、温柔、害羞，有点天然呆。你喜欢的人是 Sakura（路明非）。
        你叫他"Sakura"。你偶尔说"世界很温柔"。
        你觉得樱花很漂亮，喜欢小黄鸭。你讨厌别人欺负Sakura。
        用中文回复。语气像便签上写的短句。
        偶尔用"……"表示犹豫。不要用表情符号。
        不要说"作为绘梨衣"、"作为AI"之类的话。你就是绘梨衣本人。
        如果用户提到了过去的对话记忆，请自然地接上——你记得那些事。
        """;

    private static bool? _ollamaHasModel;
    private static DateTime _ollamaLastCheck;
    private static readonly TimeSpan OllamaCacheDuration = TimeSpan.FromSeconds(10);

    /// <summary>系统提示词 + 此刻状态注入（让她知道"现在几点、你们多熟"）</summary>
    public static string ComposeSystemPrompt(string? stateContext)
    {
        if (string.IsNullOrWhiteSpace(stateContext)) return SystemPrompt;
        return SystemPrompt + "\n\n【此刻的真实情况】" + stateContext
            + "\n（这些信息请自然地融入回复，不要罗列数字，不要主动汇报）";
    }

    /// <summary>清除本地模型缓存，强制下次重新检测</summary>
    public static void ResetOllamaCache()
    {
        _ollamaHasModel = null;
    }

    /// <summary>检查本地 Ollama 是否可用且模型已下载</summary>
    public static async Task<bool> IsOllamaAvailableAsync()
    {
        // 短时间内的 false 结果可缓存，但不要永久缓存
        if (_ollamaHasModel == false && (DateTime.Now - _ollamaLastCheck) < OllamaCacheDuration)
            return false;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "http://localhost:11434/api/tags");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var resp = await _http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) { _ollamaHasModel = false; _ollamaLastCheck = DateTime.Now; return false; }

            var json = await resp.Content.ReadAsStringAsync();
            _ollamaHasModel = json.Contains("qwen") || json.Contains("Qwen");
            _ollamaLastCheck = DateTime.Now;
            return _ollamaHasModel.Value;
        }
        catch { _ollamaHasModel = false; _ollamaLastCheck = DateTime.Now; return false; }
    }

    /// <summary>通过本地 Ollama 发送消息</summary>
    public static async Task<string?> SendLocalAsync(string userMessage, List<(string role, string content)>? history = null, string? stateContext = null)
    {
        var messages = new List<object> { new { role = "system", content = ComposeSystemPrompt(stateContext) } };
        if (history != null)
            foreach (var (role, content) in history.TakeLast(10))
                messages.Add(new { role, content });
        messages.Add(new { role = "user", content = userMessage });

        var body = new { model = "qwen2.5:1.5b", messages, stream = false,
            options = new { temperature = 0.8, num_predict = 120 } };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/chat")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await _http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim();
        }
        catch { return null; }
    }

    /// <summary>智能路由：简单闲聊走本地，复杂问题走云端</summary>
    public static bool ShouldUseLocal(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        // 短消息 + 无问号 → 本地
        if (message.Length <= 20 && !message.Contains('?') && !message.Contains('？')
            && !message.Contains("为什么") && !message.Contains("怎么") && !message.Contains("解释"))
            return true;
        return false;
    }

    /// <summary>发送聊天消息（支持本地+云端混合）</summary>
    /// <returns>AI 回复，失败返回 null</returns>
    public static async Task<string?> SendAsync(PetConfig cfg, string userMessage, List<(string role, string content)>? history = null, string? stateContext = null)
    {
        if (string.IsNullOrWhiteSpace(cfg.AiEndpoint) || string.IsNullOrWhiteSpace(cfg.AiApiKey))
            return null;

        // ─── 混合路由：仅在用户手动启用本地模型时尝试 ──
        if (cfg.UseLocalModel)
        {
            if (ShouldUseLocal(userMessage) && await IsOllamaAvailableAsync())
            {
                var local = await SendLocalAsync(userMessage, history, stateContext);
                if (local != null) return local;
            }
        }

        var messages = new List<object>
        {
            new { role = "system", content = ComposeSystemPrompt(stateContext) },
        };

        // 全量历史（上下文够大，不需要截断）
        if (history != null)
        {
            foreach (var (role, content) in history)
                messages.Add(new { role, content });
        }

        messages.Add(new { role = "user", content = userMessage });

        var body = new
        {
            model = cfg.AiModel,
            messages,
            max_tokens = 120,
            temperature = 0.8,
        };

        try
        {
            var ep = cfg.AiEndpoint.TrimEnd('/') + "/v1/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, ep)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Authorization", $"Bearer {cfg.AiApiKey}");

            using var resp = await _http.SendAsync(req);
            
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return "🔑 API Key 无效，请检查设置。";
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return "🔗 API 地址不对，请检查设置。";
            if (resp.StatusCode == System.Net.HttpStatusCode.PaymentRequired)
                return "💰 余额不足，请充值。";
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync();
                string errMsg = "";
                try
                {
                    using var errDoc = JsonDocument.Parse(errBody);
                    errMsg = errDoc.RootElement.TryGetProperty("error", out var e)
                        && e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                }
                catch { }
                var detail = !string.IsNullOrEmpty(errMsg) ? errMsg : errBody[..Math.Min(errBody.Length, 80)];
                return $"⚠️ API 错误 ({(int)resp.StatusCode}): {detail}";
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return content?.Trim() ?? null;
        }
        catch (HttpRequestException)
        {
            return "🌐 无法连接到服务器，请检查 API 地址。";
        }
        catch (TaskCanceledException)
        {
            return "⏱️ 请求超时，请检查网络。";
        }
        catch
        {
            return null;
        }
    }
}