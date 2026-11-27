using System;
using System.Threading;
using System.Windows.Threading;

namespace TaskbarCalendar.Services;

/// <summary>
/// 周期性刷新任务栏时钟区域，供钩子线程无锁读取。
/// 任务栏会随分辨率、DPI、小任务栏按钮、Explorer 重启而移动，所以不能只定位一次。
/// </summary>
public sealed class ClockBoundsMonitor : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer RefreshTimer;

    private ClockSnapshot? _snapshot_field;

    /// <summary>
    /// 时钟区域发生变化时触发。
    /// </summary>
    public event EventHandler? BoundsChanged;

    public ClockBoundsMonitor()
    {
        RefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = RefreshInterval
        };
        RefreshTimer.Tick += RefreshTimer_Tick;
    }

    /// <summary>
    /// 读取最近一次定位结果。
    /// </summary>
    public bool TryGet(out ClockBounds _bounds)
    {
        ClockSnapshot? _snapshot = Volatile.Read(ref _snapshot_field);
        if (_snapshot is null)
        {
            _bounds = default;
            return false;
        }

        _bounds = _snapshot.Bounds;
        return true;
    }

    public void Start()
    {
        Refresh();
        RefreshTimer.Start();
    }

    public void Stop()
    {
        RefreshTimer.Stop();
    }

    public void Dispose()
    {
        RefreshTimer.Stop();
        RefreshTimer.Tick -= RefreshTimer_Tick;
    }

    private void RefreshTimer_Tick(object? _sender, EventArgs _args) => Refresh();

    private void Refresh()
    {
        ClockBounds? _bounds = TaskbarClockLocator.Locate();
        ClockSnapshot? _old = Volatile.Read(ref _snapshot_field);

        if (_bounds is null)
        {
            if (_old is not null)
            {
                Volatile.Write(ref _snapshot_field, null);
                BoundsChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_old is not null && _old.Bounds == _bounds.Value)
        {
            return;
        }

        Volatile.Write(ref _snapshot_field, new ClockSnapshot(_bounds.Value));
        BoundsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ClockSnapshot
    {
        public ClockSnapshot(ClockBounds _bounds)
        {
            Bounds = _bounds;
        }

        public ClockBounds Bounds { get; }
    }
}
