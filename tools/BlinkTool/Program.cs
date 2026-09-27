using SkiaSharp;

// 眨眼帧生成器：从 erii_idle.png 加工出闭眼帧 erii_idle_blink.png
// 用法:
//   dotnet run -- inspect                → 输出默认精灵脸部放大图到 %TEMP%\erii_face_crop.png
//   dotnet run -- make x1 y1 x2 y2       → 按眼睛包围盒为全部 7 套皮肤生成眨眼帧（256×256 坐标系）

var spritesRoot = @"D:\Sakura桌宠\绘梨衣综合\erii-pet-v3.1\assets\sprites";
var mode = args.Length > 0 ? args[0] : "inspect";

if (mode == "inspect")
{
    var cx1 = args.Length >= 5 ? int.Parse(args[1]) : 96;
    var cy1 = args.Length >= 5 ? int.Parse(args[2]) : 100;
    var cx2 = args.Length >= 5 ? int.Parse(args[3]) : 176;
    var cy2 = args.Length >= 5 ? int.Parse(args[4]) : 160;
    using var src = SKBitmap.Decode(Path.Combine(spritesRoot, "erii_idle.png"));
    var crop = new SKRectI(cx1, cy1, cx2, cy2);
    using var face = new SKBitmap(crop.Width, crop.Height);
    using (var c = new SKCanvas(face))
        c.DrawBitmap(src, crop, new SKRect(0, 0, crop.Width, crop.Height));
    using var big = face.Resize(new SKImageInfo(face.Width * 5, face.Height * 5), SKFilterQuality.None);
    using var img = SKImage.FromBitmap(big);
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    var outPath = Path.Combine(Path.GetTempPath(), "erii_face_crop.png");
    using var fs = File.OpenWrite(outPath);
    data.SaveTo(fs);
    Console.WriteLine($"saved {outPath}");
    return 0;
}

if (mode == "make" && args.Length >= 9)
{
    var rects = new List<SKRectI>();
    for (int i = 1; i + 3 < args.Length; i += 4)
        rects.Add(new SKRectI(int.Parse(args[i]), int.Parse(args[i + 1]),
                              int.Parse(args[i + 2]), int.Parse(args[i + 3])));

    var files = new List<string> { Path.Combine(spritesRoot, "erii_idle.png") };
    foreach (var dir in Directory.GetDirectories(spritesRoot))
        files.Add(Path.Combine(dir, "erii_idle.png"));
    files.RemoveAll(f => !File.Exists(f));

    foreach (var file in files)
    {
        using var src = SKBitmap.Decode(file);
        if (src == null) { Console.WriteLine($"skip {file}"); continue; }
        var blink = MakeBlink(src, rects);
        var outPath = Path.Combine(Path.GetDirectoryName(file)!, "erii_idle_blink.png");
        using var img = SKImage.FromBitmap(blink);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(outPath);
        data.SaveTo(fs);
        Console.WriteLine($"ok  {outPath}");

        // 调试输出：默认皮肤脸部放大图
        if (file.EndsWith("sprites\\erii_idle.png", StringComparison.OrdinalIgnoreCase))
        {
            var crop = new SKRectI(88, 135, 184, 190);
            using var face = new SKBitmap(crop.Width, crop.Height);
            using (var c = new SKCanvas(face))
                c.DrawBitmap(blink, crop, new SKRect(0, 0, crop.Width, crop.Height));
            using var big = face.Resize(new SKImageInfo(face.Width * 5, face.Height * 5), SKFilterQuality.None);
            using var img2 = SKImage.FromBitmap(big);
            using var d2 = img2.Encode(SKEncodedImageFormat.Png, 100);
            using var fs2 = File.OpenWrite(Path.Combine(Path.GetTempPath(), "erii_blink_crop.png"));
            d2.SaveTo(fs2);
            Console.WriteLine("    preview %TEMP%\\erii_blink_crop.png");
        }
    }
    return 0;
}

if (mode == "map" && args.Length >= 5)
{
    using var src = SKBitmap.Decode(Path.Combine(spritesRoot, "erii_idle.png"));
    int x1 = int.Parse(args[1]), y1 = int.Parse(args[2]), x2 = int.Parse(args[3]), y2 = int.Parse(args[4]);
    for (int y = y1; y < y2; y++)
    {
        var row = new System.Text.StringBuilder();
        for (int x = x1; x < x2; x++)
        {
            var p = src.GetPixel(x, y);
            char c;
            if (p.Alpha < 200) c = ' ';
            else if (p.Red < 110 && p.Green < 110 && p.Blue < 110) c = 'D';          // 近黑
            else if (p.Red > 215 && p.Green > 215 && p.Blue > 215) c = 'W';          // 白高光
            else if (p.Red > 150 && p.Green < 130 && p.Blue < 130) c = 'R';          // 红（发/腮红）
            else if (p.Red > 230 && p.Green > 170) c = 's';                          // 肤
            else c = '?';
            row.Append(c);
        }
        Console.WriteLine($"{y,3} {row}");
    }
    return 0;
}

Console.WriteLine("usage: inspect [x1 y1 x2 y2] | make x1 y1 x2 y2 [x1 y1 x2 y2 ...] | map x1 y1 x2 y2");
return 1;

// ─── 核心算法：眼睛区域内整体替换为肤色，底部画闭眼睫毛线 ───
static SKBitmap MakeBlink(SKBitmap src, List<SKRectI> eyes)
{
    var bmp = src.Copy();

    foreach (var eye in eyes)
    {
        // 1. 采样眼睛周围一圈的"肤色"众数（排除红发与深色）
        var skinSamples = new List<SKColor>();
        for (int x = eye.Left - 8; x < eye.Right + 8; x++)
            for (int y = eye.Top - 8; y < eye.Bottom + 8; y++)
            {
                if (eye.Contains(x, y)) continue;
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                var p = bmp.GetPixel(x, y);
                if (p.Alpha < 200) continue;
                if (p.Red > 200 && p.Green > 150 && p.Blue > 120 && p.Green >= p.Blue)
                    skinSamples.Add(p);
            }
        if (skinSamples.Count == 0)
            skinSamples.Add(new SKColor(253, 231, 209));
        var skin = skinSamples
            .GroupBy(c => (c.Red / 8, c.Green / 8, c.Blue / 8))
            .OrderByDescending(g => g.Count()).First().First();

        // 2. 区域内整体替换（黑眼睑+红虹膜+白高光全部闭合成皮肤）
        for (int x = eye.Left; x < eye.Right; x++)
            for (int y = eye.Top; y < eye.Bottom; y++)
            {
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                var p = bmp.GetPixel(x, y);
                if (p.Alpha == 0) continue;
                bmp.SetPixel(x, y, new SKColor(skin.Red, skin.Green, skin.Blue, p.Alpha));
            }

        // 3. 眼底 2px 睫毛线（两端上收 1px），盖在虹膜底缘位置
        var lash = new SKColor(70, 34, 32);
        int midL = eye.Left + 2, midR = eye.Right - 3;
        int yLine = eye.Bottom - 2;
        for (int x = eye.Left; x < eye.Right; x++)
        {
            int y = yLine;
            if (x < midL || x > midR) y -= 1;
            for (int t = 0; t < 2; t++)
                if (y + t < bmp.Height)
                    bmp.SetPixel(x, y + t, lash);
        }
    }
    return bmp;
}
