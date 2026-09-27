using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;

namespace EriiPet.Core.Models;

/// <summary>本地 ONNX 嵌入服务 — 零 API 调用，全部本机 CPU 运算</summary>
public class LocalEmbeddingService : IDisposable
{
    private readonly string _modelDir;
    private InferenceSession? _session;
    private readonly Dictionary<string, long> _vocab = new();
    private readonly int _maxLength = 512;
    private const long UnkId = 100;  // [UNK]

    public bool Ready { get; }

    public LocalEmbeddingService()
    {
        _modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EriiPet", "embeddings");

        Directory.CreateDirectory(_modelDir);
        var modelPath = Path.Combine(_modelDir, "model.onnx");

        if (!File.Exists(modelPath))
        {
            Ready = false;
            return;
        }

        try
        {
            _session = new InferenceSession(modelPath);
            LoadVocab(Path.Combine(_modelDir, "vocab.txt"));
            Ready = true;
        }
        catch
        {
            Ready = false;
        }
    }

    /// <summary>下载模型文件（首次运行自动触发）</summary>
    public static async Task<bool> DownloadModelAsync()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EriiPet", "embeddings");
        Directory.CreateDirectory(dir);

        var modelPath = Path.Combine(dir, "model.onnx");
        if (File.Exists(modelPath)) return true;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // bge-small-zh-v1.5 ONNX (from HF mirror)
        var baseUrl = "https://huggingface.co/BAAI/bge-small-zh-v1.5/resolve/main/onnx";
        var files = new[] { "model.onnx", "vocab.txt" };

        foreach (var file in files)
        {
            try
            {
                var url = $"{baseUrl}/{file}?download=true";
                var bytes = await http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(Path.Combine(dir, file), bytes);
            }
            catch { return false; }
        }
        return true;
    }

    /// <summary>文字 → 向量（512 维）</summary>
    public float[]? Embed(string text)
    {
        if (!Ready) return null;

        try
        {
            var tokens = Tokenize(text);
            var attention = Enumerable.Repeat(1L, _maxLength).ToArray();
            var tokenTypes = new long[_maxLength];

            var inputIds = new DenseTensor<long>(tokens, [1, _maxLength]);
            var attMask = new DenseTensor<long>(attention, [1, _maxLength]);
            var typeIds = new DenseTensor<long>(tokenTypes, [1, _maxLength]);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attMask),
                NamedOnnxValue.CreateFromTensor("token_type_ids", typeIds),
            };

            using var results = _session!.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Mean pooling over token dimension (skip [CLS] and [SEP])
            var dim = output.Dimensions[2]; // 512
            var vec = new float[dim];
            int seqLen = Math.Min(tokens.Count(t => t != 0), _maxLength);
            if (seqLen <= 2) return vec; // only CLS+SEP

            for (int d = 0; d < dim; d++)
            {
                float sum = 0; int count = 0;
                for (int t = 1; t < seqLen - 1; t++) // skip CLS and SEP
                {
                    sum += output[0, t, d];
                    count++;
                }
                vec[d] = sum / count;
            }

            // L2 normalize
            var norm = MathF.Sqrt(vec.Sum(v => v * v));
            if (norm > 0)
                for (int i = 0; i < dim; i++)
                    vec[i] /= norm;

            return vec;
        }
        catch
        {
            return null;
        }
    }

    // ══════════════════════════════════
    //  Tokenizer
    // ══════════════════════════════════

    private void LoadVocab(string path)
    {
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            var token = lines[i].Trim();
            if (!_vocab.ContainsKey(token))
                _vocab[token] = i;
        }
    }

    private long[] Tokenize(string text)
    {
        var ids = new long[_maxLength];
        ids[0] = _vocab.GetValueOrDefault("[CLS]", 101);
        int pos = 1;

        int i = 0;
        while (i < text.Length && pos < _maxLength - 1)
        {
            // Chinese character tokenization with greedy longest match
            bool found = false;
            int maxTry = Math.Min(6, text.Length - i);
            for (int len = maxTry; len >= 1; len--)
            {
                var sub = text.Substring(i, len);
                if (_vocab.TryGetValue(sub, out var id))
                {
                    ids[pos++] = id;
                    i += len;
                    found = true;
                    break;
                }
            }
            if (!found) { ids[pos++] = UnkId; i++; }
        }

        ids[pos] = _vocab.GetValueOrDefault("[SEP]", 102);
        return ids;
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
