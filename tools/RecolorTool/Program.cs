using SkiaSharp;

// 主题换色工具 — generate_skins.py 的 C# 移植（无需 PIL）
// 用法: dotnet run -- <themeId> <hueShift> <satScale> <valScale> <tintR> <tintG> <tintB> <tintAlpha>
//   例:  dotnet run -- cassell 0.62 1.05 0.82 40 60 120 0.18
// 读取 assets\sprites\*.png（顶层 10 表情 + 眨眼帧），写入 assets\sprites\<themeId>\
// 肤色像素保留不处理（与 Python 版 is_skin_tone 一致）。

var root = FindSpritesRoot();
if (args.Length < 8)
{
    Console.WriteLine("usage: recolor <themeId> <hueShift> <satScale> <valScale> <tintR> <tintG> <tintB> <tintAlpha>");
    return 1;
}

var themeId = args[0];
double hueShift = double.Parse(args[1]);
double satScale = double.Parse(args[2]);
double valScale = double.Parse(args[3]);
var tint = (byte.Parse(args[4]), byte.Parse(args[5]), byte.Parse(args[6]));
double tintAlpha = double.Parse(args[7]);

var outDir = Path.Combine(root, themeId);
Directory.CreateDirectory(outDir);

var files = Directory.GetFiles(root, "*.png", SearchOption.TopDirectoryOnly);
Console.WriteLine($"🎨 主题 {themeId}：{files.Length} 张精灵 → {outDir}");

foreach (var file in files)
{
    using var src = SKBitmap.Decode(file);
    if (src == null) { Console.WriteLine($"  skip {Path.GetFileName(file)}"); continue; }
    var themed = ApplyTheme(src, hueShift, satScale, valScale, tint, tintAlpha);
    using var img = SKImage.FromBitmap(themed);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    using var fs = File.OpenWrite(Path.Combine(outDir, Path.GetFileName(file)));
    data.SaveTo(fs);
    Console.WriteLine($"  ok {Path.GetFileName(file)}");
}

// 生成/更新 skins.json（追加或替换该主题项）
var skinsPath = Path.Combine(root, "skins.json");
var entry = $"{{\"id\":\"{themeId}\",\"name\":\"🐉 卡塞尔\"}}";
var lines = File.Exists(skinsPath)
    ? File.ReadAllText(skinsPath).Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList()
    : new List<string> { "[" };
lines.RemoveAll(l => l.Contains($"\"id\":\"{themeId}\""));
if (lines.Count > 0 && lines[^1].TrimEnd().EndsWith("]"))
    lines[lines.Count - 1] = lines[^1].TrimEnd().TrimEnd(']').TrimEnd(',') + ",";
lines.Insert(lines.Count - 1, "  " + entry);
File.WriteAllText(skinsPath, string.Join("\n", lines) + "\n]");
Console.WriteLine($"✅ skins.json 已加入 {themeId}");
return 0;

static string FindSpritesRoot()
{
    var dir = AppDomain.CurrentDomain.BaseDirectory;
    for (int i = 0; i < 8; i++)
    {
        var test = Path.Combine(dir, "assets", "sprites");
        if (Directory.Exists(test) && File.Exists(Path.Combine(test, "erii_idle.png")))
            return test;
        var parent = Path.GetDirectoryName(dir);
        if (parent == null || parent == dir) break;
        dir = parent;
    }
    throw new InvalidOperationException("找不到 assets\\sprites");
}

static bool IsSkinTone(byte r, byte g, byte b)
{
    RgbToHsv(r, g, b, out var h, out var s, out var v);
    if (h is > 0.01 and < 0.12 && s is > 0.10 and < 0.70 && v > 0.50) return true; // 暖肤色含腮红
    if (s < 0.15 && v > 0.75) return true;                                          // 高亮浅肤
    return false;
}

static SKBitmap ApplyTheme(SKBitmap src, double hueShift, double satScale, double valScale,
    (byte R, byte G, byte B) tint, double tintAlpha)
{
    var dst = new SKBitmap(src.Width, src.Height);
    for (int y = 0; y < src.Height; y++)
        for (int x = 0; x < src.Width; x++)
        {
            var p = src.GetPixel(x, y);
            if (p.Alpha == 0) { dst.SetPixel(x, y, p); continue; }
            if (IsSkinTone(p.Red, p.Green, p.Blue)) { dst.SetPixel(x, y, p); continue; }

            RgbToHsv(p.Red, p.Green, p.Blue, out var h, out var s, out var v);
            h = (h + hueShift) % 1.0;
            if (h < 0) h += 1.0;
            s = Math.Clamp(s * satScale, 0, 1);
            v = Math.Clamp(v * valScale, 0, 1);
            HsvToRgb(h, s, v, out var nr, out var ng, out var nb);

            if (tintAlpha > 0)
            {
                nr = (byte)(nr * (1 - tintAlpha) + tint.R * tintAlpha);
                ng = (byte)(ng * (1 - tintAlpha) + tint.G * tintAlpha);
                nb = (byte)(nb * (1 - tintAlpha) + tint.B * tintAlpha);
            }
            dst.SetPixel(x, y, new SKColor(nr, ng, nb, p.Alpha));
        }
    return dst;
}

static void RgbToHsv(byte r8, byte g8, byte b8, out double h, out double s, out double v)
{
    double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
    var max = Math.Max(r, Math.Max(g, b));
    var min = Math.Min(r, Math.Min(g, b));
    var d = max - min;
    v = max;
    s = max <= 0 ? 0 : d / max;
    if (d <= 0) { h = 0; return; }
    if (max == r) h = ((g - b) / d) % 6;
    else if (max == g) h = (b - r) / d + 2;
    else h = (r - g) / d + 4;
    h /= 6.0;
    if (h < 0) h += 1.0;
}

static void HsvToRgb(double h, double s, double v, out byte r8, out byte g8, out byte b8)
{
    double r, g, b;
    var c = v * s;
    var x = c * (1 - Math.Abs(h * 6 % 2 - 1));
    var m = v - c;
    var sextant = (int)(h * 6) % 6;
    (r, g, b) = sextant switch
    {
        0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
        3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
    };
    r8 = (byte)Math.Clamp((r + m) * 255, 0, 255);
    g8 = (byte)Math.Clamp((g + m) * 255, 0, 255);
    b8 = (byte)Math.Clamp((b + m) * 255, 0, 255);
}
