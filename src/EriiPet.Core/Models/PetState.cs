namespace EriiPet.Core.Models;

/// <summary>
/// 宠物状态：定义所有可能的"表情"状态
/// 后续会演变为完整状态机（带动画过渡）
/// </summary>
public enum PetState
{
    Idle,        // 待机呼吸
    Smirk,       // 偷笑（悬停触发）
    Surprised,   // 惊讶（单击）
    Happy,       // 开心（双击 / 惊讶后自动转）
    Blush,       // 害羞（三击）
    Sleepy,      // 困倦（闲置 / 夜间）
    Reject,      // 拒绝（拖拽中）
    Annoyed,     // 生气（连拖三次）
    Acknowledge, // 点头（右键菜单）
    Cuddle,      // 撒娇（画圈）
    Wandering,   // 闲逛（自动桌面漫游 + 微动作）
}
