using System.Diagnostics;
using System.Text;
using NAudio.Wave;

namespace EriiPet.Core.Models;

/// <summary>Edge-TTS 语音合成 + NAudio 播放</summary>
public static class TtsService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "EriiPet", "tts");
    private static bool _muted;
    private static WaveOutEvent? _current; // 当前播放实例（供打断用）

    public static bool Muted { get => _muted; set => _muted = value; }
    public static float Volume { get; set; } = 0.9f;

    /// <summary>打断当前正在播的 TTS（原声配音抢播时用）</summary>
    public static void StopPlayback()
    {
        try { _current?.Stop(); } catch { }
        _current = null;
    }

    public static async Task SpeakAsync(string text, string voice = "zh-CN-XiaoxiaoNeural")
    {
        if (_muted || string.IsNullOrWhiteSpace(text)) return;

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(text + voice)))[..16];
        var cachePath = Path.Combine(CacheDir, $"{hash}.mp3");

        try
        {
            if (!File.Exists(cachePath))
            {
                Directory.CreateDirectory(CacheDir);
                var audio = await SynthesizeAsync(text, voice);
                if (audio == null) return;
                await File.WriteAllBytesAsync(cachePath, audio);
            }

            // Play MP3 using NAudio
            await Task.Run(() =>
            {
                using var reader = new Mp3FileReader(cachePath);
                using var waveOut = new WaveOutEvent();
                waveOut.Volume = Math.Clamp(Volume, 0f, 1f);
                _current = waveOut;
                try
                {
                    waveOut.Init(reader);
                    waveOut.Play();
                    while (waveOut.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(50);
                }
                finally { _current = null; }
            });
        }
        catch (Exception ex)
        {
            LogService.Warn("TTS", $"播放失败: {ex.Message}");
        }
    }

    private static async Task<byte[]?> SynthesizeAsync(string text, string voice)
    {
        try
        {
            var ssml = $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' " +
                       $"xmlns:mstts='http://www.w3.org/2001/mstts' xml:lang='zh-CN'>" +
                       $"<voice name='{voice}'><prosody rate='0.9' pitch='+15%'>" +
                       $"{System.Net.WebUtility.HtmlEncode(text)}</prosody></voice></speak>";

            using var req = new HttpRequestMessage(HttpMethod.Post,
                "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1")
            {
                Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml"),
            };
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            req.Headers.TryAddWithoutValidation("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
            req.Headers.TryAddWithoutValidation("Origin", "https://www.bing.com");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.bing.com/");

            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TTS] Synthesis: {ex.Message}");
            return null;
        }
    }
}
