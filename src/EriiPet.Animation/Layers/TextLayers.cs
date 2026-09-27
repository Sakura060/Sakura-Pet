using EriiPet.Core.Models;
using SkiaSharp;

namespace EriiPet.Animation;

/// <summary>台词气泡：圆角气泡 + 自动换行 + 淡入淡出</summary>
public sealed class BubbleLayer
{
    private string _text = "";
    private float _timer = -1;
    private float _extraDuration;
    private const float Duration = 3000f;
    private readonly SKTypeface _font = SKTypeface.FromFamilyName("Microsoft YaHei");

    /// <summary>从当前状态的台词池随机选一句展示</summary>
    public void ShowForState(string stateKey)
    {
        var quotes = QuoteLibrary.Get(stateKey);
        if (quotes.Length > 0)
        {
            _text = quotes[Random.Shared.Next(quotes.Length)];
            _timer = 0f;
            _extraDuration = 0f;
        }
    }

    /// <summary>展示指定文字（AI 回复/事件文本等，停留更久）</summary>
    public void ShowRaw(string text)
    {
        _text = text;
        _timer = 0f;
        _extraDuration = 2000f;
    }

    public void Draw(SKCanvas canvas, float w, float h, float deltaMs, PetWorld? world)
    {
        if (_timer < 0 || string.IsNullOrEmpty(_text)) return;

        _timer += deltaMs;
        var t = _timer;
        var maxT = Duration + _extraDuration;
        if (t > maxT) { _timer = -1; _text = ""; _extraDuration = 0f; return; }

        var alpha = 1f;
        if (t < 300) alpha = t / 300f;
        else if (t > maxT - 300) alpha = (maxT - t) / 300f;
        var slideY = t < 200 ? (1f - t / 200f) * 4f : 0f;

        float cx = world?.PetX ?? w / 2f;
        float cy = (world != null ? Math.Max(60, world.PetY - 180) : h * 0.32f) + slideY;

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            TextSize = 24,
            TextAlign = SKTextAlign.Center,
            Typeface = _font,
            Color = new SKColor(51, 51, 51, (byte)(alpha * 255)),
        };

        var maxLineWidth = w * 0.85f;
        var lines = WrapText(_text, textPaint, maxLineWidth);
        var lineHeight = textPaint.FontSpacing;

        var textMaxW = 0f;
        foreach (var l in lines) { var lw = textPaint.MeasureText(l); if (lw > textMaxW) textMaxW = lw; }
        var padX = 22f; var padY = 14f;
        var textBlockH = lineHeight * lines.Count;
        var bw = Math.Min(textMaxW + padX * 2, w - 10);
        var bh = textBlockH + padY * 2;
        var bx = cx - bw / 2f;
        var by = cy - bh;

        var bgAlpha = (byte)(alpha * 230);
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(245, 245, 245, bgAlpha),
        };
        canvas.DrawRoundRect(bx, by, bw, bh, 14, 14, bgPaint);

        var triSize = 6f;
        using var path = new SKPath();
        path.MoveTo(cx - triSize, cy - triSize);
        path.LineTo(cx + triSize, cy - triSize);
        path.LineTo(cx, cy);
        path.Close();
        canvas.DrawPath(path, bgPaint);

        for (int i = 0; i < lines.Count; i++)
        {
            var y = by + padY + (i + 0.7f) * lineHeight;
            canvas.DrawText(lines[i], cx, y, textPaint);
        }
    }

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        var remaining = text;
        while (remaining.Length > 0)
        {
            var fit = (int)paint.BreakText(remaining, maxWidth, out _);
            if (fit <= 0) { lines.Add("…"); break; }
            var breakAt = (int)fit;
            if (breakAt < remaining.Length)
            {
                for (int j = fit - 1; j > fit / 2; j--)
                {
                    if (remaining[j] == '，' || remaining[j] == '。' || remaining[j] == '、' ||
                        remaining[j] == ' ' || remaining[j] == '　')
                    {
                        breakAt = j + 1;
                        break;
                    }
                }
            }
            lines.Add(remaining[..breakAt].Trim());
            remaining = remaining[breakAt..].TrimStart();
            if (lines.Count > 8) { lines[^1] += "…"; break; }
        }
        return lines;
    }
}

/// <summary>便签本：绘梨衣身边的小本子，保留最近 3 条手写字</summary>
public sealed class NotepadLayer
{
    private readonly List<string> _entries = new(3);
    private float _showTimer;
    private const float FadeTime = 8000f;
    private readonly SKTypeface _font = SKTypeface.FromFamilyName("Microsoft YaHei");
    private readonly SKTypeface _emojiFont = SKTypeface.FromFamilyName("Segoe UI Emoji");

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _entries.Add(text.Trim());
        while (_entries.Count > 3) _entries.RemoveAt(0);
        _showTimer = 0f;
    }

    public void Draw(SKCanvas c, float w, float h, float deltaMs, PetWorld? world)
    {
        if (_entries.Count == 0) return;

        _showTimer += deltaMs;
        float alpha = _showTimer < 300 ? _showTimer / 300f
            : _showTimer > FadeTime - 500 ? Math.Max(0, (FadeTime - _showTimer) / 500f)
            : 1f;

        if (alpha <= 0) { _entries.Clear(); return; }

        float petX = world?.PetX ?? w * 0.5f;
        float petY = world?.PetY ?? h * 0.75f;
        float padW = 160, padH = 110;
        float padX = petX + 120;
        float padY = petY - padH / 2 - 20;

        using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, (byte)(alpha * 30)) };
        c.DrawRoundRect(padX + 3, padY + 3, padW, padH, 8, 8, shadow);

        using var paper = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Fill,
            Color = new SKColor(255, 250, 240, (byte)(alpha * 235)),
        };
        c.DrawRoundRect(padX, padY, padW, padH, 8, 8, paper);

        using var border = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            Color = new SKColor(210, 190, 170, (byte)(alpha * 180)), StrokeWidth = 1f,
        };
        c.DrawRoundRect(padX, padY, padW, padH, 8, 8, border);

        using var line = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            Color = new SKColor(200, 190, 180, (byte)(alpha * 100)), StrokeWidth = 0.5f,
        };
        for (int i = 1; i <= 3; i++)
            c.DrawLine(padX + 12, padY + i * 28, padX + padW - 12, padY + i * 28, line);

        using var textPaint = new SKPaint
        {
            IsAntialias = true, TextSize = 12, Color = new SKColor(60, 50, 45, (byte)(alpha * 220)),
            Typeface = _font,
        };
        int entryIndex = 0;
        float maxW = padW - 28;
        foreach (var entry in _entries)
        {
            var lines = WrapToLines(entry, textPaint, maxW);
            for (int li = 0; li < Math.Min(lines.Count, 3) && entryIndex < 3; li++, entryIndex++)
            {
                c.DrawText(lines[li], padX + 14, padY + 16 + entryIndex * 28, textPaint);
            }
            if (entryIndex >= 3) break;
        }

        using var penPaint = new SKPaint { IsAntialias = true, TextSize = 18, TextAlign = SKTextAlign.Center,
            Typeface = _emojiFont };
        c.DrawText("🖊️", padX + padW - 10, padY - 8, penPaint);
    }

    private static List<string> WrapToLines(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        int start = 0;
        while (start < text.Length && lines.Count < 3)
        {
            int end = start;
            float w = 0;
            while (end < text.Length)
            {
                float cw = paint.MeasureText(text.Substring(end, 1));
                if (w + cw > maxWidth && end > start) break;
                w += cw;
                end++;
            }
            if (end == start) end = start + 1;
            lines.Add(text[start..end]);
            start = end;
        }
        if (lines.Count == 3 && start < text.Length)
            lines[2] = lines[2][..^1] + "…";
        return lines;
    }
}
