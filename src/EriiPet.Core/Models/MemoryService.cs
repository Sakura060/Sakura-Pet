using System.Text;
using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>长期记忆服务 — 100K 上限 + 分层检索 + 本地 ONNX</summary>
public class MemoryService : IDisposable
{
    private const int MaxMemories = 100_000;
    private const int PriorityScan = 5000;  // 最近 5000 条线性扫
    private const int BucketSize = 1000;    // 旧记忆分桶抽样

    private static string JsonPath => DataPaths.Get("memories.json");

    private static string VecPath => DataPaths.Get("memories.vec");

    private readonly List<MemoryEntry> _entries;
    private readonly List<float[]> _vectors = new(); // 与 _entries 一一对应
    private readonly LocalEmbeddingService? _localEmb;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly PetConfig _cfg;

    public MemoryService(PetConfig cfg)
    {
        _cfg = cfg;
        (_entries, _vectors) = Load();

        _localEmb = new LocalEmbeddingService();
        if (!_localEmb.Ready) { _localEmb.Dispose(); _localEmb = null; }
    }

    public static Task<bool> EnsureModelAsync() => LocalEmbeddingService.DownloadModelAsync();
    public bool UseLocal => _localEmb?.Ready == true;

    // ══════════════════════════════════════
    //  存储
    // ══════════════════════════════════════

    public async Task RememberAsync(string userMessage, string aiReply)
    {
        var text = $"用户: {userMessage.Trim()}\n绘梨衣: {aiReply.Trim()}";
        var vec = await EmbedAsync(text);
        if (vec == null) return;

        _entries.Add(new MemoryEntry { Time = DateTime.Now, Text = text });
        _vectors.Add(vec);

        if (_entries.Count > MaxMemories)
        {
            var remove = _entries.Count - MaxMemories;
            _entries.RemoveRange(0, remove);
            _vectors.RemoveRange(0, remove);
            _memoryFullNotified = true;
        }

        Save();
    }

    /// <summary>检查是否需要提醒记忆已满（外部调用，如 OnPaint 或定时器）</summary>
    public bool ShouldNotifyFull()
    {
        if (_memoryFullNotified)
        {
            _memoryFullNotified = false;
            return true;
        }
        return false;
    }

    private bool _memoryFullNotified;

    // ══════════════════════════════════════
    //  检索 — 分层索引
    // ══════════════════════════════════════

    public async Task<List<string>> RetrieveAsync(string query, int topK = 5)
    {
        if (_entries.Count == 0) return new();
        var queryVec = await EmbedAsync(query);
        if (queryVec == null) return new();

        var results = new List<(float score, string text)>();
        int n = _entries.Count;

        // ── 第 1 层：最近 PriorityScan 条线性扫描 ──
        int start1 = Math.Max(0, n - PriorityScan);
        for (int i = n - 1; i >= start1; i--)
        {
            var s = CosSim(queryVec, _vectors[i]);
            if (s > 0.4f) results.Add((s, _entries[i].Text));
        }

        // ── 第 2 层：更早的记忆分桶抽样 ──
        if (start1 > 0)
        {
            int buckets = (start1 + BucketSize - 1) / BucketSize;
            for (int b = 0; b < buckets; b++)
            {
                int bucketStart = b * BucketSize;
                int bucketEnd = Math.Min(bucketStart + BucketSize, start1);
                int sampleCount = Math.Min(50, bucketEnd - bucketStart);

                for (int s = 0; s < sampleCount; s++)
                {
                    int idx = bucketStart + Random.Shared.Next(bucketEnd - bucketStart);
                    var score = CosSim(queryVec, _vectors[idx]);
                    if (score > 0.4f) results.Add((score, _entries[idx].Text));
                }
            }
        }

        return results.OrderByDescending(x => x.score).Take(topK).Select(x => x.text).ToList();
    }

    /// <summary>按时间范围检索记忆（用于时间关联回响）</summary>
    public Task<List<MemoryEntry>> RetrieveByTimeRangeAsync(DateTime from, DateTime to, int limit = 10)
    {
        var result = _entries
            .Where(e => e.Time >= from && e.Time <= to)
            .OrderByDescending(e => e.Time)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    // ══════════════════════════════════════
    //  记忆管理（v3.7 — 用户有权查看和删除她的记忆）
    // ══════════════════════════════════════

    public int Count => _entries.Count;

    /// <summary>最近 N 条记忆（新的在前）</summary>
    public List<MemoryEntry> Recent(int n = 20)
        => _entries.OrderByDescending(e => e.Time).Take(n).ToList();

    /// <summary>按 Id 删除一条记忆（正文与向量同步移除）</summary>
    public bool DeleteById(string id)
    {
        var idx = _entries.FindIndex(e => e.Id == id);
        if (idx < 0) return false;
        _entries.RemoveAt(idx);
        if (idx < _vectors.Count) _vectors.RemoveAt(idx);
        Save();
        return true;
    }

    // ══════════════════════════════════════
    //  Embedding
    // ══════════════════════════════════════

    private async Task<float[]?> EmbedAsync(string text)
    {
        var local = _localEmb?.Embed(text);
        if (local != null) return local;
        return await EmbedViaApi(text);
    }

    private async Task<float[]?> EmbedViaApi(string text)
    {
        if (string.IsNullOrWhiteSpace(_cfg.AiEndpoint) || string.IsNullOrWhiteSpace(_cfg.AiApiKey)) return null;
        try
        {
            var ep = _cfg.AiEndpoint.TrimEnd('/') + "/v1/embeddings";
            var body = new { model = _cfg.AiModel, input = text };
            using var req = new HttpRequestMessage(HttpMethod.Post, ep)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Authorization", $"Bearer {_cfg.AiApiKey}");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray();
            var vec = new float[arr.Count()];
            int i = 0;
            foreach (var v in arr) vec[i++] = v.GetSingle();
            return vec;
        }
        catch { return null; }
    }

    // ══════════════════════════════════════
    //  I/O — JSON正文 + 二进制向量
    // ══════════════════════════════════════

    private static float CosSim(float[] a, float[] b)
    {
        float d = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { d += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        var den = MathF.Sqrt(na) * MathF.Sqrt(nb);
        return den > 0 ? d / den : 0;
    }

    // DPAPI encrypt memories
    private static string JsonSecure => JsonPath.Replace(".json", ".dat");

    // ═══════...

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JsonPath)!);

        // Encrypt memory text
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = false });
        DataProtection.WriteFile(JsonSecure, json);
        if (File.Exists(JsonPath)) File.Delete(JsonPath);

        // vectors stay as binary .vec
        using var fs = new FileStream(VecPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);
        bw.Write(_vectors.Count);
        int dim = _vectors.Count > 0 ? _vectors[0].Length : 0;
        bw.Write(dim);
        foreach (var v in _vectors)
        {
            var bytes = new byte[v.Length * 4];
            Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
            bw.Write(bytes);
        }
    }

    private static (List<MemoryEntry> entries, List<float[]> vectors) Load()
    {
        var entries = new List<MemoryEntry>();
        var vectors = new List<float[]>();

        try
        {
            // Try encrypted .dat first
            var json = DataProtection.TryReadFile(JsonSecure);
            if (json != null)
            {
                entries = JsonSerializer.Deserialize<List<MemoryEntry>>(json) ?? new();
            }
            else if (File.Exists(JsonPath))
            {
                // Fallback: plain .json → migrate to encrypted
                json = File.ReadAllText(JsonPath);
                entries = JsonSerializer.Deserialize<List<MemoryEntry>>(json) ?? new();
                DataProtection.WriteFile(JsonSecure, json);
                File.Delete(JsonPath);
            }

            if (File.Exists(VecPath))
            {
                using var fs = new FileStream(VecPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                int count = br.ReadInt32();
                int dim = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    var bytes = br.ReadBytes(dim * 4);
                    if (bytes.Length < dim * 4) break;
                    var vec = new float[dim];
                    Buffer.BlockCopy(bytes, 0, vec, 0, bytes.Length);
                    vectors.Add(vec);
                }
            }
        }
        catch { }

        return (entries, vectors);
    }

    public void Dispose() => _localEmb?.Dispose();
}

public class MemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString()[..12];
    public DateTime Time { get; set; }
    public string Text { get; set; } = "";
}
