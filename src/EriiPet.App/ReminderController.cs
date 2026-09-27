using System.Windows;
using EriiPet.Animation;
using EriiPet.Core.Models;

namespace EriiPet.App;

/// <summary>提醒系统：到期弹窗、临近倒计时气泡、准时/超时的性格化反馈</summary>
public sealed class ReminderController
{
    private readonly TodoService _todo;
    private readonly PetStateMachine _stateMachine;
    private readonly SkiaRenderer _renderer;
    private readonly RelationshipService _relation;
    private readonly AttentionState _attention;
    private readonly IWindowContext _win;

    private readonly HashSet<string> _confirmedIds = new();
    private bool _reminderActive;
    private DateTime _lastConfirmTime = DateTime.MinValue;
    private DateTime _lastCountdownBubble;

    public ReminderController(TodoService todo, PetStateMachine stateMachine,
        SkiaRenderer renderer, RelationshipService relation, AttentionState attention,
        IWindowContext win)
    {
        _todo = todo;
        _stateMachine = stateMachine;
        _renderer = renderer;
        _relation = relation;
        _attention = attention;
        _win = win;
    }

    /// <summary>30s 低精度轮询：有到期提醒则弹窗</summary>
    public void OnCheck()
    {
        if ((DateTime.Now - _lastConfirmTime).TotalMinutes < 5) return;
        if (_reminderActive) return;
        var due = _todo.GetDueAndUnnotified();
        if (due.Count == 0) return;
        ShowReminder(due[0]);
    }

    /// <summary>1s 高精度：临近提醒的倒计时气泡与即时触发</summary>
    public void OnPrecisionTick()
    {
        var state = _stateMachine.Current;
        if (state != PetState.Idle && state != PetState.Wandering && state != PetState.Sleepy) return;

        var now = DateTime.Now;
        var pending = _todo.Items
            .Where(i => !i.Notified && i.RemindAt > now)
            .OrderBy(i => i.RemindAt)
            .FirstOrDefault();
        if (pending == null) return;

        var remainSec = (pending.RemindAt - now).TotalSeconds;
        if (remainSec <= 2)
        {
            _attention.LastInteraction = now;
            ShowReminder(pending);
            return;
        }
        if (remainSec < 60)
        {
            _attention.LastInteraction = now;
            _attention.StretchPlayed = true;
            if (remainSec < 10)
            {
                _renderer.ShowBubbleRaw($"⏰ {remainSec:F0}…");
            }
            else if ((now - _lastCountdownBubble).TotalSeconds > 15)
            {
                _lastCountdownBubble = now;
                _renderer.ShowBubbleRaw($"⏰ {pending.Text}…{remainSec / 60 + 1:F0}分钟后。");
            }
        }
    }

    private void ShowReminder(TodoItem item)
    {
        _todo.MarkNotified(item);
        _stateMachine.Transition(PetState.Acknowledge);
        _renderer.ShowBubbleRaw($"📋 {item.Text}");
        _ = Task.Run(async () => { try { await TtsService.SpeakAsync(item.Text); } catch { } });
        _reminderActive = true;

        // 弹窗在绘梨衣正上方，留 30px 间距，且不超出屏幕
        var popupLeft = _win.Left + (_win.Width - 360) / 2;
        var popupTop = _win.Top - 220 - 30;
        var workArea = SystemParameters.WorkArea;
        if (popupTop < workArea.Top) popupTop = workArea.Top + 10;
        if (popupLeft < workArea.Left) popupLeft = workArea.Left + 10;
        if (popupLeft + 360 > workArea.Right) popupLeft = workArea.Right - 370;

        var w = new Views.ReminderWindow(item,
            onTime => { _win.DispatcherInvoke(() => OnReminderConfirmed(item, onTime)); },
            popupLeft, popupTop);
        w.Show();
    }

    private void OnReminderConfirmed(TodoItem item, bool onTime)
    {
        if (!_confirmedIds.Add(item.Id)) return;
        _reminderActive = false;
        _lastConfirmTime = DateTime.Now;
        foreach (var due in _todo.GetDueAndUnnotified()) _todo.MarkNotified(due);
        _todo.Confirm(item, onTime);
        _attention.MarkInteracted();

        if (onTime)
        {
            _relation.RecordInteraction(RelationEventType.ReminderOnTime);
            _stateMachine.Transition(PetState.Smirk);
            _renderer.ShowBubbleRaw("帮到了Sakura 开心❤");
        }
        else
        {
            _relation.RecordInteraction(RelationEventType.ReminderLate);
            _stateMachine.Transition(PetState.Annoyed);
            _renderer.ShowBubbleRaw("哦");
            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                _win.DispatcherInvoke(() => _renderer.ShowBubbleRaw("Sakura下次会早点来嘛"));
            });
            _ = Task.Delay(15000).ContinueWith(_ =>
            {
                _win.DispatcherInvoke(() =>
                {
                    if (_stateMachine.Current == PetState.Annoyed) _stateMachine.ForceIdle();
                });
            });
        }
    }
}
