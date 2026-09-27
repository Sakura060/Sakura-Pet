using EriiPet.Core.Models;
using SkiaSharp;

namespace EriiPet.Animation;

/// <summary>
/// 小黄鸭层：脚边常驻小鸭 + 闲置彩蛋鸭（点击猜拳）。
/// 原版彩蛋逻辑原样迁移。
/// </summary>
public sealed class DuckLayer
{
    // ─── 彩蛋鸭 ──
    private float _eggTimer = -1;   // -1=未激活
    private float _eggX, _eggY;

    // ─── 脚边小鸭 ──
    private float _bobTimer;

    private readonly SKTypeface _emojiFont = SKTypeface.FromFamilyName("Segoe UI Emoji");

    public bool EggActive => _eggTimer >= 0;

    /// <summary>召唤彩蛋鸭（2.5s 弹跳动画）</summary>
    public void SpawnEgg()
    {
        if (_eggTimer >= 0) return;
        _eggTimer = 0f;
    }

    public bool HitTest(float x, float y)
    {
        if (!EggActive) return false;
        var dx = x - _eggX; var dy = y - _eggY;
        return MathF.Sqrt(dx * dx + dy * dy) < 36;
    }

    public string PlayRPS()
    {
        _eggTimer = -1;
        var choices = new[] { ("石头", 0), ("剪刀", 1), ("布", 2) };
        var pet = choices[Random.Shared.Next(3)];
        var player = choices[Random.Shared.Next(3)];
        int diff = (player.Item2 - pet.Item2 + 3) % 3;
        return $"鸭鸭出{pet.Item1}…你出{player.Item1}…" +
            (diff == 0 ? "平局呢。" : diff == 1 ? "你赢了…Sakura。" : "嘿嘿，我赢了！");
    }

    /// <summary>蹲在绘梨衣脚边的小鸭</summary>
    public void DrawCompanion(SKCanvas c, float w, float h, float deltaMs, PetWorld? world)
    {
        _bobTimer += deltaMs;
        float petX = world?.PetX ?? w * 0.55f;
        float petY = world?.PetY ?? h * 0.75f;
        float duckX = petX + 50;
        float duckY = petY + 60 + MathF.Sin(_bobTimer * 0.003f) * 3f;

        using var paint = new SKPaint
        {
            IsAntialias = true, TextSize = 20, TextAlign = SKTextAlign.Center, Typeface = _emojiFont,
        };
        c.DrawText("🐤", duckX, duckY, paint);
    }

    public void DrawEgg(SKCanvas canvas, float w, float h, float deltaMs)
    {
        if (_eggTimer < 0) return;

        _eggTimer += deltaMs;
        var total = 2.5f;
        var t = _eggTimer / 1000f;
        if (t > total) { _eggTimer = -1; return; }

        var bounceY = -15f * MathF.Sin(t / total * MathF.PI * 2);
        var scale = 0.5f;
        if (t < 0.2f) scale = t / 0.2f;
        else if (t < 0.4f) scale = 1f + 0.2f * (1f - (t - 0.2f) / 0.2f);
        else if (t < 0.6f) scale = 1f;
        else if (t < 0.8f) scale = 1f + 0.1f * (1f - (t - 0.6f) / 0.2f);
        else scale = 0.8f + 0.2f * (1f - (t - 0.8f) / (total - 0.8f));

        var alpha = (byte)(t < 0.2f ? t / 0.2f * 255 : t > total - 0.3f ? (total - t) / 0.3f * 255 : 255);

        var dx = w / 2f + 95;
        var dy = h * 0.12f + bounceY;
        _eggX = dx; _eggY = dy;

        canvas.Save();
        canvas.Translate(dx, dy);
        canvas.Scale(scale * 1.5f);

        using var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        p.Color = new SKColor(255, 220, 80, alpha);
        canvas.DrawOval(-10, -8, 22, 16, p);

        p.Color = new SKColor(255, 180, 60, alpha);
        canvas.DrawOval(8, -6, 10, 8, p);

        p.Color = new SKColor(255, 240, 120, alpha);
        canvas.DrawCircle(14, -10, 7, p);

        p.Color = new SKColor(20, 20, 20, alpha);
        canvas.DrawCircle(16, -12, 1.5f, p);

        p.Color = new SKColor(255, 140, 30, alpha);
        using var beak = new SKPath();
        beak.MoveTo(20, -10);
        beak.LineTo(25, -8);
        beak.LineTo(20, -6);
        beak.Close();
        canvas.DrawPath(beak, p);

        canvas.DrawLine(-4, 8, -6, 14, p);
        canvas.DrawLine(0, 8, 0, 14, p);

        canvas.Restore();
    }
}
