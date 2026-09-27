namespace EriiPet.Core.Models;

/// <summary>
/// 情绪状态枚举
/// 注意：这不是"表情"，表情是动画层的概念。
/// 情绪是持久状态，决定角色的行为偏好。
/// </summary>
public enum Emotion
{
    /// <summary>平静 — 默认状态，呼吸自然</summary>
    Calm = 0,

    /// <summary>开心 — 用户互动后、好感度触发</summary>
    Happy = 1,

    /// <summary>害羞 — 三连击触发</summary>
    Shy = 2,

    /// <summary>生气 — 连续拖拽触发</summary>
    Annoyed = 3,

    /// <summary>困倦 — 闲置过长 / 夜间</summary>
    Sleepy = 4,

    /// <summary>惊讶 — 单击触发，短暂</summary>
    Surprised = 5,

    /// <summary>撒娇 — 画圈触发</summary>
    Cuddly = 6,
}
