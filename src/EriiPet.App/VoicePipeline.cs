using EriiPet.Animation;
using EriiPet.Core.Models;

namespace EriiPet.App;

/// <summary>
/// 台词发声管线：字幕（气泡+便签）先行，声音按
/// 本地音色模型 (GPT-SoVITS) → Edge TTS 的顺序回退。
/// </summary>
public sealed class VoicePipeline
{
    private readonly Func<PetConfig> _config;
    private readonly SkiaRenderer _renderer;

    public VoicePipeline(Func<PetConfig> config, SkiaRenderer renderer)
    {
        _config = config;
        _renderer = renderer;
    }

    /// <summary>把配置同步给各语音服务（启动与设置保存时调用）</summary>
    public void ApplyConfig()
    {
        var cfg = _config();
        VoiceModelService.Enabled = cfg.VoiceModelEnabled;
        VoiceModelService.Endpoint = string.IsNullOrWhiteSpace(cfg.VoiceModelEndpoint)
            ? "http://127.0.0.1:9880" : cfg.VoiceModelEndpoint.Trim();
        // 参考音频/原文为空时兜底到交接文档默认值（老配置升级零操作）
        VoiceModelService.RefAudioPath = string.IsNullOrWhiteSpace(cfg.VoiceModelRefAudio)
            ? VoiceModelService.DefaultRefAudio : cfg.VoiceModelRefAudio.Trim();
        VoiceModelService.PromptText = string.IsNullOrWhiteSpace(cfg.VoiceModelPromptText)
            ? VoiceModelService.DefaultPromptText : cfg.VoiceModelPromptText.Trim();

        var vol = Math.Clamp(cfg.MasterVolume, 0.2f, 1f);
        TtsService.Volume = vol;
        SoundService.Volume = vol;
        VoiceModelService.Volume = vol;
    }

    /// <summary>发声（不等待完成）：字幕立即出现，声音异步跟上</summary>
    public void Speak(string text) => _ = SpeakAsync(text);

    public async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _renderer.ShowBubbleRaw(text);
        _renderer.AddToNotepad(text);
        try
        {
            var cfg = _config();
            if (cfg.VoiceModelEnabled)
            {
                var wav = await VoiceModelService.SynthesizeAsync(text);
                if (wav != null)
                {
                    await VoiceModelService.PlayAsync(wav);
                    return;
                }
            }
            await TtsService.SpeakAsync(text);
        }
        catch
        {
            // 语音链路整体失败时字幕已展示，静默降级
        }
    }

    /// <summary>生日等原声场景：只播打包原声，不与 TTS 叠音</summary>
    public void SpeakWithPackagedVoice(string text, string assetsDir)
    {
        _renderer.ShowBubbleRaw(text);
        _renderer.AddToNotepad(text);
        TtsService.StopPlayback();
        SoundService.PlayBirthdayVoice(assetsDir);
    }
}
