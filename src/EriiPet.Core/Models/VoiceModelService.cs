using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace EriiPet.Core.Models;

/// <summary>
/// 本地语音模型 (GPT-SoVITS api_v2) — 绘梨衣音色合成。
/// 请求只传 7 个基础字段（采样参数由服务端修复后的默认值决定，勿传）；
/// 文本按哈希缓存 wav；连续失败进入 5 分钟健康降级，由上层回退 Edge TTS。
/// </summary>
public static class VoiceModelService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EriiPet", "voice_model");
    private static readonly long CacheCapBytes = 128L * 1024 * 1024;

    /// <summary>参考音频（服务端可见路径，来自《模型交接说明.md》）</summary>
    public const string DefaultRefAudio = @"D:\GPT-SoVITS\ref_erii.wav";
    /// <summary>参考音频原文（必须与音频逐字一致，否则 400）</summary>
    public const string DefaultPromptText = "能和你一起出门冒险,是昨天就开始期待的事情.";

    public static bool Enabled { get; set; }
    public static float Volume { get; set; } = 0.9f;
    public static string Endpoint { get; set; } = "http://127.0.0.1:9880";
    public static string RefAudioPath { get; set; } = DefaultRefAudio;
    public static string PromptText { get; set; } = DefaultPromptText;

    private static int _failStreak;
    private static DateTime _downUntil = DateTime.MinValue;

    public static bool IsHealthy => DateTime.Now >= _downUntil;

    /// <summary>快速探测服务是否在监听（不发起推理）</summary>
    public static async Task<bool> PingAsync()
    {
        try
        {
            var uri = new Uri(Endpoint);
            using var tcp = new System.Net.Sockets.TcpClient();
            var connect = tcp.ConnectAsync(uri.Host, uri.Port);
            var done = await Task.WhenAny(connect, Task.Delay(800));
            return done == connect && tcp.Connected;
        }
        catch { return false; }
    }

    /// <summary>合成并返回 wav 字节；不可用时返回 null（上层回退 TTS）</summary>
    public static async Task<byte[]?> SynthesizeAsync(string text)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text) || !IsHealthy) return null;
        if (string.IsNullOrWhiteSpace(RefAudioPath)) return null;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
        var cachePath = Path.Combine(CacheDir, $"{hash}.wav");
        try
        {
            if (File.Exists(cachePath))
            {
                MarkUp();
                return await File.ReadAllBytesAsync(cachePath);
            }

            var payload = JsonSerializer.Serialize(new
            {
                text,
                text_lang = "zh",
                ref_audio_path = RefAudioPath,
                prompt_text = PromptText,
                prompt_lang = "zh",
                media_type = "wav",
                streaming_mode = false,
            });
            using var resp = await _http.PostAsync($"{Endpoint.TrimEnd('/')}/tts",
                new StringContent(payload, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode) { MarkDown(); return null; }

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 1000) { MarkDown(); return null; }

            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(cachePath, bytes);
            EvictCache();
            MarkUp();
            return bytes;
        }
        catch (Exception ex)
        {
            LogService.Warn("VoiceModel", $"合成失败: {ex.Message}");
            MarkDown();
            return null;
        }
    }

    /// <summary>播放合成 wav（先打断 Edge TTS，避免叠音）</summary>
    public static async Task PlayAsync(byte[] wav)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "EriiPet", "vm_play.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
        await File.WriteAllBytesAsync(tmp, wav);
        await Task.Run(() =>
        {
            try
            {
                using var reader = new WaveFileReader(tmp);
                using var waveOut = new WaveOutEvent();
                waveOut.Volume = Math.Clamp(Volume, 0f, 1f);
                TtsService.StopPlayback();
                waveOut.Init(reader);
                waveOut.Play();
                while (waveOut.PlaybackState == PlaybackState.Playing)
                    Thread.Sleep(50);
            }
            catch
            {
                // 播放设备被占用等场景静默放弃，字幕已先行展示
            }
        });
    }

    private static void MarkUp() => _failStreak = 0;

    private static void MarkDown()
    {
        if (++_failStreak >= 2)
        {
            _downUntil = DateTime.Now.AddMinutes(5);
            _failStreak = 0;
        }
    }

    /// <summary>缓存超限后按最旧优先淘汰</summary>
    private static void EvictCache()
    {
        try
        {
            var dir = new DirectoryInfo(CacheDir);
            if (!dir.Exists) return;
            var files = dir.GetFiles("*.wav").OrderBy(f => f.LastWriteTime).ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= CacheCapBytes) break;
                total -= f.Length;
                f.Delete();
            }
        }
        catch
        {
            // 缓存清理失败不影响主流程
        }
    }
}
