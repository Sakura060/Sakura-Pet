namespace EriiPet.Core.Models;

/// <summary>
/// 专注陪伴（番茄钟式）：期间绘梨衣保持安静（抑制随机打扰），
/// 完成时给一次夸奖。久坐提醒也在这里：N 分钟没有交互就轻推一句。
/// </summary>
public class FocusService
{
    private DateTime? _endsAt;
    private int _plannedMinutes;
    private bool _finishAnnounced;
    private bool _sedentaryNoticed;
    private DateTime _lastHalfwayCheer = DateTime.MinValue;

    /// <summary>专注是否进行中</summary>
    public bool IsActive => _endsAt.HasValue && _endsAt.Value > DateTime.Now;

    /// <summary>Tick 返回完成台词的那一秒为 true（供 UI 撒花/加好感）</summary>
    public bool JustFinished { get; private set; }

    public int RemainingMinutes => IsActive
        ? (int)Math.Ceiling((_endsAt!.Value - DateTime.Now).TotalMinutes)
        : 0;

    /// <summary>开始专注（期间半程时返回一句加油台词，只给一次）</summary>
    public string? Start(int minutes)
    {
        _endsAt = DateTime.Now.AddMinutes(minutes);
        _plannedMinutes = minutes;
        _finishAnnounced = false;
        _lastHalfwayCheer = DateTime.MinValue;
        return QuoteLibrary.Pick("focus_start");
    }

    public string? Stop()
    {
        _endsAt = null;
        return "好…那先休息一下。";
    }

    /// <summary>
    /// 每秒调用一次。返回非 null 表示要说的话：
    /// 半程加油 / 完成夸奖（只发一次）。
    /// </summary>
    public string? Tick()
    {
        JustFinished = false;
        if (_endsAt == null) return null;

        // 已结束：夸奖一次
        if (!IsActive)
        {
            if (_finishAnnounced) return null;
            _finishAnnounced = true;
            JustFinished = true;
            return QuoteLibrary.Pick("focus_done");
        }

        // 半程加油（仅 ≥30 分钟的专注，且只一次）
        var totalSec = _plannedMinutes * 60.0;
        var remainSec = (_endsAt.Value - DateTime.Now).TotalSeconds;
        if (_plannedMinutes >= 30 && remainSec <= totalSec / 2
            && _lastHalfwayCheer == DateTime.MinValue)
        {
            _lastHalfwayCheer = DateTime.Now;
            return QuoteLibrary.Pick("focus_half");
        }
        return null;
    }

    /// <summary>
    /// 久坐关怀：sedentaryMinutes &gt; 0 且距上次交互超过阈值时轻推一句；
    /// 推过之后等用户再次交互才会再次提醒。
    /// </summary>
    public string? CheckSedentary(DateTime lastInteraction, int sedentaryMinutes)
    {
        if (sedentaryMinutes <= 0) return null;
        if (IsActive) return null; // 专注期间不重复打扰

        var idleMin = (DateTime.Now - lastInteraction).TotalMinutes;
        if (idleMin >= sedentaryMinutes && !_sedentaryNoticed)
        {
            _sedentaryNoticed = true;
            return QuoteLibrary.Pick("sedentary");
        }
        if (idleMin < 1 && _sedentaryNoticed)
        {
            _sedentaryNoticed = false; // 用户动过了，重置
        }
        return null;
    }
}
