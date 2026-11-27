using System;

namespace TaskbarCalendar.Services;

/// <summary>
/// 覆盖窗口方式的点击拦截器，作为鼠标钩子不可用时的备选。
/// </summary>
public sealed class OverlayClockHitWatcher : IClockHitWatcher
{
    private readonly ClockBoundsMonitor ClockBoundsMonitor;

    private Views.ClockHitOverlayWindow? OverlayWindow;
    private bool IsDisposed = false;

    public event EventHandler? ClockClicked;

    public OverlayClockHitWatcher(ClockBoundsMonitor _clock_bounds_monitor)
    {
        ClockBoundsMonitor = _clock_bounds_monitor;
    }

    public bool IsRunning => OverlayWindow is not null;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        OverlayWindow = new Views.ClockHitOverlayWindow();
        OverlayWindow.Clicked += OverlayWindow_Clicked;
        OverlayWindow.Show();
        ClockBoundsMonitor.BoundsChanged += ClockBoundsMonitor_BoundsChanged;

        if (ClockBoundsMonitor.TryGet(out ClockBounds _bounds))
        {
            OverlayWindow.MoveTo(_bounds);
        }

        AppLog.Write("覆盖窗口已启用");
    }

    public void Stop()
    {
        ClockBoundsMonitor.BoundsChanged -= ClockBoundsMonitor_BoundsChanged;

        if (OverlayWindow is null)
        {
            return;
        }

        OverlayWindow.Clicked -= OverlayWindow_Clicked;
        OverlayWindow.Close();
        OverlayWindow = null;
        AppLog.Write("覆盖窗口已关闭");
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        Stop();
        IsDisposed = true;
    }

    private void ClockBoundsMonitor_BoundsChanged(object? _sender, EventArgs _args)
    {
        if (OverlayWindow is null)
        {
            return;
        }

        if (ClockBoundsMonitor.TryGet(out ClockBounds _bounds))
        {
            OverlayWindow.MoveTo(_bounds);
        }
    }

    private void OverlayWindow_Clicked(object? _sender, EventArgs _args)
    {
        ClockClicked?.Invoke(this, EventArgs.Empty);
    }
}
