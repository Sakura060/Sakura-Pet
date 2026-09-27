using System.Text.Json.Serialization;

namespace EriiPet.Core.Models;

/// <summary>全应用配置（JSON 序列化兼容）</summary>
public sealed class PetConfig
{
    // ─── 窗口 ──────────────────────────
    public int WindowWidth { get; set; } = 320;
    public int WindowHeight { get; set; } = 380;

    // ─── 呼吸 ──────────────────────────
    public int BreathePeriodMs { get; set; } = 3000;
    public int BreatheSlowPeriodMs { get; set; } = 5000;
    public float BreatheScaleMin { get; set; } = 0.99f;
    public float BreatheScaleMax { get; set; } = 1.01f;

    // ─── 交互 ──────────────────────────
    public int SleepTimeoutMs { get; set; } = 60_000;  // 60s 后睡觉（留40s给闲逛）
    public int RandomActionMinMs { get; set; } = 45_000;
    public int RandomActionMaxMs { get; set; } = 90_000;

    // ─── 闲逛 ──────────────────────────
    public bool WanderEnabled { get; set; } = true;             // 用户可开关
    public int WanderTriggerMinMs { get; set; } = 15_000;   // 15s 开始可能闲逛
    public int WanderTriggerMaxMs { get; set; } = 25_000;   // 25s 大概率闲逛（在睡觉30s之前）
    public int WanderDurationMs { get; set; } = 30_000;     // 一次闲逛持续 30s
    public int WanderActionIntervalMs { get; set; } = 3000; // 每 3s 换动作

    // ─── 存在感 ──────────────────────────
    public int InteractionLevel { get; set; } = 1;
    public bool AutoHideFullscreen { get; set; } = true;

    // ─── 鸭子 ──────────────────────────
    public double DuckChance { get; set; } = 0.25;
    public int DuckIntervalSec { get; set; } = 10;

    // ─── 特效 ──────────────────────────
    public int MaxParticles { get; set; } = 15;

    // ─── 夜间 ──────────────────────────
    public int NightStartHour { get; set; } = 18;
    public int NightEndHour { get; set; } = 6;

    // ─── 台词 ──────────────────────────
    public int BubbleDurationMs { get; set; } = 3000;

    // ─── AI 聊天 ────────────────────────
    public string AiEndpoint { get; set; } = "https://api.deepseek.com";
    public string AiApiKey { get; set; } = "";
    public string AiModel { get; set; } = "deepseek-chat";

    // ─── 皮肤 ──────────────────────────
    public string CurrentSkin { get; set; } = "default";

    // ─── 大小 ──────────────────────────
    public float RenderScale { get; set; } = 1.0f;

    // ─── 合规 ──────────────────────────
    public bool AgeConfirmed { get; set; } = false;       // 用户已确认年龄
    public bool ContentFilterEnabled { get; set; } = true; // 敏感词过滤开关
    public bool DesktopAwareEnabled { get; set; } = true;  // 桌面感知开关
    public bool PetalEffectEnabled { get; set; } = true;    // 樱花粒子特效
    public bool UseLocalModel { get; set; } = false;         // 本地模型（默认关闭）
    public bool WelcomeShown { get; set; } = false;           // 欢迎引导已显示

    // ─── 数据 ──────────────────────────
    public string DataPath { get; set; } = "";  // 空=默认路径    // 0.5x ~ 2.0x

    // ─── 语音 ──────────────────────────
    public bool VoicePackEnabled { get; set; } = true; // 生日等关键时刻播放原声配音

    // ─── 本地语音模型 (GPT-SoVITS) ─────
    public bool VoiceModelEnabled { get; set; } = false;              // 启用（不可用自动回退 TTS）
    public string VoiceModelEndpoint { get; set; } = "http://127.0.0.1:9880";
    public string VoiceModelRefAudio { get; set; } = VoiceModelService.DefaultRefAudio;   // 参考音频（服务端可见路径）
    public string VoiceModelPromptText { get; set; } = VoiceModelService.DefaultPromptText; // 参考音频对应原文
    public string VoiceServiceBat { get; set; } = @"D:\GPT-SoVITS\启动本地语音服务.bat"; // 一键启动脚本

    // ─── 系统 ──────────────────────────
    public bool AutoStart { get; set; } = false;        // 开机自启（写 HKCU Run 键）
    public float WindowOpacity { get; set; } = 1.0f;    // 窗口透明度 0.3 ~ 1.0
    public float MasterVolume { get; set; } = 0.9f;     // 全局音量 0.2 ~ 1.0
    public string UserBirthday { get; set; } = "";      // 用户生日 MM-dd（空=不提醒）
    public int SedentaryMinutes { get; set; } = 0;      // 久坐提醒阈值分钟（0=关闭）
    public int ScreenIndex { get; set; } = 0;           // 停靠显示器序号
    public bool NightDimEnabled { get; set; } = true;   // 深夜(22-6点)画面轻微压暗

    [JsonIgnore]
    public static PetConfig Default { get; } = new();
}
