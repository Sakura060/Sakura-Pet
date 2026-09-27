using System.Windows.Threading;

namespace EriiPet.App;

/// <summary>统一管理 DispatcherTimer：注册即启动，可按键启停，退出时统一释放</summary>
public sealed class TimerHost : IDisposable
{
    private readonly Dictionary<string, DispatcherTimer> _timers = new();

    public void Register(string key, TimeSpan interval, DispatcherPriority priority, EventHandler tick)
    {
        var timer = new DispatcherTimer(interval, priority, tick, Dispatcher.CurrentDispatcher);
        _timers[key] = timer;
        timer.Start();
    }

    public void Stop(string key)
    {
        if (_timers.TryGetValue(key, out var t)) t.Stop();
    }

    public void Start(string key)
    {
        if (_timers.TryGetValue(key, out var t)) t.Start();
    }

    public void Dispose()
    {
        foreach (var t in _timers.Values) t.Stop();
        _timers.Clear();
    }
}
