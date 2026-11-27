using System;

namespace TaskbarCalendar.Services;

/// <summary>
/// 任务栏时钟接管的总开关。
/// 本服务只管理本进程内的钩子 / 覆盖窗口，不修改注册表、不注入 Explorer，
/// 因此关闭或退出后系统原生时钟行为立即恢复。
/// </summary>
public sealed class ClockTakeoverService : IDisposable
{
    private readonly ClockBoundsMonitor ClockBoundsMonitor;
    private readonly AppSettings AppSettings;

    private IClockHitWatcher? Watcher;

    /// <summary>
    /// 用户点击了任务栏时钟，上层应展示自己的面板。
    /// </summary>
    public event EventHandler? ClockClicked;

    /// <summary>
    /// 接管开关状态变化。
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// 面板打开期间点击了鼠标左键（物理像素坐标）。
    /// </summary>
    public event EventHandler<MouseDownEventArgs>? MouseDownAt;

    public ClockTakeoverService(ClockBoundsMonitor _clock_bounds_monitor, AppSettings _app_settings)
    {
        ClockBoundsMonitor = _clock_bounds_monitor;
        AppSettings = _app_settings;
    }

    public bool IsEnabled => Watcher is not null;

    public void SetEnabled(bool _enabled)
    {
        if (_enabled == IsEnabled)
        {
            return;
        }

        if (_enabled)
        {
            IClockHitWatcher _watcher = AppSettings.HitTestMode switch
            {
                ClockHitTestMode.Overlay => new OverlayClockHitWatcher(ClockBoundsMonitor),
                _ => new MouseHookClockHitWatcher(ClockBoundsMonitor)
            };
            _watcher.ClockClicked += Watcher_ClockClicked;
            _watcher.Start();
            Watcher = _watcher;

            if (_watcher is IOutsideClickSource _outside_source)
            {
                _outside_source.MouseDownAt += OutsideSource_MouseDownAt;
            }

            AppLog.Write($"时钟接管已开启（{AppSettings.HitTestMode}）");
        }
        else
        {
            if (Watcher is IOutsideClickSource _outside_source)
            {
                _outside_source.MouseDownAt -= OutsideSource_MouseDownAt;
            }

            Watcher!.ClockClicked -= Watcher_ClockClicked;
            Watcher.Dispose();
            Watcher = null;
            AppLog.Write("时钟接管已关闭，恢复系统原生行为");
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        SetEnabled(false);
    }

    private void Watcher_ClockClicked(object? _sender, EventArgs _args)
    {
        ClockClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OutsideSource_MouseDownAt(object? _sender, MouseDownEventArgs _args)
    {
        MouseDownAt?.Invoke(this, _args);
    }
}
