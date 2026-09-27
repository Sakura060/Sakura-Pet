namespace EriiPet.Core.Models;

/// <summary>
/// 宠物状态机 — 管理状态切换、链式定时、交互锁
/// 
/// Phase 3 fix:
/// - 修复 ScheduleAuto 互相取消导致的链式状态丢失
/// - 改为独立 Task 链条，不用共享 CancellationToken
/// - 所有 SetState 加锁保证线程安全
/// 
/// Phase 4 fix (emotion chain 修复):
/// - 新增 _chainGeneration 计数器，每次手动 Transition / ForceIdle / ForceSleep / WakeUp 自增
/// - 链式 Task 延迟回调捕获创建时的 generation，过期的回调直接跳过
/// - 防止旧链的延迟任务覆盖新链的状态（如单点→双击→旧链 happy→idle 提前触发）
/// </summary>
public class PetStateMachine
{
    private static readonly Dictionary<PetState, int> StateDurations = new()
    {
        [PetState.Idle]        = -1,
        [PetState.Smirk]       = 2000,
        [PetState.Surprised]   = 300,
        [PetState.Happy]       = 800,
        [PetState.Blush]       = 2000,
        [PetState.Sleepy]      = -1,
        [PetState.Reject]      = 0,
        [PetState.Annoyed]     = 5000,
        [PetState.Acknowledge] = 1500,
        [PetState.Cuddle]      = 3000,
        [PetState.Wandering]   = 30_000, // 30s 后自动回 idle
    };

    private PetState _current = PetState.Idle;
    private readonly object _lock = new();
    private bool _interactionLocked;

    /// <summary>
    /// 链式生成计数器。
    /// 每次手动 Transition / ForceIdle / ForceSleep / WakeUp 自增。
    /// 链式任务捕获创建时的值，执行时检查：如果已过期（!= 当前值），直接跳过。
    /// 这等效于"取消"了旧链中还未触发的延迟回调。
    /// </summary>
    private int _chainGeneration;

    /// <summary>状态变化事件（在锁内触发，订阅者应快速返回）</summary>
    public event Action<PetState, PetState>? StateChanged; // (old, new)

    public PetState Current
    {
        get { lock (_lock) return _current; }
    }

    public bool Locked
    {
        get { lock (_lock) return _interactionLocked; }
        set { lock (_lock) _interactionLocked = value; }
    }

    // ═══════════════════════════════════════════════
    //  状态切换
    // ═══════════════════════════════════════════════

    public bool Transition(PetState target)
    {
        lock (_lock)
        {
            if (_interactionLocked) return false;
            if (target == _current) return false;

            // 每次手动 Transition 递增 generation，使旧链延迟任务自动失效
            _chainGeneration++;
            var capturedGen = _chainGeneration;

            // ─── surprised 链：surprised → happy → idle ──
            if (target == PetState.Surprised)
            {
                SetStateLocked(target);
                var happyMs = StateDurations[PetState.Surprised];
                var idleMs  = happyMs + StateDurations[PetState.Happy];

                ChainDelay(capturedGen, happyMs, () =>
                {
                    if (_current == PetState.Surprised && !_interactionLocked)
                        SetStateLocked(PetState.Happy);
                });
                ChainDelay(capturedGen, idleMs, () =>
                {
                    if (_current == PetState.Happy && !_interactionLocked)
                        SetStateLocked(PetState.Idle);
                });
                return true;
            }

            SetStateLocked(target);

            int dur = StateDurations.GetValueOrDefault(target, -1);
            if (dur > 0)
            {
                var expect = target;
                ChainDelay(capturedGen, dur, () =>
                {
                    if (_current == expect && !_interactionLocked)
                        SetStateLocked(PetState.Idle);
                });
            }

            return true;
        }
    }

    public void ForceIdle()
    {
        lock (_lock)
        {
            _chainGeneration++; // 手动干预，使旧链失效
            SetStateLocked(PetState.Idle);
        }
    }

    public void ForceSleep()
    {
        lock (_lock)
        {
            _chainGeneration++; // 手动干预，使旧链失效
            _interactionLocked = false;
            SetStateLocked(PetState.Sleepy);
        }
    }

    public void WakeUp()
    {
        lock (_lock)
        {
            if (_current == PetState.Sleepy)
            {
                _chainGeneration++; // 手动唤醒，旧链失效
                SetStateLocked(PetState.Idle);
            }
        }
    }

    // ─── 内部 ────────────────────────────────

    private void SetStateLocked(PetState target)
    {
        if (target == _current) return;
        var old = _current;
        _current = target;
        StateChanged?.Invoke(old, target);
    }

    /// <summary>
    /// 链式延迟：只在 generation 匹配时才执行 action。
    /// action 在锁外调用（通过 ContinueWith），但会回到锁内判断状态。
    /// </summary>
    /// <param name="expectedGen">创建时的 generation 快照</param>
    /// <param name="delayMs">延迟毫秒</param>
    /// <param name="action">要在锁内执行的动作</param>
    private void ChainDelay(int expectedGen, int delayMs, Action action)
    {
        _ = Task.Delay(delayMs).ContinueWith(_ =>
        {
            // 先快速判断：generation 不匹配 → 旧链，直接跳过
            // 这里不能直接访问 _chainGeneration，因为不在锁内。
            // 但我们有 expectedGen 快照。在锁内验证：
            lock (_lock)
            {
                // 如果 generation 已变，说明有新的手动干预，跳过
                if (_chainGeneration != expectedGen)
                    return;

                // generation 匹配 → 执行链步骤
                action();
            }
        });
    }
}
