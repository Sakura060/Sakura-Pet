using SkiaSharp;

namespace EriiPet.Animation;

/// <summary>交互粒子：开心/贴贴等状态触发的 emoji 粒子</summary>
public sealed class ParticleLayer : IDisposable
{
    private sealed class Particle
    {
        public string Emoji = "";
        public float X, Y, VelocityX, VelocityY, Life, MaxLife, FontSize, DriftX;
    }

    private readonly List<Particle> _particles = new();
    private static readonly string[] Emojis = ["💕", "💖", "💗", "✨", "🌸"];
    private readonly SKPaint _paint = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
    private readonly Func<int> _maxParticles;

    public ParticleLayer(Func<int> maxParticles)
    {
        _maxParticles = maxParticles;
    }

    /// <summary>发射 count 个粒子，持续 durationMs</summary>
    public void Spawn(int count, float durationMs)
    {
        for (int i = 0; i < count; i++)
        {
            if (_particles.Count >= _maxParticles()) break;
            var emoji = Emojis[Random.Shared.Next(Emojis.Length)];
            var angle = (float)(MathF.PI / 4 + Random.Shared.NextDouble() * MathF.PI / 2);
            var dist = 40 + Random.Shared.Next(60);
            _particles.Add(new Particle
            {
                Emoji = emoji,
                X = 0, Y = 0,
                VelocityX = MathF.Cos(angle) * dist / durationMs * 1000,
                VelocityY = -(MathF.Sin(angle) * dist + 20) / durationMs * 1000,
                Life = durationMs,
                MaxLife = durationMs,
                FontSize = 14 + Random.Shared.Next(10),
                DriftX = (float)((Random.Shared.NextDouble() - 0.5) * 40),
            });
        }
    }

    public void Draw(SKCanvas canvas, float w, float h, float deltaMs)
    {
        var cx = w / 2f;
        var cy = h * 0.55f;

        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Life -= deltaMs;
            if (p.Life <= 0) { _particles.RemoveAt(i); continue; }

            p.X += p.VelocityX * deltaMs / 1000f;
            p.Y += p.VelocityY * deltaMs / 1000f;
            var drift = p.DriftX * MathF.Sin(p.Life / p.MaxLife * MathF.PI);
            var alpha = (byte)(p.Life / p.MaxLife * 255);
            var scale = 0.6f + 0.4f * (p.Life / p.MaxLife);

            canvas.Save();
            canvas.Translate(cx + p.X + drift, cy + p.Y);
            canvas.Scale(scale);
            _paint.Color = new SKColor(255, 255, 255, alpha);
            _paint.TextSize = p.FontSize;
            canvas.DrawText(p.Emoji, 0, 0, _paint);
            canvas.Restore();
        }
    }

    public void Dispose() => _paint.Dispose();
}

/// <summary>氛围粒子主题：日常花瓣 / 雨天雨滴 / 雪天雪花</summary>
public enum AmbientTheme { Petals, Rain, Snow }

/// <summary>氛围粒子：常驻飘落（随天气切换主题）+ 生日/节日花雨增强</summary>
public sealed class PetalLayer
{
    private sealed class FloatingPetal
    {
        public string Char = ""; public float X, Y, SpeedX, SpeedY, Size, Opacity, Sway;
    }

    private static readonly string[] PetalChars = ["🌸", "🍃", "✨", "💮"];
    private static readonly string[] RainChars = ["💧", "🌧", "💧"];
    private static readonly string[] SnowChars = ["❄", "❄️", "✻"];

    private readonly List<FloatingPetal> _petals = new();
    private AmbientTheme _theme = AmbientTheme.Petals;
    private float _spawnTimer;
    private float _boost; // >0 时花瓣密集生成（樱花雨）
    private readonly Func<bool> _enabled;
    private readonly SKTypeface _emojiFont = SKTypeface.FromFamilyName("Segoe UI Emoji");

    public PetalLayer(Func<bool> enabled)
    {
        _enabled = enabled;
    }

    /// <summary>随天气切换氛围（仅在主题变化时重置粒子）</summary>
    public void SetTheme(AmbientTheme theme)
    {
        if (_theme == theme) return;
        _theme = theme;
        _petals.Clear();
    }

    private string[] CharsFor(AmbientTheme t) => t switch
    {
        AmbientTheme.Rain => RainChars,
        AmbientTheme.Snow => SnowChars,
        _ => PetalChars,
    };

    private (float MinY, float MaxY, float DriftScale) SpeedFor(AmbientTheme t) => t switch
    {
        AmbientTheme.Rain => (110f, 170f, 0.2f),   // 雨滴快落、几乎不飘
        AmbientTheme.Snow => (8f, 18f, 1.6f),      // 雪花慢落、大幅飘
        _ => (12f, 32f, 1f),
    };

    /// <summary>生日/节日樱花雨：立刻生成一批花瓣并让生成加速一段时间</summary>
    public void SpawnRain(int petals = 24)
    {
        if (!_enabled()) return;
        var saved = _theme;
        _theme = AmbientTheme.Petals; // 花雨永远是花
        _boost = 15000f; // 15 秒内花瓣密集生成
        for (int i = 0; i < petals; i++)
        {
            _petals.Add(new FloatingPetal
            {
                Char = PetalChars[Random.Shared.Next(PetalChars.Length)],
                X = Random.Shared.NextSingle() * 300f,
                Y = -Random.Shared.NextSingle() * 200f,
                SpeedY = 20 + Random.Shared.NextSingle() * 30,
                SpeedX = (Random.Shared.NextSingle() - 0.5f) * 15,
                Size = 16 + Random.Shared.NextSingle() * 10,
                Opacity = 0.8f,
                Sway = Random.Shared.NextSingle() * 2 * MathF.PI,
            });
        }
        _theme = saved;
    }

    public void Draw(SKCanvas c, float w, float h, float deltaMs)
    {
        if (!_enabled()) return;
        if (_boost > 0) _boost -= deltaMs;
        var boosted = _boost > 0;
        var spawnInterval = boosted ? 300 : _theme == AmbientTheme.Rain ? 500 : _theme == AmbientTheme.Snow ? 1400 : 2000;
        var maxCount = boosted ? 60 : _theme == AmbientTheme.Rain ? 40 : 12;
        _spawnTimer += deltaMs;
        if (_spawnTimer > spawnInterval && _petals.Count < maxCount)
        {
            _spawnTimer = 0;
            var (minY, maxY, _) = SpeedFor(_theme);
            _petals.Add(new FloatingPetal
            {
                Char = CharsFor(_theme)[Random.Shared.Next(CharsFor(_theme).Length)],
                X = Random.Shared.NextSingle() * (w - 40) + 20,
                Y = -10,
                SpeedY = minY + Random.Shared.NextSingle() * (maxY - minY),
                SpeedX = (Random.Shared.NextSingle() - 0.5f) * 15,
                Size = _theme == AmbientTheme.Rain ? 11 + Random.Shared.NextSingle() * 5
                     : 14 + Random.Shared.NextSingle() * 8,
                Opacity = boosted ? 0.8f : 0.4f + Random.Shared.NextSingle() * 0.6f,
                Sway = Random.Shared.NextSingle() * 2 * MathF.PI,
            });
        }

        var (_, _, driftScale) = SpeedFor(_theme);
        using var paint = new SKPaint
        {
            IsAntialias = true, TextAlign = SKTextAlign.Center, Typeface = _emojiFont,
        };
        for (int i = _petals.Count - 1; i >= 0; i--)
        {
            var p = _petals[i];
            p.Y += p.SpeedY * deltaMs / 1000f;
            p.X += MathF.Sin(p.Sway + p.Y * 0.02f) * p.SpeedX * driftScale * deltaMs / 1000f;
            p.Sway += deltaMs * 0.001f;
            if (p.Y > h + 30) { _petals.RemoveAt(i); continue; }
            byte a = (byte)(p.Opacity * 255);
            paint.TextSize = p.Size;
            paint.Color = new SKColor(255, 255, 255, a);
            c.DrawText(p.Char, p.X, p.Y, paint);
        }
    }
}
