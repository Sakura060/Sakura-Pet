using System.IO;
using EriiPet.Core.Models;
using SkiaSharp;

namespace EriiPet.Animation;

/// <summary>
/// Skia 渲染组合器（v3.2 图层化）：
/// 精灵(SpriteStore) / 气泡+便签(TextLayers) / 粒子+花瓣(ParticleLayers) / 鸭子(DuckLayer)。
/// 呼吸、crossfade、伸懒腰、小天地变换留在这里编排，绘制顺序与 v3.1 完全一致。
/// </summary>
public class SkiaRenderer
{
    private readonly PetConfig _config;
    private readonly SpriteStore _sprites = new();
    private readonly BubbleLayer _bubble;
    private readonly NotepadLayer _notepad = new();
    private readonly ParticleLayer _particles;
    private readonly PetalLayer _petals;
    private readonly DuckLayer _duck = new();

    public PetState CurrentState { get; private set; } = PetState.Idle;

    // ─── v3.0 小天地 ────────────────────────────
    public PetWorld? World { get; set; }

    // ─── 闲逛变换（外部驱动） ─────────────────
    public WanderTransform? ActiveWanderTransform { get; set; }
    public float WanderProgress { get; set; }

    // ─── Crossfade ─────────────────────────────────
    private SKBitmap? _prevSprite;
    private float _crossfadeProgress;
    private const float CrossfadeSpeed = 4f;

    // ─── 呼吸 ─────────────────────────────────────
    private float _breatheTime;
    private float _breatheScaleMin = 0.99f;
    private float _breatheScaleMax = 1.01f;
    private float _breathePeriod = 3000f;
    private float _breatheDrift = 0.02f;

    // ─── 伸懒腰动画（睡前触发） ─────────────────
    private float _stretchTimer = -1;       // -1=未激活
    private Action? _stretchOnComplete;     // 动画完成回调

    public SkiaRenderer(PetConfig config)
    {
        _config = config;
        _bubble = new BubbleLayer();
        _particles = new ParticleLayer(() => config.MaxParticles);
        _petals = new PetalLayer(() => config.PetalEffectEnabled);
    }

    // ═══════════════════════════════════════════════
    //  精灵加载
    // ═══════════════════════════════════════════════

    public void LoadSprites(string assetsDir, string skin = "default")
    {
        var spriteDir = skin == "default"
            ? Path.Combine(assetsDir, "sprites")
            : Path.Combine(assetsDir, "sprites", skin);
        _sprites.Load(spriteDir);
    }

    public static string StateToKey(PetState s) => SpriteStore.StateToKey(s);

    /// <summary>从 AI 回复文字中检测情绪，匹配对应表情</summary>
    public static PetState DetectEmotion(string reply)
    {
        var t = reply.Trim();

        // 害羞：提到 Sakura + 情感词，或含 ///
        if ((t.Contains("Sakura") && (t.Contains("喜欢") || t.Contains("爱") || t.Contains("最")))
            || t.Contains("……///") || t.Contains("害羞") || t.Contains("别说"))
            return PetState.Blush;

        // 开心：明显的积极情绪
        if (t.Contains("嘻嘻") || t.Contains("开心") || t.Contains("嗯！") || t.Contains("太好了")
            || t.StartsWith("嗯") || t.Contains("高兴"))
            return PetState.Happy;

        // 生气
        if (t.Contains("哼") || t.Contains("生气") || t.Contains("不理"))
            return PetState.Annoyed;

        // 困/晚安
        if (t.Contains("晚安") || t.Contains("ZZZ") || t.Contains("困") || t.Contains("睡了"))
            return PetState.Sleepy;

        // 撒娇/贴贴
        if (t.Contains("贴贴") || t.Contains("温暖") || t.Contains("抱"))
            return PetState.Cuddle;

        // 惊讶
        if (t.Contains("！") || t.Contains("啊") || t.Contains("咦"))
            return PetState.Surprised;

        // 偷笑
        if (t.Contains("嘿嘿") || t.Contains("秘密"))
            return PetState.Smirk;

        return PetState.Idle;
    }

    // ═══════════════════════════════════════════════
    //  状态切换
    // ═══════════════════════════════════════════════

    /// <summary>
    /// 切换到目标状态，带 crossfade 过渡。
    /// 如果上一个 crossfade 还在进行中（被中断），则跳过 crossfade 直接快照到新状态，
    /// 避免中途中止时出现视觉跳动。
    /// </summary>
    public void TransitionTo(PetState newState)
    {
        if (newState == CurrentState) return;

        if (_crossfadeProgress >= 1f)
        {
            var ok = SpriteStore.StateToKey(CurrentState);
            if (_sprites.TryGet(ok, out var old)) _prevSprite = old;
        }
        else
        {
            // crossfade 进行中被打断：弃用旧精灵，直接显示新状态避免残影
            _prevSprite = null;
        }

        UpdateBreathingParams(newState);
        CurrentState = newState;
        _crossfadeProgress = 0f;

        // 状态粒子
        switch (newState)
        {
            case PetState.Happy: _particles.Spawn(6, 1400); break;
            case PetState.Cuddle: _particles.Spawn(8, 2800); break;
            case PetState.Surprised: _particles.Spawn(3, 600); break;
        }
    }

    public void SetBreathing(bool slow)
    {
        if (slow)
        {
            _breatheScaleMin = 0.98f; _breatheScaleMax = 1.02f;
            _breathePeriod = _config.BreatheSlowPeriodMs; _breatheDrift = 0.02f;
        }
        else
        {
            _breatheScaleMin = _config.BreatheScaleMin; _breatheScaleMax = _config.BreatheScaleMax;
            _breathePeriod = _config.BreathePeriodMs; _breatheDrift = 0.02f;
        }
    }

    private void UpdateBreathingParams(PetState s)
    {
        if (s == PetState.Sleepy) SetBreathing(true); else SetBreathing(false);
    }

    // ═══════════════════════════════════════════════
    //  图层公开接口（转发）
    // ═══════════════════════════════════════════════

    public void SpawnParticles(int count, float durationMs) => _particles.Spawn(count, durationMs);

    /// <summary>生日/节日樱花雨</summary>
    public void SpawnPetalsRain(int petals = 24) => _petals.SpawnRain(petals);

    /// <summary>随天气切换氛围粒子：雨→雨滴，雪→雪花，其余→花瓣</summary>
    public void SetAmbientWeather(int weatherCode)
    {
        var theme = weatherCode switch
        {
            51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82 => AmbientTheme.Rain,
            71 or 73 or 75 or 77 or 85 or 86 => AmbientTheme.Snow,
            _ => AmbientTheme.Petals,
        };
        _petals.SetTheme(theme);
    }

    /// <summary>召唤小黄鸭（2.5s 弹跳动画）</summary>
    public void SpawnDuck() => _duck.SpawnEgg();

    public bool DuckActive => _duck.EggActive;

    public bool HitTestDuck(float x, float y) => _duck.HitTest(x, y);

    public string PlayRPS() => _duck.PlayRPS();

    /// <summary>播放入睡前的伸懒腰动画（~2s，完成后回调）</summary>
    public void PlayStretch(Action? onComplete = null)
    {
        if (_stretchTimer >= 0) return;
        _stretchTimer = 0f;
        _stretchOnComplete = onComplete;
    }

    /// <summary>显示台词气泡（从当前状态随机选一句）</summary>
    public void ShowBubble() => _bubble.ShowForState(SpriteStore.StateToKey(CurrentState));

    /// <summary>显示自定义文字气泡（AI 回复等）</summary>
    public void ShowBubbleRaw(string text) => _bubble.ShowRaw(text);

    public void AddToNotepad(string text) => _notepad.Add(text);

    // ═══════════════════════════════════════════════
    //  渲染
    // ═══════════════════════════════════════════════

    public void Render(SKCanvas canvas, float width, float height, float deltaMs)
    {
        canvas.Clear(SKColors.Transparent);

        // ─── v3.0 小天地背景层 ──
        _sprites.AdvanceClock(deltaMs);
        _duck.DrawCompanion(canvas, width, height, deltaMs, World);
        _duck.DrawEgg(canvas, width, height, deltaMs);
        _petals.Draw(canvas, width, height, deltaMs);
        _notepad.Draw(canvas, width, height, deltaMs, World);

        // ─── 呼吸 ──
        _crossfadeProgress = Math.Min(1f, _crossfadeProgress + CrossfadeSpeed * deltaMs / 1000f);
        var phaseDrift = 1.0f + _breatheDrift * MathF.Sin(_breatheTime * 0.0004f);
        _breatheTime += deltaMs * phaseDrift;

        var ck = _sprites.ResolveKey(SpriteStore.StateToKey(CurrentState), CurrentState);
        if (_sprites.TryGet(ck, out var cs))
        {
            var p = (_breatheTime % _breathePeriod) / _breathePeriod;
            var bs = _breatheScaleMin + (_breatheScaleMax - _breatheScaleMin) *
                (0.5f + 0.5f * MathF.Sin(p * 2 * MathF.PI - MathF.PI / 2));

            // ─── v3.0 画布内位置 ──
            float petCX = width / 2f, petCY = height * 0.75f;
            float sx = 1f, sy = 1f, rot = 0f, offY = 0f;

            if (World != null)
            {
                petCX = World.PetX;
                petCY = World.PetY;
                ApplyWorldActionTransforms(ref sx, ref sy, ref rot, ref offY);
            }

            // ─── 伸懒腰（叠加） ──
            if (_stretchTimer >= 0)
            {
                _stretchTimer += deltaMs;
                var t2 = _stretchTimer / 2000f;
                if (t2 >= 1f) { _stretchTimer = -1; _stretchOnComplete?.Invoke(); _stretchOnComplete = null; }
                else if (t2 < 0.4f) { var ph = t2 / 0.4f; sx = sy = 1f + 0.06f * MathF.Sin(ph * MathF.PI / 2); }
                else if (t2 < 0.7f) { var ph = (t2 - 0.4f) / 0.3f; sx = 1.04f + 0.02f * MathF.Sin(ph * MathF.PI * 2); sy = sx; rot = 2f * MathF.Sin(ph * MathF.PI * 3); }
                else { var ph = (t2 - 0.7f) / 0.3f; sx = sy = 1.06f - 0.06f * ph; rot = 1f * (1f - ph) * MathF.Sin(ph * MathF.PI); }
            }

            var finalScale = bs;
            DrawSpriteLayer(canvas, width, height, cs, finalScale, 1f, sx, sy, rot, _config.RenderScale, offY, petCX, petCY);
            if (_prevSprite != null && _crossfadeProgress < 1f)
            {
                DrawSpriteLayer(canvas, width, height, _prevSprite, bs, 1f - _crossfadeProgress, 1f, 1f, 0f, _config.RenderScale, offY, petCX, petCY);
                if (_crossfadeProgress >= 1f) _prevSprite = null;
            }
        }
        else DrawFallback(canvas, width, height);

        // 粒子
        _particles.Draw(canvas, width, height, deltaMs);

        // ─── v3.4 夜色氛围（22点~6点轻微压暗，可在设置关闭） ──
        if (_config.NightDimEnabled)
        {
            var hour = DateTime.Now.Hour;
            if (hour >= 22 || hour < 6)
            {
                using var night = new SKPaint { Color = new SKColor(16, 24, 56, 28) };
                canvas.DrawRect(0, 0, width, height, night);
            }
        }

        // 台词气泡
        _bubble.Draw(canvas, width, height, deltaMs, World);
    }

    // ═══════════════════════════════════════════════
    //  v3.0 小天地动作变换
    // ═══════════════════════════════════════════════

    private void ApplyWorldActionTransforms(ref float sx, ref float sy, ref float rot, ref float offY)
    {
        if (World == null) return;
        var a = World.CurrentAction;
        var p = World.ActionProgress;

        switch (a)
        {
            case WorldAction.Bounce:
                float bounce = MathF.Abs(MathF.Sin(p * MathF.PI * 3)) * 0.08f;
                sx += bounce; sy += bounce;
                break;
            case WorldAction.Spin:
                rot = p * 360f;
                break;
            case WorldAction.Sit:
                offY = 15f; sx = 1f - 0.05f * p; sy = 1f - 0.08f * p;
                break;
            case WorldAction.LieDown:
                offY = 30f; sx = 1f + 0.08f * p; sy = 1f - 0.15f * p;
                break;
            case WorldAction.HeadTilt:
                rot = MathF.Sin(p * MathF.PI * 2) * 8f;
                break;
            case WorldAction.Wave:
                rot = MathF.Sin(p * MathF.PI * 4) * 5f;
                break;
            case WorldAction.Sway:
                rot = MathF.Sin(p * MathF.PI * 2) * 6f;
                break;
            case WorldAction.Hide:
                sx = 1f - 0.3f * Math.Min(p * 2, 1f);
                sy = 1f - 0.3f * Math.Min(p * 2, 1f);
                break;
            case WorldAction.Stretch:
                sx = 1f + 0.05f * MathF.Sin(p * MathF.PI);
                sy = 1f - 0.03f * MathF.Sin(p * MathF.PI);
                break;
            case WorldAction.PlayDead:
                offY = 40f * (p < 0.7f ? p / 0.7f : 1f);
                rot = p < 0.7f ? 90f : 0f;
                break;
        }
    }

    private static void DrawSpriteLayer(SKCanvas c, float w, float h, SKBitmap s, float scale, float op,
        float stretchX = 1f, float stretchY = 1f, float rotation = 0f, float renderScale = 1f,
        float offsetY = 0f, float centerX = -1f, float centerY = -1f)
    {
        var sw = s.Width; var sh = s.Height;
        var ms = Math.Min(w * 0.88f, h * 0.78f) * renderScale;
        var fs = ms / Math.Max(sw, sh);
        var dw = sw * fs; var dh = sh * fs;
        float cx = centerX >= 0 ? centerX : w / 2f;
        float cy = centerY >= 0 ? centerY : h - dh / 2f - 12f;
        var bx = cx - dw / 2f; var by = cy - dh / 2f + offsetY;

        c.Save();
        c.Translate(cx, cy + offsetY);
        c.Scale(scale * stretchX, scale * stretchY);
        if (rotation != 0) c.RotateDegrees(rotation);
        c.Translate(-cx, -(cy + offsetY));

        using var pt = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, (byte)(op * 255)) };
        c.DrawBitmap(s, new SKRect(bx, by, bx + dw, by + dh), pt);
        c.Restore();
    }

    private void DrawFallback(SKCanvas c, float w, float h)
    {
        using var pt = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(255, 180, 180, 180) };
        c.DrawCircle(w / 2f, h * 0.75f, 80, pt);
    }

    public void Dispose()
    {
        _sprites.Dispose();
        _particles.Dispose();
    }
}
