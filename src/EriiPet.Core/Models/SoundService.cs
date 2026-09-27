using System.Diagnostics;
using NAudio.Wave;

namespace EriiPet.Core.Models;

/// <summary>
/// 原声语音包 — 播放「绘梨衣配音」原声（assets/sounds/）。
/// 与 TtsService.Muted 联动（禁音总控）；Stop 可打断（生日原声被"倾听"切歌）。
/// </summary>
public static class SoundService
{
    private static IWavePlayer? _player;
    private static AudioFileReader? _reader;
    private static readonly Lock _lock = new();

    public static bool IsPlaying
    {
        get { lock (_lock) return _player?.PlaybackState == PlaybackState.Playing; }
    }

    public static float Volume { get; set; } = 0.9f;

    /// <summary>托盘"倾听绘梨衣"入口：完整配音循环播放；已在播则停止</summary>
    public static void ToggleListen(string assetsDir)
    {
        if (IsPlaying) { Stop(); return; }
        var file = FindVoice(assetsDir, preferFull: true);
        if (file == null) return;
        Play(file, loop: true);
    }

    /// <summary>生日彩蛋：播放前 1 分钟原声（不循环，播完自然结束）</summary>
    public static void PlayBirthdayVoice(string assetsDir)
    {
        if (TtsService.Muted) return;
        var file = FindVoice(assetsDir, preferFull: false);
        if (file == null) return;
        Play(file, loop: false);
    }

    private static string? FindVoice(string assetsDir, bool preferFull)
    {
        try
        {
            var dir = Path.Combine(assetsDir, "sounds");
            if (!Directory.Exists(dir)) return null;
            var files = Directory.GetFiles(dir, "*.mp3")
                .Concat(Directory.GetFiles(dir, "*.wav")).ToList();
            if (files.Count == 0) return null;

            var full = files.FirstOrDefault(f =>
                Path.GetFileName(f).Contains("完整", StringComparison.Ordinal));
            var minute = files.FirstOrDefault(f =>
                Path.GetFileName(f).Contains("1分钟", StringComparison.Ordinal) ||
                Path.GetFileName(f).Contains("一分钟", StringComparison.Ordinal));
            return (preferFull ? (full ?? minute) : (minute ?? full)) ?? files[0];
        }
        catch { return null; }
    }

    private static void Play(string file, bool loop)
    {
        if (TtsService.Muted) return;
        try
        {
            Stop();

            var reader = new AudioFileReader(file);
            var player = new WaveOutEvent();
            player.Volume = Math.Clamp(Volume, 0f, 1f);
            IWaveProvider source = loop
                ? new LoopStream(reader)
                : reader;

            player.Init(source);
            player.PlaybackStopped += (_, _) =>
            {
                try { reader.Dispose(); player.Dispose(); } catch { }
            };

            lock (_lock)
            {
                _reader = reader;
                _player = player;
            }
            player.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Sound] {ex.Message}");
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            try { _player?.Stop(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _player?.Dispose(); } catch { }
            _player = null;
            _reader = null;
        }
    }

    /// <summary>无缝循环的 WaveStream 包装</summary>
    private sealed class LoopStream(AudioFileReader source) : WaveStream
    {
        public override WaveFormat WaveFormat => source.WaveFormat;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => source.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            if (read == 0)
            {
                source.Position = 0;
                read = source.Read(buffer, offset, count);
            }
            return read;
        }
    }
}
