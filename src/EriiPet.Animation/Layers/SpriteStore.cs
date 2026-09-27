using System.Diagnostics;
using EriiPet.Core.Models;
using SkiaSharp;

namespace EriiPet.Animation;

/// <summary>精灵仓库：加载、状态→文件名映射、眨眼调度（idle/wandering 微动画）</summary>
public sealed class SpriteStore
{
    private readonly Dictionary<string, SKBitmap> _sprites = new();

    // ─── 眨眼调度 ──
    private float _clockMs;
    private float _nextBlinkAt = 3000f;
    private float _blinkUntil = -1f;
    private const float BlinkDurationMs = 140f;

    public void Load(string spriteDir)
    {
        if (!Directory.Exists(spriteDir)) return;
        foreach (var file in Directory.GetFiles(spriteDir, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(file).Replace("erii_", "");
            try { var b = SKBitmap.Decode(file); if (b != null) _sprites[key] = b; }
            catch (Exception ex) { Debug.WriteLine($"[EriiPet] load fail: {file}: {ex.Message}"); }
        }
    }

    public bool TryGet(string key, out SKBitmap bmp) => _sprites.TryGetValue(key, out bmp!);

    public static string StateToKey(PetState s) => s switch
    {
        PetState.Idle => "idle", PetState.Smirk => "smirk", PetState.Surprised => "surprised",
        PetState.Happy => "happy", PetState.Blush => "blush", PetState.Sleepy => "sleepy",
        PetState.Reject => "reject", PetState.Annoyed => "annoyed", PetState.Acknowledge => "acknowledge",
        PetState.Cuddle => "cuddle", PetState.Wandering => "idle", // 闲逛用 idle 精灵 + 额外变换
        _ => "idle",
    };

    public void AdvanceClock(float deltaMs) => _clockMs += deltaMs;

    /// <summary>
    /// 解析当前应绘制的精灵键：idle/wandering 时叠加随机眨眼（每 2.6-6 秒一次）。
    /// 皮肤目录缺少 erii_idle_blink.png 时自动退化为不眨眼。
    /// </summary>
    public string ResolveKey(string baseKey, PetState state)
    {
        if (state is PetState.Idle or PetState.Wandering && _sprites.ContainsKey("idle_blink"))
        {
            if (_blinkUntil >= 0)
            {
                if (_clockMs >= _blinkUntil) _blinkUntil = -1f;
                else return "idle_blink";
            }
            if (_clockMs >= _nextBlinkAt)
            {
                _blinkUntil = _clockMs + BlinkDurationMs;
                _nextBlinkAt = _clockMs + 2600f + Random.Shared.NextSingle() * 3400f;
            }
        }
        return baseKey;
    }

    public void Dispose()
    {
        foreach (var s in _sprites.Values) s.Dispose();
        _sprites.Clear();
    }
}
